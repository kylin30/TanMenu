using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views.Settings;

namespace TanMenu.Wpf.Dialogs;

/// <summary>
/// The 选项 dialog — a Windows 7 property sheet, modal, delayed commit. Round 8, Task 2 replaced
/// <c>Views/SettingsPage</c> with it and took the shell's whole page model down in the same commit:
/// there is one page in the launcher now, so <c>ShellPage</c>, <c>IShellNavigator</c>,
/// <c>MainWindow.ShowPage</c> and the navigation pane's second <c>ListBox</c> all went with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>CONSTRUCTED FRESH ON EVERY OPEN.</b> That is what keeps
/// <c>BehaviorCard.OnAttachedToVisualTree</c> (the taskbar-pin state and 开机自启's OS read) and
/// <c>FoldersCard.OnAttachedToVisualTree</c> (the data-folder size) working with ZERO changes — they
/// were written for a cached page that detached and re-attached on every navigation, and a per-open
/// window gives them the same signal. A cached dialog would have needed <c>Opened</c> hooked in two
/// card files instead. The cost is nil: <c>AppearanceCard.PopulateFontCombo</c> already enumerates
/// <c>FontManager.Current.SystemFonts</c> on every <c>ReloadFromConfig</c>, i.e. on every
/// <c>SettingsChanged</c>, and always did.
/// </para>
/// <para>
/// A TabControl realises only the SELECTED tab's content, so each card's attach hook fires the first
/// time its tab is chosen rather than on open. That is a refinement of the old behaviour, not a
/// regression: the cards' own remarks ask to re-read the OS "on every re-attach including the first",
/// and this still is every re-attach. Their CONSTRUCTORS all run at parse time regardless — the six
/// <c>UserControl</c>s are declared in this window's XAML — so <see cref="ApplyText"/> can populate a
/// card that has never been shown.
/// </para>
/// <para>
/// <b><see cref="Current"/> is not a convenience.</b> It is the single-instance guard AND the owner
/// the global-hotkey failure dialog re-resolves to: <c>App.WarnHotkeyRegistrationFailedAsync</c> owns
/// its <c>MessageBox</c> to <c>OptionsWindow.Current ?? host.Window</c>, because a hotkey rejected by
/// a commit made HERE would otherwise try to <c>ShowDialog</c> on the launcher — which this window,
/// being modal on it, has already disabled. Nesting is safe: <c>WindowHost.SuppressHide</c> is
/// reference-counted, so that warning's own bracket cannot release the one
/// <c>SettingsLauncher</c> holds around this whole dialog.
/// </para>
/// </remarks>
public partial class OptionsWindow : Window
{
    /// <summary>The one open dialog, or null. Written by the constructor — <b>as its LAST statement,
    /// which is load-bearing rather than tidy</b>, see the constructor — and cleared by
    /// <see cref="OnClosed"/>. See the class remarks for both of its two jobs.</summary>
    public static OptionsWindow? Current { get; private set; }

    private readonly AppEvents _events;
    private readonly ConfigService _config;
    private readonly SettingsDraft _draft;
    private readonly IAutoStartService _autoStart;
    private readonly IWindowHost _host;

    /// <summary>Set for the duration of a commit. <see cref="SettingsCommit.ApplyAsync"/> awaits a disk
    /// write and, on failure, a modal dialog, so without this a second click during either would run a
    /// second <c>CopyOnto</c> + <c>SaveAsync</c> against the same draft.</summary>
    private bool _committing;

    /// <summary>Set once the discard prompt has been answered (or found unnecessary), so the
    /// <see cref="OnClosing"/> that follows <see cref="Window.Close()"/> lets the window go instead of
    /// asking a second time. See <see cref="OnClosing"/> for why a close cannot simply be awaited.</summary>
    private bool _closeApproved;

