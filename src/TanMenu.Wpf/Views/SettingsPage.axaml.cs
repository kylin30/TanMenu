using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.Views.Settings;

namespace TanMenu.Wpf.Views;

/// <summary>The 设置 page. Owns layout and, since Round 8, Task 1, the commit row: each card group reads
/// and writes the shared <see cref="SettingsDraft"/>, and 确定 / 取消 / 应用 here are the only things
/// that reach disk or the OS.</summary>
public partial class SettingsPage : UserControl, IDisposable
{
    private readonly AppEvents _events;
    private readonly ConfigService _config;
    private readonly SettingsDraft _draft;
    private readonly IAutoStartService _autoStart;
    private readonly IWindowHost _host;

    /// <summary>Set for the duration of a commit. <see cref="SettingsCommit.ApplyAsync"/> awaits a disk
    /// write and, on failure, a modal dialog, so without this a second click during either would run a
    /// second <c>CopyOnto</c> + <c>SaveAsync</c> against the same draft.</summary>
    private bool _committing;

    public SettingsPage()
    {
        InitializeComponent();
        _events = App.Services.GetRequiredService<AppEvents>();
        _config = App.Services.GetRequiredService<ConfigService>();
        _draft = App.Services.GetRequiredService<SettingsDraft>();
        _autoStart = App.Services.GetRequiredService<IAutoStartService>();
        _host = App.Services.GetRequiredService<IWindowHost>();

        // AppLanguage has no change notification, and a card's own change handler has no reference
        // back to this page (each card only knows the draft), so re-labelling after a language change
        // goes through the same coarse SettingsChanged signal every other listener in the app uses
        // (MainWindow's rail, TrayService, ...). SettingsCommit raises it once per commit.
        _events.SettingsChanged += ApplyStaticText;

        // 应用 lives outside the cards' visual tree, so it cannot watch them; it watches the draft.
        _draft.DirtyChanged += ApplyDirtyState;

        ApplyStaticText();
        ApplyDirtyState();
    }

    /// <summary>AppLanguage has no change notification, so labels are applied imperatively. A language
    /// change calls this and, for every ISettingsCard among CardStack's children, its ReloadFromConfig —
    /// found structurally rather than through one hardcoded call per group, so a group added to the XAML
    /// without wiring it in here (there is nothing to wire) can never be left stale.</summary>
    public void ApplyStaticText()
    {
        // The DRAFT's language, not the live config's: it is what the cards themselves label from, and
        // a committed language change has already landed in both by the time this runs.
        var lang = _draft.Working.General.Language;
        PageTitle.Text = AppLanguage.Text("NavSettings", lang);
        OkBtn.Content = AppLanguage.Text("Ok", lang);
        CancelBtn.Content = AppLanguage.Text("Cancel", lang);
        ApplyBtn.Content = AppLanguage.Text("Apply", lang);
        foreach (var card in CardStack.Children.OfType<ISettingsCard>())
            card.ReloadFromConfig();
    }

    /// <summary>应用 is enabled exactly while there is something to apply. 确定 and 取消 stay enabled
    /// unconditionally — PUBLISHED, <c>win-property-win</c>: only Apply is "initially disabled" and
    /// "enabled when users make a change".</summary>
    private void ApplyDirtyState() => ApplyBtn.IsEnabled = _draft.IsDirty;

    /// <summary>确定. On the PAGE this is the same commit 应用 performs, because there is no window to
    /// close — Round 8, Task 2 gives it the close half when the six groups move into the modal Options
    /// dialog. It still awaits the commit rather than firing and forgetting, which is what makes the
    /// hotkey-registration warning's owner resolution deterministic once that dialog exists.</summary>
    private void OnOkClick(object? sender, RoutedEventArgs e) => RunSafely(CommitAsync);

    private void OnApplyClick(object? sender, RoutedEventArgs e) => RunSafely(CommitAsync);

    /// <summary>取消 discards, and there is nothing to undo — no config field, no registry value and no
    /// theme repaint ever left the draft. On the page it re-seeds rather than closing anything, which
    /// puts every control back to what is committed and takes 应用 to disabled.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (_committing)
            return;

        _draft.Reseed();
        ApplyStaticText(); // re-populates every card from the freshly re-taken clone
    }

    private async Task CommitAsync()
    {
        if (_committing)
            return;

        _committing = true;
        try
        {
            // The row is dead for the duration: the save is awaited and a failure is a modal dialog.
            CommitRow.IsEnabled = false;
            await SettingsCommit.ApplyAsync(_draft, _config, _events, _autoStart, _host, this);
        }
        finally
        {
            _committing = false;
            CommitRow.IsEnabled = true;
            ApplyDirtyState(); // re-assert 应用's state after the row came back enabled
        }
    }

    /// <summary>Run an async click handler without letting a fault escape the event — same rationale as
    /// every card group's own RunSafely: Avalonia has no WPF-style global DispatcherUnhandledException
    /// hook, so an escaping async-void fault would tear the process down, and logging is the floor.
    /// SettingsCommit already reports a save failure itself; this is the floor under everything else.
    /// </summary>
    private static async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "设置提交失败"); }
    }

    /// <summary>Unsubscribe from the shared AppEvents singleton and from the draft, which outlives this
    /// page (both are DI singletons). Called from <c>MainWindow.OnClosed</c> — the same real-exit-only
    /// path that disposes <c>AppPage</c> — so this page's subscriptions do not outlive the window that
    /// owns it.</summary>
    public void Dispose()
    {
        _events.SettingsChanged -= ApplyStaticText;
        _draft.DirtyChanged -= ApplyDirtyState;
    }
}
