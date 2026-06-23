namespace TanMenu.Core.Models;

public class AppConfig
{
    public GeneralConfig General { get; set; } = new();

    // Defaults are intentionally EMPTY — no hardcoded personal paths.
    public List<string> Folders { get; set; } = new();

    /// <summary>Root folder whose immediate subdirectories become the launcher groups
    /// (e.g. set to the Desktop → each folder on the desktop becomes a group).</summary>
    public string RootFolder { get; set; } = "";
}

public class GeneralConfig
{
    public bool AutoClose { get; set; } = true;
    public bool TopMost { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = false;

    /// <summary>Enable a configurable global hotkey that toggles (summons/hides) the launcher.
    /// Default OFF — the user opts in and sets their own combo.</summary>
    public bool GlobalHotkeyEnabled { get; set; } = false;

    /// <summary>The global hotkey combo, e.g. "Ctrl+Alt+Space". Empty = none. Only registered
    /// when <see cref="GlobalHotkeyEnabled"/> is true.</summary>
    public string GlobalHotkey { get; set; } = "";

    public int PositionOffset { get; set; } = 8;
    public int ColButtonCount { get; set; } = 10;

    /// <summary>Active theme: "Win98" | "WinXP" | "Win7" | "Windows11".</summary>
    public string ThemeName { get; set; } = "Win7";

    /// <summary>UI font family override. Empty = use the active theme's NATIVE font (each theme
    /// defaults to the font of its corresponding Windows version).</summary>
    public string FontFamily { get; set; } = "";

    /// <summary>Show the built-in "常用工具" group (common system tools) before the folder groups.</summary>
    public bool ShowDefaultTools { get; set; } = true;

    /// <summary>The tools in the built-in "常用工具" group. Each can be toggled (Show) or edited;
    /// the list itself is customizable (add/remove entries, e.g. by editing config.json).</summary>
    public List<DefaultTool> DefaultTools { get; set; } = DefaultTool.BuiltInDefaults();
}

/// <summary>One entry in the built-in "常用工具" group: a display name + a command to launch.</summary>
public class DefaultTool
{
    public string Name { get; set; } = "";
    /// <summary>Executable or command launched via the shell (e.g. "calc.exe", "powershell.exe").</summary>
    public string Command { get; set; } = "";
    /// <summary>Whether this tool is shown in the group.</summary>
    public bool Show { get; set; } = true;

    public static List<DefaultTool> BuiltInDefaults() => new()
    {
        new() { Name = "计算器", Command = "calc.exe" },
        new() { Name = "命令行", Command = "cmd.exe" },
        new() { Name = "PowerShell", Command = "powershell.exe" },
        new() { Name = "记事本", Command = "notepad.exe" },
        new() { Name = "任务管理器", Command = "taskmgr.exe" },
        new() { Name = "控制面板", Command = "control.exe" },
        new() { Name = "注册表", Command = "regedit.exe" },
        new() { Name = "画图", Command = "mspaint.exe" },
    };
}