    /// <remarks>
    /// <b><see cref="Current"/> IS CLAIMED LAST, AND A FAULT UNSUBSCRIBES BOTH SINGLETONS ON THE WAY
    /// OUT.</b> It used to be claimed before the two subscriptions and <see cref="ApplyText"/>, which
    /// is the throw-prone part of this constructor — a XAML load plus six card constructors plus six
    /// <c>ReloadFromConfig</c>s, and exactly the code <c>SettingsLauncher.Open</c>'s own remark records
    /// throwing once already while this window was being built. A throw there left <see cref="Current"/>
    /// pointing at a window that never opened and therefore never reaches <see cref="OnClosed"/>, so
    /// <c>SettingsLauncher.OpenCoreAsync</c>'s single-instance branch would take
    /// <c>existing.Activate()</c> — a no-op on a never-shown window — for the rest of the session, and
    /// 设置, the tray entry and the empty-state button would all silently do nothing. The two leaked
    /// handlers made it worse: <see cref="ApplyText"/> would then re-throw into every later
    /// <c>RaiseSettingsChanged</c>. Claiming the field only once everything it stands for exists is what
    /// makes "<see cref="Current"/> is non-null ⇒ a usable dialog" true rather than hoped for.
    /// </remarks>
    public OptionsWindow()
    {
        InitializeComponent();

        _events = App.Services.GetRequiredService<AppEvents>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _host = App.Services.GetRequiredService<IWindowHost>();

        // AppLanguage has no change notification, and a card's own change handler has no reference back
        // to this window (each card only knows the draft), so re-labelling after a 语言 commit goes
        // through the same coarse SettingsChanged signal every other listener in the app uses
        // (MainWindow's rail, TrayService, ...). SettingsCommit raises it once per commit. UNSUBSCRIBED
        // IN OnClosed, which matters here in a way it did not on the cached page: this window is
        // per-open, so a missed unsubscribe leaks one handler per open and doubles the work on every
        // later commit.
        _events.SettingsChanged += ApplyText;

        // 应用 lives outside the cards' visual tree, so it cannot watch them; it watches the draft.
        _draft.DirtyChanged += ApplyDirtyState;

        try
        {
            ApplyText();
            ApplyDirtyState();
        }
        catch
        {
            // OnClosed is the only other place these come off, and a window whose constructor threw
            // never gets one. Two singletons outlive this object, so leaving them subscribed to a
            // half-built window would keep re-entering it on every later commit.
            _events.SettingsChanged -= ApplyText;
            _draft.DirtyChanged -= ApplyDirtyState;
            throw;
        }

        Current = this;
    }

