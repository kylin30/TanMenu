using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the main window: restores its saved placement, persists changes, and handles
/// show/hide/toggle plus opt-in hide-on-blur.
/// </summary>
/// <remarks>
/// Until 0.11 this class measured the rendered content, clamped it to the work area and placed a
/// content-sized popup bottom-center on every reveal. The window is an ordinary resizable application
/// window now, so all of that — the measurement pass, the reveal fade, the SetWindowPos placement and
/// the ContentVersion fingerprint that triggered re-measurement — is gone. What remains is placement
/// persistence, which the popup never needed because it had no user-chosen geometry to remember.
/// </remarks>
public sealed partial class WindowHost : IWindowHost
{
    /// <summary>Floor for the restored size. Below this the navigation rail and the content area stop
    /// coexisting; it matches MainWindow's MinWidth/MinHeight.</summary>
    private const double MinWidthDip = 760;
    private const double MinHeightDip = 520;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly DispatcherTimer _saveDebounce;
    private Window? _window;
    private int _suppressDepth;
    private bool _restoring;

    public WindowHost(ConfigService config, AppEvents events)
    {
        _config = config;
        _events = events;

        // Coalesces the PositionChanged/size storm a single drag produces into one disk write.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += (_, _) => FlushPlacement();
    }

    public Window? Window => _window;

    /// <summary>Bind the host to the main window. Idempotent — re-attaching the same window is a no-op,
    /// so it does not matter whether startup or the window's own code-behind calls it.</summary>
    public void Attach(Window window)
    {
        if (ReferenceEquals(_window, window))
            return;

        _window = window;

        window.Deactivated += (_, _) =>
        {
            // Config is read live on every deactivation (never cached) so toggling 失焦自动隐藏 in
            // settings takes effect immediately. Off by default since 0.12.
            if (_config.Config.General.AutoClose && _suppressDepth == 0)
                Hide();
        };

        // Persist geometry as the user changes it rather than only on exit: the process can be ended
        // by an update handoff or a hard kill, and a lost window position is a visible annoyance.
        // _restoring guards against writing back the values we are in the middle of applying.
        window.PositionChanged += (_, _) => { if (!_restoring) SchedulePlacementSave(); };
        window.PropertyChanged += (_, e) =>
        {
            if (_restoring)
                return;
            if (e.Property == Window.WindowStateProperty ||
                e.Property == Window.WidthProperty ||
                e.Property == Window.HeightProperty)
                SchedulePlacementSave();
        };
    }

    /// <summary>
    /// Defer to the next dispatcher cycle instead of calling <see cref="SavePlacement"/> inline from
    /// the property-changed handler above.
    /// </summary>
    /// <remarks>
    /// A maximize transition raises WidthProperty/HeightProperty-changed — carrying the new,
    /// screen-filling values — BEFORE WindowStateProperty flips to Maximized, all synchronously
    /// within the same WM_SIZE handling. Calling SavePlacement synchronously from the Width/Height
    /// handler therefore reads a still-stale WindowState of Normal, and its "leave the remembered
    /// normal geometry alone while maximized" guard sees Normal and wrongly persists the
    /// screen-filling size as the restore geometry — confirmed by launching the window, maximizing it,
    /// and closing (hiding) it while still maximized: WindowWidth/Height/Left/Top were overwritten
    /// with the maximized, near-zero-origin values instead of being left at the pre-maximize geometry.
    /// Posting defers the read until the whole synchronous transition has finished, so by the time
    /// SavePlacement runs, WindowState already reflects its final value.
    /// </remarks>
    private void SchedulePlacementSave() => Dispatcher.UIThread.Post(SavePlacement);

    /// <summary>Apply the saved placement, clamped to the work area the window will actually appear on.
    /// Never throws: a failure here must not prevent the window from being shown.</summary>
    public void RestorePlacement()
    {
        if (_window is null)
            return;

        _restoring = true;
        try
        {
            var g = _config.Config.General;
            var work = ResolveWorkArea();

            var placement = WindowPlacement.Resolve(
                g.WindowLeft, g.WindowTop, g.WindowWidth, g.WindowHeight,
                work, MinWidthDip, MinHeightDip);

            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * _scale),
                (int)Math.Round(placement.Top * _scale));
            _window.WindowState = g.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
            _window.Topmost = g.TopMost;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to restore window placement; using defaults");
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Updates the in-memory config immediately but DEBOUNCES the disk write. A single window drag
    /// raises PositionChanged on every mouse move, and persisting each one would rewrite config.json
    /// hundreds of times per drag. <see cref="Hide"/> flushes synchronously so the common
    /// hide-then-exit path never loses the last position.
    /// </remarks>
    public void SavePlacement()
    {
        if (_window is null || _restoring)
            return;

        try
        {
            var g = _config.Config.General;
            var maximized = _window.WindowState == WindowState.Maximized;
            g.WindowMaximized = maximized;

            // A maximized window's Width/Height are the screen's, not the size to restore to, so the
            // remembered "normal" geometry is deliberately left alone while maximized.
            if (!maximized && _window.WindowState == WindowState.Normal)
            {
                g.WindowWidth = _window.Width;
                g.WindowHeight = _window.Height;
                g.WindowLeft = _window.Position.X / _scale;
                g.WindowTop = _window.Position.Y / _scale;
            }

            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to record window placement");
        }
    }

    /// <summary>Write the recorded placement to disk now, cancelling any pending debounced write.</summary>
    private void FlushPlacement()
    {
        _saveDebounce.Stop();
        _ = _config.SaveAsync();
    }

    private double _scale = 1.0;

    /// <summary>The work area of the screen the window currently sits on, in DIPs, falling back to the
    /// primary screen and finally to a 1920×1040 guess when no screen information is available.</summary>
    private PlacementRect ResolveWorkArea()
    {
        var screens = _window?.Screens;
        var screen = (_window is not null ? screens?.ScreenFromWindow(_window) : null)
                     ?? screens?.Primary
                     ?? screens?.All.FirstOrDefault();
        if (screen is null)
            return new PlacementRect(0, 0, 1920, 1040);

        _scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var w = screen.WorkingArea;
        return new PlacementRect(w.X / _scale, w.Y / _scale, w.Width / _scale, w.Height / _scale);
    }

    public void Hide()
    {
        SavePlacement();
        FlushPlacement(); // hiding is often the last thing before exit; do not leave it to the timer
        _window?.Hide();
    }

    public void ShowAndActivate()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible)
        {
            RestorePlacement();
            _window.Show();
        }
        else if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = _config.Config.General.WindowMaximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        _window.Activate();

        // Activate() alone is unreliable when the foreground window belongs to another process.
        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            SetForegroundWindow(handle);

        _window.Focus();
        _events.RaiseLauncherShown(); // lets the app page clear + focus the search box
    }

    public void Toggle()
    {
        if (_window is null)
            return;
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
            Hide();
        else
            ShowAndActivate();
    }

    /// <summary>
    /// Hold off hide-on-blur while something modal owns focus. REFERENCE-COUNTED, not a flag: the
    /// suppressions genuinely overlap — a folder picker raised from a settings card suppresses on top
    /// of whatever suppressed before it. Unbalanced releases are clamped at zero rather than going
    /// negative, which would permanently disable hide-on-blur for the session.
    /// </summary>
    public void SuppressHide(bool on)
    {
        if (on)
            _suppressDepth++;
        else if (_suppressDepth > 0)
            _suppressDepth--;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
