using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 外观 card group: language, light/dark appearance, UI font and button size. Ported from
/// <c>SettingsWindow</c>'s 外观 tab, minus the staging: every handler here writes straight to
/// <see cref="ConfigService.Config"/>, saves, and raises <see cref="AppEvents.SettingsChanged"/>
/// instead of mutating a working clone for a later Apply.
/// </summary>
public partial class AppearanceCard : UserControl
{
    private static readonly string[] LanguageSettings = { AppLanguage.Auto, AppLanguage.ZhHans, AppLanguage.EnUs };

    private static readonly string[] AppearanceSettings =
        { AppAppearance.System, AppAppearance.Light, AppAppearance.Dark };

    private static readonly string[] ButtonSizes = { "Small", "Medium", "Large" };

    /// <summary>One row of the font dropdown. <see cref="Family"/> is the value written to config
    /// ("" = the app's own default font); <see cref="IsHeader"/> marks the group caption, which is
    /// rendered as a row because Avalonia's ComboBox has no GroupStyle.</summary>
    private sealed record FontItem(string Name, string Family, bool IsHeader);

    /// <summary>#777777 — the group-header grey the WPF GroupStyle header template used.</summary>
    private static readonly IBrush GroupHeaderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

    /// <summary>The default sentinel, then every installed system font family, sorted. The 内置字体
    /// group is gone with the bundled TTFs — every selectable family is now an installed one.</summary>
    private static List<FontItem> BuildFontItems(string? language)
    {
        var items = new List<FontItem>
        {
            new(AppLanguage.Text("DefaultFont", language), "", false),
            new(AppLanguage.Text("SystemFonts", language), "", true),
        };
        foreach (var fam in FontManager.Current.SystemFonts
                     .Select(f => f.Name)
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct()
                     .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(new FontItem(fam, fam, false));
        }
        return items;
    }

    private readonly ConfigService _config;
    private readonly AppEvents _events;

    /// <summary>Guards every SelectionChanged handler against the selections ReloadFromConfig makes
    /// while it repopulates the combos — without it, populating the Language combo would write the
    /// freshly-loaded value straight back to config as if the user had picked it.</summary>
    private bool _loading;

    /// <summary>The font row currently selected, so a click on a group header can be bounced back to it.</summary>
    private ComboBoxItem? _selectedFontRow;
    private bool _fontSelectionGuard;

