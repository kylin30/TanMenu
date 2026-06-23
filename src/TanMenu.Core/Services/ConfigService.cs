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

    public async Task SaveAsync()
    {
        try
        {
            _paths.EnsureCreated();

            var jsonContent = JsonSerializer.Serialize(Config, _jsonOptions);

            // Atomic write: serialize to a temp file in the same folder, then swap it into place,
            // so a crash mid-write can never leave a truncated/empty config.json behind.
            var tempPath = _configPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, jsonContent);
            if (File.Exists(_configPath))
                File.Replace(tempPath, _configPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _configPath);

            HasValidConfig = true;
            _logger.LogInformation("Config saved ({FolderCount} folders)", Config.Folders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config");
            throw;
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

    private void BackupCorruptedConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var backupPath = $"{_configPath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(_configPath, backupPath);
                _logger.LogInformation("Backed up corrupted config to {BackupPath}", backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to back up corrupted config");
        }
    }
}
