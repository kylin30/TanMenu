using System.Text.Json.Serialization;
using TanMenu.Core.Models;

namespace TanMenu.Core.Serialization;

/// <summary>
/// Source-generated <c>System.Text.Json</c> contracts. NativeAOT cannot use the reflection-based
/// serializer (IL2026/IL3050), so every serialize/deserialize call in this app goes through one of
/// the contexts below.
///
/// IMPORTANT: the options on each context reproduce, byte for byte, the format the reflection-based
/// code wrote before the AOT migration. Users have these files on disk already; changing indentation,
/// naming, or null handling here silently breaks their existing config or invalidates their caches.
/// The two contexts exist precisely because the two files were written with DIFFERENT options.
/// </summary>
///
/// <remarks>
/// config.json — written with <c>WriteIndented</c>, camelCase names, and nulls omitted.
/// The pre-migration options also registered a <c>JsonStringEnumConverter</c>, but the whole
/// <see cref="AppConfig"/> graph (AppConfig → GeneralConfig → List&lt;DefaultTool&gt;) contains only
/// string/bool/int members and no enum at all, so dropping it changes nothing on disk — and the
/// non-generic converter is itself AOT-hostile (IL3050).
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
public partial class ConfigJsonContext : JsonSerializerContext;

/// <summary>
/// linkCache.json — written with the serializer DEFAULTS (PascalCase property names, no indentation),
/// because the pre-migration code passed no options on read and only <c>WriteIndented = false</c> on
/// write. Do not "tidy" these options: a naming change would orphan every existing user's shortcut
/// cache and force a full cold re-resolve of their .lnk files.
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(Dictionary<string, ShortcutCacheEntry>))]
public partial class LinkCacheJsonContext : JsonSerializerContext;

/// <summary>One cached .lnk resolution. Property names are part of the on-disk contract.</summary>
public sealed class ShortcutCacheEntry
{
    public string? TargetPath { get; set; }
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
}
