namespace TanMenu.Wpf.Services;

/// <summary>
/// App-wide UI notifications for state that has no change notification of its own — the singleton
/// <c>ConfigService</c>'s contents, "the window was just summoned", and a request to return the
/// existing window to its launcher page.
/// </summary>
/// <remarks>
/// This existed to bridge two separate top-level windows. There is one window now (设置 became a page
/// inside it in 0.12), but the events are not redundant: the publishers and subscribers are still
/// unrelated — the settings surface commits (once per 确定/应用 since Round 8, Task 1, rather than once
/// per control change), and the shell, the tray, the launcher view model, the window host's 窗口置顶 and
/// the taskbar-pin notice all have to react without any of them knowing about each other.
/// </remarks>
/// <remarks>
/// THREAD-AFFINITY CONTRACT: all events are raised SYNCHRONOUSLY on the caller's thread, and every
/// raise site is on the UI thread. Subscribers rely on that — the taskbar-pin subscriber, for
/// example, immediately re-posts to <c>Dispatcher.UIThread</c> and would misbehave if it were
/// already running on a worker. If a future raise site is not on the UI thread, it must marshal
/// first (<c>Dispatcher.UIThread.Post</c>) rather than relaxing this contract.
/// </remarks>
public sealed class AppEvents
{
    /// <summary>Raised after settings (folders / options / data folder / cache clear) change so the
    /// launcher reloads.</summary>
    public event Action? SettingsChanged;

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    /// <summary>Raised when the launcher window is summoned/shown, so the UI can clear and focus
    /// the search box for immediate type-to-filter.</summary>
    public event Action? LauncherShown;

    public void RaiseLauncherShown() => LauncherShown?.Invoke();

    /// <summary>Raised when a second process asks the existing instance to activate. The shell uses
    /// this distinct request to return to the Apps page before the window is shown and focused;
    /// ordinary tray/hotkey summons keep their existing page semantics.</summary>
    public event Action? LauncherPageRequested;

    public void RaiseLauncherPageRequested() => LauncherPageRequested?.Invoke();
}
