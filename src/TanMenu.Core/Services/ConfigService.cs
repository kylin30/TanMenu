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
                        _logger.LogInformation(
                            "Config loaded: window {Width}x{Height} at ({X}, {Y}), {FolderCount} folders",
                            Config.Window.Width, Config.Window.Height, Config.Window.X, Config.Window.Y,
                            Config.Folders.Count);
                        return;
                    }
                }

                _logger.LogWarning("Config file was empty/invalid; using defaults");
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
            Config.Window.LastModified = DateTime.Now;

            var jsonContent = JsonSerializer.Serialize(Config, _jsonOptions);
            await File.WriteAllTextAsync(_configPath, jsonContent);
            HasValidConfig = true;

            _logger.LogInformation("Config saved: window {Width}x{Height} at ({X}, {Y})",
                Config.Window.Width, Config.Window.Height, Config.Window.X, Config.Window.Y);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config");
            throw;
        }
    }

    public void UpdateWindowConfig(int width, int height, int x, int y)
    {
        Config.Window.Width = width;
        Config.Window.Height = height;
        Config.Window.X = x;
        Config.Window.Y = y;
        Config.Window.LastModified = DateTime.Now;

        _logger.LogInformation("Window config updated -> ({Width}, {Height}, {X}, {Y})", width, height, x, y);
    }

    public bool ShouldUpdateWindow(int currentWidth, int currentHeight, int currentX, int currentY)
    {
        var tolerance = Config.General.Tolerance;

        var widthDiff = Math.Abs(Config.Window.Width - currentWidth);
        var heightDiff = Math.Abs(Config.Window.Height - currentHeight);
        var xDiff = Math.Abs(Config.Window.X - currentX);
        var yDiff = Math.Abs(Config.Window.Y - currentY);

        var totalDiff = widthDiff + heightDiff + xDiff + yDiff;
        var shouldUpdate = totalDiff > tolerance;

        if (shouldUpdate)
        {
            _logger.LogInformation(
                "Window change detected - total diff {TotalDiff}px (tolerance {Tolerance}px)",
                totalDiff, tolerance);
        }

        return shouldUpdate;
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
