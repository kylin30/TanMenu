using Avalonia.Controls;

namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the main window: placement persistence and show/hide/toggle.</summary>
public interface IWindowHost
{
    void Hide();
    void ShowAndActivate();
    void Toggle();

    /// <summary>Persist the current size/position/maximized state to config. Called on move, resize and
    /// hide; cheap and idempotent.</summary>
    void SavePlacement();

    /// <summary>Temporarily suppress hide-on-blur (e.g. while a modal dialog or folder picker owns
    /// focus). Must be paired with a <c>false</c> call on EVERY exit path including exceptions, or the
    /// window stops hiding on blur for the rest of the session. Only has an effect while the user has
    /// 失焦自动隐藏 switched on, which is not the default any more.</summary>
    void SuppressHide(bool on);

    /// <summary>The main window, or null before it is attached. Used as the owner for modal dialogs.</summary>
    Window? Window { get; }
}
