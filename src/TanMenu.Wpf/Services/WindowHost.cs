using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the launcher window. A browser-side ResizeObserver reports the rendered theme root's actual
/// border-box; this host applies that size, fits oversized horizontal menus to the monitor work area,
/// places the window bottom-center, and handles show/hide/toggle + hide-on-blur.
/// </summary>
public sealed class WindowHost : IWindowHost
{
    private const int MinWidthDip = 200;
    private const int MinHeightDip = 100;
    private const double MinAutoZoom = 0.25;
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private Window? _window;
    private WebView2CompositionControl? _web;
    private bool _suppressHide;

    // ContentVersion remains useful for diagnostics, but sizing is driven by the browser's rendered
    // root rather than by assumptions about a particular theme's titlebar/border/padding.
    public int ContentVersion { get; set; }
    private int _reportedW;
    private int _reportedH;
    private int _loggedFitVersion = int.MinValue;
    private CoreWebView2? _messageSource;
    private TaskCompletionSource<bool>? _sizeReportWaiter;
    private int _revealVersion;

    public WindowHost(ConfigService config, AppEvents events)
    {
        _config = config;
        _events = events;
    }

    public void Attach(Window window, WebView2CompositionControl web)
    {
        _window = window;
        _web = web;
        if (web.CoreWebView2 is not null)
            HookBrowserMessages(web.CoreWebView2);
        web.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (args.IsSuccess && web.CoreWebView2 is not null)
                HookBrowserMessages(web.CoreWebView2);
        };
        _window.Deactivated += (_, _) =>
        {
            if (_config.Config.General.AutoClose && !_suppressHide)
                Hide();
        };
    }

    /// <summary>Ask the browser to report its current rendered root, then size and place the native
    /// window from that report. No theme chrome dimensions are inferred by the host.</summary>
    public async Task<bool> ResizeToContentAndPlaceAsync()
    {
        if (_window is null || _web?.CoreWebView2 is null)
            return false;

        try
        {
            await RequestBrowserSizeReportAsync();
            if (_reportedW <= 0 || _reportedH <= 0)
                return false;

            ApplyReportedSizeAndPlace(_reportedW, _reportedH);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "ResizeToContentAndPlaceAsync failed");
            return false;
        }
    }

    private void HookBrowserMessages(CoreWebView2 core)
    {
        if (ReferenceEquals(_messageSource, core))
            return;
        if (_messageSource is not null)
            _messageSource.WebMessageReceived -= Browser_WebMessageReceived;

        _messageSource = core;
        core.WebMessageReceived += Browser_WebMessageReceived;
    }

    private async Task RequestBrowserSizeReportAsync()
    {
        if (_web?.CoreWebView2 is null)
            return;

        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sizeReportWaiter = waiter;
        try
        {
            await _web.CoreWebView2.ExecuteScriptAsync(
                "window.tmReportLauncherSize ? window.tmReportLauncherSize(true) : false");
            await waiter.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
        }
        catch (TimeoutException)
        {
            // The observer may not exist during the first transient Blazor render. A previously
            // reported size is still valid; otherwise the reveal sequence retries shortly.
        }
        finally
        {
            if (ReferenceEquals(_sizeReportWaiter, waiter))
                _sizeReportWaiter = null;
        }
    }

    private void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var json = JsonDocument.Parse(args.TryGetWebMessageAsString());
            var root = json.RootElement;
            if (!root.TryGetProperty("type", out var type) ||
                type.GetString() != "tanmenu-size" ||
                !root.TryGetProperty("width", out var widthValue) ||
                !root.TryGetProperty("height", out var heightValue))
                return;

            var width = widthValue.GetInt32();
            var height = heightValue.GetInt32();
            if (width <= 0 || height <= 0 || width > 100_000 || height > 100_000)
                return;

            void Accept()
            {
                _reportedW = width;
                _reportedH = height;
                _sizeReportWaiter?.TrySetResult(true);

                // ResizeObserver messages also arrive after live theme/content/font changes, so the
                // visible launcher follows the browser automatically without another host-side probe.
                if (_window?.IsVisible == true)
                    ApplyReportedSizeAndPlace(width, height);
            }

            if (_window?.Dispatcher.CheckAccess() == false)
                _window.Dispatcher.BeginInvoke(Accept);
            else
                Accept();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Ignored invalid launcher size message");
        }
    }

    private void ApplyReportedSizeAndPlace(int naturalWidth, int naturalHeight)
    {
        if (_window is null || _web is null)
            return;

        var w = Math.Max(naturalWidth, MinWidthDip);
        var h = Math.Max(naturalHeight, MinHeightDip);

        const int edgeMargin = 8;
        if (!TryGetPrimaryDisplay(out var primary))
        {
            ApplyUsingWpfPrimaryWorkArea(w, h, edgeMargin);
            return;
        }

        // Browser-reported CSS pixels map to WPF DIPs. Convert the primary monitor's physical
        // work-area rectangle to DIPs only for fitting; final placement stays in physical virtual-
        // screen coordinates so mixed-DPI displays cannot make WPF reinterpret Left/Top on another
        // monitor.
        var workWidthDip = primary.WorkWidthPx / primary.ScaleX;
        var workHeightDip = primary.WorkHeightPx / primary.ScaleY;
        var maxW = Math.Max(1, (int)Math.Floor(workWidthDip - edgeMargin * 2));
        var maxH = Math.Max(1, (int)Math.Floor(workHeightDip - edgeMargin * 2));
        // Clamping the native window alone leaves the no-wrap HTML at its natural width and simply
        // cuts off its right side. Page zoom changes the CSS viewport accordingly, so fit the whole
        // retro window uniformly into the work area while preserving the requested single-row layout
        // and avoiding scrollbars. Keep a practical lower bound for pathological folder counts;
        // WebView2 itself normalizes values outside its internal supported range.
        var fitScale = Math.Min(1d, Math.Min(maxW / (double)w, maxH / (double)h));
        fitScale = Math.Max(MinAutoZoom, fitScale);
        if (Math.Abs(_web.ZoomFactor - fitScale) > 0.001)
            _web.ZoomFactor = fitScale;

        w = Math.Clamp((int)Math.Ceiling(w * fitScale), 1, maxW);
        h = Math.Clamp((int)Math.Ceiling(h * fitScale), 1, maxH);
        if (fitScale < 0.999 && _loggedFitVersion != ContentVersion)
        {
            Serilog.Log.Information(
                "Fitted launcher {NaturalWidth}x{NaturalHeight} at zoom {Zoom:F3} to {Width}x{Height} " +
                "inside work area {WorkWidth}x{WorkHeight}",
                naturalWidth, naturalHeight, fitScale, w, h, maxW, maxH);
            _loggedFitVersion = ContentVersion;
        }

        _window.MaxWidth = maxW;
        _window.MaxHeight = maxH;
        _window.Width = w;
        _window.Height = h;

        var widthPx = Math.Max(1, (int)Math.Ceiling(w * primary.ScaleX));
        var heightPx = Math.Max(1, (int)Math.Ceiling(h * primary.ScaleY));
        var edgeMarginXPx = Math.Max(1, (int)Math.Round(edgeMargin * primary.ScaleX));
        var edgeMarginYPx = Math.Max(1, (int)Math.Round(edgeMargin * primary.ScaleY));
        var offsetPx = (int)Math.Round(_config.Config.General.PositionOffset * primary.ScaleY);
        var leftPx = primary.Work.Left + (primary.WorkWidthPx - widthPx) / 2;
        var topPx = primary.Work.Bottom - heightPx - offsetPx;
        leftPx = ClampToRange(
            leftPx,
            primary.Work.Left + edgeMarginXPx,
            primary.Work.Right - widthPx - edgeMarginXPx);
        topPx = ClampToRange(
            topPx,
            primary.Work.Top + edgeMarginYPx,
            primary.Work.Bottom - heightPx - edgeMarginYPx);

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !SetWindowPos(
                handle,
                IntPtr.Zero,
                leftPx,
                topPx,
                widthPx,
                heightPx,
                SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder))
        {
            // This should only be reachable before the native HWND exists. Keep the launcher usable
            // and let the next reveal/report retry the exact primary-monitor placement.
            ApplyWpfPositionFromPhysical(primary, w, h, edgeMargin);
        }

        _window.Topmost = _config.Config.General.TopMost;
        // The permanent taskbar entry is a stable launcher shortcut. Keeping this popup out of the
        // taskbar prevents a second temporary button from appearing whenever it is shown.
        _window.ShowInTaskbar = false;
        // Note: placement is recomputed from content on every show, so it is intentionally NOT
        // persisted — writing config.json here on every show/refresh was pure churn (the saved
        // Window.* values were never read back).
    }

    private void ApplyUsingWpfPrimaryWorkArea(int naturalWidth, int naturalHeight, int edgeMargin)
    {
        if (_window is null || _web is null)
            return;

        var wa = SystemParameters.WorkArea;
        var maxW = Math.Max(1, (int)Math.Floor(wa.Width - edgeMargin * 2));
        var maxH = Math.Max(1, (int)Math.Floor(wa.Height - edgeMargin * 2));
        var fitScale = Math.Max(
            MinAutoZoom,
            Math.Min(1d, Math.Min(maxW / (double)naturalWidth, maxH / (double)naturalHeight)));
        if (Math.Abs(_web.ZoomFactor - fitScale) > 0.001)
            _web.ZoomFactor = fitScale;

        var width = Math.Clamp((int)Math.Ceiling(naturalWidth * fitScale), 1, maxW);
        var height = Math.Clamp((int)Math.Ceiling(naturalHeight * fitScale), 1, maxH);
        _window.MaxWidth = maxW;
        _window.MaxHeight = maxH;
        _window.Width = width;
        _window.Height = height;
        _window.Left = ClampToRange(
            wa.Left + (wa.Width - width) / 2,
            wa.Left + edgeMargin,
            wa.Right - width - edgeMargin);
        _window.Top = ClampToRange(
            wa.Bottom - height - _config.Config.General.PositionOffset,
            wa.Top + edgeMargin,
            wa.Bottom - height - edgeMargin);
        _window.Topmost = _config.Config.General.TopMost;
        _window.ShowInTaskbar = false;
    }

    private void ApplyWpfPositionFromPhysical(PrimaryDisplay primary, int widthDip, int heightDip, int edgeMargin)
    {
        if (_window is null)
            return;

        var workLeftDip = primary.Work.Left / primary.ScaleX;
        var workTopDip = primary.Work.Top / primary.ScaleY;
        var workRightDip = primary.Work.Right / primary.ScaleX;
        var workBottomDip = primary.Work.Bottom / primary.ScaleY;
        _window.Left = ClampToRange(
            workLeftDip + (primary.WorkWidthPx / primary.ScaleX - widthDip) / 2,
            workLeftDip + edgeMargin,
            workRightDip - widthDip - edgeMargin);
        _window.Top = ClampToRange(
            workBottomDip - heightDip - _config.Config.General.PositionOffset,
            workTopDip + edgeMargin,
            workBottomDip - heightDip - edgeMargin);
    }

    private static bool TryGetPrimaryDisplay(out PrimaryDisplay primary)
    {
        IntPtr primaryHandle = IntPtr.Zero;
        NativeMonitorInfo primaryInfo = default;

        MonitorEnumProc callback = (
            IntPtr monitor,
            IntPtr monitorDeviceContext,
            ref NativeRect monitorRect,
            IntPtr data) =>
        {
            var info = new NativeMonitorInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMonitorInfo>()
            };
            if (GetMonitorInfo(monitor, ref info) && (info.Flags & MonitorInfoPrimary) != 0)
            {
                primaryHandle = monitor;
                primaryInfo = info;
            }

            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        if (primaryHandle == IntPtr.Zero)
        {
            primary = default;
            return false;
        }

        uint dpiX = 96;
        uint dpiY = 96;
        try
        {
            if (GetDpiForMonitor(primaryHandle, MonitorDpiType.Effective, out var actualX, out var actualY) == 0)
            {
                dpiX = actualX;
                dpiY = actualY;
            }
        }
        catch (DllNotFoundException)
        {
            // Win10 always provides Shcore.dll; retain 96-DPI fallback for unusual stripped systems.
        }
        catch (EntryPointNotFoundException)
        {
            // Same fallback as above.
        }

        primary = new PrimaryDisplay(
            primaryInfo.Work,
            Math.Max(1d, dpiX / 96d),
            Math.Max(1d, dpiY / 96d));
        return true;
    }

    private static double ClampToRange(double value, double min, double max)
    {
        if (max < min)
            return min;
        return Math.Min(Math.Max(value, min), max);
    }

    private static int ClampToRange(int value, int min, int max)
    {
        if (max < min)
            return min;
        return Math.Min(Math.Max(value, min), max);
    }

    public void Hide() => _window?.Hide();

    public void ShowAndActivate()
    {
        if (_window is null)
            return;

        var revealVersion = ++_revealVersion;

        // A previously rendered launcher already has a trustworthy size. Reuse it and activate the
        // window synchronously, then refresh the browser measurement in the background. This is the
        // common tray/taskbar recall path and avoids making a paged-out WebView2 round-trip part of
        // the time-to-visible after the app has sat idle for a long time.
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
        // measure fails (WebView mid-init returned a degenerate/zero size or threw), retry once after a
        // short delay rather than revealing at the XAML default size/position. A still-failed measure
        // isn't cached, so the next summon re-measures and corrects it. ResizeToContentAndPlaceAsync
        // swallows its own WebView errors and never throws, so the reveal below always runs.
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
        // happens afterwards: the browser request is harmless, but a superseded reveal must not
        // activate or otherwise resurface a window the user has since hidden.
        if (revealVersion != _revealVersion || _window?.IsVisible != true)
            return;
    }

    private void CompleteReveal()
    {
        if (_window is null)
            return;
        _window.Opacity = 1;
        _window.Activate();
        var h = new WindowInteropHelper(_window).Handle;
        if (h != IntPtr.Zero)
            SetForegroundWindow(h);
        _web?.Focus(); // give the WebView2 keyboard focus so the search box is typeable
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

    public void SuppressHide(bool on) => _suppressHide = on;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr monitorDeviceContext,
        ref NativeRect monitorRect,
        IntPtr data);

    private enum MonitorDpiType
    {
        Effective = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private readonly record struct PrimaryDisplay(NativeRect Work, double ScaleX, double ScaleY)
    {
        public int WorkWidthPx => Work.Right - Work.Left;
        public int WorkHeightPx => Work.Bottom - Work.Top;
    }
}
