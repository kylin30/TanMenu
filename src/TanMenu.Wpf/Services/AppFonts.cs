using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace TanMenu.Wpf.Services;

/// <summary>
/// The one place that turns a configured font name into a real <see cref="FontFamily"/>.
/// </summary>
/// <remarks>
/// The bundled TTFs ship INSIDE the executable, so they are not installed system fonts and cannot be
/// resolved by name alone. They are registered once as an Avalonia font collection and looked up
/// through it.
/// <para>
/// The family names are discovered at run time rather than hardcoded, because they are not stable:
/// AlibabaPuHuiTi's <c>name</c> table carries both "Alibaba PuHuiTi 2.0 55 Regular" (0x0409) and
/// "阿里巴巴普惠体 2.0 55 Regular" (0x0804), and the text stack surfaces whichever matches the
/// running system. A hardcoded name therefore resolves on one locale and silently falls back to a
/// default font on the other — silently, because a wrong family name does not throw.
/// </para>
/// <para>
/// The CONFIG values ("Alibaba PuHuiTi", "Pixel", "Press Start 2P") are a stored contract and must
/// keep meaning the same font, so they are matched to whatever the collection actually reports via
/// the markers below.
/// </para>
/// </remarks>
public static class AppFonts
{
    /// <summary>Config value meaning "the app's own default font".</summary>
    public const string DefaultConfigName = "Alibaba PuHuiTi";

    /// <summary>Last-resort family when nothing else resolves. Present on every supported Windows.</summary>
    public const string Fallback = "Segoe UI";

    /// <summary>Appended to every resolved family so a missing glyph degrades instead of rendering
    /// tofu — notably CJK under a Latin-only pick like "Press Start 2P".</summary>
    private const string GlyphFallbacks = "Microsoft YaHei, Segoe UI";

    private static readonly Uri CollectionKey = new("fonts:TanMenu");
    private static readonly Uri CollectionSource = new("avares://TanMenu.Wpf/wwwroot/fonts");

    /// <summary>Config name → substrings that identify it in whatever locale the name table yields.
    /// Order is the order the settings dropdown lists them in.</summary>
    private static readonly (string ConfigName, string[] Markers)[] BundledMarkers =
    [
        (DefaultConfigName, ["PuHuiTi", "普惠体"]),
        ("Pixel", ["Fusion Pixel"]),
        ("Press Start 2P", ["Press Start 2P"]),
    ];

    private static EmbeddedFontCollection? _collection;
    private static Dictionary<string, FontFamily>? _bundled;

    /// <summary>Register the embedded collection. Must run during <c>AppBuilder</c> setup, before any
    /// text is measured.</summary>
    public static AppBuilder WithBundledFonts(this AppBuilder builder) =>
        builder.ConfigureFonts(manager =>
        {
            _collection = new EmbeddedFontCollection(CollectionKey, CollectionSource);
            manager.AddFontCollection(_collection);
        });

    /// <summary>Config names of the bundled fonts that actually resolved, in dropdown order.</summary>
    public static IReadOnlyList<string> BundledNames => Bundled.Keys.ToList();

    private static Dictionary<string, FontFamily> Bundled
    {
        get
        {
            if (_bundled is not null)
                return _bundled;

            var map = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // The collection is an IReadOnlyList<FontFamily> of whatever the TTFs reported.
                var actual = _collection?.Select(f => f.Name).ToList() ?? [];
                foreach (var (configName, markers) in BundledMarkers)
                {
                    var match = actual.FirstOrDefault(
                        name => markers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)));
                    if (match is null)
                    {
                        Serilog.Log.Warning(
                            "Bundled font {ConfigName} did not resolve; collection reported [{Families}]",
                            configName, string.Join(", ", actual));
                        continue;
                    }
                    map[configName] = new FontFamily($"{CollectionKey}#{match}");
                }
            }
            catch (Exception ex)
            {
                // A font problem must never take the app down — every caller degrades to Fallback.
                Serilog.Log.Error(ex, "Failed to enumerate the bundled font collection");
            }

            return _bundled = map;
        }
    }

    /// <summary>Resolve a configured font name. Blank yields <paramref name="whenBlank"/> (each window
    /// decides what "app default" means for its own chrome); an unknown name is treated as an
    /// installed system font; anything that fails to resolve degrades to <see cref="Fallback"/>.</summary>
    public static FontFamily Resolve(string? configured, FontFamily? whenBlank = null)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return whenBlank ?? Resolve(DefaultConfigName);

        var name = configured.Trim();
        try
        {
            if (Bundled.TryGetValue(name, out var bundled))
                return bundled;

            // An installed system font. Appending the glyph fallbacks is what keeps a Latin-only
            // choice from rendering Chinese item names as boxes.
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
