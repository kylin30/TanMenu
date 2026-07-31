using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views;

/// <summary>The 设置 page. Owns nothing but layout: each card group reads and writes config itself, so
/// adding a group is adding one control here.</summary>
public partial class SettingsPage : UserControl, IDisposable
{
    private readonly AppEvents _events;

    public SettingsPage()
    {
        InitializeComponent();
        _events = App.Services.GetRequiredService<AppEvents>();

        // AppLanguage has no change notification, and a card's own change handler has no reference
        // back to this page (each card only knows ConfigService/AppEvents), so re-labelling after a
        // language change goes through the same coarse SettingsChanged signal every other listener in
        // the app uses (MainWindow's rail, TrayService, ...).
        _events.SettingsChanged += ApplyStaticText;

        ApplyStaticText();
    }

    /// <summary>AppLanguage has no change notification, so labels are applied imperatively. A language
    /// change calls this and every group's own ReloadFromConfig.</summary>
    public void ApplyStaticText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        PageTitle.Text = AppLanguage.Text("NavSettings", lang);
        Appearance.ReloadFromConfig();
    }

    /// <summary>Unsubscribe from the shared AppEvents singleton. Called from
    /// <c>MainWindow.OnClosed</c> — the same real-exit-only path that disposes <c>AppPage</c> — so this
    /// page's subscription does not outlive the window that owns it.</summary>
    public void Dispose() => _events.SettingsChanged -= ApplyStaticText;
}
