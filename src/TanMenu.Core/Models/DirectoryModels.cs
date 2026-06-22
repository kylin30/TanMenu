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
}

public class DirectoryContents
{
    public string Directory { get; set; } = string.Empty;
    public string DirectoryName { get; set; } = string.Empty;
    public List<DirectoryItem> Items { get; set; } = new();
}
