using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Microsoft.Win32;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the two things Themes/Shell/Tokens.axaml cannot express statically: which
/// <see cref="ThemeVariant"/> is active, and the user's real Windows accent palette.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's FluentTheme hardcodes <c>SystemAccentColor</c> to <c>#0078D7</c> — measured
/// 2026-07-30 — rather than reading Windows personalization, and Windows 11 is an accent-led design
/// language, so the accent has to be published from here.
/// </para>
/// <para>
/// Windows does not expose one accent colour but a PALETTE of eight, and a Windows 11 app is
/// expected to pick from it by context rather than tint the base colour itself: an accent fill on a
/// LIGHT surface uses a darker shade so it holds contrast against white, and on a DARK surface a
/// lighter one. That is why this publishes a theme-variant dictionary instead of a single brush.
/// </para>
/// </remarks>
public static class AppTheme
{
    /// <summary>Used when the palette cannot be read (and at design time) — the Windows default blue.</summary>
    public static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>
    /// Windows' own accent palette, in the order the registry stores it. Windows 11 personalization
    /// writes eight RGBA entries; WinUI names the same entries
    /// <c>SystemAccentColorLight3..SystemAccentColorDark3</c>.
    /// </summary>
    public enum Shade
    {
        Lighter3 = 0,
        Lighter2 = 1,
        Lighter1 = 2,
        Base = 3,
        Darker1 = 4,
        Darker2 = 5,
        Darker3 = 6,
        // index 7 is the "alt"/complementary colour Windows uses for some system surfaces; unused here.
    }

    private const string AccentKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string AccentPaletteValue = "AccentPalette";

    /// <summary>The dictionary this class owns inside <c>Application.Resources</c>, kept so a colour
    /// change replaces it instead of stacking another copy on every notification.</summary>
    private static ResourceDictionary? _published;

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
    /// the settings window calls it on every selection change.</summary>
    public static void Apply(Application app, string? appearance) =>
        app.RequestedThemeVariant = ToVariant(appearance);

    /// <summary>
    /// Publish the platform accent as the <c>Accent*</c> resources the UI binds to, per variant.
    /// Called at startup and again whenever the platform reports a colour change.
    /// </summary>
    /// <remarks>
    /// The dictionary goes into <c>Application.Resources</c>, which is searched BEFORE
    /// <c>Application.Styles</c>, so it wins over the design-time defaults in Tokens.axaml without
    /// those having to be removed — the XAML previewer and any early binding still resolve.
    /// </remarks>
    /// <summary>The accent colours one theme variant needs, chosen out of the Windows palette.</summary>
    /// <param name="Fill">Accent fills and the focus outline.</param>
    /// <param name="Text">Accent-coloured TEXT, which needs more contrast than a fill.</param>
    /// <param name="OnAccent">Text drawn ON an accent fill.</param>
    /// <param name="Gradient">Start of the splash card's accent gradient.</param>
    /// <param name="GradientShaded">End of that gradient.</param>
    public readonly record struct AccentSet(
        Color Fill, Color Text, Color OnAccent, Color Gradient, Color GradientShaded);

    /// <summary>
    /// Pick the accent colours for one variant out of Windows' eight-shade palette. Pure, so the
    /// choice is unit-tested rather than eyeballed in a screenshot.
    /// </summary>
    /// <remarks>
    /// The raw accent is tuned for the taskbar. On a LIGHT surface a fill made from it does not hold
    /// contrast under white text, so Windows 11 uses a darker shade; on a DARK surface it disappears
    /// into the background, so a lighter one is used — and light fills then need dark text on them.
    /// This is the same Dark1/Dark2 and Light2/Light3 selection WinUI makes.
    /// </remarks>
    public static AccentSet ResolveAccent(Color[]? palette, bool dark) => dark
        ? new AccentSet(
            Fill: Pick(palette, Shade.Lighter2),
            Text: Pick(palette, Shade.Lighter3),
            OnAccent: Colors.Black,
            Gradient: Pick(palette, Shade.Lighter2),
            GradientShaded: Pick(palette, Shade.Base))
        : new AccentSet(
            Fill: Pick(palette, Shade.Darker1),
            Text: Pick(palette, Shade.Darker2),
            OnAccent: Colors.White,
            Gradient: Pick(palette, Shade.Base),
            GradientShaded: Pick(palette, Shade.Darker1));

    public static void PublishAccent(Application app)
    {
        var palette = ReadAccentPalette();

        var dict = new ResourceDictionary();
        dict.ThemeDictionaries[ThemeVariant.Light] = Build(ResolveAccent(palette, dark: false));
        dict.ThemeDictionaries[ThemeVariant.Dark] = Build(ResolveAccent(palette, dark: true));

        if (_published is not null)
            app.Resources.MergedDictionaries.Remove(_published);
        app.Resources.MergedDictionaries.Add(dict);
        _published = dict;

        // TWO KEYS, NOT FIVE, AND BOTH ARE RAW Colors. Round 7 Task 7 dropped AccentTextBrush and
        // TextOnAccentBrush: the first never had a consumer anywhere in the tree (grep found it only
        // in this file), and the second's only one was TmAccentButton, the accent-filled default
        // button Task 3 deleted because Windows 7 had none to imitate. Round 7 Task 9 dropped the
        // third, AccentDefaultBrush, when it deleted the search field's accent underline — Task 8 had
        // already deleted the navigation pane's selection pill, and those two were the brush's last
        // readers. What is left is what Aero actually had a user colour FOR: a frame tint. Both
        // survivors are Colors rather than Brushes because both consumers are GradientStops (the
        // caption's glass tint in MainWindow.axaml and the splash card's gradient in
        // Controls/SplashPanel.axaml), and a GradientStop takes a Color.
        //
        // ResolveAccent still RESOLVES all five — Fill, Text and OnAccent are part of what Windows'
        // eight-shade palette means, AppThemeTests asserts on Fill in five of its seven facts, and
        // re-deriving them later would be the same work again. It is only the publication that stops.
        static ResourceDictionary Build(AccentSet a) => new()
        {
            ["AccentColor"] = a.Gradient,
            ["AccentColorShaded"] = a.GradientShaded,
        };
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

    /// <summary>
    /// Windows' eight-entry accent palette, or null when it cannot be read (a policy-locked or
    /// freshly-provisioned profile). Each entry is stored as RGBA, little-endian byte order.
    /// </summary>
    public static Color[]? ReadAccentPalette()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKeyPath);
            if (key?.GetValue(AccentPaletteValue) is not byte[] bytes || bytes.Length < 7 * 4)
                return null;

            var shades = new Color[7];
            for (var i = 0; i < shades.Length; i++)
                shades[i] = Color.FromRgb(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2]);
            return shades;
        }
        catch
        {
            // An accent is decoration; never let reading it take startup down.
            return null;
        }
    }

    /// <summary>Fall back to the plain Windows blue when the palette is unavailable, rather than
    /// inventing shades — a wrong-but-plausible tint is worse than the documented default.</summary>
    private static Color Pick(Color[]? palette, Shade shade) =>
        palette is not null && (int)shade < palette.Length ? palette[(int)shade] : FallbackAccent;

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
