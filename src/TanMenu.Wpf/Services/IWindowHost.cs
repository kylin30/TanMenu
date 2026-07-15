namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the launcher window: content-size + placement, show/hide/toggle.</summary>
public interface IWindowHost
{
    /// <summary>Measure the rendered #form-content and size+place the window bottom-center. Returns true
    /// if sized+placed; false if the measurement failed or was degenerate (not cached → next call re-measures).</summary>
    Task<bool> ResizeToContentAndPlaceAsync();

    void Hide();
    void ShowAndActivate();
    void Toggle();

    /// <summary>Temporarily suppress hide-on-blur (e.g. while a modal folder picker is open).</summary>
    void SuppressHide(bool on);

    /// <summary>Fingerprint of the currently-built menu content. The launcher sets it after each
    /// rebuild; reveal-time placement skips re-measuring the WebView while this is unchanged.</summary>
    int ContentVersion { get; set; }
}
