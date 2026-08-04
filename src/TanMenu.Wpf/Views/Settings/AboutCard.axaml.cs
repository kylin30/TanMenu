using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 关于 card group: the product tile, the publisher, the version and the copyright. One card, no
/// control — the only row anywhere whose column 2 is empty.
/// <para>
/// Round 5, item 2 ("关于集成到设置页中") replaced the rail's 关于 row and the full-page overlay it raised
/// with this. <b>Round 8, Task 4 moved the card again, without editing a line of its markup:</b> it is
/// the whole content of <c>Dialogs/AboutWindow</c>, the modal About box the menu band's 关于 item opens,
/// and it is no longer a tab of the Options dialog. See the XAML for the published sentence behind that
/// move, and for why the title is a literal while the publisher line is localized.
/// </para>
/// <para>
/// NO <c>_loading</c> GUARD, unlike most card groups, and for a reason the settings surface already has
/// two precedents for (<c>FoldersCard</c>, <c>UpdatesCard</c>): that guard exists to stop a
/// <c>SelectionChanged</c>/<c>IsCheckedChanged</c> handler writing back the value
/// <see cref="ReloadFromConfig"/> just assigned to it. Every control here is display text; there is no
/// handler to re-enter and nothing on this card writes config at all — it never touches
/// <see cref="SettingsDraft.Working"/>, which is also what makes it safe to host OUTSIDE the settings
/// surface: on the About box there is no 确定 / 取消 / 应用 at all, and this card gives them nothing to
/// commit or discard when it is not there either.
/// </para>
/// </summary>
public partial class AboutCard : UserControl, ISettingsCard
{
    private readonly SettingsDraft _draft;

    private string UiLanguage => _draft.Working.General.Language;

    public AboutCard()
    {
        InitializeComponent();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
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

        GroupHead.Text = AppLanguage.Text("About", lang);
        PublisherText.Text = AppLanguage.Text("AboutPublisher", lang);
        VersionLine.Text = AppLanguage.Format("CurrentVersion", lang, AppIdentity.Version);
        CopyrightLine.Text = AppIdentity.Copyright;
    }
}
