namespace TanMenu.Wpf.Services;

/// <summary>
/// App-wide UI events bridging the settings window and the launcher window (separate windows sharing
/// the singleton ConfigService).
/// </summary>
/// <remarks>
/// THREAD-AFFINITY CONTRACT: both events are raised SYNCHRONOUSLY on the caller's thread, and every
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
}
