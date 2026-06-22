namespace TanMenu.Core.Models;

public class AppConfig
{
    public WindowConfig Window { get; set; } = new();
    public GeneralConfig General { get; set; } = new();

    // Defaults are intentionally EMPTY — no hardcoded personal paths.
    public List<string> Folders { get; set; } = new();

    /// <summary>Root folder whose immediate subdirectories become the launcher groups
    /// (e.g. set to the Desktop → each folder on the desktop becomes a group).</summary>
    public string RootFolder { get; set; } = "";
}

public class WindowConfig
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public DateTime LastModified { get; set; } = DateTime.Now;
}

public class GeneralConfig
{
    public bool AutoClose { get; set; } = true;
    public bool TopMost { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = false;
    public int PositionOffset { get; set; } = 8;
    public int Tolerance { get; set; } = 5;
    public int ColButtonCount { get; set; } = 10;

    /// <summary>Active retro theme: "Win98" | "WinXP" | "Win7".</summary>
    public string ThemeName { get; set; } = "Win7";

    /// <summary>UI font family override. Empty = use the active theme's default font.</summary>
    public string FontFamily { get; set; } = "Alibaba PuHuiTi";
}
