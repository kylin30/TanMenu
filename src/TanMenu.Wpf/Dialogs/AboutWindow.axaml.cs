using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views.Settings;

namespace TanMenu.Wpf.Dialogs;

/// <summary>
/// The 关于 box — a classic Windows About dialog: modal, sized to its content, one 确定 button.
/// Round 8, Task 4 took 关于 out of the Options dialog's tab strip and gave it this window, which the
/// menu band's 关于 item opens and nothing else does.
/// </summary>
/// <remarks>
/// <para>
/// PUBLISHED, <c>win-property-win</c>: "Is the content solely an application's attributes? If so, use an
/// About box." The markup carries the rest of the published sentences and every layout number.
/// </para>
/// <para>
/// <b>It hosts <c>Views/Settings/AboutCard</c> rather than owning its own copy of the four strings.</b>
/// That card is still an <see cref="ISettingsCard"/> and still labels itself from
/// <see cref="SettingsDraft"/> — but it is the one card that never writes to the draft (its own remarks
/// say so: "nothing on this card writes config at all"), so hosting it here stages nothing, commits
/// nothing and gives 确定 nothing to do beyond closing.
/// </para>
/// <para>
/// <b>No <c>SettingsChanged</c> subscription, unlike <c>OptionsWindow</c>, and the difference is
/// structural rather than an omission.</b> That window re-labels itself mid-life because a 语言 commit
/// can happen INSIDE it. Nothing can be committed while this one is up: it is modal on the launcher, so
/// the band's 设置 item is unreachable, and the tray's own 设置 entry goes through
/// <c>SettingsLauncher.OpenCoreAsync</c>, whose <c>ShowDialog</c> would need the same disabled owner.
/// One <see cref="ApplyText"/> in the constructor is therefore the whole of this window's text.
/// </para>
/// </remarks>
public partial class AboutWindow : Window
{
    private readonly SettingsDraft _draft;

    public AboutWindow()
    {
        InitializeComponent();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
        ApplyText();
    }

    /// <summary>
    /// Every string this window owns — its title and its one button label — plus the hosted card's own
    /// <c>ReloadFromConfig</c>, which is what fills in the publisher, the version and the copyright.
    /// </summary>
    /// <remarks>
    /// The language read is the DRAFT's, which is what the hosted card reads, so the title and the
    /// card's group head cannot come out in two different languages. Outside the Options dialog the two
    /// are the same value anyway: the draft is re-seeded from the live config on every close of that
    /// dialog (<c>OptionsWindow.OnClosed</c>) and after every successful commit
    /// (<c>SettingsCommit.ApplyAsync</c>), and this window can only be opened while that dialog is shut.
    /// </remarks>
    private void ApplyText()
    {
        var lang = _draft.Working.General.Language;

        // The same key the card's group head takes (AboutCard.ReloadFromConfig), deliberately: 关于 /
        // About names the window and the thing inside it, and one key cannot drift from itself.
        Title = AppLanguage.Text("About", lang);
        OkBtn.Content = AppLanguage.Text("Ok", lang);

        About.ReloadFromConfig();
    }

    /// <summary>确定 closes, and that is all it does — there is nothing on this window to commit.
    /// Enter and Esc reach it through <c>IsDefault</c>/<c>IsCancel</c>, and the title-bar × does the same
    /// thing by doing nothing special.</summary>
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The plain dialog caption: no minimize box, no maximize box, no title-bar icon. See
    /// <see cref="DialogCaption"/> for the measurement behind each of the three, and for why
    /// <c>SystemDecorations</c> is the wrong tool. <c>OnOpened</c> is the earliest point at which the
    /// HWND exists.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DialogCaption.ApplyTo(this);
    }

    /// <summary>
    /// Show the About box modally on the launcher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The <c>SuppressHide</c> bracket is not optional</b>, for the same reason
    /// <c>SettingsLauncher.OpenCoreAsync</c> holds one around the Options dialog: a user with 失焦自动隐藏
    /// on loses the launcher the moment this window takes focus, and this window is modal ON the
    /// launcher, so hiding its owner is not cosmetic. The count is reference-based
    /// (<c>WindowHost.SuppressHide</c>), so it nests safely under anything else already holding one.
    /// </para>
    /// <para>
    /// <b>The hidden-owner fallback is not optional either.</b> <c>ShowDialog</c> throws
    /// <c>InvalidOperationException("Cannot show window with non-visible owner.")</c>, and while the
    /// only caller is a menu item on a visible launcher, the same test <c>MessageBox.ShowCoreAsync</c>
    /// and <c>SettingsLauncher</c> both make is made here rather than assumed away.
    /// </para>
    /// <para>
    /// <b>No single-instance guard, unlike <c>OptionsWindow.Current</c>.</b> That field exists because
    /// the Options dialog has four call sites, one of which is the tray menu — reachable while the
    /// launcher is hidden. This window has exactly one, the band's 关于 item, and the band belongs to the
    /// window <c>ShowDialog</c> has just disabled.
    /// </para>
    /// </remarks>
    internal static async Task ShowAsync(IWindowHost host)
    {
        host.SuppressHide(true);
        try
        {
            var dialog = new AboutWindow();
            var owner = host.Window is { IsVisible: true } window ? window : null;

            if (owner is not null)
            {
                await dialog.ShowDialog(owner);
                return;
            }

            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }
        finally
        {
            host.SuppressHide(false);
        }
    }
}