    public AppearanceCard()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
        _events = App.Services.GetRequiredService<AppEvents>();
    }

    /// <summary>Populate every control from the live config. Sets a guard so the SelectionChanged
    /// handlers do not write back the values they are being given.</summary>
    public void ReloadFromConfig()
    {
        _loading = true;
        try
        {
            var g = _config.Config.General;
            var lang = g.Language;

            GroupHead.Text = AppLanguage.Text("Appearance", lang);

            LanguageTitle.Text = AppLanguage.Text("Language", lang);
            LanguageSub.Text = AppLanguage.Text("LanguageCardSub", lang);
            LanguageCombo.ItemsSource = LanguageSettings.Select(s => AppLanguage.LanguageLabel(s, lang)).ToList();
            var langIdx = Array.FindIndex(LanguageSettings,
                s => string.Equals(s, AppLanguage.NormalizeSetting(g.Language), StringComparison.OrdinalIgnoreCase));
            LanguageCombo.SelectedIndex = langIdx < 0 ? 0 : langIdx;

            AppearanceTitle.Text = AppLanguage.Text("AppearanceMode", lang);
            AppearanceSub.Text = AppLanguage.Text("AppearanceCardSub", lang);
            AppearanceCombo.ItemsSource = AppearanceSettings.Select(s => AppearanceLabelFor(s, lang)).ToList();
            var appearanceIdx = Array.FindIndex(AppearanceSettings,
                s => string.Equals(s, g.Appearance, StringComparison.OrdinalIgnoreCase));
            AppearanceCombo.SelectedIndex = appearanceIdx < 0 ? 0 : appearanceIdx;

            FontTitle.Text = AppLanguage.Text("Font", lang);
            FontSub.Text = AppLanguage.Text("FontCardSub", lang);
            PopulateFontCombo(g.FontFamily ?? string.Empty, lang);

            SizeTitle.Text = AppLanguage.Text("ButtonSize", lang);
            SizeSub.Text = AppLanguage.Text("SizeCardSub", lang);
            SizeCombo.ItemsSource = ButtonSizes.Select(s => SizeLabel(s, lang)).ToList();
            var sizeIdx = Array.FindIndex(ButtonSizes, s => s == g.ButtonSize);
            SizeCombo.SelectedIndex = sizeIdx < 0 ? 0 : sizeIdx; // default = 小 (Small)
        }
        finally
        {
            _loading = false;
        }
    }

    private static string SizeLabel(string key, string? language) => key switch
    {
        "Large" => AppLanguage.Text("SizeLarge", language),
        "Medium" => AppLanguage.Text("SizeMedium", language),
        _ => AppLanguage.Text("SizeSmall", language),
    };

    private static string AppearanceLabelFor(string setting, string? language) =>
        AppAppearance.NormalizeSetting(setting) switch
        {
            AppAppearance.Light => AppLanguage.Text("AppearanceLight", language),
            AppAppearance.Dark => AppLanguage.Text("AppearanceDark", language),
            _ => AppLanguage.Text("AppearanceSystem", language),
        };

    /// <summary>Rebuild the font dropdown and restore the selection for <paramref name="selectedFamily"/>.
    /// No match (an uninstalled configured font) deliberately leaves the combo blank without touching
    /// the stored value.</summary>
    private void PopulateFontCombo(string selectedFamily, string? language)
    {
        FontCombo.Items.Clear();
        _selectedFontRow = null;

        ComboBoxItem? match = null;
        foreach (var item in BuildFontItems(language))
        {
            var row = new ComboBoxItem { Content = item.Name, Tag = item };
            if (item.IsHeader)
            {
                // Avalonia's ComboBox has no GroupStyle, so group captions are ordinary rows made
                // non-selectable: disabled here, and bounced back in OnFontChanged in case a keyboard
                // navigation ever lands on one anyway.
                row.IsEnabled = false;
                row.FontWeight = FontWeight.Bold;
                row.Foreground = GroupHeaderBrush;
                row.Margin = new Thickness(0, 3, 0, 0);
            }
            else if (match is null && string.Equals(item.Family, selectedFamily, StringComparison.OrdinalIgnoreCase))
            {
                match = row;
            }
            FontCombo.Items.Add(row);
        }

        FontCombo.SelectedItem = match;
        _selectedFontRow = match;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e) => RunSafely(async () =>
    {
        if (_loading || LanguageCombo.SelectedIndex < 0)
            return;

        _config.Config.General.Language = LanguageSettings[LanguageCombo.SelectedIndex];
        await _config.SaveAsync();
        _events.RaiseSettingsChanged();
    });

    /// <summary>The whole app repaints live, before the write even reaches disk: the dropdown is a
    /// preview of a whole-app appearance, and waiting for the save to finish would leave the user
    /// choosing 深色 while still looking at a light window.</summary>
    private void OnAppearanceChanged(object? sender, SelectionChangedEventArgs e) => RunSafely(async () =>
    {
        if (_loading || AppearanceCombo.SelectedIndex < 0)
            return;

        _config.Config.General.Appearance = AppearanceSettings[AppearanceCombo.SelectedIndex];
        if (Application.Current is { } app)
            AppTheme.Apply(app, _config.Config.General.Appearance);
        await _config.SaveAsync();
        _events.RaiseSettingsChanged();
    });

    private void OnFontChanged(object? sender, SelectionChangedEventArgs e) => RunSafely(async () =>
    {
        if (_fontSelectionGuard)
            return;

        var row = FontCombo.SelectedItem as ComboBoxItem;
        if (row?.Tag is FontItem { IsHeader: true })
        {
            // A group caption is a label, never a choice.
            _fontSelectionGuard = true;
            try { FontCombo.SelectedItem = _selectedFontRow; }
            finally { _fontSelectionGuard = false; }
            return;
        }

        _selectedFontRow = row;
        if (_loading || row?.Tag is not FontItem item)
            return;

        var fam = FontUtil.Sanitize(item.Family); // "" for the default-font entry
        if (fam == _config.Config.General.FontFamily)
            return;

        _config.Config.General.FontFamily = fam;
        await _config.SaveAsync();
        _events.RaiseSettingsChanged();
    });

    private void OnSizeChanged(object? sender, SelectionChangedEventArgs e) => RunSafely(async () =>
    {
        if (_loading || SizeCombo.SelectedIndex < 0)
            return;

        _config.Config.General.ButtonSize = ButtonSizes[SizeCombo.SelectedIndex];
        await _config.SaveAsync();
        _events.RaiseSettingsChanged();
    });

    /// <summary>Run an async change handler without letting a fault escape the SelectionChanged event —
    /// same rationale as <c>SettingsWindow.RunSafely</c>: Avalonia has no WPF-style global
    /// DispatcherUnhandledException hook, so an escaping async-void fault would tear the process down,
    /// and logging is the floor.</summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "外观设置写入失败"); }
    }
}
