using TanMenu.Core.Services;

namespace TanMenu.Core.Models;

public class AppConfig
{
    /// <summary>
    /// The only theme id the app recognizes. The CSS theme packs (WinXP / Win7 / Windows11 / Pixel,
    /// plus user-authored <c>user:&lt;name&gt;</c> stylesheets) went away with the WebView, the
    /// Windows 2000 theme that briefly replaced them went away with the Fluent 2 rebuild, and what
    /// remains is one look drawn from Themes/Shell.
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

    /// <summary>Play the bundled hover sound when the pointer enters an enabled launcher button.
    /// Default ON to preserve the behaviour of existing installations whose config predates this
    /// setting.</summary>
    public bool HoverSoundEnabled { get; set; } = true;

    /// <summary>Play the bundled click sound when an enabled launcher button is invoked. Default ON
    /// to preserve the behaviour of existing installations whose config predates this setting.</summary>
    public bool ClickSoundEnabled { get; set; } = true;

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

    /// <summary>Legacy setting retained only so config files written before Round 4 deserialize
    /// cleanly — the same treatment <see cref="WindowWidth"/>/<see cref="WindowHeight"/> above and
    /// <see cref="NavRailExpanded"/>/<see cref="ShowInTaskbar"/>/<see cref="ButtonSize"/> below get.
    /// These two used to hold the restored window position in DIPs, nullable rather than
    /// negative-sentinelled because a monitor to the LEFT of the primary occupies negative coordinates.
    /// Round 4, item 4 replaced "come back where you dragged it" with one predictable placement — the
    /// window is always horizontally centred and anchored a fixed gap above the primary monitor's
    /// taskbar, on a cold start, on every hotkey/tray summon and after every content re-fit (see
    /// <c>TanMenu.Core.Services.WindowPlacement.ResolveBottomCentred</c> and
    /// <c>WindowHost.RestorePlacement</c>). The two rules are mutually exclusive, and the code cannot
    /// tell a cold start from a summon: the same method serves both. Nothing reads or writes these
    /// properties any more; they are kept only so a config.json that carries a <c>windowLeft</c>/
    /// <c>windowTop</c> key still loads without error. <b>Which files carry one is not what this remark
    /// used to say</b> — it claimed "every file saved by 0.12.0 and earlier has one", and that is wrong
    /// in both directions for a reason the retirement itself created: these two are the only retired
    /// fields that are NULLABLE, and <c>ConfigJsonContext</c> is declared
    /// <c>DefaultIgnoreCondition = WhenWritingNull</c>, so once Round 4 stopped writing them they are
    /// null on every save and the serializer OMITS them. A file written by a pre-Round-4 build that had
    /// actually placed the window carries them; one written since does not. Both load, which is the
    /// whole point of keeping the properties. A value in such a file has no effect on
    /// anything.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public bool WindowMaximized { get; set; } = false;

    /// <summary>Legacy setting retained only so config files that carry the key deserialize cleanly —
    /// the same treatment <see cref="WindowWidth"/>/<see cref="WindowHeight"/> above and
    /// <see cref="ButtonSize"/> below get. <b>There is no navigation rail and no navigation pane in
    /// this app at all any more, so this flag names nothing.</b> Round 2 gave the rail a hamburger
    /// toggle and two widths (48 px icon-only, 200 px icon+label) and persisted the choice here; Round
    /// 4, item 1 deleted the toggle, the second width and the row labels; Round 7, Task 8 widened the
    /// one surviving width to 160 and gave the labels back; and Round 8, Task 3 deleted the pane
    /// outright — the shell's five commands are a horizontal menu band across the top of the client
    /// area now (<c>MainWindow.axaml</c>'s <c>MainMenu</c>), and <c>RailWidthDip</c>/<c>PaneBorderDip</c>
    /// went with it. This remark is rewritten rather than trimmed because the sentence it replaces
    /// ("the rail is permanently 48 px icon-only") had been false since Round 7 and false twice over
    /// since Round 8. Nothing reads or writes this field; every save this app makes still WRITES the
    /// key, because the property is public and the serializer only omits nulls
    /// (<c>ConfigJsonContext</c>), and a file written before the property existed simply loads the
    /// <c>false</c> below. Both halves are held by
    /// <c>ConfigServiceTests.SaveThenLoad_RoundTripsNavRailExpanded</c> and
    /// <c>.LoadAsync_ConfigWithNoNavRailExpandedKey_DefaultsToCollapsed</c>. A <c>true</c> in such a
    /// file has no effect on anything.</summary>
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
    /// </para>
    /// <para>
    /// The window has a minimum HEIGHT of 568 DIP (<c>WindowHost.MinWindowHeightDip</c>), which the
    /// placement arithmetic clamps every fit up to, so every value low enough that its page plus the
    /// shell chrome measures under 568 DIP produces the SAME window height. This setting therefore only
    /// becomes height-EFFECTIVE above that cut-over. Below it, lowering this value still visibly changes
    /// the layout, just by trading height for WIDTH instead: fewer rows per column means more columns
    /// across, and a small enough value (3, in Round 3 testing) produced a 4648x568 DIP window. Not a
    /// defect — nothing in the Round 3 plan required this setting to be height-effective across its
    /// whole [1, 30] range, only that the default not need a horizontal scrollbar, which holds — but
    /// worth knowing before assuming a low value "does nothing" just because the window did not get
    /// shorter.
    /// </para>
    /// <para>
    /// Round 3 measured that cut-over at roughly 13 against a HIGHER floor: the window then also carried
    /// a platform floor of 908 physical / 605.3 DIP outer (897 physical / 598 DIP client, at 150%
    /// scale), which Windows clamped every smaller request up to. Round 4, item 4 removed it — it was an
    /// arithmetic consequence of <c>MainWindow.axaml</c>'s <c>MinHeight="568"</c> (Avalonia's
    /// <c>WM_GETMINMAXINFO</c> handler adds the frame's <c>BorderThickness</c> to it, which for this
    /// window double-counts the caption the extended client area already swallowed), not a platform
    /// constant, so deleting the attribute deleted the floor. The cut-over is consequently a little
    /// LOWER than 13 now; it was not re-measured in Round 4, so no exact value is claimed here.
    /// Round 3's own measurements and the correction:
    /// docs/superpowers/reports/2026-08-02-win11-shell-round3-findings.md.
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
    /// (小/中/大) setting entirely, leaving every tile at the fixed width that setting used to call
    /// "小" (168 DIP), and Round 4, item 3 removed that fixed width too: a launcher tile is now as wide
    /// as its own icon-plus-label content — see <c>Themes/Shell/Launcher.axaml</c>'s
    /// <c>TmTileButton</c>. Nothing reads this field any more; it is kept only so a config.json written
    /// before Round 3 — which can carry "Medium" or "Large" — still deserializes without error.</summary>
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
