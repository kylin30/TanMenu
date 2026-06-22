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
    private Window? _window;
    private WebView2CompositionControl? _web;
    private bool _suppressHide;

    public WindowHost(ConfigService config) => _config = config;

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

        // #form-content is inline-block (natural content size) but excludes the retro window's
        // titlebar/borders. The outer .win31-window is width:100% (reflects the viewport, not its
        // natural size). So measure #form-content's NATURAL size, then add the chrome around it,
        // derived from form-content's offset inside .win31-window (titlebar+border on top/left,
        // assumed symmetric on right/bottom). Yields the natural full-window size → no scrollbar.
        const string js =
            "(function(){var fc=document.querySelector('#form-content');" +
            "if(!fc)return '{\"width\":0,\"height\":0}';" +
            "var fr=fc.getBoundingClientRect();var lc=fr.left,tc=fr.top;" +
            "var win=document.querySelector('.win31-window');" +
            "if(win){var wr=win.getBoundingClientRect();lc=fr.left-wr.left;tc=fr.top-wr.top;}" +
            "return JSON.stringify({width:Math.ceil(fr.width+lc*2),height:Math.ceil(fr.height+tc+lc)});})()";

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

        _config.UpdateWindowConfig(w, h, (int)_window.Left, (int)_window.Top);
        await _config.SaveAsync();
    }

    public void Hide() => _window?.Hide();

    public void ShowAndActivate()
    {
        if (_window is null)
            return;
        _window.Show();
        _ = ResizeToContentAndPlaceAsync();
        _window.Activate();
        var h = new WindowInteropHelper(_window).Handle;
        if (h != IntPtr.Zero)
            SetForegroundWindow(h);
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
