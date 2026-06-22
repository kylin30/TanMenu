namespace TanMenu.Core.Models;

public class AppConfig
{
    public WindowConfig Window { get; set; } = new();
    public GeneralConfig General { get; set; } = new();

    // Defaults are intentionally EMPTY — no hardcoded personal paths.
    // First-run UX (folder picker / guidance) is added in a later milestone.
    public List<string> Folders { get; set; } = new();
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
    public int ColButtonCount { get; set; } = 8;

    /// <summary>Active retro theme: "Win31" | "Win7" | "ModernRetro".</summary>
    public string ThemeName { get; set; } = "Win31";
}
