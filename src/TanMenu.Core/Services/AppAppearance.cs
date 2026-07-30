namespace TanMenu.Core.Services;

/// <summary>
/// The stored vocabulary for how the app should look: the light/dark setting, and the font names
/// that are no longer offered.
/// </summary>
/// <remarks>
/// This lives in Core rather than next to the UI because both are <c>config.json</c> contracts —
/// values already on users' disks that <c>ConfigService.Normalize</c> has to canonicalize on load.
/// The mapping from these strings to an actual Avalonia <c>ThemeVariant</c> is the UI layer's job
/// (<c>TanMenu.Wpf.Services.AppTheme</c>).
/// </remarks>
public static class AppAppearance
{
    /// <summary>Follow the OS light/dark setting. The default, and what an older config that has no
    /// <c>appearance</c> key at all deserializes to.</summary>
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static bool IsValidSetting(string? appearance) =>
        string.Equals(appearance, System, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(appearance, Light, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(appearance, Dark, StringComparison.OrdinalIgnoreCase);

    /// <summary>Coerce any stored value to one of the three legal ones. Anything unrecognized becomes
    /// <see cref="System"/>, which is also how a fresh config behaves.</summary>
    public static string NormalizeSetting(string? appearance) =>
        string.Equals(appearance, Light, StringComparison.OrdinalIgnoreCase) ? Light :
        string.Equals(appearance, Dark, StringComparison.OrdinalIgnoreCase) ? Dark :
        System;

    /// <summary>
    /// Config values that used to name a font shipped inside the executable. The Fluent 2 rebuild
    /// dropped all four bundled TTFs in favour of the system UI face, so these names now resolve to
    /// nothing. A config still carrying one is normalized to "" (= the app default) rather than being
    /// left to render in whatever the text stack silently substitutes.
    /// </summary>
    public static readonly string[] RetiredFontNames =
        ["Alibaba PuHuiTi", "Pixel", "Press Start 2P"];

    public static bool IsRetiredFontName(string? family) =>
        !string.IsNullOrWhiteSpace(family) &&
        Array.Exists(RetiredFontNames,
            n => string.Equals(n, family.Trim(), StringComparison.OrdinalIgnoreCase));
}
