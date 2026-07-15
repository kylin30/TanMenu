using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class ConfigServiceTests
{
    private static IAppDataPaths NewPaths(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "TanMenuCfg_" + Path.GetRandomFileName());
        var paths = new AppDataPaths(Path.Combine(root, "Local"), Path.Combine(root, "Cache"));
        paths.EnsureCreated();
        return paths;
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsFoldersAndGeneral()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc1 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc1.LoadAsync();                       // creates default (empty folders) + saves
            Assert.Empty(svc1.Config.Folders);

            svc1.Config.Folders.Add(@"C:\Windows");
            svc1.Config.General.ColButtonCount = 4;
            await svc1.SaveAsync();

            Assert.True(File.Exists(paths.ConfigFilePath));

            var svc2 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc2.LoadAsync();

            Assert.True(svc2.HasValidConfig);
            Assert.Contains(@"C:\Windows", svc2.Config.Folders);
            Assert.Equal(4, svc2.Config.General.ColButtonCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_LegacyFluent2Theme_NormalizedToWindows11()
    {
        var paths = NewPaths(out var root);
        try
        {
            // A config written before the "Fluent2" → "Windows11" theme rename (commit b0f41e5).
            await File.WriteAllTextAsync(paths.ConfigFilePath, "{\"general\":{\"themeName\":\"Fluent2\"}}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            // Normalized once on load so every consumer (settings dropdown, stylesheet) sees one key.
            Assert.Equal("Windows11", svc.Config.General.ThemeName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_EmptyOrWhitespaceConfig_BacksUpBeforeResetting()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Simulate a crash-truncated config file (existing but effectively empty).
            await File.WriteAllTextAsync(paths.ConfigFilePath, "   ");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            // The empty file must be backed up before being replaced with defaults, so the
            // user's prior setup is never silently discarded.
            var dir = Path.GetDirectoryName(paths.ConfigFilePath)!;
            var backups = Directory.GetFiles(dir, Path.GetFileName(paths.ConfigFilePath) + ".backup.*");
            Assert.NotEmpty(backups);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BackupThenRestore_RoundTripsConfig()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.RootFolder = @"C:\Windows";
            svc.Config.General.ColButtonCount = 7;
            await svc.SaveAsync();

            var backup = Path.Combine(root, "backup.json");
            await svc.BackupToAsync(backup);
            Assert.True(File.Exists(backup));

            // Mutate the live config, then restore from the backup.
            svc.Config.RootFolder = @"C:\Other";
            svc.Config.General.ColButtonCount = 1;
            await svc.SaveAsync();

            Assert.True(await svc.RestoreFromAsync(backup));
            Assert.Equal(@"C:\Windows", svc.Config.RootFolder);
            Assert.Equal(7, svc.Config.General.ColButtonCount);

            // Restore must also persist to the live config file (a fresh load sees it).
            var reload = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await reload.LoadAsync();
            Assert.Equal(@"C:\Windows", reload.Config.RootFolder);
            Assert.Equal(7, reload.Config.General.ColButtonCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreFromAsync_InvalidFile_ReturnsFalseAndKeepsConfig()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.General.ColButtonCount = 5;
            await svc.SaveAsync();

            var bad = Path.Combine(root, "bad.json");
            await File.WriteAllTextAsync(bad, "{ this is not valid json ");

            Assert.False(await svc.RestoreFromAsync(bad));
            Assert.Equal(5, svc.Config.General.ColButtonCount); // unchanged on failure
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreFromAsync_ValidJsonButNotAConfig_RejectedAndKeepsConfig()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.RootFolder = @"C:\Keep";
            svc.Config.General.ColButtonCount = 9;
            await svc.SaveAsync();

            // A parseable JSON object that is NOT a TanMenu config (no "general") — e.g. another app's
            // settings file, or "{}". Must be rejected, not silently turned into an all-defaults config.
            var notAConfig = Path.Combine(root, "other.json");
            await File.WriteAllTextAsync(notAConfig, "{\"foo\":1,\"bar\":\"x\"}");

            Assert.False(await svc.RestoreFromAsync(notAConfig));
            Assert.Equal(@"C:\Keep", svc.Config.RootFolder);          // unchanged — not silently wiped
            Assert.Equal(9, svc.Config.General.ColButtonCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreFromAsync_EmptyGeneralObject_RejectedAndKeepsConfig()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.RootFolder = @"C:\Keep";
            svc.Config.General.ColButtonCount = 9;
            await svc.SaveAsync();

            // A file whose "general" is an EMPTY object carries no real settings: deserializing it would
            // reset everything to defaults (RootFolder→Desktop) and silently wipe the live config, so it
            // must be rejected just like a file with no "general" at all.
            var emptyGeneral = Path.Combine(root, "emptygeneral.json");
            await File.WriteAllTextAsync(emptyGeneral, "{\"general\":{}}");

            Assert.False(await svc.RestoreFromAsync(emptyGeneral));
            Assert.Equal(@"C:\Keep", svc.Config.RootFolder);          // unchanged — not silently wiped
            Assert.Equal(9, svc.Config.General.ColButtonCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreFromAsync_PartialBackupMissingRootFolder_RejectedAndKeepsConfig()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.RootFolder = @"C:\Keep";
            svc.Config.General.ColButtonCount = 9;
            await svc.SaveAsync();

            // A partial/foreign file with a non-empty general but NO top-level rootFolder. Restoring it
            // would otherwise inherit RootFolder's default (now the Desktop) and silently expose the
            // whole desktop as groups, so it must be rejected. TanMenu-written backups always include
            // rootFolder, so this only rejects partial/foreign files.
            var partial = Path.Combine(root, "partial.json");
            await File.WriteAllTextAsync(partial, "{\"general\":{\"themeName\":\"Win98\"}}");

            Assert.False(await svc.RestoreFromAsync(partial));
            Assert.Equal(@"C:\Keep", svc.Config.RootFolder);          // unchanged — not silently set to Desktop
            Assert.Equal(9, svc.Config.General.ColButtonCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreFromAsync_ExplicitNullRootFolder_RejectedAndKeepsConfig()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();
            svc.Config.RootFolder = @"C:\Keep";
            await svc.SaveAsync();

            // rootFolder present but explicitly null is not a meaningful value — reject like a missing key,
            // so a restore can't leave RootFolder == null where every code path expects an empty string.
            var nullRoot = Path.Combine(root, "nullroot.json");
            await File.WriteAllTextAsync(nullRoot, "{\"general\":{\"themeName\":\"Win98\"},\"rootFolder\":null}");

            Assert.False(await svc.RestoreFromAsync(nullRoot));
            Assert.Equal(@"C:\Keep", svc.Config.RootFolder); // unchanged
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
