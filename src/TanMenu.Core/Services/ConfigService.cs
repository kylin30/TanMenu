using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;
using TanMenu.Core.Serialization;

namespace TanMenu.Core.Services;

public class ConfigService
{
    private readonly IAppDataPaths _paths;
    private readonly ILogger<ConfigService> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1); // serialize SaveAsync: callers share one .tmp path

    /// <summary>The on-disk contract for config.json (camelCase, indented, nulls omitted).
    /// See <see cref="ConfigJsonContext"/> — changing it breaks existing users' settings.</summary>
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<AppConfig> ConfigType =
        ConfigJsonContext.Default.AppConfig;

    /// <summary>Resolved fresh each access so a runtime data-folder change is picked up immediately.</summary>
    private string _configPath => _paths.ConfigFilePath;

    public AppConfig Config { get; private set; }
    public bool HasValidConfig { get; private set; }

    public ConfigService(IAppDataPaths paths, ILogger<ConfigService> logger)
    {
        _paths = paths;
        _logger = logger;

        Config = new AppConfig();
        HasValidConfig = false;

        _logger.LogInformation("Config path: {ConfigPath}", _configPath);
    }

    // NOTE: CloneConfig() lived here until 0.12. Its only consumer was SettingsWindow's staged-edit
    // model (mutate a clone, apply it on 确定/应用), and the settings PAGE that replaced that window
    // wrote through to Config immediately — so the clone had no caller left. It was deleted rather
    // than kept "in case", on the grounds that a deep-clone helper on the live config service invites
    // exactly the staged-edit workflow the redesign removed.
    //
    // ROUND 8, TASK 1 RESTORED THAT WORKFLOW — the settings surface is a delayed commit again — and
    // this helper still does not come back. The clone belongs to the surface that stages the edit
    // (TanMenu.Wpf.Views.Settings.SettingsDraft, a JSON round-trip through the same source-generated
    // JsonTypeInfo SaveAsync uses) rather than to the live config service, and the commit writes a
    // NAMED LIST of properties rather than swapping the object — which is what keeps a concurrent
    // WindowHost.SavePlacement write to General.WindowMaximized from being reverted.

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var jsonContent = await File.ReadAllTextAsync(_configPath);

                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    var loadedConfig = JsonSerializer.Deserialize(jsonContent, ConfigType);
                    if (loadedConfig != null)
                    {
                        Config = loadedConfig;
                        Normalize(Config);
                        HasValidConfig = true;
                        _logger.LogInformation("Config loaded: {FolderCount} folders, root '{Root}'",
                            Config.Folders.Count, Config.RootFolder);
                        return;
                    }
                }

                // File existed but was empty/whitespace/null-deserialized — back it up BEFORE
                // overwriting with defaults so the user's setup is never silently discarded.
                _logger.LogWarning("Config file was empty/invalid; backing up and using defaults");
                BackupCorruptedConfig();
            }
            else
            {
                _logger.LogInformation("Config file not found; using defaults");
            }

            Config = new AppConfig();
            HasValidConfig = false;
            await SaveAsync();
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Config JSON format error");
            await HandleCorruptedConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config");
            await HandleCorruptedConfig();
        }
    }

    /// <summary>Canonicalize a freshly loaded config so every consumer sees one set of values instead
    /// of each having to special-case history.</summary>
    private static void Normalize(AppConfig config)
    {
        // A hand-edited or truncated file can carry `{"general": null}` (or a null folders/tools
        // array). That used to load with HasValidConfig = true and then NullReference on the first
        // read — RestoreFromAsync rejects the shape, but LoadAsync sailed straight past it.
        config.General ??= new GeneralConfig();
        config.Folders ??= [];
        config.General.DefaultTools ??= DefaultTool.BuiltInDefaults();

        config.General.Language = AppLanguage.NormalizeSetting(config.General.Language);
        config.General.ThemeName = AppConfig.CanonicalTheme;
        config.General.Appearance = AppAppearance.NormalizeSetting(config.General.Appearance);

        // The four bundled TTFs went away with the Fluent 2 rebuild. Left alone, a config naming one
        // would ask the text stack for a family that does not exist and silently get an arbitrary
        // substitute; "" means the app's own default, which is what the user now actually gets.
        if (AppAppearance.IsRetiredFontName(config.General.FontFamily))
            config.General.FontFamily = "";

        // Schema 1: the 0.11 flyout wrote AutoClose/TopMost = true because it hid on blur and floated
        // above everything. This build is a taskbar-resident application window, where both are wrong.
        // A brand-new config also arrives here with SchemaVersion 0, which is harmless: its AutoClose
        // and TopMost already ARE false, so the migration only stamps the version.
        if (config.General.SchemaVersion < AppConfig.CurrentSchemaVersion)
        {
            config.General.AutoClose = false;
            config.General.TopMost = false;
            config.General.SchemaVersion = AppConfig.CurrentSchemaVersion;
        }
    }

    /// <remarks>
    /// EVERY await inside this method is <c>ConfigureAwait(false)</c>, which is load-bearing rather
    /// than stylistic: <c>App.ExitApp</c> BLOCKS the UI thread on this task so 退出 cannot lose a
    /// placement change still inside the debounce window (see <c>WindowHost.FlushPlacementBlocking</c>).
    /// If the continuations resumed on the captured UI context they would be queued behind that block
    /// and <c>_saveLock</c> would never be released — a deadlock on exit. Nothing in here touches the
    /// UI, so there is no context worth capturing; the caller's own await still resumes on its own
    /// context, which is why <c>SettingsCommit.ApplyAsync</c> (and <c>SettingsWrite.CommitAsync</c>,
    /// still live for the taskbar-pin notice) can raise SettingsChanged on the UI thread afterwards.
    /// </remarks>
    public async Task SaveAsync()
    {
        // Serialize concurrent saves: all callers write the same _configPath+".tmp", so two overlapping
        // saves (e.g. two settings cards committing back to back) would otherwise race on that temp file.
        await _saveLock.WaitAsync().ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            _paths.EnsureCreated();

            var jsonContent = JsonSerializer.Serialize(Config, ConfigType);

            // Atomic write: serialize to a temp file in the same folder, then swap it into place,
            // so a crash mid-write can never leave a truncated/empty config.json behind.
            tempPath = _configPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, jsonContent).ConfigureAwait(false);
            try
            {
                if (File.Exists(_configPath))
                    File.Replace(tempPath, _configPath, destinationBackupFileName: null);
                else
                    File.Move(tempPath, _configPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // File.Replace (and some File.Move cases) are unsupported on FAT32/exFAT and some SMB
                // shares — volumes the data folder can be relocated onto. Fall back to a plain in-place
                // overwrite so settings still persist there; it's less crash-safe than the atomic swap,
                // but BackupToAsync already accepts exactly this tradeoff for portable targets. (On failure
                // here File.Replace leaves the original config.json intact, so the overwrite is safe.)
                _logger.LogWarning(ex, "Atomic config swap unsupported here; falling back to a plain write");
                await File.WriteAllTextAsync(_configPath, jsonContent).ConfigureAwait(false);
            }

            HasValidConfig = true;
            _logger.LogInformation("Config saved ({FolderCount} folders)", Config.Folders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config");
            throw;
        }
        finally
        {
            // The atomic swap consumes the temp; the fallback write (and any failure) can leave it behind.
            if (tempPath != null)
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* harmless leftover */ }
            _saveLock.Release();
        }
    }

    /// <summary>Write the current config to <paramref name="destPath"/> (a user-chosen backup file).</summary>
    public async Task BackupToAsync(string destPath)
    {
        // Plain write (NOT the atomic temp-swap SaveAsync uses). The backup target is a user-chosen,
        // often external/removable path (USB/FAT32/exFAT/network/cloud-synced), where File.Replace can
        // throw (it's unsupported on FAT32/exFAT and some SMB shares) or strand a leftover .tmp. A backup
        // is a throwaway copy — a crash mid-write just means re-running 备份 — so a plain write is both
        // simpler and more portable than risking the atomic path on an arbitrary destination.
        var json = JsonSerializer.Serialize(Config, ConfigType);
        await File.WriteAllTextAsync(destPath, json);
    }

    /// <summary>Replace the live config with one read from <paramref name="srcPath"/> (a backup),
    /// validating it first. Returns false and changes nothing if the file isn't a valid config.</summary>
    public async Task<bool> RestoreFromAsync(string srcPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(srcPath);

            // Validate it's actually a TanMenu config, not just any parseable JSON: require a NON-EMPTY
            // "general" OBJECT AND a top-level "rootFolder" field. Both checks matter: without the
            // non-empty general check {"general":{}} deserializes to an all-defaults AppConfig; and
            // without the rootFolder check a partial/foreign file that has a general but omits rootFolder
            // would silently inherit RootFolder's default (now the Desktop), exposing every desktop folder
            // as a group. TanMenu-written backups always serialize both. Deserialize straight from THIS
            // document so the JSON is parsed only once (no separate JsonSerializer.Deserialize(json) pass).
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("general", out var general) ||
                general.ValueKind != JsonValueKind.Object ||
                !general.EnumerateObject().MoveNext() ||
                !root.TryGetProperty("rootFolder", out var rf) ||
                rf.ValueKind == JsonValueKind.Null) // an explicit null rootFolder is not a meaningful value
                return false;

            var restored = JsonSerializer.Deserialize(root, ConfigType);
            if (restored?.General is null)
                return false;

            // Back up the config we're about to overwrite, so a mistaken restore is recoverable.
            var overwriteBackup = BackupCorruptedConfig();
            Normalize(restored);

            // Swap the live config in, but roll back if the save fails: we must not keep running the
            // restored config in memory while reporting failure (HalfApplied state). On a SaveAsync throw
            // (disk full/locked) restore the previous config so the live state matches the returned false.
            var previous = Config;
            var previousValid = HasValidConfig;
            Config = restored;
            HasValidConfig = true;
            try
            {
                await SaveAsync();
            }
            catch
            {
                Config = previous;              // roll back the in-memory swap to match the reported failure
                HasValidConfig = previousValid; // ...including HasValidConfig, so the rollback is complete
                // The overwrite never took effect (save failed, rolled back), so the just-taken backup is
                // pointless — delete it rather than leave an orphan timestamped copy in the data folder.
                if (overwriteBackup != null)
                    try { File.Delete(overwriteBackup); } catch { /* best effort */ }
                throw;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore config from {Path}", srcPath);
            return false;
        }
    }

    private async Task HandleCorruptedConfig()
    {
        BackupCorruptedConfig();
        Config = new AppConfig();
        HasValidConfig = false;

        try
        {
            await SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create replacement config");
        }
    }

    /// <summary>Copy the current config aside as a timestamped <c>.backup</c>. Returns the backup path,
    /// or null if there was nothing to back up or the copy failed.</summary>
    private string? BackupCorruptedConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var backupPath = $"{_configPath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(_configPath, backupPath);
                _logger.LogInformation("Backed up corrupted config to {BackupPath}", backupPath);
                return backupPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to back up corrupted config");
        }
        return null;
    }
}
