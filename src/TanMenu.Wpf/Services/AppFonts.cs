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
/// installed system fonts and cannot be resolved by name. The typeface is the system's own, so the
/// collection, the locale-dependent name-table probing it needed, and the TTFs all went away. That
/// held for Fluent 2 and holds harder for Aero: Segoe UI 9 pt IS the Windows 7 system font, and the
/// Aero theme's own seven bundled Inter TTFs were deliberately not vendored (Round 7, Task 2 — no
/// font licence upstream, and Inter has no CJK coverage at all).
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
    /// The app's default UI stack. Plain Segoe UI, which
    /// <c>https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-fonts</c> gives as the Windows 7
    /// system font ("Segoe UI 9 pt is the system font"); Microsoft YaHei UI is appended so a CJK item
    /// name never renders as tofu. Kept byte-identical to <c>AppFontFamily</c> in
    /// Themes/Shell/Tokens.axaml — the XAML value styles the standard controls, this one is what the
    /// launcher's font setting falls back to.
    /// </summary>
    /// <remarks>
    /// It was <c>Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI</c> until Round 7, Task 7. Segoe
    /// UI *Variable*'s three optical sizes are a Windows 11 mechanism and Windows 7 had one face at
    /// one weight per size, so the conversion drops them; the token they mirrored dropped the same
    /// three families (<c>FontFamilySmall</c> / <c>FontFamilyText</c> / <c>FontFamilyDisplay</c>) in
    /// the same task, two of which grep had already found unreferenced.
    /// </remarks>
    public const string UiStack = "Segoe UI, Microsoft YaHei UI";

    /// <summary>
    /// The face for headings. IDENTICAL to <see cref="UiStack"/> since Round 7, Task 7, and kept as a
    /// separate constant rather than collapsed into it because it has a separate JOB: it is what
    /// <c>LauncherViewModel.TitleFontFamily</c> falls back to when the user has chosen no font, and
    /// the two could legitimately diverge again.
    /// </summary>
    /// <remarks>
    /// It was <c>Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI</c> — Segoe UI Variable ships
    /// three optical sizes and Windows 11 selects between them by size band (Small below 14, Text at
    /// 14, Display from 18). Aero has no such band: its published ramp is one family at 9 pt body,
    /// 11 pt group headers and 12 pt main instructions, and hierarchy comes from colour and weight.
    /// </remarks>
    public const string DisplayStack = "Segoe UI, Microsoft YaHei UI";

    /// <summary>Appended to a user-chosen family so a Latin-only pick still renders CJK item names.</summary>
    private const string GlyphFallbacks = "Microsoft YaHei UI, Segoe UI";

    /// <summary>The default UI font as a resolved family.</summary>
    public static FontFamily Default { get; } = new(UiStack);

    /// <summary>The default heading font — see <see cref="DisplayStack"/>.</summary>
    public static FontFamily DefaultDisplay { get; } = new(DisplayStack);

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
