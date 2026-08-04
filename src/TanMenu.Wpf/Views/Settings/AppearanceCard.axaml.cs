using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 外观 card group: language, light/dark appearance, UI font, and (since Round 3, Task 4) items per
/// column. Ported from <c>SettingsWindow</c>'s 外观 tab, minus the staging: every handler here writes
/// straight to <see cref="ConfigService.Config"/>, then hands the save off to
/// <see cref="SettingsWrite.CommitAsync"/>, which raises <see cref="AppEvents.SettingsChanged"/> on
/// success or rolls the field (and, for Appearance, the live repaint) back and reports the error on
/// failure — instead of mutating a working clone for a later Apply. (The 按钮大小 row this group used to
/// carry — 小/中/大 button size — was removed in that same task; see
/// <see cref="TanMenu.Core.Models.GeneralConfig.ButtonSize"/>'s own remarks for what replaced it.)
/// </summary>
public partial class AppearanceCard : UserControl, ISettingsCard
{
    private static readonly string[] LanguageSettings = { AppLanguage.Auto, AppLanguage.ZhHans, AppLanguage.EnUs };

    private static readonly string[] AppearanceSettings =
        { AppAppearance.System, AppAppearance.Light, AppAppearance.Dark };

    /// <summary>One row of the font dropdown. <see cref="Family"/> is the value written to config
    /// ("" = the app's own default font); <see cref="IsHeader"/> marks the group caption, which is
    /// rendered as a row because Avalonia's ComboBox has no GroupStyle.</summary>
    private sealed record FontItem(string Name, string Family, bool IsHeader);

    /// <summary>
    /// The vendored Aero key the font list's group captions take their ink from.
    /// </summary>
    /// <remarks>
    /// It was the literal <c>#777777</c> until Round 7, Task 6 — the grey the pre-Avalonia WPF
    /// build's <c>GroupStyle</c> header template used. Two reasons it is a key now. The colour is
    /// the disabled colour, and these rows ARE disabled (that is how they are made unpickable), so
    /// naming the disabled token says what the row is instead of restating what it looked like; and
    /// a literal in code is a colour the theme layer cannot follow, which is the whole point of
    /// <c>ShellTokenTests.LauncherStyles_ContainNoColourLiterals</c> being a test at all.
    /// <c>AeroDisabledForeground</c> is <c>#FF7F7F7F</c> in BOTH variants (VENDORED,
    /// <c>Themes/Aero/Variant/Light.axaml</c> and <c>Dark.axaml</c>), so light and dark agree here
    /// by construction rather than by coincidence.
    /// <para>
    /// Bound as a resource OBSERVABLE rather than resolved once: a row is built before it is in any
    /// visual tree, and the observable re-evaluates when the element gains a resource host and
    /// whenever <c>ActualThemeVariant</c> changes, so nothing depends on when
    /// <see cref="PopulateFontCombo"/> happens to run.
    /// </para>
    /// </remarks>
    private const string GroupHeaderForegroundKey = "AeroDisabledForeground";

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
    private readonly IWindowHost _host;

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
        _host = App.Services.GetRequiredService<IWindowHost>();
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

            ColCountTitle.Text = AppLanguage.Text("ColButtonCount", lang);
            ColCountSub.Text = AppLanguage.Text("ColButtonCountCardSub", lang);
            // Clamped for DISPLAY only — a hand-edited config.json can carry a value outside the
            // NumericUpDown's own [1, 30] range (see GeneralConfig.ColButtonCount's own remarks); this
            // does not write the clamped value back, only shows something sane until the user actually
            // changes it (at which point OnColCountChanged persists whatever they picked).
            ColCountUpDown.Value = Math.Clamp(g.ColButtonCount, 1, 30);
        }
        finally
        {
            _loading = false;
        }
    }

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
                row.Bind(TemplatedControl.ForegroundProperty,
                    row.GetResourceObservable(GroupHeaderForegroundKey));
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

        var previous = _config.Config.General.Language;
        _config.Config.General.Language = LanguageSettings[LanguageCombo.SelectedIndex];
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.Language = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>The whole app repaints live, before the write even reaches disk: the dropdown is a
    /// preview of a whole-app appearance, and waiting for the save to finish would leave the user
    /// choosing 深色 while still looking at a light window.</summary>
    private void OnAppearanceChanged(object? sender, SelectionChangedEventArgs e) => RunSafely(async () =>
    {
        if (_loading || AppearanceCombo.SelectedIndex < 0)
            return;

        var previous = _config.Config.General.Appearance;
        var chosen = AppearanceSettings[AppearanceCombo.SelectedIndex];
        _config.Config.General.Appearance = chosen;
        if (Application.Current is { } app)
            AppTheme.Apply(app, chosen);
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () =>
            {
                _config.Config.General.Appearance = previous;
                // The theme was already repainted live above; a failed save must repaint it back,
                // not just revert the field, or the app is left showing a theme it never kept.
                if (Application.Current is { } rollbackApp)
                    AppTheme.Apply(rollbackApp, previous);
            },
            reload: ReloadFromConfig);
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

        var previous = _config.Config.General.FontFamily;
        _config.Config.General.FontFamily = fam;
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.FontFamily = previous,
            reload: ReloadFromConfig);
    });

    /// <summary>每列个数 (GeneralConfig.ColButtonCount). Re-fitting the window is not this handler's
    /// job: <see cref="SettingsWrite.CommitAsync"/> raises <see cref="AppEvents.SettingsChanged"/> on a
    /// successful save, which <c>LauncherViewModel.OnSettingsChanged</c> turns into a rebuild
    /// (re-chunking every group's columns — see <c>LauncherViewModel.ApplyGroupSettings</c>) that ends
    /// by raising <c>ContentMetricsChanged</c>, which <c>MainWindow</c> turns into a re-fit — the same
    /// chain every other 外观 setting here already rides.</summary>
    private void OnColCountChanged(object? sender, NumericUpDownValueChangedEventArgs e) => RunSafely(async () =>
    {
        if (_loading || e.NewValue is not { } value)
            return; // an emptied/unparsable box reports null; leave the stored setting alone

        var next = (int)Math.Clamp(value, 1, 30);
        var previous = _config.Config.General.ColButtonCount;
        if (next == previous)
            return;

        _config.Config.General.ColButtonCount = next;
        await SettingsWrite.CommitAsync(_config, _events, _host, this,
            rollback: () => _config.Config.General.ColButtonCount = previous,
            reload: ReloadFromConfig);
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
