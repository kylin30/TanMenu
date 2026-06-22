using System.Collections.Concurrent;
using System.IO;
using System.Media;
using Microsoft.Extensions.Logging;

namespace TanMenu.Core.Services;

public sealed class SoundService : IDisposable
{
    private readonly ILogger<SoundService> _logger;
    private readonly ConcurrentDictionary<string, SoundPlayer> _soundPlayers = new();
    private readonly SemaphoreSlim _clickSemaphore = new(1, 1);
    private readonly SemaphoreSlim _hoverSemaphore = new(1, 1);
    private DateTime _lastClickTime = DateTime.MinValue;
    private DateTime _lastHoverTime = DateTime.MinValue;
    private bool _isInitialized;
    private bool _disposed;

    private const int ClickThrottleMs = 20;
    private const int HoverThrottleMs = 50;

    public SoundService(ILogger<SoundService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads win9x-click.wav and win9x-hover.wav from the given base directory
    /// (e.g. the packaged install dir's Assets\sounds folder).
    /// Missing files are tolerated — IsReady will be false if neither wav loads.
    /// Safe to call only once; subsequent calls on an already-initialized instance are no-ops.
    /// </summary>
    public void Initialize(string soundsBaseDir)
    {
        if (_isInitialized || _disposed) return;

        try
        {
            _logger.LogInformation("Initializing sound service from {SoundsBaseDir}", soundsBaseDir);
            LoadSound("click", Path.Combine(soundsBaseDir, "win9x-click.wav"));
            LoadSound("hover", Path.Combine(soundsBaseDir, "win9x-hover.wav"));
            _isInitialized = true;
            _logger.LogInformation("Sound service initialized with {Count} sounds loaded", _soundPlayers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sound service initialization failed");
        }
    }

    private void LoadSound(string key, string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                var player = new SoundPlayer(fullPath);
                player.LoadAsync();
                _soundPlayers[key] = player;
                _logger.LogInformation("Loaded sound {Key} <- {FullPath}", key, fullPath);
            }
            else
            {
                _logger.LogWarning("Sound file not found: {FullPath}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sound {Key}", key);
        }
    }

    public async Task PlayClickSoundAsync()
    {
        var now = DateTime.Now;
        if ((now - _lastClickTime).TotalMilliseconds < ClickThrottleMs) return;
        if (!await _clickSemaphore.WaitAsync(0)) return;
        try
        {
            _lastClickTime = now;
            await PlaySoundAsync("click");
        }
        finally { _clickSemaphore.Release(); }
    }

    public async Task PlayHoverSoundAsync()
    {
        var now = DateTime.Now;
        if ((now - _lastHoverTime).TotalMilliseconds < HoverThrottleMs) return;
        if (!await _hoverSemaphore.WaitAsync(0)) return;
        try
        {
            _lastHoverTime = now;
            await PlaySoundAsync("hover");
        }
        finally { _hoverSemaphore.Release(); }
    }

    private async Task PlaySoundAsync(string soundKey)
    {
        if (_disposed || !_isInitialized) return;

        try
        {
            if (_soundPlayers.TryGetValue(soundKey, out var player))
            {
                await Task.Run(() =>
                {
                    try { player.Play(); }
                    catch (Exception ex) { _logger.LogError(ex, "Error playing sound {Key}", soundKey); }
                });
            }
            else
            {
                _logger.LogWarning("Sound not found: {Key}", soundKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play sound {Key}", soundKey);
        }
    }

    /// <summary>True when Initialize has been called and at least one wav loaded successfully.</summary>
    public bool IsReady => _isInitialized && !_disposed && _soundPlayers.Count > 0;

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            foreach (var kvp in _soundPlayers) kvp.Value?.Dispose();
            _soundPlayers.Clear();
            _clickSemaphore.Dispose();
            _hoverSemaphore.Dispose();
            _disposed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing sound service");
        }
    }
}
