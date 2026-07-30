using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the launcher window. Measures the rendered content against the width the launcher is allowed
/// to occupy, clamps the result to the monitor work area, places the window bottom-center on the
/// primary monitor, and handles show/hide/toggle + hide-on-blur.
/// </summary>
/// <remarks>
/// <para>
/// The WPF build got its size from a browser-side <c>ResizeObserver</c> over WebView2's message
/// channel, which is why the old code had a request/response protocol with a 500 ms timeout and a
/// TaskCompletionSource identity guard. Native layout is synchronous, so all of that collapses into
/// <see cref="TryMeasureNaturalSize"/>.
/// </para>
/// <para>
/// The Fluent 2 rebuild also removed the fit-to-work-area <c>ScaleTransform</c> this class used to
/// apply. Shrinking the entire UI was a workaround for a grid that could not wrap or scroll: the
/// retro layout was one non-wrapping row of group boxes, so an oversized menu could only be made to
/// fit by zooming it out. Tiles wrap and the body scrolls now, so oversize is handled by the layout
/// itself and the window simply clamps.
/// </para>
/// </remarks>
public sealed partial class WindowHost : IWindowHost
{
    private const int MinWidthDip = 200;
    private const int MinHeightDip = 100;

    /// <summary>The widest the launcher is allowed to get, regardless of how much room the monitor
    /// has. Past roughly this width a wrapping tile grid stops reading as columns and starts reading
    /// as a wall, and the eye has to travel the whole screen to scan one section.</summary>
    private const double ContentMaxWidthDip = 1100;

    /// <summary>Fraction of the work-area height the popup may occupy before the body scrolls.</summary>
    private const double MaxHeightFraction = 0.72;

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private Window? _window;
    private int _suppressDepth;

    private int _contentVersion;
    private int _reportedW;
    private int _reportedH;
    private int _revealVersion;
    private bool _applyingPlacement;

    public WindowHost(ConfigService config, AppEvents events)
    {
        _config = config;
        _events = events;
    }

    public Window? Window => _window;

    /// <inheritdoc />
    public int ContentVersion
    {
        get => _contentVersion;
        set
        {
            if (_contentVersion == value)
                return;
            _contentVersion = value;

            // The launcher folds every size-affecting setting into this fingerprint, so it is also the
            // signal that a VISIBLE popup has to re-measure and re-place — the native replacement for
            // the ResizeObserver messages that used to arrive after a live theme/content/font change.
            // Posted at Background priority because callers set it before the new content has been
            // laid out; the measure itself forces a layout pass anyway.
            if (_window?.IsVisible == true)
            {
                Dispatcher.UIThread.Post(
                    () => { if (_window?.IsVisible == true) _ = ResizeToContentAndPlaceAsync(); },
                    DispatcherPriority.Background);
            }
        }
    }

    /// <summary>Bind the host to the launcher window. Idempotent — re-attaching the same window is a
    /// no-op, so it does not matter whether startup or the window's own code-behind calls it.</summary>
    public void Attach(Window window)
    {
        if (ReferenceEquals(_window, window))
            return;

        _window = window;

        // Placement assigns explicit Width/Height, which SizeToContent would immediately overwrite.
        // Natural sizing is done by measuring the content directly instead.
        window.SizeToContent = SizeToContent.Manual;
        // The permanent taskbar entry is a stable launcher shortcut; this popup must never add a
        // second, temporary button. Re-forced on every placement pass as well.
        window.ShowInTaskbar = false;

        // Start invisible-but-shown so a cold start never flashes an unsized transparent rectangle at
        // the screen's top-left. CompleteReveal sets it to 1 and never puts it back.
        if (_revealVersion == 0)
            window.Opacity = 0;

        window.Deactivated += (_, _) =>
        {
            // Config is read live on every deactivation (never cached) so toggling 失焦自动隐藏 in
            // settings takes effect immediately.
            if (_config.Config.General.AutoClose && _suppressDepth == 0)
                Hide();
        };
    }

