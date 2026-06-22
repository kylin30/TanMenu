using TanMenu.Core.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Bridges Core's <see cref="MenuDataService"/> (which yields <c>IconKey</c> paths) to the
/// retro Blazor UI's view models (which bind <c>IconBase64</c>), encoding icons via
/// <see cref="IIconProvider"/>. Base64 lives in the UI layer only; Core stays icon-format-agnostic.
/// </summary>
public sealed class MenuService
{
    private readonly MenuDataService _data;
    private readonly IIconProvider _icons;

    /// <summary>Flat pixel "generic file" icon (48×48 PNG, base64) used when an item has no
    /// extractable icon — i.e. invalid/broken shortcuts (IsDisabled, no IconKey) or rare
    /// extraction failures — so every button still shows an icon.</summary>
    private const string DefaultIconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAACrSURBVGhD7dM7DsMgFERRL5s1sRLWRJNUbkYkT9jv40T3StMCp+A4iIh+ptba6+70zNT0MVen56Z1PmDOeXmlCA/AGKMO4QUoQ3gCShDegHREBCAVEQVIQ3gAvi0cAcAYAKu/BPTebw3ATiuA5wBYATBWAtBPuTsAO60AngNgBcBYCUA/5e4A7LQCeA6AFQBjJQD9lJ+mj10NgNV5QfT0Xrf0oqjpvUREz+0N2/Xp7Z0GKaEAAAAASUVORK5CYII=";

    public MenuService(MenuDataService data, IIconProvider icons)
    {
        _data = data;
        _icons = icons;
    }

    /// <summary>Build the menu from a ROOT folder: each immediate subdirectory becomes a group.</summary>
    public Task<List<MenuGroupVm>> GetMenuFromRootAsync(string? rootFolder)
    {
        string[] dirs;
        try
        {
            dirs = string.IsNullOrWhiteSpace(rootFolder) || !System.IO.Directory.Exists(rootFolder)
                ? Array.Empty<string>()
                : System.IO.Directory.GetDirectories(rootFolder);
        }
        catch
        {
            dirs = Array.Empty<string>();
        }
        return GetMenuAsync(dirs);
    }

    public async Task<List<MenuGroupVm>> GetMenuAsync(IEnumerable<string> folders)
    {
        var contents = await _data.GetDirectoryContents(folders);
        var groups = new List<MenuGroupVm>(contents.Count);
        foreach (var c in contents)
        {
            var items = new List<MenuItemVm>(c.Items.Count);
            foreach (var it in c.Items)
            {
                string? b64 = null;
                if (!string.IsNullOrEmpty(it.IconKey))
                {
                    var bytes = _icons.GetIconPngBytes(it.IconKey);
                    if (bytes is { Length: > 0 })
                        b64 = Convert.ToBase64String(bytes);
                }
                // Invalid shortcuts (IsDisabled → no IconKey) and any failed extraction fall back
                // to a default icon so the button isn't iconless.
                b64 ??= DefaultIconBase64;
                items.Add(new MenuItemVm
                {
                    Name = it.Name,
                    FullPath = it.FullPath,
                    TargetPath = it.TargetPath,
                    IsDirectory = it.IsDirectory,
                    IsDisabled = it.IsDisabled,
                    IconBase64 = b64,
                });
            }
            groups.Add(new MenuGroupVm
            {
                Directory = c.Directory,
                DirectoryName = c.DirectoryName,
                Items = items,
            });
        }
        return groups;
    }
}
