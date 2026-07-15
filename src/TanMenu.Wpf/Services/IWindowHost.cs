namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the launcher window: content-size + placement, show/hide/toggle.</summary>
public interface IWindowHost
{
    /// <summary>Request the browser's current ResizeObserver-reported theme-root size and place the
    /// window bottom-center. Returns false only when no valid browser report is available yet.</summary>
    Task<bool> ResizeToContentAndPlaceAsync();

    void Hide();
    void ShowAndActivate();
    void Toggle();

    /// <summary>Temporarily suppress hide-on-blur (e.g. while a modal folder picker is open).</summary>
    void SuppressHide(bool on);

    /// <summary>Fingerprint of the currently-built menu content, used for fit diagnostics. Actual
    /// sizing is driven by the browser's rendered root rather than this version.</summary>
    int ContentVersion { get; set; }
}
