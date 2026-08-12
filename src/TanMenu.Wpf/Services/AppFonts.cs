using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace TanMenu.Wpf.Services;

/// <summary>Registers and resolves TanMenu's embedded Alibaba PuHuiTi font family.</summary>
public static class AppFonts
{
    /// <summary>Stable setting value used by older TanMenu releases for the bundled family.</summary>
    public const string BundledConfigName = "Alibaba PuHuiTi";

    /// <summary>Last-resort family present on every supported Windows version.</summary>
    public const string Fallback = "Segoe UI";

    /// <summary>System fallbacks used only if the embedded family or a glyph cannot be resolved.</summary>
    public const string UiStack = "Microsoft YaHei UI, Segoe UI";

    public const string DisplayStack = UiStack;

    private const string GlyphFallbacks = "Microsoft YaHei UI, Segoe UI";
    private static readonly Uri CollectionKey = new("fonts:TanMenu");
    private static readonly Uri CollectionSource = new("avares://TanMenu.Wpf/wwwroot/fonts");

    private static EmbeddedFontCollection? _collection;
    private static FontFamily? _bundledDefault;

    /// <summary>Registers the TTF resources before any control measures text.</summary>
    public static AppBuilder WithBundledFonts(this AppBuilder builder) =>
        builder.ConfigureFonts(manager =>
        {
            _collection = new EmbeddedFontCollection(CollectionKey, CollectionSource);
            manager.AddFontCollection(_collection);
        });

    /// <summary>The embedded family, with a system stack as a defensive startup fallback.</summary>
    public static FontFamily Default => _bundledDefault ??= ResolveBundledDefault();

    public static FontFamily DefaultDisplay => Default;

    private static FontFamily ResolveBundledDefault()
    {
        try
        {
            // The TTF name table is localized. Match a stable marker and then address the family by
            // the exact name Avalonia discovered on this machine.
            var actualName = _collection?
                .Select(static family => family.Name)
                .FirstOrDefault(static name =>
                    name.Contains("PuHuiTi", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("普惠体", StringComparison.Ordinal));

            if (!string.IsNullOrWhiteSpace(actualName))
                return new FontFamily($"{CollectionKey}#{actualName}");

            Serilog.Log.Warning("Embedded Alibaba PuHuiTi family was not reported by the font collection");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to resolve embedded Alibaba PuHuiTi");
        }

        return new FontFamily(UiStack);
    }

    /// <summary>Blank and the historical bundled name select the embedded font; any other value is
    /// treated as a user-selected installed family.</summary>
    public static FontFamily Resolve(string? configured, FontFamily? whenBlank = null)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return whenBlank ?? Default;

        var name = configured.Trim();
        if (string.Equals(name, BundledConfigName, StringComparison.OrdinalIgnoreCase))
            return Default;

        try
        {
            var system = new FontFamily($"{name}, {GlyphFallbacks}");
            return Resolves(system) ? system : new FontFamily(Fallback);
        }
        catch
        {
            return new FontFamily(Fallback);
        }
    }

    public static bool Resolves(FontFamily family)
    {
        try { return FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out _); }
        catch { return false; }
    }
}
