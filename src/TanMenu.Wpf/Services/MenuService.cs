using System.IO;
using TanMenu.Core.Models;
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

    /// <summary>Display name of the built-in common-tools group.</summary>
    public const string DefaultToolsGroupName = "常用工具";

    public MenuService(MenuDataService data, IIconProvider icons)
    {
        _data = data;
        _icons = icons;
    }

    /// <summary>Build the built-in "常用工具" group from the configured tools (only the shown ones).</summary>
    public MenuGroupVm BuildDefaultToolsGroup(IEnumerable<DefaultTool>? tools)
    {
        var items = new List<MenuItemVm>();
        foreach (var t in tools ?? Array.Empty<DefaultTool>())
        {
            if (!t.Show || string.IsNullOrWhiteSpace(t.Command))
                continue;

            var resolved = ResolveCommandPath(t.Command);
            string? b64 = null;
            var bytes = _icons.GetIconPngBytes(resolved);
            if (bytes is { Length: > 0 })
                b64 = Convert.ToBase64String(bytes);
            b64 ??= DefaultIconBase64;

            items.Add(new MenuItemVm
            {
                Name = t.Name,
                // Prefer the resolved full path; the shell still resolves a bare command otherwise.
                FullPath = File.Exists(resolved) ? resolved : t.Command,
                IsDirectory = false,
                IsDisabled = false,
                IconBase64 = b64,
            });
        }
        // Directory left empty → the group title isn't a clickable "open folder".
        return new MenuGroupVm { Directory = "", DirectoryName = DefaultToolsGroupName, Items = items };
    }

    /// <summary>Resolve a bare exe/command to a full path (System32, then PATH); returns the command
    /// unchanged if not found (LaunchService then lets the shell resolve it via App Paths/PATH).</summary>
    private static string ResolveCommandPath(string command)
    {
        try
        {
            if (Path.IsPathRooted(command))
                return command;

            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), command);
            if (File.Exists(sys))
                return sys;

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir, command);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { /* fall through */ }
        return command;
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
