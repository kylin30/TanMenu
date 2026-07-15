using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Models;

namespace TanMenu.Core.Services;

public class ConfigService
{
    private readonly IAppDataPaths _paths;
    private readonly ILogger<ConfigService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _saveLock = new(1, 1); // serialize SaveAsync: callers share one .tmp path

    /// <summary>Resolved fresh each access so a runtime data-folder change is picked up immediately.</summary>
    private string _configPath => _paths.ConfigFilePath;

    public AppConfig Config { get; private set; }
    public bool HasValidConfig { get; private set; }

    public ConfigService(IAppDataPaths paths, ILogger<ConfigService> logger)
    {
        _paths = paths;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Config = new AppConfig();
        HasValidConfig = false;

        _logger.LogInformation("Config path: {ConfigPath}", _configPath);
    }

    /// <summary>Deep clone of the current config — used by the settings window's edit-then-apply
    /// workflow so changes only take effect on "应用/确定", not immediately.</summary>
    public AppConfig CloneConfig() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(Config, _jsonOptions), _jsonOptions)
        ?? new AppConfig();

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var jsonContent = await File.ReadAllTextAsync(_configPath);

                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    var loadedConfig = JsonSerializer.Deserialize<AppConfig>(jsonContent, _jsonOptions);
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

    /// <summary>Canonicalize legacy/renamed config values once on load, so every consumer sees one set of
    /// keys instead of each having to special-case the old value. "Fluent2" was renamed to "Windows11"
    /// (commit b0f41e5); the removed classic themes "Win98"/"Win31"/"Win2000" all map to the default
    /// "Win7".</summary>
    private static void Normalize(AppConfig config)
    {
        if (config.General is null)
            return;
        config.General.Language = AppLanguage.NormalizeSetting(config.General.Language);
        if (string.Equals(config.General.ThemeName, "Fluent2", StringComparison.OrdinalIgnoreCase))
            config.General.ThemeName = "Windows11";
        // The removed classic themes ("Win98" and the interim "Win31"/"Win2000") migrate to the default
        // "Win7", so the settings dropdown shows a valid selection (ResolveTheme also falls back to Win7).
        if (string.Equals(config.General.ThemeName, "Win98", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(config.General.ThemeName, "Win31", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(config.General.ThemeName, "Win2000", StringComparison.OrdinalIgnoreCase))
            config.General.ThemeName = "Win7";
    }

    public async Task SaveAsync()
    {
        // Serialize concurrent saves: all callers write the same _configPath+".tmp", so two overlapping
        // saves (e.g. a double-click on 应用/确定) would otherwise race on that temp file.
        await _saveLock.WaitAsync();
        string? tempPath = null;
        try
        {
            _paths.EnsureCreated();

            var jsonContent = JsonSerializer.Serialize(Config, _jsonOptions);

            // Atomic write: serialize to a temp file in the same folder, then swap it into place,
            // so a crash mid-write can never leave a truncated/empty config.json behind.
            tempPath = _configPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, jsonContent);
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
                await File.WriteAllTextAsync(_configPath, jsonContent);
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
        var json = JsonSerializer.Serialize(Config, _jsonOptions);
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

            var restored = root.Deserialize<AppConfig>(_jsonOptions);
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
