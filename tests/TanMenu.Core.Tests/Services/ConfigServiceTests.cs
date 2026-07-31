using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
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

    // Every historical themeName, from every era of the CSS theme system, plus a user-authored
    // stylesheet id and outright junk. The Avalonia rewrite ships ONE theme, so all of them must
    // land on the single canonical id instead of leaving the settings dropdown with no valid
    // selection (the old code fell back to "Win7" in the renderer but "WinXP" in the dropdown).
    [Theory]
    [InlineData("Fluent2")]      // renamed to Windows11 in b0f41e5
    [InlineData("Windows11")]
    [InlineData("Win98")]        // removed classic themes
    [InlineData("Win31")]
    [InlineData("Win2000")]
    [InlineData("WinXP")]
    [InlineData("Win7")]         // the previous default
    [InlineData("Pixel")]
    [InlineData("Classic")]      // the Windows 2000 theme the Fluent 2 rebuild replaced
    [InlineData("user:my custom skin")] // a user-authored .css theme; the feature is gone
    [InlineData("")]
    [InlineData("nonsense")]
    public async Task LoadAsync_AnyHistoricalTheme_CoercedToTheSingleSurvivingTheme(string stored)
    {
        var paths = NewPaths(out var root);
        try
        {
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, $"{{\"general\":{{\"themeName\":\"{stored}\"}}}}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.Equal(AppConfig.CanonicalTheme, svc.Config.General.ThemeName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Alibaba PuHuiTi")]
    [InlineData("alibaba puhuiti")]  // stored case is not guaranteed
    [InlineData("Pixel")]
    [InlineData("Press Start 2P")]
    public async Task LoadAsync_RetiredBundledFont_CoercedToTheAppDefault(string stored)
    {
        var paths = NewPaths(out var root);
        try
        {
            // These three named TTFs that shipped inside the executable. The Fluent 2 rebuild deleted
            // them, so the stored name now resolves to nothing and the text stack would silently
            // substitute an arbitrary family. "" means "the app's own default", which is the truth.
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, $"{{\"general\":{{\"fontFamily\":\"{stored}\"}}}}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.Equal("", svc.Config.General.FontFamily);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_InstalledFontName_IsLeftAlone()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Only the three retired BUNDLED names are coerced. A system family the user picked is
            // still perfectly resolvable and must survive the upgrade untouched.
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, "{\"general\":{\"fontFamily\":\"Verdana\"}}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.Equal("Verdana", svc.Config.General.FontFamily);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Light", AppAppearance.Light)]
    [InlineData("dark", AppAppearance.Dark)]
    [InlineData("System", AppAppearance.System)]
    [InlineData("", AppAppearance.System)]
    [InlineData("nonsense", AppAppearance.System)]
    public async Task LoadAsync_NormalizesAppearance(string stored, string expected)
    {
        var paths = NewPaths(out var root);
        try
        {
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, $"{{\"general\":{{\"appearance\":\"{stored}\"}}}}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.Equal(expected, svc.Config.General.Appearance);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ConfigWithNoAppearanceKey_DefaultsToFollowingTheSystem()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Every config.json written before the Fluent 2 rebuild is in this shape.
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, "{\"general\":{\"language\":\"zh-Hans\"}}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.Equal(AppAppearance.System, svc.Config.General.Appearance);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_NullGeneral_DoesNotProduceANullConfigSection()
    {
        var paths = NewPaths(out var root);
        try
        {
            // A hand-edited or truncated file. This used to load with HasValidConfig = true and then
            // NullReference on the very first read of Config.General — RestoreFromAsync rejected the
            // shape but LoadAsync did not.
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, "{\"general\":null,\"folders\":null,\"rootFolder\":\"C:\\\\X\"}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.NotNull(svc.Config.General);
            Assert.NotNull(svc.Config.Folders);
            Assert.NotNull(svc.Config.General.DefaultTools);
            Assert.Equal("Auto", svc.Config.General.Language);
            Assert.Equal(AppConfig.CanonicalTheme, svc.Config.General.ThemeName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_NullDefaultTools_RestoresTheBuiltInList()
    {
        var paths = NewPaths(out var root);
        try
        {
            await File.WriteAllTextAsync(
                paths.ConfigFilePath, "{\"general\":{\"defaultTools\":null},\"rootFolder\":\"C:\\\\X\"}");

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.NotEmpty(svc.Config.General.DefaultTools);
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

    [Fact]
    public async Task Load_PreSchemaConfig_MigratesFlyoutBehaviourToAppWindowDefaults()
    {
        var paths = NewPaths(out var root);
        try
        {
            // A 0.11-era config: flyout behaviour written explicitly, no schemaVersion at all.
            var json = """
                {"general":{"autoClose":true,"topMost":true,"colButtonCount":6},"folders":[],"rootFolder":"C:\\"}
                """;
            await File.WriteAllTextAsync(paths.ConfigFilePath, json);

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.False(svc.Config.General.AutoClose);
            Assert.False(svc.Config.General.TopMost);
            Assert.Equal(AppConfig.CurrentSchemaVersion, svc.Config.General.SchemaVersion);
            Assert.Equal(6, svc.Config.General.ColButtonCount); // untouched by the migration
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Load_MigratedConfig_KeepsDeliberateAutoCloseChoice()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Already migrated, and the user has since re-enabled the flyout behaviour on purpose.
            var json = $$"""
                {"general":{"schemaVersion":{{AppConfig.CurrentSchemaVersion}},"autoClose":true,"topMost":true},"folders":[]}
                """;
            await File.WriteAllTextAsync(paths.ConfigFilePath, json);

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.True(svc.Config.General.AutoClose);
            Assert.True(svc.Config.General.TopMost);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>A SynchronizationContext that only runs posted callbacks when someone explicitly pumps
    /// it — the essential property of a UI thread that is blocked inside a synchronous wait.</summary>
    private sealed class NeverPumpedContext : SynchronizationContext
    {
        public int Posted { get; private set; }

        public override void Post(SendOrPostCallback d, object? state) => Posted++;
        public override void Send(SendOrPostCallback d, object? state) => Posted++;
    }

    [Fact]
    public void SaveAsync_WaitedOnFromABlockedUiThread_DoesNotDeadlock()
    {
        // App.ExitApp BLOCKS the UI thread on this save (WindowHost.FlushPlacementBlocking) so 退出
        // cannot lose a geometry change still inside the 500 ms debounce window. That is only safe
        // while SaveAsync awaits with ConfigureAwait(false): a continuation posted back to the blocked
        // thread would never run, _saveLock would never be released, and 退出 would hang instead of
        // exiting. This reproduces exactly that hazard — a context that records posts but never runs
        // them — so removing a ConfigureAwait(false) from SaveAsync fails here rather than in the
        // field.
        var paths = NewPaths(out var root);
        var previous = SynchronizationContext.Current;
        var context = new NeverPumpedContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            svc.Config.General.WindowLeft = 9500;

            // xUnit1031 warns that blocking on a task in a test can deadlock. Blocking is precisely
            // what is under test here (it is what App.ExitApp does), and the bounded Wait turns a
            // regression into a clean failure rather than a hung run — so the rule is suppressed for
            // this one call, deliberately, not silenced project-wide.
#pragma warning disable xUnit1031
            var completed = svc.SaveAsync().Wait(TimeSpan.FromSeconds(10));
#pragma warning restore xUnit1031

            Assert.True(completed, "SaveAsync did not complete while the calling thread was blocked");
            Assert.Equal(0, context.Posted);
            Assert.True(File.Exists(paths.ConfigFilePath));
            Assert.False(File.Exists(paths.ConfigFilePath + ".tmp")); // no orphan temp left behind
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task RestoreFrom_PreSchemaBackup_MigratesFlyoutBehaviour()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            // A backup file taken from 0.11: flyout behaviour written explicitly, no schemaVersion.
            // Normalize runs from BOTH LoadAsync and RestoreFromAsync and both want the migration —
            // restoring a 0.11-era backup must produce app-window behaviour just as loading a 0.11-era
            // config does. Only the load path was pinned by a test before this one.
            var backup = Path.Combine(root, "0.11-backup.json");
            await File.WriteAllTextAsync(backup, """
                {"general":{"autoClose":true,"topMost":true,"colButtonCount":6},"folders":[],"rootFolder":"C:\\"}
                """);

            Assert.True(await svc.RestoreFromAsync(backup));

            Assert.False(svc.Config.General.AutoClose);
            Assert.False(svc.Config.General.TopMost);
            Assert.Equal(AppConfig.CurrentSchemaVersion, svc.Config.General.SchemaVersion);
            Assert.Equal(6, svc.Config.General.ColButtonCount);  // untouched by the migration
            Assert.Equal(@"C:\", svc.Config.RootFolder);         // the restore itself still happened

            // ...and the migrated values are what a fresh process reads back, not just what is in memory.
            var reload = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await reload.LoadAsync();
            Assert.False(reload.Config.General.AutoClose);
            Assert.False(reload.Config.General.TopMost);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsWindowPlacement()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc1 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc1.LoadAsync();
            svc1.Config.General.WindowWidth = 1234;
            svc1.Config.General.WindowHeight = 777;
            svc1.Config.General.WindowLeft = 42;
            svc1.Config.General.WindowTop = 24;
            svc1.Config.General.WindowMaximized = true;
            svc1.Config.General.TaskbarPinPromptDismissed = true;
            await svc1.SaveAsync();

            var svc2 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc2.LoadAsync();

            Assert.Equal(1234, svc2.Config.General.WindowWidth);
            Assert.Equal(777, svc2.Config.General.WindowHeight);
            Assert.Equal(42, svc2.Config.General.WindowLeft);
            Assert.Equal(24, svc2.Config.General.WindowTop);
            Assert.True(svc2.Config.General.WindowMaximized);
            Assert.True(svc2.Config.General.TaskbarPinPromptDismissed);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task SaveThenLoad_KeepsNegativeWindowPositionFromALeftHandMonitor()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc1 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc1.LoadAsync();
            svc1.Config.General.WindowLeft = -1500;
            svc1.Config.General.WindowTop = 120;
            await svc1.SaveAsync();

            var svc2 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc2.LoadAsync();

            Assert.Equal(-1500, svc2.Config.General.WindowLeft);
            Assert.Equal(120, svc2.Config.General.WindowTop);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
