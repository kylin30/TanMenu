using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>System-tray icon: left-click toggles the launcher; right-click menu = 显示 / 设置 / 退出.</summary>
public sealed class TrayService : IDisposable
{
    /// <summary>Embedded copy of the app icon. Preferred over the loose <c>&lt;exe&gt;\app.ico</c>
    /// because it cannot be deleted, locked by AV, or missed by a partial copy — the tray icon is the
    /// primary way to re-summon a hidden launcher, so it should not depend on the filesystem.</summary>
    private const string EmbeddedIconUri = "avares://TanMenu.Wpf/wwwroot/app.ico";

    private readonly IWindowHost _host;
    private readonly Action _exit;
    private readonly Action _openSettings;
    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private TrayIcon? _icon;
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _settingsItem;
    private NativeMenuItem? _exitItem;

    public TrayService(IWindowHost host, Action exit, Action openSettings, ConfigService config, AppEvents events)
    {
        _host = host;
        _exit = exit;
        _openSettings = openSettings;
        _config = config;
        _events = events;
        _events.SettingsChanged += ApplyMenuText;
    }

    public void Create()
    {
        _showItem = new NativeMenuItem();
        _showItem.Click += (_, _) => _host.ShowAndActivate();
        _settingsItem = new NativeMenuItem();
        _settingsItem.Click += (_, _) => _openSettings();
        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => _exit();
        ApplyMenuText();

        var menu = new NativeMenu();
        menu.Items.Add(_showItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_exitItem);

        _icon = new TrayIcon
        {
            ToolTipText = "TanMenu", // deliberately not localized
            Menu = menu,
            IsVisible = true,
        };
        // A single left click toggles immediately — there is no double-click behavior to wait for.
        _icon.Clicked += (_, _) => _host.Toggle();
        ApplyIcon();

        // Registering the icons on the Application is what actually creates the shell notify icon.
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
    }

    private void ApplyIcon()
    {
        if (_icon is null)
            return;

        try
        {
            using var embedded = AssetLoader.Open(new Uri(EmbeddedIconUri));
            _icon.Icon = new WindowIcon(embedded);
            return;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load the embedded tray icon {Uri}", EmbeddedIconUri);
        }

        // Fall back to the copy the build drops next to the exe (it exists for Velopack's shortcut
        // creation anyway). A corrupt/locked .ico must not take down the whole tray — leave a blank
        // icon; the menu still works.
        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        try
        {
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                _icon.Icon = new WindowIcon(file);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to set tray icon from {Path}", path);
        }
    }

    /// <summary>Re-read the menu labels from config. Called once from <see cref="Create"/> and again
    /// on every <see cref="AppEvents.SettingsChanged"/>, so a language change updates the tray menu
    /// live with no restart.</summary>
    private void ApplyMenuText()
    {
        try
        {
            var language = _config.Config.General.Language;
            if (_showItem != null) _showItem.Header = AppLanguage.Text("TrayShow", language);
            if (_settingsItem != null) _settingsItem.Header = AppLanguage.Text("TraySettings", language);
            if (_exitItem != null) _exitItem.Header = AppLanguage.Text("TrayExit", language);
        }
        catch { /* best effort; tray text is non-critical */ }
    }

    public void Dispose()
    {
        _events.SettingsChanged -= ApplyMenuText;
        if (_icon is not null)
        {
            _icon.IsVisible = false;
            _icon.Dispose();
            _icon = null;
        }
        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, new TrayIcons());
    }
}
