using Avalonia.Media;
using Avalonia.Styling;
using TanMenu.Core.Services;
using TanMenu.Wpf.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

/// <summary>
/// Pins how the launcher picks accent colours out of Windows' palette, and how the appearance
/// setting maps to a theme variant.
/// </summary>
/// <remarks>
/// The accent choice is the kind of thing that looks fine in a screenshot while being subtly wrong —
/// a fill one shade off still reads as "blue". Testing the selection directly is why
/// <see cref="AppTheme.ResolveAccent"/> is a pure function separate from the Application plumbing.
/// </remarks>
public class AppThemeTests
{
    /// <summary>A stand-in for the eight-entry palette Windows writes, with each shade a value that
    /// cannot be confused with another.</summary>
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x01, 0x01, 0x01), // Lighter3
        Color.FromRgb(0x02, 0x02, 0x02), // Lighter2
        Color.FromRgb(0x03, 0x03, 0x03), // Lighter1
        Color.FromRgb(0x04, 0x04, 0x04), // Base
        Color.FromRgb(0x05, 0x05, 0x05), // Darker1
        Color.FromRgb(0x06, 0x06, 0x06), // Darker2
        Color.FromRgb(0x07, 0x07, 0x07), // Darker3
    ];

    [Fact]
    public void ResolveAccent_Light_UsesTheDarkerShades()
    {
        var a = AppTheme.ResolveAccent(Palette, dark: false);

        // A fill made from the RAW accent does not hold white text on a light surface, so Windows 11
        // steps one shade darker; accent TEXT steps two.
        Assert.Equal(Palette[4], a.Fill);   // Darker1
        Assert.Equal(Palette[5], a.Text);   // Darker2
        Assert.Equal(Colors.White, a.OnAccent);
        Assert.Equal(Palette[3], a.Gradient);        // Base
        Assert.Equal(Palette[4], a.GradientShaded);  // Darker1
    }

    [Fact]
    public void ResolveAccent_Dark_UsesTheLighterShades()
    {
        var a = AppTheme.ResolveAccent(Palette, dark: true);

        Assert.Equal(Palette[1], a.Fill);   // Lighter2
        Assert.Equal(Palette[0], a.Text);   // Lighter3
        // A light fill needs DARK text on it. Getting this backwards is invisible until someone
        // switches to dark mode and reads white-on-pale-blue.
        Assert.Equal(Colors.Black, a.OnAccent);
        Assert.Equal(Palette[1], a.Gradient);
        Assert.Equal(Palette[3], a.GradientShaded);
    }

    [Fact]
    public void ResolveAccent_LightAndDark_NeverAgree()
    {
        var light = AppTheme.ResolveAccent(Palette, dark: false);
        var dark = AppTheme.ResolveAccent(Palette, dark: true);

        Assert.NotEqual(light.Fill, dark.Fill);
        Assert.NotEqual(light.OnAccent, dark.OnAccent);
    }

    [Fact]
    public void ResolveAccent_WithoutAPalette_FallsBackRatherThanInventingShades()
    {
        // A policy-locked or freshly-provisioned profile has no AccentPalette value. Deriving tints
        // from a single colour would look plausible and be wrong; the documented Windows blue is not.
        var a = AppTheme.ResolveAccent(null, dark: false);

        Assert.Equal(AppTheme.FallbackAccent, a.Fill);
        Assert.Equal(AppTheme.FallbackAccent, a.Text);
        Assert.Equal(AppTheme.FallbackAccent, a.Gradient);
    }

    [Fact]
    public void ResolveAccent_WithATruncatedPalette_StillFallsBackPerShade()
    {
        var a = AppTheme.ResolveAccent([Color.FromRgb(1, 1, 1)], dark: false);

        Assert.Equal(AppTheme.FallbackAccent, a.Fill); // Darker1 is out of range
    }

    [Theory]
    [InlineData(AppAppearance.Light)]
    [InlineData(AppAppearance.Dark)]
    [InlineData(AppAppearance.System)]
    [InlineData("nonsense")]
    public void ToVariant_MapsEverySetting(string appearance)
    {
        var variant = AppTheme.ToVariant(appearance);

        var expected = appearance switch
        {
            AppAppearance.Light => ThemeVariant.Light,
            AppAppearance.Dark => ThemeVariant.Dark,
            // Default means "inherit from the platform", which is what makes the System setting
            // track the OS without this app watching for theme changes itself.
            _ => ThemeVariant.Default,
        };
        Assert.Equal(expected, variant);
    }

    [Fact]
    public void ReadAccentPalette_OnThisMachine_ReturnsSevenShadesOrNull()
    {
        // Not asserting specific colours — the developer's accent is whatever they picked. The
        // contract under test is the SHAPE: either a full set of shades, or null.
        var palette = AppTheme.ReadAccentPalette();

        if (palette is not null)
            Assert.Equal(7, palette.Length);
    }
}
