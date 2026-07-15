namespace TanMenu.Wpf.ViewModels;

/// <summary>A launcher item shaped for the retro Blazor UI (icon as raw base64 PNG).</summary>
public sealed class MenuItemVm
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string? TargetPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsDisabled { get; init; }
    // RAW base64 (Button.razor prepends data:image/png;base64,). Mutable so the async icon-fill pass
    // can replace the placeholder fallback with the real icon once the structure has rendered.
    public string? IconBase64 { get; set; }
}

/// <summary>A folder group of launcher items.</summary>
public sealed class MenuGroupVm
{
    public string Directory { get; init; } = "";
    public string DirectoryName { get; init; } = "";
    public List<MenuItemVm> Items { get; init; } = new();
}
