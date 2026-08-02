using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Views.Settings;

/// <summary>
/// 关于 card group: the product tile, the publisher, the version and the copyright. One card, no
/// control — the only row on the 设置 page whose column 2 is empty.
/// <para>
/// Round 5, item 2 ("关于集成到设置页中") replaced the rail's 关于 row and the full-page overlay it raised
/// with this. See the XAML for why it is its own group, last on the page, and why the title is a literal
/// while the publisher line is localized.
/// </para>
/// <para>
/// NO <c>_loading</c> GUARD, unlike most card groups, and for a reason this page already has two
/// precedents for (<c>FoldersCard</c>, <c>UpdatesCard</c>): that guard exists to stop a
/// <c>SelectionChanged</c>/<c>IsCheckedChanged</c> handler writing back the value
/// <see cref="ReloadFromConfig"/> just assigned to it. Every control here is display text; there is no
/// handler to re-enter and nothing on this card writes config at all — no
/// <see cref="SettingsWrite.CommitAsync"/> call appears in this file.
/// </para>
/// </summary>
public partial class AboutCard : UserControl, ISettingsCard
{
    private readonly ConfigService _config;

    private string UiLanguage => _config.Config.General.Language;

    public AboutCard()
    {
        InitializeComponent();
        _config = App.Services.GetRequiredService<ConfigService>();
    }

    /// <summary>Populate every line. Nothing here is OS-backed or time-critical, so unlike
    /// <c>BehaviorCard</c>/<c>UpdatesCard</c> there is no <c>OnAttachedToVisualTree</c> refresh: the
    /// version cannot change while the process lives, and <see cref="AppIdentity.Copyright"/>'s year can
    /// only turn over at midnight on New Year's Eve with the settings page already open, which is not a
    /// case worth an attach hook.</summary>
    public void ReloadFromConfig()
    {
        var lang = UiLanguage;

        GroupHead.Text = AppLanguage.Text("About", lang);
        PublisherText.Text = AppLanguage.Text("AboutPublisher", lang);
        VersionLine.Text = AppLanguage.Format("CurrentVersion", lang, AppIdentity.Version);
        CopyrightLine.Text = AppIdentity.Copyright;
    }
}
