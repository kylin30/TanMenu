namespace TanMenu.Core.Models;

public class AppConfig
{
    /// <summary>
    /// The only theme id the app recognizes. The CSS theme packs (WinXP / Win7 / Windows11 / Pixel,
    /// plus user-authored <c>user:&lt;name&gt;</c> stylesheets) went away with the WebView in the
    /// Avalonia + NativeAOT rewrite; a single native Windows 2000 classic theme replaced them.
    /// <para>
    /// The field is kept rather than deleted so an old config still round-trips, and every legacy
    /// value is coerced to this one on load (<c>ConfigService.Normalize</c>). Deliberately NOT reusing
    /// any historical id: "Win7" would name a theme the app no longer renders, and "Win2000" was
    /// previously a *removed* value that migrated away — either would make the file lie about what
    /// is on screen.
    /// </para>
    /// </summary>
    public const string CanonicalTheme = "Classic";

    public GeneralConfig General { get; set; } = new();

    // Defaults are intentionally EMPTY — no hardcoded personal paths.
    public List<string> Folders { get; set; } = new();

    /// <summary>Root folder whose immediate subdirectories become the launcher groups (each folder on
    /// the desktop becomes a group). Defaults to the current user's Desktop — resolved per-user at
    /// runtime, so it's not a hardcoded personal path. Empty = no folder groups (only 常用工具).</summary>
    public string RootFolder { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}

public class GeneralConfig
{
    /// <summary>UI language: "Auto" follows the OS UI language; explicit values: "zh-Hans", "en-US".</summary>
    public string Language { get; set; } = "Auto";

    public bool AutoClose { get; set; } = true;
    public bool TopMost { get; set; } = true;
    /// <summary>Legacy 0.9.x setting retained only so old config files deserialize cleanly.
    /// The launcher popup no longer creates a transient taskbar button; users pin the stable
    /// application shortcut instead.</summary>
    public bool ShowInTaskbar { get; set; } = false;

    /// <summary>Enable a configurable global hotkey that toggles (summons/hides) the launcher.
    /// Default OFF — the user opts in and sets their own combo.</summary>
    public bool GlobalHotkeyEnabled { get; set; } = false;

    /// <summary>The global hotkey combo, e.g. "Ctrl+Alt+Space". Empty = none. Only registered
    /// when <see cref="GlobalHotkeyEnabled"/> is true.</summary>
    public string GlobalHotkey { get; set; } = "";

    public int PositionOffset { get; set; } = 8;
    public int ColButtonCount { get; set; } = 10;

    /// <summary>Active theme. Exactly one theme exists — see <see cref="AppConfig.CanonicalTheme"/>.
    /// Any other value (including every historical id) is coerced on load.</summary>
    public string ThemeName { get; set; } = AppConfig.CanonicalTheme;

    /// <summary>UI font family, applied to the whole launcher window (default 阿里巴巴普惠体, which
    /// ships embedded in the executable). Empty = fall back to the theme's own chrome font.
    /// Resolution lives in <c>TanMenu.Wpf.Services.AppFonts</c>.</summary>
    public string FontFamily { get; set; } = "Alibaba PuHuiTi";

    /// <summary>Launcher button size: "Small" | "Medium" | "Large". Default Small.</summary>
    public string ButtonSize { get; set; } = "Small";

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
    /// <summary>Executable, document, folder, or shell-resolved command to launch.</summary>
    public string Command { get; set; } = "";
    /// <summary>Whether this tool is shown in the group.</summary>
    public bool Show { get; set; } = true;

    public static List<DefaultTool> BuiltInDefaults() => new()
    {
        new() { Name = "计算器", Command = "calc.exe" },
        new() { Name = "记事本", Command = "notepad.exe" },
        new() { Name = "任务管理器", Command = "taskmgr.exe" },
        new() { Name = "控制面板", Command = "control.exe" },
        new() { Name = "画图", Command = "mspaint.exe" },
    };
}