    /// <summary>
    /// Every string this window owns: its title, its six tab headers and its three button labels — plus
    /// a <c>ReloadFromConfig</c> for every card, found structurally among the tabs rather than through
    /// one hardcoded call per group, so a tab added to the XAML without wiring it in here (there is
    /// nothing to wire) can never be left stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The six tab headers take the six keys the cards' own group heads take, and this method owns
    /// the writes.</b> No <c>Binding</c>: <c>AppLanguage</c> has no change notification, which is the
    /// same reason <c>MainWindow.ApplyMenuText</c> exists (it was <c>ApplyRailText</c> until Round 8,
    /// Task 3 replaced the navigation pane with the menu band). Round 8, Task 6 deletes five of the six
    /// group boxes and with them five <c>GroupHead</c> <c>TextBlock</c>s and the five
    /// <c>GroupHead.Text</c> assignments behind them, at which point the <c>TabItem</c> header is the
    /// only remaining statement of a group's name — so the text had to move here first.
    /// </para>
    /// <para>
    /// <b>THE DIALOG'S SIZE WAS MEASURED IN THIS TASK</b>, rather than inherited from the design's
    /// provisional 540 x 540 (the pre-<c>28158a8</c> <c>SettingsWindow</c>'s own figures). The build
    /// was launched with the window temporarily switched to <c>SizeToContent</c>, every tab was
    /// selected in turn through UI Automation's <c>SelectionItemPattern</c>, and the window's UIA
    /// <c>BoundingRectangle</c> — which for an Avalonia window is the CLIENT rectangle, confirmed
    /// against <c>GetWindowRect</c>/<c>GetClientRect</c> on the same HWND — was read after each. At
    /// 150 % scale, in Chinese, with the app's default font:
    /// </para>
    /// <list type="table">
    ///   <item><description>free width: 外观 646x345, 行为 584x452, 文件夹 784x357, 常用工具 305x320,
    ///   更新 582x213, 关于 283x230 DIP</description></item>
    ///   <item><description>width pinned to 711: 外观 x345, 行为 x452, 文件夹 x373, 常用工具 x320,
    ///   更新 x213, 关于 x230 DIP</description></item>
    /// </list>
    /// <para>
    /// <b>711 is chosen over the widest tab's 784, and that is a deviation from the design's literal
    /// instruction ("take the maximum over the six"), taken for a stated reason.</b> 784 is 文件夹's
    /// DESIRED width, and what wants it is one wrapping subtitle: <c>tm-card-sub</c> carries
    /// <c>TextWrapping="Wrap"</c> and <c>FoldersCard</c>'s own remarks record it wrapping to two and
    /// three lines on the page it came from, at the app's floor, as intended behaviour. Adopting 784
    /// would put the OUTER window at 798.67 DIP against the PUBLISHED 800 x 600 floor — compliant by
    /// 1.33 DIP, i.e. a dialog that exactly fills an 800-pixel screen. 711 is the width the settings
    /// surface's CONTENT PANE has had since Round 4 and that four measurement records in this tree
    /// rest on, and it gives each card MORE room than the page did, not less: the page inset
    /// <c>CardStack</c> by 22 a side (667 DIP of card), this dialog insets 11 a side and the vendored
    /// <c>TabControl</c> template takes 4 more (685 DIP of card). Nothing that fitted before can clip
    /// now. The cost is the 16 DIP of extra height 文件夹 takes to wrap — 357 becomes 373 — which is
    /// below 行为's 452 and therefore costs nothing at all.
    /// </para>
    /// <para>
    /// The ruling for a maximum that did NOT fit inside 800 x 600 was to SPLIT the 文件夹 tab rather
    /// than accept a scrollbar. It was not needed: 711 x 452 client is 725.67 x 489.33 DIP outer, the
    /// frame measured as 22 x 56 physical px on this machine.
    /// </para>
    /// <para>
    /// The language read is the DRAFT's, not the live config's: it is what the cards themselves label
    /// from, and a committed 语言 change has already landed in both by the time this runs.
    /// </para>
    /// </remarks>
    public void ApplyText()
    {
        var lang = _draft.Working.General.Language;

        Title = AppLanguage.Text("Options", lang);

        AppearanceTab.Header = AppLanguage.Text("Appearance", lang);
        BehaviorTab.Header = AppLanguage.Text("Behavior", lang);
        FoldersTab.Header = AppLanguage.Text("Folders", lang);
        ToolsTab.Header = AppLanguage.Text("CommonTools", lang);
        UpdatesTab.Header = AppLanguage.Text("Updates", lang);
        AboutTab.Header = AppLanguage.Text("About", lang);

        OkBtn.Content = AppLanguage.Text("Ok", lang);
        CancelBtn.Content = AppLanguage.Text("Cancel", lang);

        // 应用 is the one commit button with an access key (PUBLISHED, win-property-win: "Assign 'A'
        // as the access key"; 确定/取消 own Enter and Esc instead, PUBLISHED, inter-keyboard).
        //
        // AN EXPLICIT AccessText RATHER THAN A BARE STRING, and the reason is in the vendored theme
        // rather than in taste: a bare string is turned into an AccessText only when the button's
        // template sets ContentPresenter.RecognizesAccessKey, which defaults to FALSE (decompiled,
        // Avalonia.Controls.Presenters.ContentPresenter — the property is registered with
        // defaultValue: false and only then is FuncDataTemplate.Access used instead of .Default), and
        // Themes/Aero/ControlStyles/Button_RepeatButton_ToggleButton.axaml's PART_ContentPresenter
        // does not set it. Handing the presenter a ready-made AccessText needs no /template/ selector
        // and no edit to a vendored file. AccessText registers itself with the TopLevel's
        // AccessKeyHandler on attach and unregisters on detach (decompiled, same assembly), and it
        // strips the marker before drawing — so the label reads 应用(A) / Apply, never with a stray
        // underscore, and the underline appears only while Alt is held (its ShowAccessKey branch).
        // NOT VERIFIABLE ON THIS MACHINE: there is no keyboard here, so the Alt+A gesture itself was
        // never exercised. What was verified is the rendering, in a capture.
        ApplyBtn.Content = new AccessText { Text = AppLanguage.Text("ApplyWithAccessKey", lang) };

        // …and an explicit accessible name WITHOUT the marker. MEASURED, not assumed: with only the
        // AccessText content set, this button's UIA Name read literally "应用(_A)" — Avalonia's own
        // AccessText peer is a NoneAutomationPeer, so the button's name falls back to the raw content
        // string with the marker still in it, and a screen reader would say "underscore A". The
        // visible text is fine (AccessText strips the marker before it draws); only the name needs
        // saying. 确定 and 取消 need no such write — their content is a plain string.
        AutomationProperties.SetName(ApplyBtn, AppLanguage.Text("Apply", lang));

        foreach (var card in Cards())
            card.ReloadFromConfig();
    }

