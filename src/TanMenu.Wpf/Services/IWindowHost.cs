namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the launcher window: content-size + placement, show/hide/toggle.</summary>
public interface IWindowHost
{
    /// <summary>Measure the rendered #form-content and size+place the window bottom-center.</summary>
    Task ResizeToContentAndPlaceAsync();

    void Hide();
    void ShowAndActivate();
    void Toggle();

    /// <summary>Temporarily suppress hide-on-blur (e.g. while a modal folder picker is open).</summary>
    void SuppressHide(bool on);
}
