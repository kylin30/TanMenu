using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using TanMenu.Core.Models;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the launcher window: measures the rendered <c>#form-content</c> via the WebView2,
/// sizes the window to it (CSS px == WPF DIPs, so no manual DPI scaling), places it
/// bottom-center above the taskbar, and handles show/hide/toggle + hide-on-blur.
/// </summary>
public sealed class WindowHost : IWindowHost
{
    private const int MinWidthDip = 200;
    private const int MinHeightDip = 100;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private Window? _window;
    private WebView2CompositionControl? _web;
    private bool _suppressHide;

    // Content-version gate for reveal-time sizing: ContentVersion is the launcher's menu fingerprint;
    // _measured* remember the last WebView measurement so an unchanged re-summon reuses the cached
    // natural size instead of paying another ExecuteScript round-trip.
    public int ContentVersion { get; set; }
    private int _measuredVersion = int.MinValue;
    private int _measuredW;
    private int _measuredH;

    public WindowHost(ConfigService config, AppEvents events)
    {
        _config = config;
        _events = events;
    }

    public void Attach(Window window, WebView2CompositionControl web)
    {
        _window = window;
        _web = web;
        _window.Deactivated += (_, _) =>
        {
            if (_config.Config.General.AutoClose && !_suppressHide)
                Hide();
        };
    }

    /// <summary>Measure #form-content, size+place the window. Returns true on success; false when the
    /// measurement failed or was degenerate (then nothing is cached, so the next call re-measures).</summary>
    public async Task<bool> ResizeToContentAndPlaceAsync()
    {
        if (_window is null || _web?.CoreWebView2 is null)
            return false;

        int w, h;
        // Skip the WebView measurement round-trip when the menu content is unchanged since the last
        // measure (same ContentVersion) — reuse the cached natural size. Placement still runs every
        // time (cheap), so the window stays correctly positioned even if the work area moved.
        if (ContentVersion == _measuredVersion && _measuredW > 0)
        {
            w = _measuredW;
            h = _measuredH;
        }
        else
        {
            // #form-content is width:max-content (natural content size) but excludes the retro window's
            // titlebar/borders. The outer .window (RetroWindow's root, present in all themes) is
            // width:100% (reflects the viewport, not its natural size). So measure #form-content's
            // NATURAL size, then add the chrome around it, derived from form-content's offset inside
            // .window (titlebar+border on top/left, assumed symmetric on right/bottom). Yields the
            // natural full-window size → no scrollbar.
            const string js =
                "(function(){var fc=document.querySelector('#form-content');" +
                "if(!fc)return '{\"width\":0,\"height\":0}';" +
                "var fr=fc.getBoundingClientRect();var lc=fr.left,tc=fr.top;" +
                "var win=document.querySelector('.window');" +
                "if(win){var wr=win.getBoundingClientRect();lc=fr.left-wr.left;tc=fr.top-wr.top;}" +
                // Use scrollHeight (natural content height) so #form-content's max-height/overflow-y
                // (which lets oversized menus scroll) doesn't cap the measured size — the window still
                // grows to fit content, and only scrolls once clamped to the work area.
                // Use scrollWidth (natural content width, incl. anything clipped by overflow-x:hidden) for
                // the SAME reason as scrollHeight above — #form-content is width:auto, so fr.width is just
                // the current (possibly too-narrow) window width, and the menu's right edge stays clipped.
                "return JSON.stringify({width:Math.ceil(fc.scrollWidth+lc*2),height:Math.ceil(fc.scrollHeight+tc+lc)});})()";

            try
            {
                var raw = await _web.CoreWebView2.ExecuteScriptAsync(js);
                // ExecuteScriptAsync returns the JS string result JSON-encoded → unwrap twice.
                var inner = string.IsNullOrEmpty(raw) ? "{}" : (JsonSerializer.Deserialize<string>(raw) ?? "{}");
                var dim = JsonSerializer.Deserialize<DimensionInfo>(inner) ?? new DimensionInfo();

                // A degenerate measurement (#form-content briefly absent during WebView (re)init → the JS
                // returns {0,0}) must NOT be cached: caching the resulting min-size under this
                // ContentVersion would pin the launcher at a tiny box on every later summon. Bail without
                // caching so the next summon re-measures.
                if (dim.NaturalWidth <= 0 || dim.NaturalHeight <= 0)
                    return false;

                w = Math.Max(dim.NaturalWidth, MinWidthDip) + 4;
                h = Math.Max(dim.NaturalHeight, MinHeightDip) + 4;
                _measuredW = w;
                _measuredH = h;
                _measuredVersion = ContentVersion;
            }
            catch (Exception ex)
            {
                // Measuring via the WebView can throw if it's mid-navigation/teardown, or return a
                // non-JSON result. Sizing is best-effort: log and leave the window at its current size
                // (nothing cached → next call re-measures), rather than letting the throw escape to the
                // (often fire-and-forget) callers.
                Serilog.Log.Error(ex, "ResizeToContentAndPlaceAsync failed");
                return false;
            }
        }

        var wa = SystemParameters.WorkArea; // DIP work area of the primary screen
        const int edgeMargin = 8;
        var maxW = Math.Max(1, (int)Math.Floor(wa.Width - edgeMargin * 2));
        var maxH = Math.Max(1, (int)Math.Floor(wa.Height - edgeMargin * 2));
        w = Math.Clamp(w, 1, maxW);
        h = Math.Clamp(h, 1, maxH);

        _window.MaxWidth = maxW;
        _window.MaxHeight = maxH;
        _window.Width = w;
        _window.Height = h;

        var left = wa.Left + (wa.Width - w) / 2;
        var top = wa.Bottom - h - _config.Config.General.PositionOffset;
        _window.Left = ClampToRange(left, wa.Left + edgeMargin, wa.Right - w - edgeMargin);
        _window.Top = ClampToRange(top, wa.Top + edgeMargin, wa.Bottom - h - edgeMargin);
        _window.Topmost = _config.Config.General.TopMost;
        _window.ShowInTaskbar = _config.Config.General.ShowInTaskbar;
        // Note: placement is recomputed from content on every show, so it is intentionally NOT
        // persisted — writing config.json here on every show/refresh was pure churn (the saved
        // Window.* values were never read back).
        return true;
    }

    private static double ClampToRange(double value, double min, double max)
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
        _window.Show();
        _ = RevealSequenceAsync();
    }

    private async Task RevealSequenceAsync()
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
}