    /// <summary>
    /// Select the 关于 tab. <b>Round 8, Task 3, and it is provisional in exactly the way its one caller
    /// says it is:</b> the menu band's 关于 item has to land somewhere, 关于 is a tab of this dialog
    /// today, and Round 8, Task 4 moves that content into a real About box
    /// (<c>Dialogs/AboutWindow</c>) and deletes both this method and the tab it selects. PUBLISHED,
    /// <c>win-property-win</c>: "Is the content solely an application's attributes? If so, use an About
    /// box."
    /// </summary>
    public void ShowAboutTab() => Tabs.SelectedItem = AboutTab;

    /// <summary>Every settings card hosted by a tab, in tab order. Structural discovery, inherited from
    /// the page this window replaced: the tabs are the authority on which groups exist.</summary>
    private IEnumerable<ISettingsCard> Cards() =>
        Tabs.Items.OfType<TabItem>().Select(t => t.Content).OfType<ISettingsCard>();

    /// <summary>应用 is enabled exactly while there is something to apply. 确定 and 取消 stay enabled
    /// unconditionally — PUBLISHED, <c>win-property-win</c>: only Apply is "initially disabled" and
    /// "enabled when users make a change".</summary>
    private void ApplyDirtyState() => ApplyBtn.IsEnabled = _draft.IsDirty;

    /// <summary>确定 = commit AND close, which is the half the settings PAGE could not have: it had no
    /// window, so 确定 and 应用 did the same thing there and Task 1 said so.
    /// <para>
    /// It closes only if the commit SUCCEEDED. A failed save leaves the draft holding what the user
    /// asked for (see <see cref="SettingsCommit.ApplyAsync"/>), so closing anyway would discard a
    /// pending edit the user was told had failed — the opposite of what the message box just said.
    /// </para>
    /// <para>
    /// It AWAITS the commit before closing, and that ordering is load-bearing rather than tidy: the
    /// global-hotkey warning is <c>Dispatcher.UIThread.Post</c>ed from <c>GlobalHotkeyService</c>'s
    /// SettingsChanged handler, so it lands after this method's await resumes. On 应用 the dialog is
    /// still open and <see cref="Current"/> owns it; on 确定 the dialog has closed, <see cref="Current"/>
    /// is null, and it falls back to the launcher — which is enabled again by then. Firing and
    /// forgetting would make which of the two happens a race.
    /// </para></summary>
    private void OnOkClick(object? sender, RoutedEventArgs e) => RunSafely(async () =>
    {
        if (await CommitAsync())
        {
            _closeApproved = true; // nothing left to discard; do not ask on the way out
            Close();
        }
    });

    private void OnApplyClick(object? sender, RoutedEventArgs e) => RunSafely(() => CommitAsync());

