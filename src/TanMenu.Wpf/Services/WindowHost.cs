using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
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
    private readonly IShellNavigator _navigator;
    private readonly DispatcherTimer _saveDebounce;
    private Window? _window;
    private int _suppressDepth;
    private bool _restoring;

    public WindowHost(ConfigService config, AppEvents events, IShellNavigator navigator)
    {
        _config = config;
        _events = events;
        _navigator = navigator;

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

        // 窗口置顶 has to reach a window that is already on screen. Restore applies it too, but that
        // only runs on a cold start or an un-hidden window, so without this the checkbox would appear
        // to do nothing until the next hide/show cycle.
        _events.SettingsChanged += ApplyTopmost;
        ApplyTopmost();

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

    /// <summary>Push the current 窗口置顶 setting onto a live window.</summary>
    private void ApplyTopmost()
    {
        if (_window is not null)
            _window.Topmost = _config.Config.General.TopMost;
    }

    /// <summary>Last-resort work area when the platform reports no screens at all. Only reachable on a
    /// headless/remote session where <see cref="Screens"/> is empty.</summary>
    private static readonly PlacementRect UnknownWorkArea = new(0, 0, 1920, 1040);

    /// <summary>The screen the window currently sits on, falling back to the primary screen and then to
    /// the first enumerated one. Null when no screen information is available yet.</summary>
    /// <remarks>
    /// For SAVING only. Do NOT use this to restore: at restore time the window has not been placed
    /// yet, so this reports wherever Win32 cascaded it (the primary) rather than the monitor the saved
    /// rectangle belongs to. <see cref="ResolveTargetScreen"/> is the restore-side answer.
    /// </remarks>
    private Screen? CurrentScreen()
    {
        var screens = _window?.Screens;
        return (_window is not null ? screens?.ScreenFromWindow(_window) : null)
               ?? screens?.Primary
               ?? screens?.All.FirstOrDefault();
    }

    /// <summary>Pixels-per-DIP of the screen the window is on RIGHT NOW. Deliberately NOT cached:
    /// dragging the window from a 100% monitor to a 150% one changes it, and converting a position with
    /// a stale scale records a placement wrong by the ratio between the two.</summary>
    private double CurrentScale()
    {
        var scaling = CurrentScreen()?.Scaling ?? 1.0;
        return scaling <= 0 ? 1.0 : scaling;
    }

    private static double SafeScale(Screen screen) => screen.Scaling <= 0 ? 1.0 : screen.Scaling;

    /// <summary>A screen's work area in DIPs, converted with THAT screen's own scaling — the exact
    /// inverse of what <see cref="SavePlacement"/> records.</summary>
    private static PlacementRect WorkAreaOf(Screen screen)
    {
        var scale = SafeScale(screen);
        var w = screen.WorkingArea;
        return new PlacementRect(w.X / scale, w.Y / scale, w.Width / scale, w.Height / scale);
    }

    /// <summary>
    /// Choose the monitor a SAVED placement should be restored onto, and hand back both its work area
    /// (DIPs) and its scaling. Falls back to a 1920×1040 guess at scale 1 when the platform reports no
    /// screens.
    /// </summary>
    /// <remarks>
    /// The choice is driven by the saved rectangle, never by the window's current position: the window
    /// has not been placed yet when this runs, so its position is whatever Win32 cascaded it to. The
    /// decision itself lives in <see cref="WindowPlacement.ChooseWorkArea"/> so it can be unit-tested
    /// without a display; this method only translates Avalonia's screen list into and out of it. The
    /// scaling returned is the TARGET screen's, not the current one's — converting a saved DIP position
    /// with the wrong monitor's scale lands the window off by the ratio between the two.
    /// </remarks>
    private (PlacementRect Work, double Scale) ResolveTargetScreen(PlacementRect? saved)
    {
        var all = _window?.Screens?.All;
        if (all is null || all.Count == 0)
            return (UnknownWorkArea, 1.0);

        var areas = new PlacementRect[all.Count];
        var primaryIndex = 0;
        for (var i = 0; i < all.Count; i++)
        {
            areas[i] = WorkAreaOf(all[i]);
            if (all[i].IsPrimary)
                primaryIndex = i;
        }

        var index = WindowPlacement.ChooseWorkArea(saved, areas, primaryIndex);
        return index < 0 ? (UnknownWorkArea, 1.0) : (areas[index], SafeScale(all[index]));
    }

    /// <summary>The remembered window rectangle in DIPs, or null when no POSITION is remembered (a
    /// fresh config). Null is what tells <see cref="WindowPlacement.ChooseWorkArea"/> to centre on the
    /// primary rather than hunt for a monitor.</summary>
    private PlacementRect? SavedRect()
    {
        var g = _config.Config.General;
        return g.WindowLeft is { } left && g.WindowTop is { } top
            ? new PlacementRect(left, top, g.WindowWidth, g.WindowHeight)
            : null;
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

    /// <summary>Apply the saved placement, clamped to the work area of the monitor the SAVED rectangle
    /// belongs to. Never throws: a failure here must not prevent the window from being shown.</summary>
    /// <remarks>
    /// Called BEFORE <see cref="Window.Show"/> on a cold start (<c>App.ShowLauncherWindow</c>) and
    /// before every un-hide (<see cref="ShowAndActivate"/>), never afterwards: applying geometry to a
    /// window that is already on screen paints it once at the OS cascade position and then jumps it,
    /// which is exactly what deleting the flyout's <c>Opacity="0"</c> assumed could not happen.
    /// </remarks>
    public void RestorePlacement()
    {
        if (_window is null)
            return;

        _restoring = true;
        try
        {
            var g = _config.Config.General;
            var (work, scale) = ResolveTargetScreen(SavedRect());

            var placement = WindowPlacement.Resolve(
                g.WindowLeft, g.WindowTop, g.WindowWidth, g.WindowHeight,
                work, MinWidthDip, MinHeightDip);

            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));
            _window.WindowState = g.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
            ApplyTopmost();

            // Logged at Information, not Debug: this is the only externally visible record of WHICH
            // monitor was chosen and at what scale, and it is what proves the placement was applied
            // before the window was shown (the "Launcher window shown" line follows it).
            Serilog.Log.Information(
                "Window placement applied: {Left},{Top} {Width}x{Height} DIP on work area " +
                "{WorkLeft},{WorkTop} {WorkWidth}x{WorkHeight} at scale {Scale}",
                placement.Left, placement.Top, placement.Width, placement.Height,
                work.Left, work.Top, work.Width, work.Height, scale);
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
    /// hundreds of times per drag. <see cref="Hide"/> cancels the debounce and starts the write
    /// immediately (still asynchronously) so a hide does not sit on an unwritten position;
    /// <see cref="FlushPlacementBlocking"/> is the variant 退出 uses, which also WAITS for it.
    /// </remarks>
    public void SavePlacement()
    {
        if (_window is null || _restoring)
            return;

        try
        {
            var state = _window.WindowState;

            // Minimized carries NO placement information: its Width/Height/Position say nothing about
            // what to restore to, and reading it as "not maximized" would erase a maximized window's
            // remembered state the moment the user minimizes it.
            if (state == WindowState.Minimized)
                return;

            var g = _config.Config.General;
            g.WindowMaximized = state == WindowState.Maximized;

            // A maximized window's Width/Height are the screen's, not the size to restore to, so the
            // remembered "normal" geometry is deliberately left alone while maximized.
            if (state == WindowState.Normal)
            {
                var scale = CurrentScale();
                g.WindowWidth = _window.Width;
                g.WindowHeight = _window.Height;
                g.WindowLeft = _window.Position.X / scale;
                g.WindowTop = _window.Position.Y / scale;
            }

            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to record window placement");
        }
    }

    /// <summary>Start writing the recorded placement to disk, cancelling any pending debounced write.
    /// FIRE-AND-FORGET — the returned write may still be in flight when this returns. Use
    /// <see cref="FlushPlacementBlocking"/> on any path that is about to end the process.</summary>
    public void FlushPlacement() => _ = FlushPlacementAsync();

    private Task FlushPlacementAsync()
    {
        _saveDebounce.Stop();
        try
        {
            return _config.SaveAsync();
        }
        catch (Exception ex)
        {
            // SaveAsync only throws synchronously if serialization itself fails; a disk failure comes
            // back on the task. Either way a placement write must never take a caller down.
            Serilog.Log.Warning(ex, "Failed to start a window placement flush");
            return Task.CompletedTask;
        }
    }

    /// <summary>Flush the recorded placement and BLOCK until the write has finished. For
    /// <see cref="App.ExitApp"/> only: the fire-and-forget form yields at the first await inside
    /// <c>SaveAsync</c>, and <c>desktop.Shutdown()</c> then ends the dispatcher loop out from under it,
    /// losing any geometry change made inside the 500 ms debounce window and potentially stranding an
    /// orphan <c>config.json.tmp</c>. Blocking here is only deadlock-free because <c>SaveAsync</c>
    /// awaits with <c>ConfigureAwait(false)</c> throughout — see its remarks.</summary>
    public void FlushPlacementBlocking()
    {
        try
        {
            // Bounded rather than indefinite: the data folder can be a network share, and 退出 must
            // still end the process if that share is wedged. A few-KB write never approaches this.
            if (!FlushPlacementAsync().Wait(TimeSpan.FromSeconds(5)))
                Serilog.Log.Warning("Timed out flushing the window placement on exit");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to flush the window placement on exit");
        }
    }

    public void Hide()
    {
        SavePlacement();
        FlushPlacement(); // hiding is often the last thing before exit; do not leave it to the timer
        _window?.Hide();

        // Reset the shell to 应用 AFTER the window is off screen, so the page swap is never visible.
        // Spec §2: "the window always opens on 应用". ShellNavigator.Current is process-lifetime state
        // that survives hide/show, so without this a session that visited 设置 re-summons onto 设置 —
        // with AppPage detached, which also silently kills the search box's type-to-filter focus.
        // Done on HIDE rather than inside ShowAndActivate deliberately: SettingsLauncher.OpenCore
        // navigates to 设置 and THEN calls ShowAndActivate, so resetting on show would break the tray's
        // 选项 entry.
        _navigator.Navigate(ShellPage.App);
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
        var handle = WindowHandle();
        if (handle != IntPtr.Zero)
            SetForegroundWindow(handle);

        _window.Focus();
        _events.RaiseLauncherShown(); // lets the app page clear + focus the search box
    }

    /// <summary>
    /// The hotkey / tray-click gesture: dismiss the launcher if it is what you are looking at, summon
    /// it otherwise.
    /// </summary>
    /// <remarks>
    /// "Visible" is NOT "focused". Under the flyout, 失焦自动隐藏 defaulted to true, so a visible window
    /// was always the foreground one and the two were interchangeable. Since 0.12 the window is an
    /// ordinary taskbar-resident app with hide-on-blur OFF by default, so it routinely sits visible
    /// behind whatever the user is working in — and treating that as "dismiss" made the global hotkey
    /// and the tray left-click HIDE the launcher the user was trying to summon. Ask Windows who is
    /// actually in front instead.
    /// </remarks>
    public void Toggle()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized)
        {
            ShowAndActivate();
            return;
        }

        if (IsForeground())
            Hide();
        else
            ShowAndActivate(); // visible but buried: raise it, do not dismiss it
    }

    private IntPtr WindowHandle() => _window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    /// <summary>True when our window is the system's foreground window. A modal we own (a MessageBox,
    /// a folder picker) is a DIFFERENT HWND, so it reads as "not foreground" and Toggle raises rather
    /// than hides — which is the right answer while a dialog is up.</summary>
    private bool IsForeground()
    {
        var handle = WindowHandle();
        return handle != IntPtr.Zero && GetForegroundWindow() == handle;
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

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
}
