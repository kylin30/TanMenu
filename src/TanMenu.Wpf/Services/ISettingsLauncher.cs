using Avalonia.Controls;
using Avalonia.Threading;
using TanMenu.Wpf.Dialogs;

namespace TanMenu.Wpf.Services;

/// <summary>Shows the 选项 dialog. Round 8, Task 2 made it a modal window again — Task 13 had absorbed
/// the old standalone settings window into the shell as a page, and a page cannot carry the delayed
/// commit's 确定/取消 (PUBLISHED, <c>win-property-win</c>: "Properties for an application are displayed
/// in a modal dialog box").</summary>
public interface ISettingsLauncher
{
    /// <summary>Completes when the dialog CLOSES, not when it opens. Every call site is
    /// fire-and-forget (<see cref="Open"/>), so nothing waits on it today; the signature is kept
    /// because it is what lets a caller wait if one ever wants to, and because changing it would touch
    /// the tray menu and the empty state for no gain.</summary>
    Task OpenAsync();

    /// <summary>Fire-and-forget form for the call sites that only want the dialog on screen and have no
    /// result to wait for. THREE of them, grepped rather than remembered: the tray menu
    /// (<c>App.OnLauncherOpened</c>), the 应用 page's empty-state 打开设置… button
    /// (<c>LauncherViewModel.OpenSettings</c>), and the menu band's 设置 item
    /// (<c>MainWindow.OnOptionsClick</c>). The band replaced the navigation pane this list used to name,
    /// in Round 8, Task 3, which briefly added a fourth — 关于, which opened this dialog and then
    /// selected its 关于 tab. Round 8, Task 4 gave 关于 a window of its own (<c>Dialogs/AboutWindow</c>)
    /// and took that caller away again.</summary>
    void Open();
}

public sealed class SettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;

    public SettingsLauncher(IWindowHost host)
    {
        _host = host;
    }

    /// <summary>Fire-and-forget, but NOT fault-and-forget. <c>_ = OpenAsync()</c> on its own drops any
    /// exception into an unobserved Task, and the whole of this path — a XAML load, five card
    /// constructors, a DI resolve, <c>ShowDialog</c> — is code that can throw at run time and produce
    /// nothing but a dialog that silently does not appear. Round 8, Task 2 hit exactly that while
    /// building this method and added the continuation rather than the log line it needed once.</summary>
    public void Open() =>
        _ = OpenAsync().ContinueWith(
            t => Serilog.Log.Error(t.Exception, "打开选项对话框失败"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// <summary>Marshalled to the UI thread — the tray menu calls this from a shell thread — and then
    /// awaited to the dialog's close, so a fault inside <see cref="OpenCoreAsync"/> surfaces on the
    /// returned Task instead of on an async void.</summary>
    public Task OpenAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
            return OpenCoreAsync();

        var marshalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await OpenCoreAsync();
                marshalled.TrySetResult();
            }
            catch (Exception ex)
            {
                marshalled.TrySetException(ex);
            }
        });
        return marshalled.Task;
    }

    /// <summary>
    /// Bring the launcher forward, then run the Options dialog modally on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SuppressHide brackets the dialog's WHOLE lifetime</b>, exactly as the pre-<c>28158a8</c>
    /// <c>OpenCoreAsync</c> did before settings became a page. Without it a user with 失焦自动隐藏 on
    /// loses the launcher the instant the dialog takes focus — and the dialog is modal ON the launcher,
    /// so hiding its owner is not a cosmetic problem. The count is reference-based
    /// (<c>WindowHost.SuppressHide</c>), so every picker, message box and hotkey warning raised from
    /// inside the dialog nests safely under this one.
    /// </para>
    /// <para>
    /// <b>The hidden-owner fallback is not optional.</b> <c>ShowDialog</c> throws
    /// <c>InvalidOperationException("Cannot show window with non-visible owner.")</c> and the launcher
    /// is routinely hidden — closed to the tray, toggled off by the hotkey. <c>ShowAndActivate</c> above
    /// normally fixes that, but it is a no-op when no window has been attached yet, so the same test
    /// <c>MessageBox.ShowCoreAsync</c> makes is made here and a null owner falls back to a standalone,
    /// centred window.
    /// </para>
    /// <para>
    /// <b>Single instance is <c>OptionsWindow.Current</c></b> — a static field the window sets as the
    /// LAST statement of its constructor and clears in <c>OnClosed</c>. A second call while one is up
    /// activates the one that exists instead of stacking a second modal on the same owner. That same
    /// field is what <c>App.WarnHotkeyRegistrationFailedAsync</c> re-owners its warning to. The
    /// ordering inside that constructor is this branch's business: if the field were claimed before the
    /// XAML load and the five <c>ReloadFromConfig</c>s — the very code the <see cref="Open"/> remark
    /// above records throwing once — a constructor that faulted would leave <c>Current</c> pointing at
    /// a window that never opens and therefore never reaches <c>OnClosed</c>, and this branch would
    /// then <c>Activate()</c> that dead window, silently, for the rest of the session.
    /// </para>
    /// </remarks>
    private async Task OpenCoreAsync()
    {
        if (OptionsWindow.Current is { } existing)
        {
            existing.Activate();
            return;
        }

        _host.ShowAndActivate();
        _host.SuppressHide(true);
        try
        {
            var dialog = new OptionsWindow();
            var owner = _host.Window is { IsVisible: true } window ? window : null;

            if (owner is not null)
            {
                await dialog.ShowDialog(owner);
                return;
            }

            // No usable owner: nothing to be modal to, so show it standalone and wait for it to close.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }
}