    /// <summary>Measure the launcher's natural size, then size and place the native window from it.
    /// Contract: this never throws — the reveal sequence depends on that.</summary>
    public Task<bool> ResizeToContentAndPlaceAsync()
    {
        if (_window is null)
            return Task.FromResult(false);

        try
        {
            // Measure against the width the launcher may actually occupy — the smaller of the app's
            // own cap and what this monitor offers — so the WrapPanel wraps exactly as it will render.
            var availableWidth = ContentMaxWidthDip;
            if (TryGetPrimaryDisplay(out var display, out var displayScale))
                availableWidth = Math.Min(availableWidth, Math.Max(MinWidthDip, display.Width / displayScale - 16));

            if (TryMeasureNaturalSize(availableWidth, out var width, out var height))
            {
                _reportedW = width;
                _reportedH = height;
            }

            // A failed measure is deliberately NOT cached: the last known-good size stays valid (this
            // is the "a previously reported size is still valid" rule the browser protocol had), and
            // if there has never been one the caller retries.
            if (_reportedW <= 0 || _reportedH <= 0)
                return Task.FromResult(false);

            ApplyReportedSizeAndPlace(_reportedW, _reportedH);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "ResizeToContentAndPlaceAsync failed");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// The natural size of the launcher content in DIPs, measured against the width it is actually
    /// allowed to use.
    /// </summary>
    /// <remarks>
    /// The available width is NOT infinite, and that is load-bearing: the tiles live in a
    /// <c>WrapPanel</c>, which given unbounded width lays every tile out on one line and reports a
    /// natural width of "all of them side by side". Measuring against the real cap is what makes the
    /// reported height the height of the wrapped grid.
    /// </remarks>
    private bool TryMeasureNaturalSize(double availableWidth, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (_window is null)
            return false;

        var root = _window.Content as Control;
        if (root is null)
            return false;

        // Force a real layout pass first: templates and styles have to be applied before a measure
        // means anything, and a cold start can reach here on the very first frame. UpdateLayout is
        // synchronous and complete, so the nested ItemsControls (sections → tiles) are all realized
        // by the time the measure below runs — no repeated passes are needed.
        _window.UpdateLayout();
        root.Measure(new Size(availableWidth, double.PositiveInfinity));
        var desired = root.DesiredSize;
        // The child was just measured against an availableSize the real pass will not use; invalidate
        // so the next pass re-measures under the window's own constraint.
        root.InvalidateMeasure();

        if (double.IsNaN(desired.Width) || double.IsNaN(desired.Height) ||
            double.IsInfinity(desired.Width) || double.IsInfinity(desired.Height))
            return false;

        var w = (int)Math.Ceiling(desired.Width);
        var h = (int)Math.Ceiling(desired.Height);
        // Same sanity bound the browser reports were held to: a degenerate or absurd measurement is
        // dropped rather than used to resize the window.
        if (w <= 0 || h <= 0 || w > 100_000 || h > 100_000)
            return false;

        width = w;
        height = h;
        return true;
    }

    private void ApplyReportedSizeAndPlace(int naturalWidth, int naturalHeight)
    {
        if (_window is null || _applyingPlacement)
            return;

        _applyingPlacement = true;
        try
        {
            var w = Math.Max(naturalWidth, MinWidthDip);
            var h = Math.Max(naturalHeight, MinHeightDip);

            const int edgeMargin = 8;
            if (!TryGetPrimaryDisplay(out var work, out var scale))
            {
                ApplyWithoutScreenInfo(w, h);
                return;
            }

            // The measured natural size is in DIPs. Convert the primary monitor's physical work-area
            // rectangle to DIPs only for fitting; final placement stays in physical virtual-screen
            // coordinates so mixed-DPI displays cannot make the toolkit reinterpret the position on
            // another monitor.
            var workWidthDip = work.Width / scale;
            var workHeightDip = work.Height / scale;
            var maxW = Math.Max(1, (int)Math.Floor(
                Math.Min(workWidthDip - edgeMargin * 2, ContentMaxWidthDip)));
            // Height is capped well below the work area on purpose: a launcher that fills the screen
            // stops feeling like a popup. Past this the body scrolls.
            var maxH = Math.Max(1, (int)Math.Floor(
                Math.Min(workHeightDip - edgeMargin * 2, workHeightDip * MaxHeightFraction)));

            // Clamp only. Content that does not fit wraps (width) or scrolls (height); nothing is
            // scaled down any more.
            w = Math.Clamp(w, 1, maxW);
            h = Math.Clamp(h, 1, maxH);

            _window.MaxWidth = maxW;
            _window.MaxHeight = maxH;
            _window.Width = w;
            _window.Height = h;

            var widthPx = Math.Max(1, (int)Math.Ceiling(w * scale));
            var heightPx = Math.Max(1, (int)Math.Ceiling(h * scale));
            var edgeMarginXPx = Math.Max(1, (int)Math.Round(edgeMargin * scale));
            var edgeMarginYPx = Math.Max(1, (int)Math.Round(edgeMargin * scale));
            var offsetPx = (int)Math.Round(_config.Config.General.PositionOffset * scale);
            var leftPx = work.X + (work.Width - widthPx) / 2;
            var topPx = work.Bottom - heightPx - offsetPx;
            leftPx = ClampToRange(leftPx, work.X + edgeMarginXPx, work.Right - widthPx - edgeMarginXPx);
            topPx = ClampToRange(topPx, work.Y + edgeMarginYPx, work.Bottom - heightPx - edgeMarginYPx);

            // SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER are essential: this runs on live
            // content changes while the window is visible and must not steal focus or reorder z-order
            // as a side effect.
            var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero || !SetWindowPos(
                    handle, IntPtr.Zero, leftPx, topPx, widthPx, heightPx,
                    SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder))
            {
                // Only reachable before the native HWND exists. Keep the launcher usable and let the
                // next reveal retry the exact primary-monitor placement. Window.Position is already in
                // physical screen pixels, so no unit conversion is needed here.
                _window.Position = new PixelPoint(leftPx, topPx);
            }

            _window.Topmost = _config.Config.General.TopMost;
            _window.ShowInTaskbar = false;
            // Note: placement is recomputed from content on every show, so it is intentionally NOT
            // persisted — writing config.json here on every show/refresh was pure churn (the saved
            // Window.* values were never read back).
        }
        finally
        {
            _applyingPlacement = false;
        }
    }

