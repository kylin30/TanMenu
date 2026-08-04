using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives; // AccessText, on 应用's label
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
/// and this still is every re-attach. Their CONSTRUCTORS all run at parse time regardless — the five
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
    /// is the throw-prone part of this constructor — a XAML load plus five card constructors plus five
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
    /// Every string this window owns: its title, its five tab headers and its three button labels — plus
    /// a <c>ReloadFromConfig</c> for every card, found structurally among the tabs rather than through
    /// one hardcoded call per group, so a tab added to the XAML without wiring it in here (there is
    /// nothing to wire) can never be left stale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The five tab headers take the five keys the cards' own group heads take, and this method owns
    /// the writes.</b> No <c>Binding</c>: <c>AppLanguage</c> has no change notification, which is the
    /// same reason <c>MainWindow.ApplyMenuText</c> exists (it was <c>ApplyRailText</c> until Round 8,
    /// Task 3 replaced the navigation pane with the menu band). Round 8, Task 6 took the group box off
    /// every tab whose controls are one homogeneous set, and each one it took deleted that card's
    /// <c>GroupHead</c> <c>TextBlock</c> and the <c>GroupHead.Text</c> assignment behind it, so the
    /// <c>TabItem</c> header is now the ONLY statement of a group's name — which is why the text had to
    /// move here first, one task earlier.
    /// </para>
    /// <para>
    /// <b>THE DIALOG'S SIZE IS MEASURED, NEVER GUESSED</b>, and Round 8 has measured it three times by
    /// one method. Task 2 established it against the design's provisional 540 x 540 (the
    /// pre-<c>28158a8</c> <c>SettingsWindow</c>'s figures); the Task 4 fix round re-derived it; Task 6
    /// re-derived it again because Task 6 changed what the tabs contain. THE METHOD, unchanged: launch
    /// the build with the window temporarily switched to <c>SizeToContent="Height"</c> at
    /// <c>Width="711"</c>, select every tab in turn through UI Automation's
    /// <c>SelectionItemPattern</c>, and read the window's UIA <c>BoundingRectangle</c> after each —
    /// which for an Avalonia window is the CLIENT rectangle, confirmed against
    /// <c>GetWindowRect</c>/<c>GetClientRect</c> on the same HWND. Then restore the markup and pin the
    /// maximum. All figures below are at 150 % scale with the app's default font.
    /// </para>
    /// <list type="table">
    ///   <item><description><b>Task 2, free width, six tabs:</b> 外观 646x345, 行为 584x452,
    ///   文件夹 784x357, 常用工具 305x320, 更新 582x213, 关于 283x230 DIP. 关于 is listed because it is
    ///   what shows that Task 4 removing it moved NEITHER number — it held neither maximum: the
    ///   narrowest of the six widths, but only the second-shortest height, above 更新's 213. (Task 4
    ///   wrote "the smallest tab on both axes" here, which the 更新 row disproves; the conclusion stood,
    ///   the reason did not.)</description></item>
    ///   <item><description><b>Task 4 fix round, pinned to 711, five tabs, Chinese:</b> 外观 345.33,
    ///   行为 452, 文件夹 373.33, 常用工具 320, 更新 212.67 DIP of client height. 452 was 行为's and
    ///   nothing else's, and that is where the old <c>Height</c> came from.</description></item>
    ///   <item><description><b>Task 6, pinned to 711, five tabs, BOTH languages:</b> Chinese 外观 246,
    ///   行为 350.67, 文件夹 468.67, 常用工具 294.67, 更新 198; English 263.33, 345.33, 478.67, 289.33,
    ///   214.67. The maximum is 文件夹's 478.67, and <c>Height="479"</c> is that rounded up to the next
    ///   whole DIP.</description></item>
    /// </list>
    /// <para>
    /// <b>Task 6 moved the maximum from 行为 to 文件夹</b> — it deleted the icon column, the subtitle
    /// line and the group box from the four tabs whose controls are one homogeneous set, and split
    /// 维护 into 缓存 and 备份 on the one tab that keeps boxes. Four tabs shrank and one grew.
    /// </para>
    /// <para>
    /// <b>The 文件夹 tab's own wording was measured rather than reasoned about, because it is the tab
    /// with no slack left.</b> Its 数据文件夹 help line has three build-dependent wordings — portable,
    /// packaged and plain — and only the portable one is reachable on this machine's sandbox. So the
    /// longest, <c>DataFolderPackagedHelp</c>, was forced into the row for one throwaway build and the
    /// tab re-measured in both languages: 468.67 / 478.67 DIP, IDENTICAL to the portable wording,
    /// because at 685 DIP of content width all three still render on one line. The build was restored.
    /// The plain wording (<c>DataFolderHelp</c>) is shorter than the packaged one and was not measured
    /// separately.
    /// </para>
    /// <para>
    /// <b>The first cut of Task 6's 文件夹 tab did not fit, and the fix is recorded here because the
    /// round has a settled ruling for that case.</b> With each of the four boxes' button strips on a
    /// row of its own the tab measured 577.33 DIP of client, i.e. 614.67 DIP of outer window, which
    /// breaks the PUBLISHED 800 x 600 floor. The ruling was to split the tab in two rather than accept
    /// a scrollbar; the published sentence offers a first resort ahead of both — "Don't scroll property
    /// pages. Instead of using scrollbars, make the property pages larger and lay out the pages
    /// efficiently" — and docking the four strips beside the content they act on took 108.67 DIP off
    /// the tab, which is more than the 14.67 the floor was breached by. The split stays available and
    /// stays the answer if this tab grows again.
    /// </para>
    /// <para>
    /// <b>711 was chosen over the widest tab's 784, and that was a deviation from the design's literal
    /// instruction ("take the maximum over the six"), taken for a stated reason.</b> 784 was 文件夹's
    /// DESIRED width in Task 2, and what wanted it was one wrapping description line — the row cards'
    /// subtitle class carried <c>TextWrapping="Wrap"</c>, and <c>FoldersCard</c>'s remarks recorded it
    /// wrapping to two and three lines on the page the card came from, at the app's floor, as intended
    /// behaviour. (That class was deleted in Task 6 with the rest of the row grammar; the wrapping
    /// lines it describes are stated locally on the two folder help lines now, and the argument is
    /// recorded rather than re-asserted.) Adopting 784 would have put the OUTER window at 798.67 DIP
    /// against the PUBLISHED 800 x 600 floor — compliant by 1.33 DIP, i.e. a dialog that exactly fills
    /// an 800-pixel screen. 711 is the width the settings surface's CONTENT PANE has had since Round 4
    /// and that four measurement records in this tree rest on, and it gives each card MORE room than
    /// the page did, not less: the page inset <c>CardStack</c> by 22 a side (667 DIP of card), this
    /// dialog insets 11 a side and the vendored <c>TabControl</c> template takes 4 more (685 DIP of
    /// card). Nothing that fitted before can clip now.
    /// </para>
    /// <para>
    /// 711 x 479 client is 725.67 x 516.33 DIP outer, the frame measured as 22 x 56 physical px on this
    /// machine — 83.67 DIP of headroom under the PUBLISHED 600.
    /// </para>
    /// <para>
    /// <b>The language read is the COMMITTED one, and since Round 8, Task 6 that is true of every card
    /// on this surface too.</b> It read the draft's until then, on the argument that the cards labelled
    /// from the draft and a committed 语言 change lands in both before this runs — the second half is
    /// still true (<see cref="SettingsCommit.ApplyAsync"/> writes and re-seeds BEFORE it raises
    /// <c>SettingsChanged</c>), which is exactly why the committed value is the safe one. The first
    /// half was the defect: between a 语言 pick and the commit that lands it, nothing on this surface
    /// re-labels, so the draft's language is never what is on screen — and a card that generated a
    /// string from it mid-edit handed it to <see cref="MessageBox"/>, which resolves its own
    /// 确定/取消 captions from <see cref="ConfigService"/> and cannot be told otherwise.
    /// <see cref="ConfirmDiscardAsync"/> was corrected for that in Task 2 and was the only call site
    /// that had been; Task 6 made the rule uniform instead of case by case.
    /// </para>
    /// </remarks>
    public void ApplyText()
    {
        var lang = _config.Config.General.Language;

        Title = AppLanguage.Text("Options", lang);

        AppearanceTab.Header = AppLanguage.Text("Appearance", lang);
        BehaviorTab.Header = AppLanguage.Text("Behavior", lang);
        FoldersTab.Header = AppLanguage.Text("Folders", lang);
        ToolsTab.Header = AppLanguage.Text("CommonTools", lang);
        UpdatesTab.Header = AppLanguage.Text("Updates", lang);

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

    /// <summary>The plain dialog caption: no minimize box, no maximize box, no title-bar icon. Round 8,
    /// Task 2 measured all three defects on this very window and wrote the Win32 correction inline here;
    /// Task 4 moved it to <see cref="DialogCaption"/> unchanged when the About box turned out to need
    /// exactly the same three writes. That file carries the measurement, and the reason Avalonia's
    /// <c>SystemDecorations</c> — the design's own suggested remedy — is the wrong tool for it.
    /// <c>OnOpened</c> is the earliest point at which the HWND exists.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        DialogCaption.ApplyTo(this);
    }
}
