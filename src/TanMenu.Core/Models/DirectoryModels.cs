namespace TanMenu.Core.Models;

public class DirectoryItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsDisabled { get; set; }

    /// <summary>
    /// The filesystem path whose icon should represent this item:
    /// the resolved target for a .lnk, otherwise the item's own path.
    /// Null when the item is disabled / unresolved. The App layer maps this
    /// to an ImageSource via IIconProvider; Core does not produce bitmaps.
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>Last-write-time (UTC) and size of the <see cref="IconKey"/> file, captured during the
    /// directory scan so the icon cache can be validated without an extra per-item FileInfo stat. Left
    /// zero for static keys (folders, unresolved items) — they cache under a stable (default, 0) key.</summary>
    public DateTime IconMtime { get; set; }
    public long IconSize { get; set; }
}

public class DirectoryContents
{
    public string Directory { get; set; } = string.Empty;
    public string DirectoryName { get; set; } = string.Empty;
    public List<DirectoryItem> Items { get; set; } = new();
}