    /// <summary>Fallback for the (practically unreachable) case where no screen can be enumerated at
    /// all. Avalonia has no logical-units equivalent of WPF's <c>SystemParameters.WorkArea</c>, so
    /// there is nothing to clamp to: apply the natural size and leave the window where it is. The
    /// next reveal retries the real primary-monitor placement.</summary>
    private void ApplyWithoutScreenInfo(int width, int height)
    {
        if (_window is null)
            return;

        _window.MaxWidth = ContentMaxWidthDip;
        _window.MaxHeight = double.PositiveInfinity;
        _window.Width = Math.Min(width, ContentMaxWidthDip);
        _window.Height = height;
        _window.Topmost = _config.Config.General.TopMost;
        _window.ShowInTaskbar = false;
    }

    /// <summary>Physical work-area rectangle and DPI scale of the primary monitor.</summary>
    /// <remarks>
    /// This replaces the WPF build's EnumDisplayMonitors + GetMonitorInfo + GetDpiForMonitor trio:
    /// <c>Screens.Primary</c> hands back the same MONITORINFO work rect in physical pixels plus the
    /// effective per-monitor scale, with no delegate marshalling and no shcore.dll dependency — the
    /// 96-DPI fallback those catch blocks existed for is Avalonia's problem now. The floor of 1.0 on
    /// the scale is kept: it must never drop below 1 even on a sub-96-DPI configuration.
    /// </remarks>
    private bool TryGetPrimaryDisplay(out PixelRect work, out double scale)
    {
        work = default;
        scale = 1d;

        var screens = _window?.Screens;
        // Degrade to the first enumerated screen rather than giving up entirely; the WPF fallback
        // (SystemParameters.WorkArea) resolved to the primary monitor, which is what this is too.
        var screen = screens?.Primary ?? screens?.All.FirstOrDefault();
        if (screen is null)
            return false;

        work = screen.WorkingArea;
        scale = Math.Max(1d, screen.Scaling);
        return work.Width > 0 && work.Height > 0;
    }

