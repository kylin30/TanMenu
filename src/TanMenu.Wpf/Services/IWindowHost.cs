using Avalonia.Controls;

namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the launcher window: content-size + placement, show/hide/toggle.</summary>
public interface IWindowHost
{
    /// <summary>Measure the launcher's natural content size, fit it to the primary monitor's work
    /// area and place the window bottom-center. Returns false only when no valid size is available
    /// yet (e.g. the first layout pass hasn't produced one). Never throws.</summary>
    Task<bool> ResizeToContentAndPlaceAsync();

    void Hide();
    void ShowAndActivate();
    void Toggle();

    /// <summary>Temporarily suppress hide-on-blur (e.g. while a modal dialog or folder picker owns
    /// focus). Must be paired with a <c>false</c> call on EVERY exit path including exceptions,
    /// or the launcher stops hiding on blur for the rest of the session.</summary>
    void SuppressHide(bool on);

    /// <summary>Fingerprint of everything that can change the rendered size (menu content, button
    /// size, column count, font, language). Setting it to a new value re-measures and re-places the
    /// launcher when it is visible, and resets the once-per-content fit log.</summary>
    int ContentVersion { get; set; }

    /// <summary>The launcher window, or null before it is attached. Used as the owner for modal
    /// dialogs so they stay attached to the popup instead of floating free.</summary>
    Window? Window { get; }
}
