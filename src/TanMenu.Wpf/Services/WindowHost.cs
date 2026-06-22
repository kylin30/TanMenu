namespace TanMenu.Wpf.Services;

/// <summary>
/// M2 stub — no-op placement/show/hide so the project builds and renders.
/// Replaced with the real WPF implementation in M3 (measure #form-content, DPI,
/// bottom-center placement, hide-on-blur).
/// </summary>
public sealed class WindowHost : IWindowHost
{
    public Task ResizeToContentAndPlaceAsync() => Task.CompletedTask;
    public void Hide() { }
    public void ShowAndActivate() { }
    public void Toggle() { }
    public void SuppressHide(bool on) { }
}