    /// <summary>取消 = discard AND close. Nothing has to be undone — no config field, no registry value
    /// and no theme repaint ever left the draft — so discarding is re-seeding it. Identical in every
    /// respect to the title-bar ×, which is PUBLISHED, <c>win-property-win</c>: "Make sure the Close
    /// button on the title bar has the same effect as Cancel or Close. Never give it the same effect as
    /// OK."</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// The discard prompt, and the reason it lives in <c>Closing</c> rather than in 取消's handler: the
    /// title-bar × does not go through any handler of ours, and the two must behave identically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a NAMED DEVIATION.</b> PUBLISHED, <c>win-property-win</c>: "Don't confirm if users
    /// discard their changes." It is deviated from on this round's own instruction and on one ground
    /// this dialog can defend: two of its settings are HIGH-EFFORT to re-enter — a captured global
    /// hotkey (<c>BehaviorCard.OnHotkeyKeyDown</c>) and a picked root folder
    /// (<c>FoldersCard.OnPickRootClick</c>) — and each costs the user a real interaction to redo.
    /// The recon reports a narrow published exception for exactly that case; ITS EXACT WORDING COULD
    /// NOT BE VERIFIED, so the deviation is booked as a deviation rather than dressed up as compliance.
    /// </para>
    /// <para>
    /// A close cannot simply be awaited: <c>Closing</c> is synchronous and the prompt is not, so the
    /// first pass CANCELS the close, asks, and — on "yes" — closes again with
    /// <see cref="_closeApproved"/> set. 确定's own success path sets the same flag, because a committed
    /// draft has nothing left to discard.
    /// </para>
    /// <para>
    /// <b>The re-seed is NOT here — it is in <see cref="OnClosed"/>, and that is a correction.</b> This
    /// method used to hold two <c>Reseed()</c> calls, one per discard branch, under a comment claiming
    /// the draft was re-seeded on every close. It was not: 确定 sets <see cref="_closeApproved"/> before
    /// calling <c>Close()</c>, so the close it triggers takes the first branch below and returned above
    /// both of those calls. The draft is a DI singleton that outlives this window, so the stale seed survived
    /// into the next open — concretely, committing 开机自启 with 确定 left <c>AutoStartAtSeed</c> holding
    /// the PRE-commit OS state, and re-opening the dialog and toggling the row back compared the new
    /// desire against that stale seed, left <c>IsDirty</c> false, and left 应用 disabled over a real
    /// pending edit. <see cref="OnClosed"/> runs once on every close however the window went away, which
    /// is the only place that claim can be made by construction instead of by case analysis.
    /// </para>
    /// </remarks>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // A REAL APPLICATION EXIT IS NEVER PROMPTED. 退出 from the tray reaches
        // ClassicDesktopStyleApplicationLifetime.Shutdown, which closes every window and stops the loop
        // whether or not one of them cancels; a prompt there would be a dialog nobody can answer over a
        // process that is already going away. MainWindow.OnClosing tests the same flag for the mirror
        // reason — it hides to the tray on every close EXCEPT that one.
        if (App.IsExiting)
            return;

        if (_closeApproved || _committing)
        {
            // A commit in flight is awaiting a disk write or its own failure dialog; let neither be
            // closed out from under it. _committing is cleared in CommitAsync's finally.
            if (_committing && !_closeApproved)
                e.Cancel = true;
            return;
        }

        if (!_draft.IsDirty)
        {
            _closeApproved = true;
            return;
        }

        e.Cancel = true;
        RunSafely(async () =>
        {
            if (!await ConfirmDiscardAsync())
                return;

            _closeApproved = true;
            Close();
        });
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        // THE COMMITTED LANGUAGE, NOT THE DRAFT'S, and that is a measured correction rather than a
        // preference. Every card labels itself from the draft, which is right for a card: the draft is
        // what a card DISPLAYS. This prompt is not — it appears over a window whose own labels were
        // last written by ApplyText, i.e. in the COMMITTED language, and a pending 语言 change does not
        // re-label anything until it is applied. Reading the draft here produced a genuinely mixed
        // dialog on the first build of this window: with English staged but not applied, the title and
        // the message came out English while MessageBox's own 是/否 buttons — which resolve the
        // language from ConfigService, not from any draft — stayed Chinese. Captured, then fixed.
        var language = _config.Config.General.Language;

