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

    /// <summary>Legacy setting retained only so pre-Round-2 config files deserialize cleanly (same
    /// pattern as <see cref="ShowInTaskbar"/>/<see cref="ColButtonCount"/> below). Round 2 decided
    /// "size is recomputed from content on every start; position is still remembered" — the window's
    /// Width/Height are no longer restored from these values at all (<c>WindowHost.RestorePlacement</c>
    /// uses a fixed placeholder instead, immediately superseded by <c>WindowHost.FitToContent</c> once
    /// the launcher's async startup build produces something to measure) and nothing writes to these
    /// two properties any more either — a value the app can no longer act on is not worth persisting,
    /// and continuing to write it without ever reading it back would be exactly the kind of stale,
    /// misleading field this comment exists to warn a future reader about.</summary>
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

    /// <summary>Whether the navigation rail is pinned open (200 px, icon+label) rather than collapsed
    /// (48 px, icon-only — the Windows 11 shell convention, e.g. Task Manager's default rail state).
    /// Defaults to collapsed/false: an absent key (every config written before this field existed)
    /// deserializes to that same default, so old configs open collapsed rather than expanded.
    /// <para>
    /// Persisted the same way as <see cref="WindowMaximized"/>/<see cref="WindowLeft"/>/
    /// <see cref="WindowTop"/> above: written directly through <c>ConfigService.SaveAsync</c> from
    /// <c>MainWindow</c>'s hamburger-toggle handler, NOT through <c>SettingsWrite.CommitAsync</c> and
    /// without raising <c>AppEvents.SettingsChanged</c>. This is shell chrome the rail itself owns and
    /// applies live, exactly like the window's own placement — not a setting a card on the 设置 page
    /// exposes, so there is no card to roll back or reload on a failed save, and no other subscriber
    /// needs telling. A failed save is logged and otherwise ignored, the same as a failed placement
    /// flush, rather than shown as a blocking error dialog for what is a purely cosmetic layout choice.
    /// </para></summary>
    public bool NavRailExpanded { get; set; } = false;

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

    /// <summary>Items per category column on the 应用 page — user-configurable again since Round 3
    /// (设置 → 外观 → 每列个数). A category's items fill a column top to bottom up to this many, then
    /// wrap into a new column: see <see cref="TanMenu.Core.Services.MenuLayout.ChunkIntoColumns{T}"/>,
    /// which <c>TanMenu.Wpf.ViewModels.MenuGroupVm.SetColumnCapacity</c> calls on every build and every
    /// settings change. Also drives the window's own height via <c>WindowHost.FitToContent</c>: since
    /// the tallest COLUMN is capped at this value, a category with far more items than this no longer
    /// grows the window past it (the fit measures the page's natural size, and a column now IS at most
    /// this tall no matter how it is measured — see that method's own remarks).
    /// <para>
    /// Default 10, the same default this field has carried since before the Fluent 2 rebuild — during
    /// that rebuild it was deliberately kept only as a round-tripped legacy value while tiles wrapped
    /// to the window's own height instead (see
    /// docs/superpowers/specs/2026-07-31-win11-shell-redesign-design.md for that interim state, now
    /// superseded).
    /// </para>
    /// <para>
    /// Not clamped here: a value below 1 is defended against at the point of use
    /// (<see cref="TanMenu.Core.Services.MenuLayout.ChunkIntoColumns{T}"/>'s own <c>Math.Max(1, ...)</c>),
    /// and the 设置 page's own NumericUpDown restricts the UI to [1, 30], but a hand-edited config.json
    /// can still carry anything.
    /// </para></summary>
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

    /// <summary>Legacy setting retained only so old config files deserialize cleanly — the same
    /// treatment <see cref="WindowWidth"/>/<see cref="WindowHeight"/> above get, and
    /// <see cref="ColButtonCount"/> itself got for a while. Round 3, Task 4 removed the 按钮大小
    /// (小/中/大) setting entirely: launcher tiles are always the one size that used to be called "小"
    /// (Small, 168 DIP) — see <c>Themes/Fluent2/Launcher.axaml</c>'s <c>TmTileButton</c>. Nothing reads
    /// this field any more; it is kept only so a config.json written before Round 3 — which can carry
    /// "Medium" or "Large" — still deserializes without error.</summary>
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
