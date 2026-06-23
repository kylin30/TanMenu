using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Registers a CONFIGURABLE global hotkey (off by default) on the launcher window via Win32
/// RegisterHotKey, and toggles the launcher on WM_HOTKEY. Re-applies whenever settings change,
/// so enabling/disabling or re-binding the hotkey takes effect immediately.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0xB001;
    public const int WM_HOTKEY = 0x0312;

    private readonly ConfigService _config;
    private readonly IWindowHost _host;
    private readonly AppEvents _events;
    private readonly ILogger<GlobalHotkeyService> _logger;

    private IntPtr _hwnd;
    private bool _registered;
    private string? _currentCombo; // last combo successfully registered (restored on a failed rebind)

    /// <summary>Raised (with the offending combo) when a requested hotkey can't be registered, e.g.
    /// it's already owned by another app — so the settings UI can warn the user.</summary>
    public event Action<string>? RegistrationFailed;

    public GlobalHotkeyService(ConfigService config, IWindowHost host, AppEvents events,
        ILogger<GlobalHotkeyService> logger)
    {
        _config = config;
        _host = host;
        _events = events;
        _logger = logger;
        _events.SettingsChanged += Apply; // re-register when the user changes the hotkey/toggle
    }

    /// <summary>Bind to the launcher window handle (once available) and register from config.</summary>
    public void Attach(IntPtr hwnd)
    {
        _hwnd = hwnd;
        Apply();
    }

    /// <summary>(Re)register the hotkey from the current config; safe to call repeatedly.</summary>
    public void Apply()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        var g = _config.Config.General;
        var want = g.GlobalHotkeyEnabled && !string.IsNullOrWhiteSpace(g.GlobalHotkey);

        Unregister();
        if (!want)
        {
            _currentCombo = null;
            return;
        }

        if (TryRegister(g.GlobalHotkey))
        {
            _currentCombo = g.GlobalHotkey;
            _logger.LogInformation("已注册全局热键: {Hotkey}", g.GlobalHotkey);
            return;
        }

        // Occupied/invalid — restore the previously-working hotkey instead of silently dropping it,
        // and surface the failure so the user isn't left with no hotkey and only a log line.
        _logger.LogWarning("注册全局热键失败(可能已被占用或无效): {Hotkey}", g.GlobalHotkey);
        if (_currentCombo != null)
            TryRegister(_currentCombo);
        RegistrationFailed?.Invoke(g.GlobalHotkey);
    }

    private bool TryRegister(string combo)
    {
        if (!HotkeyParser.TryParse(combo, out var mods, out var vk))
            return false;
        if (RegisterHotKey(_hwnd, HotkeyId, mods | HotkeyParser.MOD_NOREPEAT, vk))
        {
            _registered = true;
            return true;
        }
        return false;
    }

    /// <summary>Call from the window's WndProc — toggles the launcher if it's our hotkey message.</summary>
    public bool ProcessMessage(int msg, IntPtr wParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _host.Toggle();
            return true;
        }
        return false;
    }

    private void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        _events.SettingsChanged -= Apply;
        Unregister();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
