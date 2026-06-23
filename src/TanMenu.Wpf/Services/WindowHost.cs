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

    public async Task ResizeToContentAndPlaceAsync()
    {
        if (_window is null || _web?.CoreWebView2 is null)
            return;

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
            "return JSON.stringify({width:Math.ceil(fr.width+lc*2),height:Math.ceil(fc.scrollHeight+tc+lc)});})()";

        var raw = await _web.CoreWebView2.ExecuteScriptAsync(js);
        // ExecuteScriptAsync returns the JS string result JSON-encoded → unwrap twice.
        var inner = JsonSerializer.Deserialize<string>(raw) ?? "{}";
        var dim = JsonSerializer.Deserialize<DimensionInfo>(inner) ?? new DimensionInfo();

        var w = Math.Max(dim.NaturalWidth, MinWidthDip) + 4;
        var h = Math.Max(dim.NaturalHeight, MinHeightDip) + 4;

        var wa = SystemParameters.WorkArea; // DIP work area of the primary screen
        w = Math.Min(w, (int)wa.Width);
        h = Math.Min(h, (int)wa.Height);

        _window.Width = w;
        _window.Height = h;
        _window.Left = wa.Left + (wa.Width - w) / 2;
        _window.Top = Math.Max(wa.Top, wa.Bottom - h - _config.Config.General.PositionOffset);
        _window.Topmost = _config.Config.General.TopMost;
        _window.ShowInTaskbar = _config.Config.General.ShowInTaskbar;
        // Note: placement is recomputed from content on every show, so it is intentionally NOT
        // persisted — writing config.json here on every show/refresh was pure churn (the saved
        // Window.* values were never read back).
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
        // transparent rectangle at screen top-left (the window starts at Opacity=0).
        await ResizeToContentAndPlaceAsync();
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
