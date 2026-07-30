using Avalonia.Media;

namespace TanMenu.Wpf.Services;

/// <summary>
/// The one place that turns a configured font name into a real <see cref="FontFamily"/>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is bundled any more. The retro build shipped four TTFs inside the executable — Alibaba
/// PuHuiTi Regular and Bold, Fusion Pixel and Press Start 2P, 22.7 MB together, a third of the whole
/// payload — and registered them as an Avalonia font collection because embedded families are not
/// installed system fonts and cannot be resolved by name. Fluent 2's typeface is the system's own, so
/// the collection, the locale-dependent name-table probing it needed, and the TTFs all went away.
/// </para>
/// <para>
/// The three retired config values are coerced to "" (= the app default) on load by
/// <c>ConfigService.Normalize</c>; see <see cref="RetiredBundledNames"/>.
/// </para>
/// </remarks>
public static class AppFonts
{
    /// <summary>Last-resort family. Present on every supported Windows.</summary>
    public const string Fallback = "Segoe UI";

    /// <summary>
    /// The app's default UI stack. Segoe UI Variable Text is Windows 11's Fluent 2 UI face; Segoe UI
    /// covers Windows 10; Microsoft YaHei UI is appended so a CJK item name never renders as tofu.
    /// Kept byte-identical to <c>AppFontFamily</c> in Themes/Fluent2/Tokens.axaml — the XAML value
    /// styles the standard controls, this one is what the launcher's font setting falls back to.
    /// </summary>
    public const string UiStack = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";

    /// <summary>Appended to a user-chosen family so a Latin-only pick still renders CJK item names.</summary>
    private const string GlyphFallbacks = "Microsoft YaHei UI, Segoe UI";

    /// <summary>The default UI font as a resolved family.</summary>
    public static FontFamily Default { get; } = new(UiStack);

    /// <summary>Resolve a configured font name. Blank yields <paramref name="whenBlank"/> (each window
    /// decides what "app default" means for its own chrome); an unknown name is treated as an
    /// installed system font; anything that fails to resolve degrades to <see cref="Fallback"/>.</summary>
    public static FontFamily Resolve(string? configured, FontFamily? whenBlank = null)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return whenBlank ?? Default;

        var name = configured.Trim();
        try
        {
            // Appending the glyph fallbacks is what keeps a Latin-only choice from rendering Chinese
            // item names as boxes.
            var system = new FontFamily($"{name}, {GlyphFallbacks}");
            return Resolves(system) ? system : new FontFamily(Fallback);
        }
        catch
        {
            return new FontFamily(Fallback);
        }
    }

    /// <summary>A wrong family name does NOT throw — the text stack quietly substitutes a default.
    /// Ask the font manager whether the name really produced a glyph typeface.</summary>
    public static bool Resolves(FontFamily family)
    {
        try { return FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out _); }
        catch { return false; }
    }
}
