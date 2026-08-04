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
/// 外观, the Options dialog's first tab: language, light/dark appearance, UI font, and (since Round 3,
/// Task 4) items per column. Every handler here writes the shared <see cref="SettingsDraft"/>'s working
/// clone and calls <see cref="SettingsDraft.Revalidate"/>; nothing reaches disk until 确定 / 应用 runs
/// <see cref="SettingsCommit.ApplyAsync"/> (Round 8, Task 1). (The 按钮大小 row this group used to
/// carry — 小/中/大 button size — was removed in Round 3, Task 4; see
/// <see cref="TanMenu.Core.Models.GeneralConfig.ButtonSize"/>'s own remarks for what replaced it.)
/// <para>
/// Round 8, Task 6 replaced the row grammar: four label-and-control rows on a two-column Grid instead
/// of four <c>tm-card</c> Borders, no icons, no subtitles, no group box — the tab header carries the
/// group's name. The XAML carries the published citation for every number and for what the subtitles
/// were. This file is the reference for the <c>ReloadFromConfig</c> contract every settings card
/// follows: populate from the draft under a <c>_loading</c> guard, label from
/// <see cref="UiLanguage"/>, and never write anything back while doing it.
/// </para>
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

    private readonly SettingsDraft _draft;
    private readonly ConfigService _config;

    /// <summary>Guards every SelectionChanged handler against the selections ReloadFromConfig makes
    /// while it repopulates the combos — without it, populating the Language combo would write the
    /// freshly-loaded value straight back to config as if the user had picked it.</summary>
    private bool _loading;

    /// <summary>The font row currently selected, so a click on a group header can be bounced back to it.</summary>
    private ComboBoxItem? _selectedFontRow;
    private bool _fontSelectionGuard;

    /// <summary>
    /// The language every string this card writes is rendered in: <b>the COMMITTED one, never the
    /// draft's</b>. Round 8, Task 6 made that rule uniform across all six cards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing on this surface re-labels itself between a 语言 pick and the commit that lands it:
    /// <see cref="ReloadFromConfig"/> is the only re-label path, and it runs from
    /// <c>OptionsWindow.ApplyText</c> — on the constructor, and on <c>SettingsChanged</c>, which
    /// <see cref="SettingsCommit.ApplyAsync"/> raises only AFTER re-seeding the draft onto what it
    /// just wrote. So at every moment the draft's language differs from the committed one, the
    /// committed one is what is actually on screen.
    /// </para>
    /// <para>
    /// That matters because a card's strings do not all reach the screen through
    /// <see cref="ReloadFromConfig"/>: <c>FoldersCard</c> and <c>BehaviorCard</c> hand some of them to
    /// <see cref="TanMenu.Wpf.Dialogs.MessageBox"/>, which resolves its OWN 确定/取消 captions from
    /// <see cref="ConfigService"/> and from nothing else (<c>MessageBox.ResolveLanguage</c>). Reading
    /// the draft produced a genuinely mixed dialog the moment the pending edit WAS the language —
    /// captured on this build for <c>OptionsWindow</c>'s discard prompt, fixed there in Task 2, and
    /// left in every card until here.
    /// </para>
    /// </remarks>
    private string UiLanguage => _config.Config.General.Language;

    public AppearanceCard()
    {
        InitializeComponent();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
        _config = App.Services.GetRequiredService<ConfigService>();
    }

    /// <summary>Populate every control from the draft's working clone. Sets a guard so the
    /// SelectionChanged handlers do not write back the values they are being given. The VALUES come
    /// from the draft; the TEXT comes from <see cref="UiLanguage"/> — see its remarks.</summary>
    public void ReloadFromConfig()
    {
        _loading = true;
        try
        {
            var g = _draft.Working.General;
            var lang = UiLanguage;

            LanguageLabel.Text = AppLanguage.Text("Language", lang);
            LanguageCombo.ItemsSource = LanguageSettings.Select(s => AppLanguage.LanguageLabel(s, lang)).ToList();
            var langIdx = Array.FindIndex(LanguageSettings,
                s => string.Equals(s, AppLanguage.NormalizeSetting(g.Language), StringComparison.OrdinalIgnoreCase));
            LanguageCombo.SelectedIndex = langIdx < 0 ? 0 : langIdx;

            AppearanceLabel.Text = AppLanguage.Text("AppearanceMode", lang);
            AppearanceCombo.ItemsSource = AppearanceSettings.Select(s => AppearanceLabelFor(s, lang)).ToList();
            var appearanceIdx = Array.FindIndex(AppearanceSettings,
                s => string.Equals(s, g.Appearance, StringComparison.OrdinalIgnoreCase));
            AppearanceCombo.SelectedIndex = appearanceIdx < 0 ? 0 : appearanceIdx;

            FontLabel.Text = AppLanguage.Text("Font", lang);
            PopulateFontCombo(g.FontFamily ?? string.Empty, lang);

            ColCountLabel.Text = AppLanguage.Text("ColButtonCount", lang);
            ColCountHelp.Text = AppLanguage.Text("ColButtonCountCardSub", lang);
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

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedIndex < 0)
            return;

        _draft.Working.General.Language = LanguageSettings[LanguageCombo.SelectedIndex];
        _draft.Revalidate();
    }

    /// <summary>
    /// 外观 no longer previews live. Until Round 8, Task 1 this handler called <c>AppTheme.Apply</c> on
    /// the spot, on the argument that "waiting for the save to finish would leave the user choosing 深色
    /// while still looking at a light window" — which delayed commit forbids outright (PUBLISHED,
    /// <c>win-property-win</c>: "changes take effect only when users click OK or Apply"). The repaint
    /// moved to <see cref="SettingsCommit.ApplyAsync"/>, and 应用 is precisely the affordance that gives
    /// the preview back on demand, without closing the window.
    /// </summary>
    private void OnAppearanceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || AppearanceCombo.SelectedIndex < 0)
            return;

        _draft.Working.General.Appearance = AppearanceSettings[AppearanceCombo.SelectedIndex];
        _draft.Revalidate();
    }

    private void OnFontChanged(object? sender, SelectionChangedEventArgs e)
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
        if (fam == _draft.Working.General.FontFamily)
            return;

        _draft.Working.General.FontFamily = fam;
        _draft.Revalidate();
    }

    /// <summary>每列个数 (GeneralConfig.ColButtonCount). Re-fitting the window is not this handler's
    /// job: <see cref="SettingsCommit.ApplyAsync"/> raises <see cref="AppEvents.SettingsChanged"/> once
    /// per commit, which <c>LauncherViewModel.OnSettingsChanged</c> turns into a rebuild (re-chunking
    /// every group's columns — see <c>LauncherViewModel.ApplyGroupSettings</c>) that ends by raising
    /// <c>ContentMetricsChanged</c>, which <c>MainWindow</c> turns into a re-fit — the same chain every
    /// other 外观 setting here already rides, now fired on 确定/应用 rather than on every keystroke of
    /// the spinner.</summary>
    private void OnColCountChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading || e.NewValue is not { } value)
            return; // an emptied/unparsable box reports null; leave the stored setting alone

        var next = (int)Math.Clamp(value, 1, 30);
        if (next == _draft.Working.General.ColButtonCount)
            return;

        _draft.Working.General.ColButtonCount = next;
        _draft.Revalidate();
    }
}
