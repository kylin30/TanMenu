using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 关于: the product tile, the publisher, the version and the copyright — the whole content of
/// <c>Dialogs/AboutWindow</c>, the modal About box the menu band's 关于 item opens.
/// <para>
/// Round 5, item 2 ("关于集成到设置页中") replaced the rail's 关于 row and the full-page overlay it raised
/// with this. <b>Round 8, Task 4 moved the card into its own window without editing a line of its
/// markup</b>, deliberately leaving the group box for <b>Task 6</b>, which deleted it — and with it the
/// header that printed 关于 a second time under a caption already reading 关于. See the XAML for the
/// published sentence behind both moves, for where the group box's bottom margin went, and for why the
/// title is a literal while the publisher line is localized.
/// </para>
/// <para>
/// NO <c>_loading</c> GUARD, unlike most card groups, and for a reason the settings surface already has
/// two precedents for (<c>FoldersCard</c>, <c>UpdatesCard</c>): that guard exists to stop a
/// <c>SelectionChanged</c>/<c>IsCheckedChanged</c> handler writing back the value
/// <see cref="ReloadFromConfig"/> just assigned to it. Every control here is display text; there is no
/// handler to re-enter and nothing on this card writes config at all. It does not even READ the draft
/// since Task 6 — its three localized lines come from <see cref="ConfigService"/> like every other
/// card's — which is what makes it safe to host OUTSIDE the settings surface: on the About box there
/// is no 确定 / 取消 / 应用, and this card gives them nothing to commit or discard.
/// </para>
/// </summary>
public partial class AboutCard : UserControl, ISettingsCard
{
    private readonly ConfigService _config;

    /// <summary>The language this card's three localized lines are rendered in: <b>the COMMITTED
    /// one</b> — see <see cref="AppearanceCard.UiLanguage"/>'s remarks. It read
    /// <c>SettingsDraft.Working</c> until Round 8, Task 6, which is why this card held a
    /// <see cref="SettingsDraft"/> reference at all; with the rule made uniform it holds none, and
    /// that is the honest dependency list for a card hosted OUTSIDE the settings surface, where there
    /// is no draft being edited and no 确定 / 取消 / 应用 to answer to.</summary>
    private string UiLanguage => _config.Config.General.Language;

    public AboutCard()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
    }

    /// <summary>Populate every line. Nothing here is OS-backed or time-critical, so unlike
    /// <c>BehaviorCard</c>/<c>UpdatesCard</c> there is no <c>OnAttachedToVisualTree</c> refresh: the
    /// version cannot change while the process lives, and <see cref="AppIdentity.Copyright"/>'s year can
    /// only turn over at midnight on New Year's Eve with the About box already open, which is not a
    /// case worth an attach hook. <c>AboutWindow.ApplyText</c> calls this once, from the constructor;
    /// that window is built fresh on every open, so there is nothing staler to refresh.</summary>
    public void ReloadFromConfig()
    {
        var lang = UiLanguage;

        // NO GroupHead HERE ANY MORE. It wrote AppLanguage.Text("About", …) into the deleted group
        // box's header — the same key AboutWindow.ApplyText writes into the window's TITLE — so the
        // box printed 关于 a second time, two lines under the caption that already said it. Round 8,
        // Task 6 deleted the box and this line with it; the window's title is the one statement of
        // the name now. See the XAML.
        PublisherText.Text = AppLanguage.Text("AboutPublisher", lang);
        VersionLine.Text = AppLanguage.Format("CurrentVersion", lang, AppIdentity.Version);
        CopyrightLine.Text = AppIdentity.Copyright;
    }
}
