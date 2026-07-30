using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the two things Themes/Fluent2/Tokens.axaml cannot express statically: which
/// <see cref="ThemeVariant"/> is active, and what the user's real Windows accent colour is.
/// </summary>
/// <remarks>
/// The accent is not a static token because Avalonia's FluentTheme hardcodes
/// <c>SystemAccentColor</c> to <c>#0078D7</c> — measured 2026-07-30 — rather than reading Windows
/// personalization. Fluent 2 is an accent-led language, so the launcher reads the real value from
/// <see cref="Application.PlatformSettings"/> and republishes it into <c>Application.Resources</c>,
/// which is searched BEFORE <c>Application.Styles</c> and therefore wins over the design-time
/// defaults in Tokens.axaml.
/// <para>
/// In Avalonia 12 platform settings hang off <c>Application</c>, not off a <c>TopLevel</c> as they
/// did in 11, so the accent is available before the first window exists and there is no
/// "wait for a window" dance.
/// </para>
/// </remarks>
public static class AppTheme
{
    /// <summary>Used when the platform cannot report an accent (and at design time).</summary>
    public static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary><see cref="ThemeVariant.Default"/> means "inherit from the platform", which is
    /// exactly what the System setting wants — Avalonia then tracks OS theme changes itself.</summary>
    public static ThemeVariant ToVariant(string? appearance) =>
        AppAppearance.NormalizeSetting(appearance) switch
        {
            AppAppearance.Light => ThemeVariant.Light,
            AppAppearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

    /// <summary>Apply the configured appearance to the running application. Safe to call repeatedly;
    /// the settings window calls it on every Apply.</summary>
    public static void Apply(Application app, string? appearance) =>
        app.RequestedThemeVariant = ToVariant(appearance);

    /// <summary>
    /// Publish the platform accent as the <c>Accent*</c> brushes the launcher binds to. Called at
    /// startup and again whenever the platform reports a colour change.
    /// </summary>
    public static void PublishAccent(Application app)
    {
        var accent = ReadPlatformAccent(app);

        var shaded = Shade(accent, -0.18);

        app.Resources["AccentDefaultBrush"] = new SolidColorBrush(accent);
        // Accent-on-surface text needs to survive both variants, so it is shifted away from the
        // surface rather than used raw: darkened for light backgrounds, lightened for dark ones.
        app.Resources["AccentTextBrush"] = new SolidColorBrush(Shade(accent, -0.35));
        app.Resources["AccentTextBrushDark"] = new SolidColorBrush(Shade(accent, 0.45));
        app.Resources["TextOnAccentBrush"] =
            new SolidColorBrush(RelativeLuminance(accent) > 0.5 ? Colors.Black : Colors.White);

        // Raw Colors as well as brushes: a GradientStop takes a Color, not a Brush, and the splash
        // card is an accent gradient.
        app.Resources["AccentColor"] = accent;
        app.Resources["AccentColorShaded"] = shaded;
    }

    /// <summary>Subscribe to OS colour changes so a live launcher re-tints without a restart.
    /// Returns an unsubscriber, or null when the platform exposes no settings object.</summary>
    public static IDisposable? TrackPlatformChanges(Application app)
    {
        var settings = app.PlatformSettings;
        if (settings is null)
            return null;

        void Handler(object? sender, PlatformColorValues e) => PublishAccent(app);

        settings.ColorValuesChanged += Handler;
        return new Unsubscriber(() => settings.ColorValuesChanged -= Handler);
    }

    private static Color ReadPlatformAccent(Application app)
    {
        try
        {
            return app.PlatformSettings?.GetColorValues().AccentColor1 ?? FallbackAccent;
        }
        catch
        {
            // An accent is decoration; never let reading it take startup down.
            return FallbackAccent;
        }
    }

    /// <summary>Move a colour toward black (negative) or white (positive) by a fraction.</summary>
    private static Color Shade(Color c, double amount)
    {
        static byte Mix(byte channel, double amount) => amount < 0
            ? (byte)Math.Clamp(channel * (1 + amount), 0, 255)
            : (byte)Math.Clamp(channel + (255 - channel) * amount, 0, 255);

        return Color.FromArgb(c.A, Mix(c.R, amount), Mix(c.G, amount), Mix(c.B, amount));
    }

    /// <summary>sRGB relative luminance, used only to pick black-or-white text on the accent.</summary>
    private static double RelativeLuminance(Color c)
    {
        static double Linear(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            var d = _dispose;
            _dispose = null;
            d?.Invoke();
        }
    }
}
