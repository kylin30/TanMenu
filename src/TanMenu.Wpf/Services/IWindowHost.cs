using Avalonia.Controls;

namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the main window: placement persistence and show/hide/toggle.</summary>
public interface IWindowHost
{
    /// <summary>Take the launcher off screen. A <see cref="SuppressHide"/> in force makes it a no-op —
    /// see <c>WindowHost.Hide</c>'s remarks and the defect they record.</summary>
    void Hide();

    void ShowAndActivate();
    void Toggle();

    /// <summary>Persist the current size/position/maximized state to config. Called on move, resize and
    /// hide; cheap and idempotent.</summary>
    void SavePlacement();

    /// <summary>Temporarily hold the launcher on screen while a modal dialog or folder picker we own has
    /// the user's attention. Must be paired with a <c>false</c> call on EVERY exit path including
    /// exceptions, or the launcher stops hiding for the rest of the session.
    /// <para>
    /// IT SUPPRESSES <see cref="Hide"/> ITSELF, not only hide-on-blur, and that widening is the Round 8
    /// review's HIGH fix: hiding the launcher does not close the dialog it owns, it strands it —
    /// <c>WindowHost.Hide</c>'s remarks carry the measurement. An earlier revision of this line said it
    /// "only has an effect while the user has 失焦自动隐藏 switched on"; that was true of the one caller
    /// that tested the count and never of the three that did not.
    /// </para></summary>
    void SuppressHide(bool on);

    /// <summary>The main window, or null before it is attached. Used as the owner for modal dialogs.</summary>
    Window? Window { get; }
}
