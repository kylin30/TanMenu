namespace TanMenu.Wpf.Services;

/// <summary>
/// App-wide UI events bridging the native settings window and the Blazor launcher
/// (separate windows sharing the singleton ConfigService).
/// </summary>
public sealed class AppEvents
{
    /// <summary>Raised after settings (folders / theme / options) change so the launcher reloads.</summary>
    public event Action? SettingsChanged;

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();
}
