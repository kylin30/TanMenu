using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
using TanMenu.Core.Serialization;
using TanMenu.Core.Services;
using TanMenu.Wpf.Serialization;
using Xunit;

namespace TanMenu.Core.Tests.Serialization;

/// <summary>
/// Pins the ON-DISK JSON contract. The NativeAOT migration replaced the reflection-based
/// <c>JsonSerializer</c> with source-generated contexts; all three files below already exist in every
/// user's data folder, so a change in naming, indentation, or null handling would silently discard
/// their settings, orphan their shortcut cache, or force a full cold re-extraction of every icon.
/// Each file is covered twice: the shape this build WRITES, and a verbatim pre-migration file it
/// must still READ. These tests fail if that contract drifts.
/// </summary>
public class JsonContractTests
{
    private static IAppDataPaths NewPaths(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "TanMenuJson_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(root, "Local"), Path.Combine(root, "Cache"));
        paths.EnsureCreated();
        return paths;
    }

    [Fact]
    public void ConfigJson_IsCamelCaseAndIndented()
    {
        var json = JsonSerializer.Serialize(new AppConfig(), ConfigJsonContext.Default.AppConfig);

        // camelCase names, and WriteIndented puts a space after the colon plus real newlines.
        Assert.Contains("\"general\": {", json);
        Assert.Contains("\"rootFolder\":", json);
        Assert.Contains($"\"themeName\": \"{AppConfig.CanonicalTheme}\"", json);
        Assert.Contains($"\"appearance\": \"{AppAppearance.System}\"", json);
        Assert.Contains("\"colButtonCount\": 10", json);
        Assert.Contains("\"defaultTools\": [", json);
        Assert.Contains("\n", json);

        // ...and definitively NOT the serializer's default PascalCase.
        Assert.DoesNotContain("\"General\"", json);
        Assert.DoesNotContain("\"ThemeName\"", json);
        Assert.DoesNotContain("\"RootFolder\"", json);
    }

    [Fact]
    public async Task LoadAsync_ReadsAConfigWrittenBeforeTheAotMigration()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Verbatim shape of what the pre-migration reflection serializer produced
            // (camelCase + indented + nulls omitted).
            const string legacy = """
                {
                  "general": {
                    "language": "zh-Hans",
                    "autoClose": false,
                    "topMost": false,
                    "showInTaskbar": false,
                    "globalHotkeyEnabled": true,
                    "globalHotkey": "Ctrl+Alt+Space",
                    "positionOffset": 12,
                    "colButtonCount": 6,
                    "themeName": "WinXP",
                    "fontFamily": "Microsoft YaHei",
                    "buttonSize": "Large",
                    "showDefaultTools": false,
                    "defaultTools": [
                      { "name": "\u8BA1\u7B97\u5668", "command": "calc.exe", "show": true },
                      { "name": "\u8BB0\u4E8B\u672C", "command": "notepad.exe", "show": false }
                    ]
                  },
                  "folders": [ "C:\\Tools" ],
                  "rootFolder": "C:\\Users\\Someone\\Desktop"
                }
                """;
            await File.WriteAllTextAsync(paths.ConfigFilePath, legacy);

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.True(svc.HasValidConfig);
            Assert.Equal("zh-Hans", svc.Config.General.Language);
            Assert.False(svc.Config.General.AutoClose);
            Assert.False(svc.Config.General.TopMost);
            Assert.True(svc.Config.General.GlobalHotkeyEnabled);
            Assert.Equal("Ctrl+Alt+Space", svc.Config.General.GlobalHotkey);
            Assert.Equal(12, svc.Config.General.PositionOffset);
            Assert.Equal(6, svc.Config.General.ColButtonCount);
            // The stored value was "WinXP". Every setting around it survives the upgrade untouched;
            // only the theme is coerced, because that theme no longer exists.
            Assert.Equal(AppConfig.CanonicalTheme, svc.Config.General.ThemeName);
            Assert.Equal("Microsoft YaHei", svc.Config.General.FontFamily);
            Assert.Equal("Large", svc.Config.General.ButtonSize);
            Assert.False(svc.Config.General.ShowDefaultTools);
            Assert.Equal(2, svc.Config.General.DefaultTools.Count);
            Assert.Equal("计算器", svc.Config.General.DefaultTools[0].Name);
            Assert.False(svc.Config.General.DefaultTools[1].Show);
            Assert.Contains(@"C:\Tools", svc.Config.Folders);
            Assert.Equal(@"C:\Users\Someone\Desktop", svc.Config.RootFolder);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_ProducesJsonTheOldReaderShapeStillMatches()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.General.ThemeName = "Pixel";
            svc.Config.General.GlobalHotkey = "Ctrl+Space";
            svc.Config.Folders.Add(@"D:\Apps");
            await svc.SaveAsync();

