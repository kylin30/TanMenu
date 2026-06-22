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

    public MenuService(MenuDataService data, IIconProvider icons)
    {
        _data = data;
        _icons = icons;
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
                if (!it.IsDisabled && !string.IsNullOrEmpty(it.IconKey))
                {
                    var bytes = _icons.GetIconPngBytes(it.IconKey);
                    if (bytes is { Length: > 0 })
                        b64 = Convert.ToBase64String(bytes);
                }
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