    /// <summary>Clamp with one deliberate property: when <paramref name="max"/> is below
    /// <paramref name="min"/> (a window larger than the work area minus its margins) the result is
    /// <paramref name="min"/>, pinning the window to the top-left of the work area instead of
    /// producing an inverted range.</summary>
    private static int ClampToRange(int value, int min, int max)
    {
        if (max < min)
            return min;
        return Math.Min(Math.Max(value, min), max);
    }

    public void Hide() => _window?.Hide(); // does NOT reset Opacity

    public void ShowAndActivate()
    {
        if (_window is null)
            return;

        var revealVersion = ++_revealVersion;

        // A previously rendered launcher already has a trustworthy size. Reuse it and activate the
        // window synchronously, then refresh the measurement in the background. This is the common
        // tray/taskbar recall path, and keeping it off the async path is what makes the popup appear
        // instantly after the app has sat idle for a long time.
        if (_reportedW > 0 && _reportedH > 0)
        {
            ApplyReportedSizeAndPlace(_reportedW, _reportedH);
            _window.Show();
            CompleteReveal();
            _ = RefreshPlacementAfterRevealAsync(revealVersion);
            return;
        }

        _window.Show();
        _ = RevealSequenceAsync(revealVersion);
    }

    private async Task RevealSequenceAsync(int revealVersion)
    {
        if (_window is null)
            return;

        // Size + place the window BEFORE revealing it, so a cold start never flashes an unsized
        // transparent rectangle at screen top-left (the window starts at Opacity=0). If the first
        // measure fails (the content has not been through a real layout pass yet), retry once after a
        // short delay rather than revealing at the placeholder size/position. A still-failed measure
        // isn't cached, so the next summon re-measures and corrects it. ResizeToContentAndPlaceAsync
        // never throws, so the reveal below always runs.
        if (!await ResizeToContentAndPlaceAsync())
        {
            await Task.Delay(60);
            await ResizeToContentAndPlaceAsync();
        }
        if (revealVersion != _revealVersion || _window?.IsVisible != true)
            return;
        CompleteReveal();
    }

    private async Task RefreshPlacementAfterRevealAsync(int revealVersion)
    {
        await ResizeToContentAndPlaceAsync();
        // ResizeToContentAndPlaceAsync applies the result itself. The version check deliberately
        // happens afterwards: re-measuring is harmless, but a superseded reveal must not activate or
        // otherwise resurface a window the user has since hidden.
        if (revealVersion != _revealVersion || _window?.IsVisible != true)
            return;
    }

    private void CompleteReveal()
    {
        if (_window is null)
            return;
        _window.Opacity = 1;
        _window.Activate();
        // Activate() alone is unreliable when the foreground window belongs to another process.
        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            SetForegroundWindow(handle);
        _window.Focus(); // give the launcher keyboard focus so the search box is typeable
        _events.RaiseLauncherShown(); // let the launcher clear + focus the search box
    }

    public void Toggle()
    {
        if (_window is null)
            return;
        if (_window.IsVisible)
            Hide();
        else
            ShowAndActivate();
    }

    /// <summary>
    /// Hold off hide-on-blur while something modal owns focus. REFERENCE-COUNTED, not a flag: the
    /// suppressions genuinely overlap — the settings window suppresses for its whole lifetime, and a
    /// dialog raised from inside it (a failed hotkey registration, a folder picker, an update prompt)
    /// suppresses again and releases on close. With a plain boolean the inner release re-armed
    /// hide-on-blur underneath the still-open settings window, so the launcher vanished behind it.
    /// Unbalanced releases are clamped at zero rather than going negative, which would permanently
    /// disable hide-on-blur for the session.
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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
