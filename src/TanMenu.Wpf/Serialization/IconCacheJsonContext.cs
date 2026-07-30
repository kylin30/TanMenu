using System.Text.Json.Serialization;

namespace TanMenu.Wpf.Serialization;

/// <summary>
/// One persisted icon-cache entry. The property NAMES are part of the on-disk contract for
/// <c>&lt;dataRoot&gt;\cache\iconCache.json</c> — renaming or re-casing any of them silently orphans
/// every existing user's cache and forces a full cold re-extraction of every icon on first run.
/// </summary>
/// <remarks>
/// Lifted out of <c>MenuService</c> (where it was a private nested class) because the
/// <c>System.Text.Json</c> source generator cannot reference a private nested type. Moving it changes
/// nothing on disk: the serialized shape is identical.
/// </remarks>
public sealed class PersistedIcon
{
    /// <summary>The icon file's <c>LastWriteTimeUtc</c> when the icon was extracted.</summary>
    public DateTime Mtime { get; set; }

    /// <summary>The icon file's length when the icon was extracted.</summary>
    public long Size { get; set; }

    /// <summary>The extracted icon as raw PNG bytes; System.Text.Json emits this as base64.</summary>
    public byte[] Png { get; set; } = [];
}

/// <summary>
/// Source-generated contract for <c>iconCache.json</c>. NativeAOT cannot use the reflection-based
/// serializer (IL2026/IL3050), so the cache read/write in <c>MenuService</c> goes through this.
///
/// The options are the serializer DEFAULTS on purpose: the pre-migration code called
/// <c>JsonSerializer.Serialize(snapshot)</c> and <c>JsonSerializer.Deserialize&lt;…&gt;(json)</c> with
/// NO options at all, which means PascalCase property names, no indentation, and case-SENSITIVE
/// reads. Adding <c>WriteIndented</c> or a naming policy here would rewrite the file in a shape the
/// old (and this) reader cannot match, and the <c>catch</c> around the load would hide the damage —
/// every user would silently re-extract every icon. Mirrors <c>LinkCacheJsonContext</c> in
/// TanMenu.Core, which is frozen for the same reason.
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(Dictionary<string, PersistedIcon>))]
public partial class IconCacheJsonContext : JsonSerializerContext;