            // Re-read as a raw document: every key the app depends on must be present under its
            // camelCase name, in the nesting the previous format used.
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(paths.ConfigFilePath));
            var rootEl = doc.RootElement;
            Assert.True(rootEl.TryGetProperty("general", out var general));
            Assert.True(rootEl.TryGetProperty("folders", out var folders));
            Assert.True(rootEl.TryGetProperty("rootFolder", out _));
            Assert.Equal("Pixel", general.GetProperty("themeName").GetString());
            Assert.Equal("Ctrl+Space", general.GetProperty("globalHotkey").GetString());
            Assert.Equal(@"D:\Apps", folders[0].GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConfigJson_EscapesNonAsciiExactlyAsTheOldWriterDid()
    {
        // The default JavaScriptEncoder escapes every non-ASCII character as an uppercase \uXXXX
        // sequence, so the built-in tool names land on disk as "计算器" rather than
        // "计算器". That is what every existing config.json contains. A source-generated context
        // configured with a different encoder would silently rewrite all of them on the first save.
        var cfg = new AppConfig();
        var json = JsonSerializer.Serialize(cfg, ConfigJsonContext.Default.AppConfig);

        Assert.Contains("\\u8BA1\\u7B97\\u5668", json);   // 计算器
        Assert.DoesNotContain("计算器", json);

        // ...and it still round-trips back to the real characters.
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;
        Assert.Equal("计算器", back.General.DefaultTools[0].Name);
    }

    [Fact]
    public void LinkCacheJson_StaysPascalCaseAndCompact()
    {
        var snapshot = new Dictionary<string, ShortcutCacheEntry>
        {
            [@"c:\x\a.lnk"] = new()
            {
                TargetPath = @"C:\Program Files\App\app.exe",
                LastModified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                FileSize = 1234,
            },
        };

        var json = JsonSerializer.Serialize(
            snapshot, LinkCacheJsonContext.Default.DictionaryStringShortcutCacheEntry);

        // The pre-migration writer used the serializer DEFAULTS here (not the config options),
        // so these names are PascalCase and the document is not indented.
        Assert.Contains("\"TargetPath\":", json);
        Assert.Contains("\"LastModified\":", json);
        Assert.Contains("\"FileSize\":1234", json);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\"targetPath\"", json);

        var back = JsonSerializer.Deserialize(
            json, LinkCacheJsonContext.Default.DictionaryStringShortcutCacheEntry);
        Assert.NotNull(back);
        Assert.Equal(@"C:\Program Files\App\app.exe", back![@"c:\x\a.lnk"].TargetPath);
        Assert.Equal(1234, back[@"c:\x\a.lnk"].FileSize);
    }

    [Fact]
    public void LinkCacheJson_ReadsACacheWrittenBeforeTheAotMigration()
    {
        // Exactly what the previous `JsonSerializer.Serialize(snapshot, new(){WriteIndented=false})`
        // emitted, including a null TargetPath for an unresolvable link.
        const string legacy =
            """{"c:\\a.lnk":{"TargetPath":"C:\\Windows\\notepad.exe","LastModified":"2026-01-02T03:04:05","FileSize":940},"c:\\b.lnk":{"TargetPath":null,"LastModified":"2025-06-07T08:09:10","FileSize":0}}""";

        var back = JsonSerializer.Deserialize(
            legacy, LinkCacheJsonContext.Default.DictionaryStringShortcutCacheEntry);

        Assert.NotNull(back);
        Assert.Equal(2, back!.Count);
        Assert.Equal(@"C:\Windows\notepad.exe", back[@"c:\a.lnk"].TargetPath);
        Assert.Equal(940, back[@"c:\a.lnk"].FileSize);
        Assert.Null(back[@"c:\b.lnk"].TargetPath);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), back[@"c:\a.lnk"].LastModified);
    }

    [Fact]
    public void IconCacheJson_KeepsTheLiteralMtimeSizePngKeys()
    {
        var snapshot = new Dictionary<string, PersistedIcon>
        {
            [@"c:\x\app.exe"] = new()
            {
                Mtime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Size = 940,
                Png = [137, 80, 78, 71],
            },
        };

        var json = JsonSerializer.Serialize(
            snapshot, IconCacheJsonContext.Default.DictionaryStringPersistedIcon);

        // Serializer DEFAULTS, exactly like the pre-migration writer: PascalCase, no indentation.
        // These three key names ARE the contract — re-casing any of them orphans every cached icon.
        Assert.Contains("\"Mtime\":", json);
        Assert.Contains("\"Size\":940", json);
        Assert.Contains("\"Png\":", json);
        Assert.DoesNotContain("\"mtime\"", json);
        Assert.DoesNotContain("\"png\"", json);
        Assert.DoesNotContain("\n", json);
        // byte[] lands on disk as base64, not as a JSON array of numbers.
        Assert.Contains("\"Png\":\"iVBORw==\"", json);
    }

    [Fact]
    public void IconCacheJson_ReadsACacheWrittenBeforeTheAotMigration()
    {
        // Verbatim output of the previous `JsonSerializer.Serialize(snapshot)` (no options at all),
        // where PersistedIcon was still a private nested class inside MenuService.
        const string legacy =
            """{"c:\\x\\app.exe":{"Mtime":"2026-01-02T03:04:05Z","Size":940,"Png":"iVBORw=="}}""";

        var back = JsonSerializer.Deserialize(
            legacy, IconCacheJsonContext.Default.DictionaryStringPersistedIcon);

        Assert.NotNull(back);
        var entry = Assert.Single(back!).Value;
        // MenuService compares this against FileInfo.LastWriteTimeUtc, so it must come back as UTC.
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), entry.Mtime.ToUniversalTime());
        Assert.Equal(940, entry.Size);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, entry.Png);
    }
}
