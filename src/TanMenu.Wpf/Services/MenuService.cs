using System.Collections.Concurrent;
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

    /// <summary>The standard fallback icon (base64 PNG) for items with no extractable icon — the
    /// Windows stock application icon, or the bundled flat-file icon if that can't be obtained.</summary>
    private readonly Lazy<string> _fallbackIcon;

    /// <summary>Last-resort bundled fallback icon (flat "generic file" 48×48 PNG, base64), used only
    /// when the Windows stock application icon (the primary no-icon fallback, see <see cref="_fallbackIcon"/>)
    /// can't be obtained.</summary>
    private const string DefaultIconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAFiUAABYlAUlSJPAAAACrSURBVGhD7dM7DsMgFERRL5s1sRLWRJNUbkYkT9jv40T3StMCp+A4iIh+ptba6+70zNT0MVen56Z1PmDOeXmlCA/AGKMO4QUoQ3gCShDegHREBCAVEQVIQ3gAvi0cAcAYAKu/BPTebw3ATiuA5wBYATBWAtBPuTsAO60AngNgBcBYCUA/5e4A7LQCeA6AFQBjJQD9lJ+mj10NgNV5QfT0Xrf0oqjpvUREz+0N2/Xp7Z0GKaEAAAAASUVORK5CYII=";

    /// <summary>Display name of the built-in common-tools group.</summary>
    public const string DefaultToolsGroupName = "常用工具";

    public MenuService(MenuDataService data, IIconProvider icons)
    {
        _data = data;
        _icons = icons;
        _fallbackIcon = new Lazy<string>(() =>
        {
            var bytes = _icons.GetDefaultAppIconPngBytes();
            return bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : DefaultIconBase64;
        });
    }

    // Cache extracted icons by path so repeat reloads (Refresh / settings change) don't re-run
    // SHGetFileInfo + GDI+ encode + base64 for unchanged files; invalidated by last-write-time + size.
    private readonly ConcurrentDictionary<string, (DateTime Mtime, long Size, string B64)> _iconCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Base64 PNG for an icon path, served from cache when the underlying file is unchanged;
    /// falls back to the stock icon for empty/unextractable keys.</summary>
    private string IconBase64For(string? iconKey)
    {
        if (string.IsNullOrEmpty(iconKey))
            return _fallbackIcon.Value;

        DateTime mtime = default;
        long size = 0;
        try
        {
            var fi = new FileInfo(iconKey);
            if (fi.Exists) { mtime = fi.LastWriteTimeUtc; size = fi.Length; }
        }
        catch { /* unreadable path → treat as a static key (folders, bare commands) */ }

        if (_iconCache.TryGetValue(iconKey, out var hit) && hit.Mtime == mtime && hit.Size == size)
            return hit.B64;

        var bytes = _icons.GetIconPngBytes(iconKey);
        var b64 = bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : _fallbackIcon.Value;
        _iconCache[iconKey] = (mtime, size, b64);
        return b64;
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
            items.Add(new MenuItemVm
            {
                Name = t.Name,
                // Prefer the resolved full path; the shell still resolves a bare command otherwise.
                FullPath = File.Exists(resolved) ? resolved : t.Command,
                IsDirectory = false,
                IsDisabled = false,
                IconBase64 = IconBase64For(resolved),
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

    public Task<List<MenuGroupVm>> GetMenuAsync(IEnumerable<string> folders)
    {
        var list = folders as IReadOnlyList<string> ?? folders.ToList();
        // Run the folder scan + .lnk resolution + icon extraction OFF the UI thread; Blazor marshals
        // the awaited result back to the UI sync context for the StateHasChanged.
        return Task.Run(async () =>
        {
            var contents = await _data.GetDirectoryContents(list);
            var groups = new List<MenuGroupVm>(contents.Count);
            foreach (var c in contents)
            {
                var items = new List<MenuItemVm>(c.Items.Count);
                foreach (var it in c.Items)
                {
                    items.Add(new MenuItemVm
                    {
                        Name = it.Name,
                        FullPath = it.FullPath,
                        TargetPath = it.TargetPath,
                        IsDirectory = it.IsDirectory,
                        IsDisabled = it.IsDisabled,
                        // Invalid shortcuts (no IconKey) and failed extraction fall back to the stock icon.
                        IconBase64 = IconBase64For(it.IconKey),
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
        });
    }
}
