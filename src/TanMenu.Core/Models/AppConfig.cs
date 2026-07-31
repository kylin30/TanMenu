using TanMenu.Core.Services;

namespace TanMenu.Core.Models;

public class AppConfig
{
    /// <summary>
    /// The only theme id the app recognizes. The CSS theme packs (WinXP / Win7 / Windows11 / Pixel,
    /// plus user-authored <c>user:&lt;name&gt;</c> stylesheets) went away with the WebView, the
    /// Windows 2000 theme that briefly replaced them went away with the Fluent 2 rebuild, and what
    /// remains is one look drawn from Themes/Fluent2.
    /// <para>
    /// The field is kept rather than deleted so an old config still round-trips, and every legacy
    /// value — including the outgoing "Classic" — is coerced to this one on load
    /// (<c>ConfigService.Normalize</c>). Light and dark are NOT theme ids: they are
    /// <see cref="GeneralConfig.Appearance"/>, because one design language rendering in two variants
    /// is not two themes.
    /// </para>
    /// </summary>
    public const string CanonicalTheme = "Fluent";

    /// <summary>Schema the CURRENT build writes. <see cref="GeneralConfig.SchemaVersion"/> carries the
    /// version a config file was last normalized to; <c>ConfigService.Normalize</c> migrates anything
    /// lower and stamps this value.</summary>
    public const int CurrentSchemaVersion = 1;

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

    /// <summary>Hide the window when it loses focus. Default OFF since 0.12: this is a taskbar-resident
    /// application window, and a window that vanishes when you click elsewhere reads as a crash. The
    /// 0.11 flyout defaulted it ON — see the schema-1 migration in <c>ConfigService.Normalize</c>.</summary>
    public bool AutoClose { get; set; } = false;

    /// <summary>Keep the window above others. Default OFF since 0.12, for the same reason as
    /// <see cref="AutoClose"/>: an ordinary app window does not float over everything.</summary>
    public bool TopMost { get; set; } = false;

    /// <summary>Schema this config was last normalized to. MUST default to 0, not
    /// <see cref="AppConfig.CurrentSchemaVersion"/>: System.Text.Json leaves an absent property at its
    /// initializer value, so a non-zero default would make a pre-0.12 file — which has no
    /// <c>schemaVersion</c> key — indistinguishable from an already-migrated one and skip the
    /// migration. Zero therefore means "old config OR brand-new config", and the migration is written
    /// to be a no-op on the latter.</summary>
    public int SchemaVersion { get; set; } = 0;

    /// <summary>Restored window size, in DIPs.</summary>
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 640;

    /// <summary>Restored window position, in DIPs. Null means "no remembered position — centre me",
    /// which is what a first run gets.
    /// <para>
    /// Deliberately nullable rather than a negative sentinel: a monitor to the LEFT of the primary one
    /// occupies negative coordinates, so a genuinely remembered position can be negative and must not
    /// be mistaken for "unset". Treating `&lt; 0` as the sentinel would teleport such a window to the
    /// primary monitor's centre on every restart.
    /// </para></summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public bool WindowMaximized { get; set; } = false;

    /// <summary>The user chose 不再提示 on the in-app taskbar-pin notice. The 行为 settings card keeps an
    /// always-available 固定到任务栏 button regardless of this flag.</summary>
    public bool TaskbarPinPromptDismissed { get; set; } = false;

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

    /// <summary>Legacy 0.9.x/0.10.x setting retained only so old config files deserialize cleanly.
    /// The launcher's tiles wrap to the available width now, so nothing chunks items into columns of
    /// a fixed count and this value shapes nothing.</summary>
    public int ColButtonCount { get; set; } = 10;

    /// <summary>Active theme. Exactly one theme exists — see <see cref="AppConfig.CanonicalTheme"/>.
    /// Any other value (including every historical id) is coerced on load.</summary>
    public string ThemeName { get; set; } = AppConfig.CanonicalTheme;

    /// <summary>Light/dark: "System" (default, follows the OS) | "Light" | "Dark". Absent from every
    /// config written before the Fluent 2 rebuild, which deserializes to the default.
    /// See <see cref="TanMenu.Core.Services.AppAppearance"/>.</summary>
    public string Appearance { get; set; } = AppAppearance.System;

    /// <summary>UI font family, applied to the whole launcher window. Empty = the app's own default
    /// (the system UI face). No fonts ship inside the executable any more, so a value here always
    /// names an INSTALLED family; resolution lives in <c>TanMenu.Wpf.Services.AppFonts</c>.</summary>
    public string FontFamily { get; set; } = "";

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