        // The prompt takes focus; keep the launcher alive for users who have 失焦自动隐藏 on. The bracket
        // nests inside SettingsLauncher's own, which is safe because SuppressHide is reference-counted.
        _host.SuppressHide(true);
        try
        {
            var answer = await MessageBox.ShowAsync(this,
                AppLanguage.Text("DiscardChangesConfirm", language),
                AppLanguage.Text("Options", language),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return answer == MessageBoxResult.Yes;
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Run the one commit path both 确定 and 应用 use.</summary>
    /// <returns>true if the config write landed (a failed OS effect does not make it false — see
    /// <see cref="SettingsCommit"/>'s own remarks).</returns>
    private async Task<bool> CommitAsync()
    {
        if (_committing)
            return false;

        _committing = true;
        try
        {
            // The row is dead for the duration: the save is awaited and a failure is a modal dialog.
            CommitRow.IsEnabled = false;
            return await SettingsCommit.ApplyAsync(_draft, _config, _events, _autoStart, _host, this);
        }
        finally
        {
            _committing = false;
            CommitRow.IsEnabled = true;
            ApplyDirtyState(); // re-assert 应用's state after the row came back enabled
        }
    }

    /// <summary>
    /// Let go of everything that outlives this window: the two singleton subscriptions, the
    /// single-instance field, and the draft's contents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two handlers would otherwise accumulate one per open, because the dialog is constructed
    /// fresh every time.
    /// </para>
    /// <para>
    /// <b>THE RE-SEED IS HERE BECAUSE THIS IS THE ONE METHOD EVERY CLOSE REACHES</b> — 确定, 取消, the
    /// title-bar ×, the discard prompt's second pass, and the application exit that
    /// <see cref="OnClosing"/> deliberately does not prompt. <see cref="OnClosing"/> cannot make that
    /// claim: 确定 approves the close before it runs, so both of its discard branches are skipped. The
    /// draft is a DI singleton, so anything left in it survives into the next open — including an
    /// <c>AutoStartAtSeed</c> that a commit has just made stale and an <c>AutoStartDesired</c> that
    /// happens to equal the OS state and therefore never made <c>IsDirty</c> true. Re-seeding is one
    /// JSON round trip plus one OS read.
    /// </para>
    /// <para>
    /// It runs AFTER the unsubscribes on purpose: <c>Reseed</c> ends in <c>Revalidate</c>, which can
    /// raise <c>DirtyChanged</c>, and there is no reason to drive this closed window's commit row from
    /// it. A successful commit re-seeds too, in <see cref="SettingsCommit.ApplyAsync"/>, which is what
    /// covers 应用 — that path leaves the dialog OPEN and so never reaches here.
    /// </para>
    /// </remarks>
    protected override void OnClosed(EventArgs e)
    {
        _events.SettingsChanged -= ApplyText;
        _draft.DirtyChanged -= ApplyDirtyState;
        if (ReferenceEquals(Current, this))
            Current = null;
        _draft.Reseed();
        base.OnClosed(e);
    }

    /// <summary>Run an async click handler without letting a fault escape the event — same rationale as
    /// every card group's own RunSafely: Avalonia has no WPF-style global DispatcherUnhandledException
    /// hook, so an escaping async-void fault would tear the process down, and logging is the floor.
    /// SettingsCommit already reports a save failure itself; this is the floor under everything else.
    /// </summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "选项对话框操作失败"); }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The caption's minimize and maximize boxes
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_EX_DLGMODALFRAME = 0x00000001;
    private const uint WM_SETICON = 0x0080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>
    /// Make Windows draw a DIALOG caption: the Close button alone, and no title-bar icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUBLISHED, <c>win-dialog-box</c>: "Resizable dialogs can have a Maximize button", i.e. a fixed
    /// one has none; and a modal dialog with a live Minimize box is a window the user can send to the
    /// taskbar while its owner is disabled. The design flagged this as COULD NOT VERIFY without a
    /// build and told this task to check it. <b>MEASURED, and it was real:</b> on the first build of
    /// this dialog, <c>GetWindowLong(GWL_STYLE)</c> read <c>0x16CA0000</c> — <c>WS_MINIMIZEBOX</c> SET,
    /// <c>WS_MAXIMIZEBOX</c> clear, <c>WS_THICKFRAME</c> clear — and UIA reported a caption with an
    /// ENABLED "最小化" button and a greyed "最大化" beside it. Windows draws both boxes when either
    /// style bit is present and greys the absent one, so clearing <c>CanResize</c> alone gets you a
    /// dead maximize box and a live minimize box.
    /// </para>
    /// <para>
    /// <b>The design's own stated remedy — narrowing <c>SystemDecorations</c> — is NOT used, and this
    /// is the one place this task departs from it.</b> Avalonia's <c>SystemDecorations</c> has three
    /// values (<c>None</c>, <c>BorderOnly</c>, <c>Full</c>); anything below <c>Full</c> removes the
    /// whole title bar, which would take the Close button and the 选项 title with it and break two
    /// PUBLISHED rules (<c>win-dialog-box</c>: "Don't disable the Close button"; <c>win-property-win</c>:
    /// "Options dialog box titles — Use 'Options.'") to fix one. Clearing the two style bits is what a
    /// real Win32 dialog does, and this app already reaches for the same class of tool one window over
    /// — <c>MainWindow.RestoreSystemCloseCommand</c> re-enables <c>SC_CLOSE</c> through
    /// <c>GetSystemMenu</c>/<c>EnableMenuItem</c> for a defect Avalonia's own client-area extension
    /// creates.
    /// </para>
    /// <para>
    /// <b>THE TITLE-BAR ICON IS THE SAME KIND OF FINDING AND IS REMOVED IN THE SAME BLOCK.</b>
    /// PUBLISHED, <c>win-dialog-box</c>: "Dialog boxes don't have title bar icons." This window sets no
    /// <c>Icon</c>, which is not enough — MEASURED on a <c>PrintWindow</c> capture of the first build,
    /// Windows drew the app's own icon left of 选项 regardless, because an owned window with no icon of
    /// its own falls back to its owner's. The Win32 answer is the classic pair: add
    /// <c>WS_EX_DLGMODALFRAME</c>, which tells Windows this is a modal dialog frame and suppresses the
    /// icon slot, and blank both icon slots with <c>WM_SETICON</c> so nothing can be drawn into them.
    /// One <c>SWP_FRAMECHANGED</c> below commits all three writes at once.
    /// </para>
    /// <para>
    /// Runs from <see cref="OnOpened"/>, because the HWND does not exist before that.
    /// <c>SWP_FRAMECHANGED</c> with no move, no size and no z-order change is what makes Windows
    /// recompute and repaint the non-client area after the style writes; without it the old caption
    /// stays on screen until something else invalidates it. Failure is logged and ignored: a dialog
    /// with a spare minimize box is worse-looking, not broken.
    /// </para>
    /// </remarks>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            var style = GetWindowLongW(hwnd, GWL_STYLE);
            var exStyle = GetWindowLongW(hwnd, GWL_EXSTYLE);
            var wantedStyle = style & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            var wantedExStyle = exStyle | WS_EX_DLGMODALFRAME;

            if (wantedStyle != style)
                SetWindowLongW(hwnd, GWL_STYLE, wantedStyle);
            if (wantedExStyle != exStyle)
                SetWindowLongW(hwnd, GWL_EXSTYLE, wantedExStyle);

            // ICON_SMALL = 0, ICON_BIG = 1. Blanking both is what leaves the caption with nothing to
            // draw once WS_EX_DLGMODALFRAME has removed the slot.
            SendMessageW(hwnd, WM_SETICON, IntPtr.Zero, IntPtr.Zero);
            SendMessageW(hwnd, WM_SETICON, new IntPtr(1), IntPtr.Zero);

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);

            Serilog.Log.Debug(
                "Options dialog caption: style 0x{Before:X8} -> 0x{After:X8}, exstyle 0x{ExBefore:X8} -> " +
                "0x{ExAfter:X8} (minimize/maximize boxes and the title-bar icon removed)",
                style, GetWindowLongW(hwnd, GWL_STYLE), exStyle, GetWindowLongW(hwnd, GWL_EXSTYLE));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to give the Options dialog a plain dialog caption");
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
