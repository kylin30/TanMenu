# Windows 11 App Shell Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn TanMenu's light-dismiss acrylic flyout into an ordinary Windows 11 application window — a Mica window with a system title bar, a two-entry navigation rail (应用 / 设置), horizontally arranged categories, an in-page taskbar-pin notice, and the former settings window absorbed as a page of immediately-applying setting cards.

**Architecture:** The window model moves from "measure content, place popup, hide on blur" to "restore saved placement, live in the taskbar, close to tray". `MainWindow` becomes a shell (title bar + rail + page host) and the launcher UI moves into `Views/AppPage`; the deleted `SettingsWindow` reappears as `Views/SettingsPage` composed of five card-group controls. `LauncherViewModel` and all of `TanMenu.Core`'s menu/config/icon services keep their current contracts — only the view layer and the window host change.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`, win-x64, NativeAOT), Avalonia 12.1.0 (+ `Avalonia.Themes.Fluent`), Microsoft.Extensions.DependencyInjection 10.0.0, Serilog 4.3.0, Velopack 1.2.0, xunit 2.9.3.

Source spec: `docs/superpowers/specs/2026-07-31-win11-shell-redesign-design.md`.

## Global Constraints

- **Kill the app before building.** A running `TanMenu.Wpf.exe` locks `TanMenu.Core.dll` in the output directory and the build fails with `MSB3027`. Every build/test step must be preceded by `Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force`.
- **Test baseline: 124 tests pass.** `dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj` must never regress below the count carried by the previous task.
- **No new NuGet packages.** Everything is built from Avalonia 12.1's own primitives plus the existing `Themes/Fluent2` tokens.
- **NativeAOT rules.** `AvaloniaUseCompiledBindingsByDefault` is `true`: every `DataTemplate` and every `UserControl` that binds needs `x:DataType`. No reflection-based binding, no `BuiltInComInteropSupport`.
- **Shipped identity is frozen.** `TanMenu.Wpf.exe` filename, the `TanMenuWpf` single-instance mutex name, the `Software\TanMenuWpf` registry key and the `TanMenuWpf` Run-key value name must not change — Velopack updates, the MSIX Executable entry and existing taskbar pins reference them.
- **Avalonia 12 API notes**, both established by probe on 2026-07-31, not recalled:
  - `ExtendClientAreaChromeHints` does **not** exist in 12.1 (it is the Avalonia 11 name; 12 replaced it with `Avalonia.Controls.Chrome.*` and `Window.WindowDecorationsTheme`). This plan uses the **system title bar** — `WindowDecorations` stays at its default `Full` — so no chrome API is needed at all.
  - `Screens.ScreenFromWindow(Window)` returning `Screen?` **does** compile against 12.1, even though it carries no XML documentation. `Screen.WorkingArea` (a `PixelRect`) and `Screen.Scaling` are documented and already used by the current `WindowHost.TryGetPrimaryDisplay`.
- **Language.** Code, comments and commit messages in English, matching the existing tree. UI strings go through `AppLanguage` with both a Chinese and an English variant.
- **Branch discipline.** Commit directly on `main`. Do not create branches or worktrees.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/TanMenu.Core/Models/AppConfig.cs` | New window/prompt fields, new defaults | 1 |
| `src/TanMenu.Core/Services/ConfigService.cs` | Schema-version migration in `Normalize` | 1 |
| `src/TanMenu.Core/Services/WindowPlacement.cs` | **New.** Pure placement arithmetic (clamp to work area) | 2 |
| `src/TanMenu.Wpf/Services/IWindowHost.cs` | Slimmed contract: no measurement, no `ContentVersion` | 3 |
| `src/TanMenu.Wpf/Services/WindowHost.cs` | Restore/save placement, show/hide/toggle, hide-on-blur | 3 |
| `src/TanMenu.Wpf/ViewModels/LauncherViewModel.cs` | Drop the `ContentVersion` fingerprint | 3 |
| `src/TanMenu.Wpf/MainWindow.axaml{,.cs}` | App window + shell: title, rail, page host | 3, 5 |
| `src/TanMenu.Wpf/Program.cs` | Broadcast-message single-instance handshake | 4 |
| `src/TanMenu.Wpf/Services/GlobalHotkeyService.cs` | Match the registered message id | 4 |
| `src/TanMenu.Wpf/Services/IShellNavigator.cs` | **New.** Page navigation seam for tray/settings entry points | 5 |
| `src/TanMenu.Wpf/Views/AppPage.axaml{,.cs}` | **New.** Search, categories, empty states, keyboard grid nav | 5, 6 |
| `src/TanMenu.Wpf/Controls/InfoBar.axaml{,.cs}` | **New.** Fluent 2 InfoBar primitive | 7 |
| `src/TanMenu.Wpf/App.axaml.cs` | Drop the modal pin prompt and the reveal plumbing | 3, 7 |
| `src/TanMenu.Wpf/Views/SettingsPage.axaml{,.cs}` | **New.** Scrolling host for the five card groups | 8 |
| `src/TanMenu.Wpf/Views/Settings/AppearanceCard.axaml{,.cs}` | **New.** 语言 / 外观 / 字体 / 按钮大小 | 8 |
| `src/TanMenu.Wpf/Views/Settings/BehaviorCard.axaml{,.cs}` | **New.** 隐藏 / 置顶 / 自启 / 固定 / 热键 | 9 |
| `src/TanMenu.Wpf/Views/Settings/FoldersCard.axaml{,.cs}` | **New.** 主文件夹 / 数据文件夹 / 缓存 / 备份恢复 | 10 |
| `src/TanMenu.Wpf/Views/Settings/ToolsCard.axaml{,.cs}` | **New.** 常用工具开关与勾选列表 | 11 |
| `src/TanMenu.Wpf/Views/Settings/UpdatesCard.axaml{,.cs}` | **New.** 版本 / 检查 / 下载 | 12 |
| `src/TanMenu.Wpf/SettingsWindow.axaml{,.cs}` | **Deleted** | 13 |
| `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` | Nav, card and InfoBar control themes | 5, 7, 8 |
| `src/TanMenu.Core/Services/AppLanguage.cs` | New UI strings | 5, 7, 8 |

Every task leaves the application buildable and runnable. Tasks 8–12 add settings card groups one at a time while `SettingsWindow` still exists and stays reachable through `ISettingsLauncher`; task 13 deletes it and switches that entry point to the new page.

---

### Task 1: Config schema, new fields and migration

**Files:**
- Modify: `src/TanMenu.Core/Models/AppConfig.cs`
- Modify: `src/TanMenu.Core/Services/ConfigService.cs:94-112` (`Normalize`)
- Test: `tests/TanMenu.Core.Tests/Services/ConfigServiceTests.cs`, `tests/TanMenu.Core.Tests/Models/AppConfigTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppConfig.CurrentSchemaVersion` (const int = 1); `GeneralConfig.SchemaVersion` (int), `.WindowWidth` / `.WindowHeight` (double), `.WindowLeft` / `.WindowTop` (**`double?`**, null = no remembered position), `.WindowMaximized` (bool), `.TaskbarPinPromptDismissed` (bool); `GeneralConfig.AutoClose` and `.TopMost` now default to `false`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/TanMenu.Core.Tests/Services/ConfigServiceTests.cs`:

```csharp
    [Fact]
    public async Task Load_PreSchemaConfig_MigratesFlyoutBehaviourToAppWindowDefaults()
    {
        var paths = NewPaths(out var root);
        try
        {
            // A 0.11-era config: flyout behaviour written explicitly, no schemaVersion at all.
            var json = """
                {"general":{"autoClose":true,"topMost":true,"colButtonCount":6},"folders":[],"rootFolder":"C:\\"}
                """;
            await File.WriteAllTextAsync(paths.ConfigFilePath, json);

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.False(svc.Config.General.AutoClose);
            Assert.False(svc.Config.General.TopMost);
            Assert.Equal(AppConfig.CurrentSchemaVersion, svc.Config.General.SchemaVersion);
            Assert.Equal(6, svc.Config.General.ColButtonCount); // untouched by the migration
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Load_MigratedConfig_KeepsDeliberateAutoCloseChoice()
    {
        var paths = NewPaths(out var root);
        try
        {
            // Already migrated, and the user has since re-enabled the flyout behaviour on purpose.
            var json = $$"""
                {"general":{"schemaVersion":{{AppConfig.CurrentSchemaVersion}},"autoClose":true,"topMost":true},"folders":[]}
                """;
            await File.WriteAllTextAsync(paths.ConfigFilePath, json);

            var svc = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc.LoadAsync();

            Assert.True(svc.Config.General.AutoClose);
            Assert.True(svc.Config.General.TopMost);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsWindowPlacement()
    {
        var paths = NewPaths(out var root);
        try
        {
            var svc1 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc1.LoadAsync();
            svc1.Config.General.WindowWidth = 1234;
            svc1.Config.General.WindowHeight = 777;
            svc1.Config.General.WindowLeft = 42;
            svc1.Config.General.WindowTop = 24;
            svc1.Config.General.WindowMaximized = true;
            svc1.Config.General.TaskbarPinPromptDismissed = true;
            await svc1.SaveAsync();

            var svc2 = new ConfigService(paths, NullLogger<ConfigService>.Instance);
            await svc2.LoadAsync();

            Assert.Equal(1234, svc2.Config.General.WindowWidth);
            Assert.Equal(777, svc2.Config.General.WindowHeight);
            Assert.Equal(42, svc2.Config.General.WindowLeft);
            Assert.Equal(24, svc2.Config.General.WindowTop);
            Assert.True(svc2.Config.General.WindowMaximized);
            Assert.True(svc2.Config.General.TaskbarPinPromptDismissed);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
```

Append to `tests/TanMenu.Core.Tests/Models/AppConfigTests.cs`, inside the existing defaults test class:

```csharp
    [Fact]
    public void Defaults_AreAppWindowNotFlyout()
    {
        var general = new GeneralConfig();

        Assert.False(general.AutoClose);
        Assert.False(general.TopMost);
        Assert.Equal(0, general.SchemaVersion); // 0 = "not yet stamped"; see ConfigService.Normalize
        Assert.Equal(1000, general.WindowWidth);
        Assert.Equal(640, general.WindowHeight);
        Assert.Null(general.WindowLeft);
        Assert.Null(general.WindowTop);
        Assert.False(general.WindowMaximized);
        Assert.False(general.TaskbarPinPromptDismissed);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj -v minimal
```

Expected: compile errors — `GeneralConfig` has no `SchemaVersion` / `WindowWidth` / … and `AppConfig` has no `CurrentSchemaVersion`.

- [ ] **Step 3: Add the fields**

In `src/TanMenu.Core/Models/AppConfig.cs`, add to `AppConfig` next to `CanonicalTheme`:

```csharp
    /// <summary>Schema the CURRENT build writes. <see cref="GeneralConfig.SchemaVersion"/> carries the
    /// version a config file was last normalized to; <c>ConfigService.Normalize</c> migrates anything
    /// lower and stamps this value.</summary>
    public const int CurrentSchemaVersion = 1;
```

In `GeneralConfig`, change the two behaviour defaults and add the new fields:

```csharp
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
```

- [ ] **Step 4: Add the migration**

In `src/TanMenu.Core/Services/ConfigService.cs`, at the end of `Normalize`:

```csharp
        // Schema 1: the 0.11 flyout wrote AutoClose/TopMost = true because it hid on blur and floated
        // above everything. This build is a taskbar-resident application window, where both are wrong.
        // A brand-new config also arrives here with SchemaVersion 0, which is harmless: its AutoClose
        // and TopMost already ARE false, so the migration only stamps the version.
        if (config.General.SchemaVersion < AppConfig.CurrentSchemaVersion)
        {
            config.General.AutoClose = false;
            config.General.TopMost = false;
            config.General.SchemaVersion = AppConfig.CurrentSchemaVersion;
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj -v minimal
```

Expected: PASS, 127 tests.

- [ ] **Step 6: Commit**

```bash
git add src/TanMenu.Core/Models/AppConfig.cs src/TanMenu.Core/Services/ConfigService.cs tests/TanMenu.Core.Tests
git commit -m "feat(config): add window placement fields and the schema-1 app-window migration"
```

---

### Task 2: Pure placement arithmetic

**Files:**
- Create: `src/TanMenu.Core/Services/WindowPlacement.cs`
- Test: `tests/TanMenu.Core.Tests/Services/WindowPlacementTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record struct PlacementRect(double Left, double Top, double Width, double Height)` and `static class WindowPlacement` with `PlacementRect Resolve(double? savedLeft, double? savedTop, double savedWidth, double savedHeight, PlacementRect workArea, double minWidth, double minHeight)`. Task 3's `WindowHost.RestorePlacement` calls it. The position parameters are **nullable, not negative-sentinel**: monitors left of the primary have negative coordinates, so `< 0` cannot mean "unset".

- [ ] **Step 1: Write the failing test**

Create `tests/TanMenu.Core.Tests/Services/WindowPlacementTests.cs`:

```csharp
using TanMenu.Core.Services;
using Xunit;

namespace TanMenu.Core.Tests.Services;

public class WindowPlacementTests
{
    private static readonly PlacementRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Resolve_SavedRectInsideWorkArea_IsKept()
    {
        var r = WindowPlacement.Resolve(200, 100, 1000, 640, Work, 760, 520);

        Assert.Equal(new PlacementRect(200, 100, 1000, 640), r);
    }

    [Fact]
    public void Resolve_UnsetPosition_CentresOnWorkArea()
    {
        var r = WindowPlacement.Resolve(null, null, 1000, 640, Work, 760, 520);

        Assert.Equal(460, r.Left);   // (1920 - 1000) / 2
        Assert.Equal(200, r.Top);    // (1040 - 640) / 2
    }

    [Fact]
    public void Resolve_RealNegativePosition_OnLeftHandMonitor_IsKept()
    {
        // A monitor to the LEFT of the primary occupies negative coordinates. This position is
        // remembered, not unset, and must survive: a negative-sentinel design would centre it on the
        // primary monitor instead, teleporting the window on every restart.
        var leftMonitor = new PlacementRect(-1920, 0, 1920, 1040);

        var r = WindowPlacement.Resolve(-1500, 120, 1000, 640, leftMonitor, 760, 520);

        Assert.Equal(-1500, r.Left);
        Assert.Equal(120, r.Top);
    }

    [Fact]
    public void Resolve_RectOffTheRightEdge_IsPulledBackInside()
    {
        // Saved on a second monitor that is no longer connected.
        var r = WindowPlacement.Resolve(3000, 50, 1000, 640, Work, 760, 520);

        Assert.Equal(920, r.Left);   // 1920 - 1000
        Assert.Equal(50, r.Top);
    }

    [Fact]
    public void Resolve_RectLargerThanWorkArea_IsShrunkToFit()
    {
        var r = WindowPlacement.Resolve(0, 0, 4000, 3000, Work, 760, 520);

        Assert.Equal(1920, r.Width);
        Assert.Equal(1040, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Resolve_SizeBelowMinimum_IsRaisedToMinimum()
    {
        var r = WindowPlacement.Resolve(10, 10, 100, 80, Work, 760, 520);

        Assert.Equal(760, r.Width);
        Assert.Equal(520, r.Height);
    }

    [Fact]
    public void Resolve_WorkAreaSmallerThanMinimum_PrefersFittingTheScreen()
    {
        var tiny = new PlacementRect(0, 0, 640, 400);

        var r = WindowPlacement.Resolve(null, null, 1000, 640, tiny, 760, 520);

        Assert.Equal(640, r.Width);
        Assert.Equal(400, r.Height);
        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Resolve_WorkAreaWithOffset_PositionsRelativeToIt()
    {
        // A left-hand secondary monitor: the work area does not start at the origin.
        var offset = new PlacementRect(-1920, 0, 1920, 1040);

        var r = WindowPlacement.Resolve(null, null, 1000, 640, offset, 760, 520);

        Assert.Equal(-1460, r.Left);  // -1920 + (1920 - 1000) / 2
        Assert.Equal(200, r.Top);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj --filter WindowPlacementTests -v minimal
```

Expected: compile error — `WindowPlacement` and `PlacementRect` do not exist.

- [ ] **Step 3: Implement**

Create `src/TanMenu.Core/Services/WindowPlacement.cs`:

```csharp
namespace TanMenu.Core.Services;

/// <summary>A window rectangle in DIPs. Deliberately not an Avalonia type: this lives in Core so the
/// arithmetic can be unit-tested without a UI thread or a display.</summary>
public readonly record struct PlacementRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Turns a saved (possibly stale or hostile) window placement into one that is guaranteed to
/// be visible on the supplied work area.</summary>
public static class WindowPlacement
{
    /// <param name="savedLeft">Remembered X, or null when there is none — centre instead.</param>
    /// <param name="savedTop">Remembered Y, or null when there is none — centre instead.</param>
    /// <param name="savedWidth">Remembered width; clamped into [min, work area].</param>
    /// <param name="savedHeight">Remembered height; clamped into [min, work area].</param>
    /// <param name="workArea">The target monitor's work area (excludes the taskbar). Its origin is
    /// NOT assumed to be (0,0): a monitor left of or above the primary one has negative coordinates.</param>
    /// <param name="minWidth">Floor for the resolved width, unless the work area is narrower.</param>
    /// <param name="minHeight">Floor for the resolved height, unless the work area is shorter.</param>
    /// <remarks>
    /// <para>
    /// Size is resolved first, because position depends on it. A saved size is clamped to
    /// [min, work-area]; when the work area itself is smaller than the minimum, FITTING THE SCREEN
    /// wins — a window larger than the display cannot be moved back into view, whereas one below the
    /// nominal minimum is merely cramped.
    /// </para>
    /// <para>
    /// "No remembered position" is null, NOT a negative number. A monitor to the left of the primary
    /// occupies negative coordinates, so a real remembered X can be negative; a negative sentinel
    /// would silently discard it and re-centre the window on every restart.
    /// </para>
    /// </remarks>
    public static PlacementRect Resolve(
        double? savedLeft,
        double? savedTop,
        double savedWidth,
        double savedHeight,
        PlacementRect workArea,
        double minWidth,
        double minHeight)
    {
        var width = Clamp(savedWidth, Math.Min(minWidth, workArea.Width), workArea.Width);
        var height = Clamp(savedHeight, Math.Min(minHeight, workArea.Height), workArea.Height);

        var left = savedLeft is { } l
            ? Clamp(l, workArea.Left, workArea.Right - width)
            : workArea.Left + (workArea.Width - width) / 2;
        var top = savedTop is { } t
            ? Clamp(t, workArea.Top, workArea.Bottom - height)
            : workArea.Top + (workArea.Height - height) / 2;

        return new PlacementRect(left, top, width, height);
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);
}
```

- [ ] **Step 4: Run the test to verify it passes**

```powershell
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj --filter WindowPlacementTests -v minimal
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TanMenu.Core/Services/WindowPlacement.cs tests/TanMenu.Core.Tests/Services/WindowPlacementTests.cs
git commit -m "feat(core): add work-area-clamping window placement arithmetic"
```

---

### Task 3: The window becomes an application window

**Files:**
- Modify: `src/TanMenu.Wpf/Services/IWindowHost.cs`
- Modify: `src/TanMenu.Wpf/Services/WindowHost.cs` (rewrite)
- Modify: `src/TanMenu.Wpf/MainWindow.axaml:1-18` (window attributes) and `MainWindow.axaml:37-59` (acrylic panels)
- Modify: `src/TanMenu.Wpf/MainWindow.axaml.cs:41-152` (backdrop, corners, reveal)
- Modify: `src/TanMenu.Wpf/ViewModels/LauncherViewModel.cs:675-684` (`ContentVersion` fingerprint)
- Modify: `src/TanMenu.Wpf/App.axaml.cs:170-192` (`ShowLauncherWindow`)

**Interfaces:**
- Consumes: `WindowPlacement.Resolve` and `PlacementRect` (Task 2); `GeneralConfig.Window*` (Task 1).
- Produces: `IWindowHost` without `ResizeToContentAndPlaceAsync` and without `ContentVersion`, plus `void SavePlacement()`. `WindowHost.Attach(Window)` and `WindowHost.RestorePlacement()` stay concrete-only.

- [ ] **Step 1: Slim the interface**

Replace the body of `src/TanMenu.Wpf/Services/IWindowHost.cs` with:

```csharp
using Avalonia.Controls;

namespace TanMenu.Wpf.Services;

/// <summary>Abstraction over the main window: placement persistence and show/hide/toggle.</summary>
public interface IWindowHost
{
    void Hide();
    void ShowAndActivate();
    void Toggle();

    /// <summary>Persist the current size/position/maximized state to config. Called on move, resize and
    /// hide; cheap and idempotent.</summary>
    void SavePlacement();

    /// <summary>Temporarily suppress hide-on-blur (e.g. while a modal dialog or folder picker owns
    /// focus). Must be paired with a <c>false</c> call on EVERY exit path including exceptions, or the
    /// window stops hiding on blur for the rest of the session. Only has an effect while the user has
    /// 失焦自动隐藏 switched on, which is not the default any more.</summary>
    void SuppressHide(bool on);

    /// <summary>The main window, or null before it is attached. Used as the owner for modal dialogs.</summary>
    Window? Window { get; }
}
```

- [ ] **Step 2: Rewrite the host**

Replace `src/TanMenu.Wpf/Services/WindowHost.cs` entirely:

```csharp
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>
/// Owns the main window: restores its saved placement, persists changes, and handles
/// show/hide/toggle plus opt-in hide-on-blur.
/// </summary>
/// <remarks>
/// Until 0.11 this class measured the rendered content, clamped it to the work area and placed a
/// content-sized popup bottom-center on every reveal. The window is an ordinary resizable application
/// window now, so all of that — the measurement pass, the reveal fade, the SetWindowPos placement and
/// the ContentVersion fingerprint that triggered re-measurement — is gone. What remains is placement
/// persistence, which the popup never needed because it had no user-chosen geometry to remember.
/// </remarks>
public sealed partial class WindowHost : IWindowHost
{
    /// <summary>Floor for the restored size. Below this the navigation rail and the content area stop
    /// coexisting; it matches MainWindow's MinWidth/MinHeight.</summary>
    private const double MinWidthDip = 760;
    private const double MinHeightDip = 520;

    private readonly ConfigService _config;
    private readonly AppEvents _events;
    private readonly DispatcherTimer _saveDebounce;
    private Window? _window;
    private int _suppressDepth;
    private bool _restoring;

    public WindowHost(ConfigService config, AppEvents events)
    {
        _config = config;
        _events = events;

        // Coalesces the PositionChanged/size storm a single drag produces into one disk write.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += (_, _) => FlushPlacement();
    }

    public Window? Window => _window;

    /// <summary>Bind the host to the main window. Idempotent — re-attaching the same window is a no-op,
    /// so it does not matter whether startup or the window's own code-behind calls it.</summary>
    public void Attach(Window window)
    {
        if (ReferenceEquals(_window, window))
            return;

        _window = window;

        window.Deactivated += (_, _) =>
        {
            // Config is read live on every deactivation (never cached) so toggling 失焦自动隐藏 in
            // settings takes effect immediately. Off by default since 0.12.
            if (_config.Config.General.AutoClose && _suppressDepth == 0)
                Hide();
        };

        // 窗口置顶 has to reach a window that is already on screen. Restore applies it too, but that
        // only runs on a cold start or an un-hidden window, so without this the checkbox would appear
        // to do nothing until the next hide/show cycle.
        _events.SettingsChanged += ApplyTopmost;
        ApplyTopmost();

        // Persist geometry as the user changes it rather than only on exit: the process can be ended
        // by an update handoff or a hard kill, and a lost window position is a visible annoyance.
        // _restoring guards against writing back the values we are in the middle of applying.
        window.PositionChanged += (_, _) => { if (!_restoring) SavePlacement(); };
        window.PropertyChanged += (_, e) =>
        {
            if (_restoring)
                return;
            if (e.Property == Window.WindowStateProperty ||
                e.Property == Window.WidthProperty ||
                e.Property == Window.HeightProperty)
                SavePlacement();
        };
    }

    /// <summary>Apply the saved placement, clamped to the work area the window will actually appear on.
    /// Never throws: a failure here must not prevent the window from being shown.</summary>
    public void RestorePlacement()
    {
        if (_window is null)
            return;

        _restoring = true;
        try
        {
            var g = _config.Config.General;
            var work = ResolveWorkArea();

            var placement = WindowPlacement.Resolve(
                g.WindowLeft, g.WindowTop, g.WindowWidth, g.WindowHeight,
                work, MinWidthDip, MinHeightDip);

            var scale = CurrentScale();
            _window.Width = placement.Width;
            _window.Height = placement.Height;
            _window.Position = new PixelPoint(
                (int)Math.Round(placement.Left * scale),
                (int)Math.Round(placement.Top * scale));
            _window.WindowState = g.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
            ApplyTopmost();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to restore window placement; using defaults");
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Updates the in-memory config immediately but DEBOUNCES the disk write. A single window drag
    /// raises PositionChanged on every mouse move, and persisting each one would rewrite config.json
    /// hundreds of times per drag. <see cref="Hide"/> flushes synchronously so the common
    /// hide-then-exit path never loses the last position.
    /// </remarks>
    public void SavePlacement()
    {
        if (_window is null || _restoring)
            return;

        try
        {
            var state = _window.WindowState;

            // Minimized carries NO placement information: its Width/Height/Position say nothing about
            // what to restore to, and reading it as "not maximized" would erase a maximized window's
            // remembered state the moment the user minimizes it.
            if (state == WindowState.Minimized)
                return;

            var g = _config.Config.General;
            g.WindowMaximized = state == WindowState.Maximized;

            // A maximized window's Width/Height are the screen's, not the size to restore to, so the
            // remembered "normal" geometry is deliberately left alone while maximized.
            if (state == WindowState.Normal)
            {
                var scale = CurrentScale();
                g.WindowWidth = _window.Width;
                g.WindowHeight = _window.Height;
                g.WindowLeft = _window.Position.X / scale;
                g.WindowTop = _window.Position.Y / scale;
            }

            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to record window placement");
        }
    }

    /// <summary>Write the recorded placement to disk now, cancelling any pending debounced write.
    /// Public so the exit path can flush before the process goes away — geometry changed inside the
    /// debounce window would otherwise be lost.</summary>
    public void FlushPlacement()
    {
        _saveDebounce.Stop();
        _ = _config.SaveAsync();
    }

    /// <summary>Push the current 窗口置顶 setting onto a live window.</summary>
    private void ApplyTopmost()
    {
        if (_window is not null)
            _window.Topmost = _config.Config.General.TopMost;
    }

    /// <summary>The screen the window currently sits on, falling back to the primary screen and then to
    /// the first enumerated one. Null when no screen information is available yet.</summary>
    private Screen? CurrentScreen()
    {
        var screens = _window?.Screens;
        return (_window is not null ? screens?.ScreenFromWindow(_window) : null)
               ?? screens?.Primary
               ?? screens?.All.FirstOrDefault();
    }

    /// <summary>Pixels-per-DIP of the screen the window is on RIGHT NOW. Deliberately NOT cached:
    /// dragging the window from a 100% monitor to a 150% one changes it, and converting a position with
    /// a stale scale records a placement wrong by the ratio between the two.</summary>
    private double CurrentScale()
    {
        var scaling = CurrentScreen()?.Scaling ?? 1.0;
        return scaling <= 0 ? 1.0 : scaling;
    }

    /// <summary>The work area of the screen the window currently sits on, in DIPs, falling back to a
    /// 1920×1040 guess when no screen information is available.</summary>
    private PlacementRect ResolveWorkArea()
    {
        var screen = CurrentScreen();
        if (screen is null)
            return new PlacementRect(0, 0, 1920, 1040);

        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var w = screen.WorkingArea;
        return new PlacementRect(w.X / scale, w.Y / scale, w.Width / scale, w.Height / scale);
    }

    public void Hide()
    {
        SavePlacement();
        FlushPlacement(); // hiding is often the last thing before exit; do not leave it to the timer
        _window?.Hide();
    }

    public void ShowAndActivate()
    {
        if (_window is null)
            return;

        if (!_window.IsVisible)
        {
            RestorePlacement();
            _window.Show();
        }
        else if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = _config.Config.General.WindowMaximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        _window.Activate();

        // Activate() alone is unreliable when the foreground window belongs to another process.
        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            SetForegroundWindow(handle);

        _window.Focus();
        _events.RaiseLauncherShown(); // lets the app page clear + focus the search box
    }

    public void Toggle()
    {
        if (_window is null)
            return;
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
            Hide();
        else
            ShowAndActivate();
    }

    /// <summary>
    /// Hold off hide-on-blur while something modal owns focus. REFERENCE-COUNTED, not a flag: the
    /// suppressions genuinely overlap — a folder picker raised from a settings card suppresses on top
    /// of whatever suppressed before it. Unbalanced releases are clamped at zero rather than going
    /// negative, which would permanently disable hide-on-blur for the session.
    /// </summary>
    public void SuppressHide(bool on)
    {
        if (on)
            _suppressDepth++;
        else if (_suppressDepth > 0)
            _suppressDepth--;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
```

Add `using Avalonia;` at the top for `PixelPoint`.

- [ ] **Step 3: Make the window an app window**

In `src/TanMenu.Wpf/MainWindow.axaml`, replace the `<Window …>` attribute block (lines 1–18) with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:TanMenu.Wpf.ViewModels"
        xmlns:ctl="using:TanMenu.Wpf.Controls"
        x:Class="TanMenu.Wpf.MainWindow"
        x:DataType="vm:LauncherViewModel"
        Title="TanMenu"
        Width="1000" Height="640" MinWidth="760" MinHeight="520"
        WindowStartupLocation="Manual"
        TransparencyLevelHint="Mica"
        Background="Transparent"
        Icon="/wwwroot/app.ico"
        FontFamily="{Binding WindowFontFamily}"
        FontSize="14"
        Foreground="{DynamicResource TextPrimaryBrush}">
```

Delete the `ExperimentalAcrylicBorder` (`Acrylic`) and the `OpaqueFallback` `Border` from the visual tree, and give the root `Panel` an opaque-capable background instead:

```xml
  <Panel x:Name="Root"
         Background="Transparent"
         Classes.size-small="{Binding IsSmallButtons}"
         Classes.size-medium="{Binding IsMediumButtons}"
         Classes.size-large="{Binding IsLargeButtons}">
```

Update the header comment block above `<Panel>` to describe the app window (Mica, system title bar, restored placement) instead of the flyout.

- [ ] **Step 4: Update the code-behind**

In `src/TanMenu.Wpf/MainWindow.axaml.cs`:

- Replace `ApplyBackdrop` with the Mica-fallback form and delete `ApplyRoundedCorners`, `DwmSetWindowAttribute` and the two DWM constants — Windows 11 rounds ordinary decorated windows itself:

```csharp
    /// <summary>Mica is a request, not a guarantee: Windows 10, "show transparency" turned off and
    /// remote desktop all refuse it, and the window would then be a hole rather than a surface. Paint
    /// the opaque Fluent surface colour when it was not granted. (Same guard SettingsWindow used.)</summary>
    private void ApplyBackdrop()
    {
        if (ActualTransparencyLevel != WindowTransparencyLevel.Mica)
            Background = this.FindResource("SurfaceBaseBrush") as IBrush ?? Brushes.White;
    }
```

Add `using Avalonia.Media;` for `IBrush` / `Brushes`.

- In `OnOpened`, restore placement before the first layout and drop the `partial` keyword's DWM justification comment:

```csharp
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ApplyBackdrop();

        // The host owns show/hide/placement and needs the window once its HWND exists. The DI
        // registration deliberately hands out the same instance for IWindowHost and WindowHost.
        var host = _host as WindowHost;
        host?.Attach(this);
        host?.RestorePlacement();

        _ = _viewModel.InitializeAsync();
    }
```

- The class no longer needs `partial` for a `LibraryImport` stub, but `partial` is still required by the XAML compiler — leave the declaration as `public partial class MainWindow : Window` and delete only the stale comment on line 22–23.

- Replace `ConsumePendingViewWork`'s reveal latch: the window is shown by `App.ShowLauncherWindow` directly and there is nothing to fade in, so drop `_revealed` and `_host.ShowAndActivate()` from it:

```csharp
    private void ConsumePendingViewWork()
    {
        if (_viewModel.PendingFocus)
        {
            _viewModel.PendingFocus = false;
            try
            {
                SearchBox.Focus();
            }
            catch
            {
                /* focus is a nicety; never let it take startup down */
            }
        }
    }
```

Delete the `_revealed` field and drop `nameof(LauncherViewModel.LoadedOnce)` / `nameof(LauncherViewModel.HasGroups)` from the `OnViewModelPropertyChanged` filter, leaving `PendingFocus`.

- Close now means "hide to tray" and must not be a real close (the tray and hotkey have to resurface it):

```csharp
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing hides to the tray; the app exits only through 退出 (tray menu or overflow menu),
        // which calls App.ExitApp and shuts the lifetime down explicitly.
        if (!App.IsExiting)
        {
            e.Cancel = true;
            _host.Hide();
            return;
        }

        base.OnClosing(e);
    }
```

Add `using Avalonia.Controls;` if not present (it is).

- [ ] **Step 5: Add the exit latch and show the window**

In `src/TanMenu.Wpf/App.axaml.cs`:

- `OnLauncherOpened` must raise `LauncherShown` once the window is realized. Startup used to reach it
  through the deleted reveal latch (`ConsumePendingViewWork` → `ShowAndActivate` → `RaiseLauncherShown`),
  and that event is what focuses the search box and evaluates the taskbar-pin state. Without this, a
  cold start leaves a type-to-search launcher unfocused. Append to the end of `OnLauncherOpened`'s
  `try` block:

```csharp
            // A cold start must produce the same LauncherShown a tray or hotkey summon does: the
            // search box takes focus from it, and so does the taskbar-pin check. Raised here rather
            // than from Show() because the window is realized and placed by the time Opened runs.
            Services.GetRequiredService<AppEvents>().RaiseLauncherShown();
```

- `ExitApp` must flush the debounced placement write before tearing services down, or geometry changed
  within the last 500 ms is lost and `SaveAsync` can still be in flight when `Services.Dispose()`
  disposes the semaphore it holds. Call `Services.GetRequiredService<WindowHost>().FlushPlacement();`
  immediately after setting `IsExiting`.

- Add the latch near `SingleInstanceMutex`:

```csharp
    /// <summary>Set by <see cref="ExitApp"/> before the lifetime is shut down, so MainWindow.OnClosing
    /// can tell a real exit from the close button (which hides to the tray).</summary>
    public static bool IsExiting { get; private set; }
```

Set `IsExiting = true;` as the first statement of `ExitApp`.

- In `ShowLauncherWindow`, drop the forced `Title` (the caption is no longer protocol — see Task 4) and keep the rest:

```csharp
    private static void ShowLauncherWindow()
    {
        var window = new MainWindow();

        var host = Services.GetRequiredService<WindowHost>();
        host.Attach(window);

        // The HWND-dependent wiring (message hook, global hotkey, tray) needs a realized window, so it
        // runs from Opened — the Avalonia analogue of WPF's SourceInitialized.
        window.Opened += OnLauncherOpened;
        window.Show();

        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;
    }
```

- [ ] **Step 6: Drop the ContentVersion fingerprint**

In `src/TanMenu.Wpf/ViewModels/LauncherViewModel.cs`, delete the `_host.ContentVersion = HashCode.Combine(...)` assignment and its comment block (lines 675–684). Nothing replaces it: nothing re-measures now.

- [ ] **Step 7: Build and run**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj -v minimal
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Expected: build with 0 errors, 131 tests pass, and a **resizable, taskbar-present, Mica-backed window with a normal title bar reading "TanMenu"**, showing the existing vertical category list. Verify by hand:
1. Resize and move the window, close it (hides to tray), reopen from the tray — the size and position come back.
2. Click another application — the window stays put (失焦自动隐藏 is off by default now).
3. Tray icon single click hides and shows it.

- [ ] **Step 8: Commit**

```bash
git add src/TanMenu.Wpf src/TanMenu.Core
git commit -m "feat(shell): turn the launcher flyout into a Mica application window"
```

---

### Task 4: Single-instance handshake without the window caption

**Files:**
- Modify: `src/TanMenu.Wpf/Program.cs:19-26,94-106`
- Modify: `src/TanMenu.Wpf/Services/GlobalHotkeyService.cs:55-74`

**Interfaces:**
- Consumes: nothing.
- Produces: `Program.ShowFirstInstanceMessage` (`static uint`, lazily registered). `Program.MainWindowTitle` and `Program.WmShowFirstInstance` are deleted.

- [ ] **Step 1: Replace the signal in Program.cs**

Delete `MainWindowTitle`, `WmShowFirstInstance`, `FindWindowW` and `PostMessageW`'s HWND-targeted use; replace with:

```csharp
    /// <summary>Broadcast message a second instance posts to resurface the first.
    /// <para>
    /// Registered by NAME rather than addressed to a window found by caption. The caption used to be
    /// protocol (<c>FindWindow(null, "TanMenuWpf")</c>), which forced the visible window title to be
    /// the internal name; now that the window lives in the taskbar and Alt+Tab that title is
    /// user-visible, and matching "TanMenu" by caption would be unsafe because the data folder is also
    /// called TanMenu and an Explorer window could match first.
    /// </para></summary>
    private const string ShowFirstInstanceMessageName = "TanMenuWpf.ShowFirstInstance";

    /// <summary>The registered message id, identical in every process that registers the same string.
    /// Zero if registration failed, in which case second-instance activation degrades to a no-op.</summary>
    public static uint ShowFirstInstanceMessage { get; } = RegisterWindowMessageW(ShowFirstInstanceMessageName);

    private const int HwndBroadcast = 0xFFFF;

    /// <summary>Best-effort: ask an already-running instance to resurface. If the first instance has not
    /// installed its message hook yet the broadcast is silently dropped — no retry, no error dialog;
    /// this second process simply exits.</summary>
    private static void SignalFirstInstance()
    {
        if (ShowFirstInstanceMessage != 0)
            PostMessageW(HwndBroadcast, ShowFirstInstanceMessage, IntPtr.Zero, IntPtr.Zero);
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
```

`PostMessageW`'s first argument is `IntPtr`, so call it as `PostMessageW((IntPtr)HwndBroadcast, …)`.

- [ ] **Step 2: Match the message in the hook**

In `src/TanMenu.Wpf/Services/GlobalHotkeyService.cs`, replace the `WmShowFirstInstance` comparison:

```csharp
        // Registered messages are never zero when registration succeeded; guarding against zero stops
        // a failed registration from turning every stray message into a show request.
        if (Program.ShowFirstInstanceMessage != 0 && msg == Program.ShowFirstInstanceMessage)
        {
            // Deliberately SHOW, never toggle: clicking the taskbar pin again must not hide the window
            // the user just asked for.
            _host.ShowAndActivate();
            handled = true;
        }
```

- [ ] **Step 3: Build and verify by hand**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
$exe = 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
Start-Process $exe
Start-Sleep -Seconds 4
Start-Process $exe          # second instance: must exit immediately and resurface the first
Start-Sleep -Seconds 2
(Get-Process -Name TanMenu.Wpf).Count
```

Expected: `1` — the second process exited, and the first window came to the foreground. Also hide the window to the tray first, then launch the exe again: the hidden window must reappear.

- [ ] **Step 4: Commit**

```bash
git add src/TanMenu.Wpf/Program.cs src/TanMenu.Wpf/Services/GlobalHotkeyService.cs
git commit -m "refactor(shell): signal the first instance by registered message, not window caption"
```

---

### Task 5: Navigation rail and the app page

**Files:**
- Create: `src/TanMenu.Wpf/Views/AppPage.axaml` + `AppPage.axaml.cs`
- Create: `src/TanMenu.Wpf/Services/IShellNavigator.cs`
- Modify: `src/TanMenu.Wpf/MainWindow.axaml` (becomes the shell) + `MainWindow.axaml.cs`
- Modify: `src/TanMenu.Wpf/App.axaml.cs` (DI registration for the navigator)
- Modify: `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` (nav item theme)
- Modify: `src/TanMenu.Core/Services/AppLanguage.cs` (rail labels)

**Interfaces:**
- Consumes: `LauncherViewModel` (unchanged).
- Produces: `enum ShellPage { App, Settings }`; `interface IShellNavigator { void Navigate(ShellPage page); ShellPage Current { get; } }`; `class ShellNavigator : IShellNavigator` with `void Attach(MainWindow window)`. `AppPage` is a `UserControl` whose `DataContext` is the shared `LauncherViewModel`.

- [ ] **Step 1: Add the rail strings**

In `src/TanMenu.Core/Services/AppLanguage.cs`, add to the `Texts` dictionary:

```csharp
            ["NavApps"] = ("应用", "Apps"),
            ["NavSettings"] = ("设置", "Settings"),
```

- [ ] **Step 2: Add the navigator seam**

Create `src/TanMenu.Wpf/Services/IShellNavigator.cs`:

```csharp
namespace TanMenu.Wpf.Services;

public enum ShellPage
{
    App,
    Settings,
}

/// <summary>Page switching for the shell's two-entry rail. Exists so the tray menu and the app page's
/// overflow menu can reach the settings page without holding a reference to the window.</summary>
public interface IShellNavigator
{
    ShellPage Current { get; }
    void Navigate(ShellPage page);
}

public sealed class ShellNavigator : IShellNavigator
{
    private MainWindow? _window;

    public ShellPage Current { get; private set; } = ShellPage.App;

    /// <summary>Called once from MainWindow's constructor. Navigation before this is remembered and
    /// applied when the window attaches, so a tray click during startup is not lost.</summary>
    public void Attach(MainWindow window)
    {
        _window = window;
        window.ShowPage(Current);
    }

    public void Navigate(ShellPage page)
    {
        Current = page;
        _window?.ShowPage(page);
    }
}
```

Register it in `App.StartupAsync` alongside the other app services:

```csharp
        services.AddSingleton<ShellNavigator>();
        services.AddSingleton<IShellNavigator>(sp => sp.GetRequiredService<ShellNavigator>());
```

- [ ] **Step 3: Move the launcher UI into AppPage**

Create `src/TanMenu.Wpf/Views/AppPage.axaml`. Its content is everything that is inside today's `MainWindow.axaml` `<ctl:OverlayPanel>` — the header (search + overflow), the body `ScrollViewer` with the update notice, the groups `ItemsControl`, the splash, the empty state and the no-matches state, plus the toast and about overlays. Wrap it as:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:TanMenu.Wpf.ViewModels"
             xmlns:ctl="using:TanMenu.Wpf.Controls"
             x:Class="TanMenu.Wpf.Views.AppPage"
             x:DataType="vm:LauncherViewModel">

  <!-- The launcher proper. The window shell owns the title bar and the navigation rail; everything
       here is the 应用 page: search, the taskbar-pin notice (Task 7), the categories, and the three
       non-menu states (loading, nothing configured, nothing matched). -->

  <ctl:OverlayPanel>
    <!-- … the existing DockPanel from MainWindow.axaml, minus the tm-surface Border … -->
  </ctl:OverlayPanel>
</UserControl>
```

Move the code-behind wholesale: `AppPage.axaml.cs` takes `OnCloseClick` (now "hide window"), `OnOpenRootClick`, `OnRefreshClick`, `OnAboutClick`, `OnExitClick`, `OnSearchFocusChanged`, `OnClearFilterClick`, `OnSearchKeyDown`, `OnItemClick`, `OnItemPointerEntered`, `OnGroupTitlePressed`, `OnUpdateNoticeClick`, `OnToastPressed`, `OnAboutPressed`, plus the whole keyboard-navigation block (`OnKeyDown`, `IsTile`, `FocusFirstTile`, `Tiles`, `FindAdjacentTile`, `CenterOf`) and `ConsumePendingViewWork` / `OnViewModelPropertyChanged`:

```csharp
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Wpf.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf.Views;

/// <summary>
/// The 应用 page. All state lives in <see cref="LauncherViewModel"/>; this is the thin view layer that
/// wires clicks and keys to it and owns the two things that genuinely need the visual tree — deferred
/// search focus and geometric keyboard navigation across the tiles.
/// </summary>
public partial class AppPage : UserControl
{
    private readonly LauncherViewModel _viewModel;
    private readonly IWindowHost _host;
    private readonly IShellNavigator _navigator;

    public AppPage()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        _navigator = App.Services.GetRequiredService<IShellNavigator>();
        _viewModel = LauncherViewModel.Create(App.Services);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;

        _ = _viewModel.InitializeAsync();
    }

    // … moved members …
}
```

`OnOpenSettingsClick` is deleted from the overflow menu (the 选项 `MenuItem` goes with it) — the rail is that entry point now. `MainWindow` keeps `OnClosing`, `ApplyBackdrop` and the host attachment and loses everything else.

- [ ] **Step 4: Rewrite MainWindow as the shell**

`src/TanMenu.Wpf/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="using:TanMenu.Wpf.Views"
        x:Class="TanMenu.Wpf.MainWindow"
        Title="TanMenu"
        Width="1000" Height="640" MinWidth="760" MinHeight="520"
        WindowStartupLocation="Manual"
        TransparencyLevelHint="Mica"
        Background="Transparent"
        Icon="/wwwroot/app.ico"
        FontSize="14"
        Foreground="{DynamicResource TextPrimaryBrush}">

  <!--
    The shell: a system-decorated Mica window (Windows 11 rounds and shadows it for us) split into a
    fixed navigation rail and a page host. Exactly two pages exist, so the rail is a ListBox rather
    than anything more elaborate, and the selected page is view state — the window always opens on 应用.
  -->

  <Grid ColumnDefinitions="200,*">

    <ListBox x:Name="NavRail"
             Grid.Column="0"
             Classes="tm-nav"
             SelectedIndex="0"
             SelectionChanged="OnNavSelectionChanged"
             Background="Transparent"
             BorderThickness="0"
             Padding="4,12">
      <ListBoxItem>
        <StackPanel Orientation="Horizontal" Spacing="12">
          <TextBlock Classes="tm-icon" Text="&#xE71D;" VerticalAlignment="Center" />
          <TextBlock x:Name="NavAppsLabel" Text="应用" VerticalAlignment="Center" />
        </StackPanel>
      </ListBoxItem>
      <ListBoxItem>
        <StackPanel Orientation="Horizontal" Spacing="12">
          <TextBlock Classes="tm-icon" Text="&#xE713;" VerticalAlignment="Center" />
          <TextBlock x:Name="NavSettingsLabel" Text="设置" VerticalAlignment="Center" />
        </StackPanel>
      </ListBoxItem>
    </ListBox>

    <ContentControl x:Name="PageHost" Grid.Column="1" />
  </Grid>
</Window>
```

`MainWindow.axaml.cs` keeps the backdrop guard and the close-to-tray override, and gains:

```csharp
    private readonly AppPage _appPage = new();
    private Control? _settingsPage;

    public MainWindow()
    {
        InitializeComponent();

        _host = App.Services.GetRequiredService<IWindowHost>();
        App.Services.GetRequiredService<ShellNavigator>().Attach(this);
        ApplyRailText();
    }

    /// <summary>Swap the page host's content. Both pages are constructed lazily and then kept alive:
    /// the app page holds the built menu and the settings page holds staged control state, and
    /// rebuilding either on every rail click would be visible.</summary>
    public void ShowPage(ShellPage page)
    {
        NavRail.SelectedIndex = page == ShellPage.Settings ? 1 : 0;
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new SettingsPage()   // Task 8 adds this type; until then use _appPage
            : _appPage;
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        App.Services.GetRequiredService<ShellNavigator>()
            .Navigate(NavRail.SelectedIndex == 1 ? ShellPage.Settings : ShellPage.App);

    /// <summary>AppLanguage has no change notification, so the rail is labelled imperatively — the same
    /// approach the settings UI uses.</summary>
    private void ApplyRailText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        NavAppsLabel.Text = AppLanguage.Text("NavApps", lang);
        NavSettingsLabel.Text = AppLanguage.Text("NavSettings", lang);
    }
```

Until Task 8 exists, `ShowPage(ShellPage.Settings)` must not reference a missing type — implement it in this task as:

```csharp
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new TextBlock { Text = "…", Margin = new Thickness(24) }
            : _appPage;
```

and replace that placeholder with `new SettingsPage()` in Task 8.

- [ ] **Step 5: Style the rail**

Append to `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml`:

```xml
  <!-- Navigation rail. Fluent 2's NavigationView selection idiom: a 3 px accent bar on the leading
       edge of the selected row, a subtle fill on hover, and no border anywhere. -->
  <Style Selector="ListBox.tm-nav ListBoxItem">
    <Setter Property="Padding" Value="12,10" />
    <Setter Property="Margin" Value="4,2" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusControl}" />
    <Setter Property="Background" Value="Transparent" />
  </Style>
  <Style Selector="ListBox.tm-nav ListBoxItem:pointerover /template/ ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource SubtleFillSecondaryBrush}" />
  </Style>
  <Style Selector="ListBox.tm-nav ListBoxItem:selected /template/ ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource SubtleFillTertiaryBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource AccentDefaultBrush}" />
    <Setter Property="BorderThickness" Value="3,0,0,0" />
  </Style>
```

- [ ] **Step 6: Build and verify by hand**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj -v minimal
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Expected: a 200 px rail with 应用 selected and the launcher content to its right; clicking 设置 shows the placeholder and the accent bar moves; search, tile clicks, arrow-key navigation and the overflow menu all still work.

- [ ] **Step 7: Commit**

```bash
git add src/TanMenu.Wpf src/TanMenu.Core/Services/AppLanguage.cs
git commit -m "feat(shell): add the navigation rail and extract the launcher into AppPage"
```

---

### Task 6: Categories side by side

**Files:**
- Modify: `src/TanMenu.Wpf/Views/AppPage.axaml` (groups panel + body scrolling)
- Modify: `src/TanMenu.Wpf/Views/AppPage.axaml.cs` (`FindAdjacentTile` comment + column semantics)
- Modify: `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` (group column theme)

**Interfaces:**
- Consumes: `MenuGroupVm.VisibleItems`, `.HasVisibleItems`, `.DirectoryName`, `.IsDirectoryClickable`, `.TitleTooltip` (all unchanged).
- Produces: nothing new.

- [ ] **Step 1: Turn the body scroller sideways**

In `AppPage.axaml`, the body `ScrollViewer` becomes horizontal-only, and the groups `ItemsControl` lays its items out in a row:

```xml
          <ScrollViewer x:Name="Body"
                        HorizontalScrollBarVisibility="Auto"
                        VerticalScrollBarVisibility="Disabled">
            <StackPanel Orientation="Horizontal"
                        Margin="{DynamicResource SurfacePadding}"
                        Spacing="14">

              <!-- … update notice and the three states keep their own placement (see Step 3) … -->

              <ItemsControl IsVisible="{Binding HasVisibleItems}" ItemsSource="{Binding Groups}">
                <ItemsControl.ItemsPanel>
                  <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal" Spacing="24" />
                  </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                  <DataTemplate x:DataType="vm:MenuGroupVm">
                    <!-- DockPanel, NOT StackPanel: a vertical StackPanel measures its children with
                         infinite height, so the WrapPanel below would never learn where the bottom of
                         the window is and would never wrap into a second column — items just overflow
                         past the edge with no way to reach them. Docking the heading Top leaves the
                         WrapPanel filling the real remaining height. -->
                    <DockPanel Classes="tm-group" IsVisible="{Binding HasVisibleItems}">

                      <Border DockPanel.Dock="Top" Classes="tm-section-head"
                              Classes.clickable="{Binding IsDirectoryClickable}"
                              ToolTip.Tip="{Binding TitleTooltip}"
                              PointerPressed="OnGroupTitlePressed"
                              HorizontalAlignment="Left">
                        <StackPanel Orientation="Horizontal" Spacing="6">
                          <TextBlock Classes="tm-section" Text="{Binding DirectoryName}" />
                          <TextBlock Classes="tm-icon subtle small tm-folder"
                                     IsVisible="{Binding IsDirectoryClickable}"
                                     Text="&#xE8B7;" />
                        </StackPanel>
                      </Border>

                      <!-- Items fill a column top-to-bottom and wrap into the NEXT COLUMN when they
                           reach the bottom of the window — the pre-Avalonia layout, except that items
                           per column follow the window height instead of a fixed ColButtonCount. -->
                      <ItemsControl ItemsSource="{Binding VisibleItems}">
                        <ItemsControl.ItemsPanel>
                          <ItemsPanelTemplate>
                            <WrapPanel Orientation="Vertical" />
                          </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                          <DataTemplate x:DataType="vm:MenuItemVm">
                            <!-- … the existing tm-tile Button, unchanged … -->
                          </DataTemplate>
                        </ItemsControl.ItemTemplate>
                      </ItemsControl>

                    </DockPanel>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </StackPanel>
          </ScrollViewer>
```

- [ ] **Step 2: Keep the non-menu states centred**

The update notice, splash, empty and no-matches blocks must not be dragged into the horizontal row. Move them out of the scrolling `StackPanel` and dock them in the page's `DockPanel` instead: the update notice `DockPanel.Dock="Top"` above the body, and the three states as siblings of the `ScrollViewer` inside the existing `ctl:OverlayPanel`, each with `HorizontalAlignment="Center" VerticalAlignment="Center"`.

- [ ] **Step 3: Let a group report its true width, and keep the heading from inflating it**

The heading is the one piece of unbounded content in a group: `DirectoryName` is a user-chosen folder
name, and an unconstrained `TextBlock` widens the whole category. Trim it like the tiles already are:

```xml
                          <TextBlock Classes="tm-section" Text="{Binding DirectoryName}"
                                     MaxWidth="320" TextTrimming="CharacterEllipsis" />
```

Then append to `Themes/Fluent2/Launcher.axaml`:

```xml
  <!-- A group is as wide as its content but never wider than a comfortable reading column; without
       the cap a single long shortcut name would stretch its whole category. -->
  <!-- NO MaxWidth here. A cap would only clamp the width the group REPORTS to the horizontal
       StackPanel; the inner WrapPanel keeps laying out however many columns it needs and nothing
       clips it, so a category with more columns than the cap allows renders straight over its
       right-hand neighbour. Tiles already have a fixed width per size class, so a group's honest
       desired width is exactly columns × column width — bounded, and what the scroller needs. -->
  <Style Selector="DockPanel.tm-group">
    <Setter Property="VerticalAlignment" Value="Top" />
  </Style>
```

- [ ] **Step 4: Correct the keyboard-navigation comment**

In `AppPage.axaml.cs`, `OnKeyDown`'s doc comment still says tiles wrap into rows. Replace with:

```csharp
    /// <summary>
    /// Arrow keys move by GEOMETRY, not by index. Items now fill columns and categories sit side by
    /// side, so "the tile to the right" can be the next column of the same category OR the first
    /// column of the next one — an index step could not express either. Enter and Space are left
    /// alone: a focused Avalonia Button already handles both.
    /// </summary>
```

The `sameRow` test and the scoring stay as they are: they are computed from rendered bounds and remain correct for a column layout.

- [ ] **Step 5: Build, run and look at it**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Expected: categories run left to right with a horizontal scrollbar when they overflow; within a category, items fill downwards and start a new column at the bottom edge. Shrink the window vertically — columns must get shorter and more numerous. Type in the search box — non-matching categories disappear and the remaining ones close up.

- [ ] **Step 6: Commit**

```bash
git add src/TanMenu.Wpf/Views/AppPage.axaml src/TanMenu.Wpf/Views/AppPage.axaml.cs src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml
git commit -m "feat(app-page): lay categories out horizontally with column-filling items"
```

---

### Task 7: Taskbar-pin notice as an InfoBar

**Files:**
- Create: `src/TanMenu.Wpf/Controls/InfoBar.axaml` + `InfoBar.axaml.cs`
- Modify: `src/TanMenu.Wpf/Views/AppPage.axaml` + `.axaml.cs`
- Modify: `src/TanMenu.Wpf/App.axaml.cs` (delete the modal prompt)
- Modify: `src/TanMenu.Core/Services/AppLanguage.cs`

**Interfaces:**
- Consumes: `TaskbarPinService.GetStateAsync()`, `.RequestPinAsync()`, `.OpenManualPinLocation()`, `TaskbarPinStatus`; `GeneralConfig.TaskbarPinPromptDismissed` (Task 1).
- Produces: `InfoBar` control with `string Message`, `bool IsOpen` styled properties and a `Content` slot for actions.

- [ ] **Step 1: Add the strings**

In `AppLanguage.Texts`:

```csharp
            ["PinNow"] = ("固定", "Pin"),
            ["PinDontAskAgain"] = ("不再提示", "Don't ask again"),
            ["Dismiss"] = ("关闭", "Dismiss"),
```

- [ ] **Step 2: Build the InfoBar primitive**

Create `src/TanMenu.Wpf/Controls/InfoBar.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TanMenu.Wpf.Controls.InfoBar">

  <!-- Fluent 2's informational InfoBar: accent-tinted surface, leading glyph, message, then whatever
       actions the host puts in ActionContent, then a close glyph. Avalonia has no InfoBar of its own.

       NOTE: the action slot is a separate ActionContent property, NOT the UserControl's own Content.
       A UserControl's XAML root IS its Content, so binding a ContentPresenter inside it back to
       Content would be self-referential. Every value below is wired from code-behind rather than
       with RelativeSource bindings, because compiled bindings are on by default and this control
       deliberately has no x:DataType. -->
  <Border x:Name="Surface" Classes="tm-infobar" IsVisible="False">
    <DockPanel LastChildFill="True">
      <TextBlock DockPanel.Dock="Left" Classes="tm-icon small" Text="&#xE946;"
                 Margin="0,0,10,0" VerticalAlignment="Center" />
      <Button DockPanel.Dock="Right" x:Name="CloseButton"
              Theme="{StaticResource TmIconButton}" Click="OnCloseClick"
              Margin="8,0,0,0" VerticalAlignment="Center">
        <TextBlock Classes="tm-icon small" Text="&#xE8BB;" />
      </Button>
      <ContentPresenter DockPanel.Dock="Right" x:Name="ActionHost"
                        VerticalAlignment="Center" Margin="12,0,0,0" />
      <TextBlock x:Name="MessageText" TextWrapping="Wrap" VerticalAlignment="Center" />
    </DockPanel>
  </Border>
</UserControl>
```

`InfoBar.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TanMenu.Wpf.Controls;

/// <summary>An informational bar the page can raise inline instead of showing a modal dialog. The host
/// owns the actions (they go in <see cref="ActionContent"/>, deliberately NOT the control's own
/// Content, which for a UserControl is its XAML root); this control owns the surface, the icon, the
/// message and the close glyph.</summary>
public partial class InfoBar : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfoBar, string?>(nameof(Message));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<InfoBar, bool>(nameof(IsOpen));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<InfoBar, object?>(nameof(ActionContent));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Buttons the host wants on the bar's trailing edge.</summary>
    [Content]
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>Raised when the user clicks the close glyph. The host decides what dismissal means —
    /// this control never hides itself, so a host that persists the decision stays in control of it.</summary>
    public event EventHandler? Dismissed;

    public InfoBar()
    {
        InitializeComponent();

        // Wired here rather than with RelativeSource bindings in XAML: compiled bindings are on by
        // default and this control has no x:DataType to resolve them against.
        MessageText.Bind(TextBlock.TextProperty, this.GetObservable(MessageProperty));
        Surface.Bind(IsVisibleProperty, this.GetObservable(IsOpenProperty));
        ActionHost.Bind(ContentPresenter.ContentProperty, this.GetObservable(ActionContentProperty));
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);
}
```

Add the surface style to `Themes/Fluent2/Launcher.axaml`:

```xml
  <Style Selector="Border.tm-infobar">
    <Setter Property="Background" Value="{DynamicResource SubtleFillSecondaryBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource StrokeControlBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusControl}" />
    <Setter Property="Padding" Value="12,10" />
  </Style>
```

- [ ] **Step 3: Put it on the app page**

In `AppPage.axaml`, directly under the header and above the body:

```xml
          <ctl:InfoBar x:Name="PinNotice" DockPanel.Dock="Top" Margin="12,0,12,8"
                       IsOpen="False" Dismissed="OnPinNoticeDismissed">
            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button x:Name="PinNowButton" Click="OnPinNowClick" Padding="14,5" />
              <Button x:Name="PinNeverButton" Click="OnPinNeverClick" Padding="14,5" />
            </StackPanel>
          </ctl:InfoBar>
```

In `AppPage.axaml.cs`:

```csharp
    /// <summary>Evaluate the taskbar-pin notice. Runs on every reveal (the state can change behind our
    /// back — the user may pin from Explorer) but never blocks it.</summary>
    private async Task RefreshPinNoticeAsync()
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        if (cfg.Config.General.TaskbarPinPromptDismissed)
        {
            PinNotice.IsOpen = false;
            return;
        }

        var pin = App.Services.GetRequiredService<TaskbarPinService>();
        var state = await pin.GetStateAsync();
        var lang = cfg.Config.General.Language;

        PinNotice.Message = AppLanguage.Text("TaskbarPinRequiredPrompt", lang);
        PinNowButton.Content = AppLanguage.Text("PinNow", lang);
        PinNeverButton.Content = AppLanguage.Text("PinDontAskAgain", lang);
        PinNotice.IsOpen = state.Status != TaskbarPinStatus.Pinned;
    }

    private void OnPinNoticeDismissed(object? sender, EventArgs e) => PinNotice.IsOpen = false;

    private async void OnPinNeverClick(object? sender, RoutedEventArgs e)
    {
        var cfg = App.Services.GetRequiredService<ConfigService>();
        cfg.Config.General.TaskbarPinPromptDismissed = true;
        await cfg.SaveAsync();
        PinNotice.IsOpen = false;
    }

    private async void OnPinNowClick(object? sender, RoutedEventArgs e)
    {
        var pin = App.Services.GetRequiredService<TaskbarPinService>();
        var cfg = App.Services.GetRequiredService<ConfigService>();
        var lang = cfg.Config.General.Language;

        // Windows' own pin confirmation takes focus. Keep the window alive across it for users who
        // have 失焦自动隐藏 switched on.
        _host.SuppressHide(true);
        try
        {
            var state = await pin.GetStateAsync();
            if (state.Status is TaskbarPinStatus.Unsupported or TaskbarPinStatus.NotAllowed
                or TaskbarPinStatus.Failed)
            {
                await ShowManualPinLocationAsync(pin, lang);
                return;
            }

            state = await pin.RequestPinAsync();
            if (state.Status is TaskbarPinStatus.Pinned)
            {
                PinNotice.IsOpen = false;
                return;
            }

            if (state.Status is TaskbarPinStatus.Available)
                return; // the user declined the Windows confirmation; leave the notice up

            await ShowManualPinLocationAsync(pin, lang);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Taskbar pin request failed");
        }
        finally
        {
            _host.SuppressHide(false);
        }
    }

    /// <summary>Windows 10 often cannot expose the consent-based TaskbarManager flow to an unpackaged
    /// desktop app. Show the app's Start entry and let the user use Explorer's supported right-click
    /// command. Never mutate the protected taskbar pin store directly.</summary>
    private async Task ShowManualPinLocationAsync(TaskbarPinService pin, string? lang)
    {
        if (pin.OpenManualPinLocation())
            return;

        await MessageBox.ShowAsync(_host.Window, AppLanguage.Text("PinToTaskbarManualOpenFailed", lang),
            "TanMenu", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }
```

Call `_ = RefreshPinNoticeAsync();` from the `AppPage` constructor and from the `AppEvents.LauncherShown` handler.

- [ ] **Step 4: Delete the modal prompt**

In `src/TanMenu.Wpf/App.axaml.cs`, delete `PromptForTaskbarPinAfterReveal`, `PromptForTaskbarPinAsync`, `ShowManualPinLocationAsync`, the `_taskbarPinPromptActive` field, and the `AppEvents.LauncherShown += PromptForTaskbarPinAfterReveal;` subscription (line 164).

- [ ] **Step 5: Build and verify by hand**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Expected: **no modal dialog on startup**. If the app is not pinned, an InfoBar sits above the categories. `不再提示` makes it disappear permanently (verify `taskbarPinPromptDismissed: true` in `Data\config.json` and relaunch). Reset the flag to `false` by hand and confirm `固定` runs the Windows pin flow.

- [ ] **Step 6: Commit**

```bash
git add src/TanMenu.Wpf src/TanMenu.Core/Services/AppLanguage.cs
git commit -m "feat(app-page): replace the modal taskbar-pin dialog with an inline InfoBar"
```

---

### Task 8: Settings page shell + 外观 card group

**Files:**
- Create: `src/TanMenu.Wpf/Views/SettingsPage.axaml` + `.axaml.cs`
- Create: `src/TanMenu.Wpf/Views/Settings/AppearanceCard.axaml` + `.axaml.cs`
- Modify: `src/TanMenu.Wpf/MainWindow.axaml.cs` (replace the Task 5 placeholder)
- Modify: `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` (card theme)

**Interfaces:**
- Consumes: `ConfigService`, `AppEvents.RaiseSettingsChanged()`, `AppAppearance`, `AppFonts`, `AppLanguage`.
- Produces: `SettingsPage` (UserControl, hosts the card groups in a `ScrollViewer`); `AppearanceCard` (UserControl). Each card group exposes `void ReloadFromConfig()` so a language change can re-label every group.

- [ ] **Step 1: Add the card surface theme**

Append to `Themes/Fluent2/Launcher.axaml`:

```xml
  <!-- Windows 11 Settings' row card: a layered surface with a hairline outline, an icon, a title +
       secondary line, and the control itself trailing. -->
  <Style Selector="Border.tm-card">
    <Setter Property="Background" Value="{DynamicResource SurfaceLayerBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource StrokeControlBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource RadiusControl}" />
    <Setter Property="Padding" Value="16,12" />
    <Setter Property="Margin" Value="0,0,0,4" />
  </Style>
  <Style Selector="TextBlock.tm-card-title">
    <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}" />
  </Style>
  <Style Selector="TextBlock.tm-card-sub">
    <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}" />
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}" />
  </Style>
  <Style Selector="TextBlock.tm-group-head">
    <Setter Property="FontSize" Value="{DynamicResource FontSizeSubtitle}" />
    <Setter Property="Margin" Value="0,20,0,8" />
  </Style>
```

- [ ] **Step 2: Create the page host**

`src/TanMenu.Wpf/Views/SettingsPage.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:cards="using:TanMenu.Wpf.Views.Settings"
             x:Class="TanMenu.Wpf.Views.SettingsPage">

  <!-- Windows 11 Settings' shape: one scrolling column of grouped cards, no tabs and no
       OK/Cancel/Apply. Every control writes through to config the moment it changes; the three
       destructive actions in FoldersCard keep their own confirmation dialogs. -->

  <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24,16,24,24">
    <StackPanel MaxWidth="720" HorizontalAlignment="Left">
      <TextBlock x:Name="PageTitle" Classes="tm-group-head" Margin="0,0,0,8" />
      <cards:AppearanceCard x:Name="Appearance" />
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

`SettingsPage.axaml.cs`:

```csharp
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Views;

/// <summary>The 设置 page. Owns nothing but layout: each card group reads and writes config itself, so
/// adding a group is adding one control here.</summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        ApplyStaticText();
    }

    /// <summary>AppLanguage has no change notification, so labels are applied imperatively. A language
    /// change calls this and every group's own ReloadFromConfig.</summary>
    public void ApplyStaticText()
    {
        var lang = App.Services.GetRequiredService<ConfigService>().Config.General.Language;
        PageTitle.Text = AppLanguage.Text("NavSettings", lang);
        Appearance.ReloadFromConfig();
    }
}
```

- [ ] **Step 3: Build the 外观 group**

`src/TanMenu.Wpf/Views/Settings/AppearanceCard.axaml` — four cards, each `Border.tm-card` with a `DockPanel`: leading `TextBlock.tm-icon`, trailing control, and a middle `StackPanel` with `tm-card-title` + `tm-card-sub`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TanMenu.Wpf.Views.Settings.AppearanceCard">
  <StackPanel>
    <TextBlock x:Name="GroupHead" Classes="tm-group-head" />

    <Border Classes="tm-card">
      <DockPanel>
        <TextBlock DockPanel.Dock="Left" Classes="tm-icon" Text="&#xE774;" Margin="0,0,14,0" VerticalAlignment="Center" />
        <ComboBox DockPanel.Dock="Right" x:Name="LanguageCombo" Width="220" VerticalAlignment="Center"
                  SelectionChanged="OnLanguageChanged" />
        <StackPanel VerticalAlignment="Center">
          <TextBlock x:Name="LanguageTitle" Classes="tm-card-title" />
          <TextBlock x:Name="LanguageSub" Classes="tm-card-sub" />
        </StackPanel>
      </DockPanel>
    </Border>

    <!-- 外观 (E793), 字体 (E8D2), 按钮大小 (E8A9) repeat the same structure with
         AppearanceCombo / FontCombo / SizeCombo and their SelectionChanged handlers. -->
  </StackPanel>
</UserControl>
```

`AppearanceCard.axaml.cs` ports the corresponding members from `SettingsWindow.axaml.cs`: `BuildFontItems` (line 44), `PopulateFontCombo` (389), `Font_SelectionChanged` (419), `Language_Changed` (445), `Size_Changed` (486), `Appearance_Changed` (498), `AppearanceLabelFor` (508), `SizeLabel` (372). Two changes from the originals:

1. Every handler writes straight to `ConfigService.Config.General` and calls `await cfg.SaveAsync()` plus `AppEvents.RaiseSettingsChanged()`, instead of mutating a `_staged` clone and waiting for Apply.
2. A guard flag suppresses handler reentry while `ReloadFromConfig` populates the combos:

```csharp
    private bool _loading;

    /// <summary>Populate every control from the live config. Sets a guard so the SelectionChanged
    /// handlers do not write back the values they are being given.</summary>
    public void ReloadFromConfig()
    {
        _loading = true;
        try
        {
            // … populate combos and labels from ConfigService.Config.General …
        }
        finally
        {
            _loading = false;
        }
    }
```

Every handler starts with `if (_loading) return;`.

- [ ] **Step 4: Point the rail at the real page**

In `MainWindow.axaml.cs`, replace the Task 5 placeholder:

```csharp
        PageHost.Content = page == ShellPage.Settings
            ? _settingsPage ??= new SettingsPage()
            : _appPage;
```

with `private SettingsPage? _settingsPage;` as the field type.

- [ ] **Step 5: Build and verify by hand**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj -v minimal
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Expected: 设置 shows four cards. Changing 外观 to 深色 repaints the app immediately and survives a restart; changing 字体 restyles the app page; changing 语言 re-labels the rail and the cards without a restart; 按钮大小 changes the tiles. Verify each value landed in `Data\config.json`.

- [ ] **Step 6: Commit**

```bash
git add src/TanMenu.Wpf
git commit -m "feat(settings): add the settings page shell and the appearance card group"
```

---

### Task 9: 行为 card group

**Files:**
- Create: `src/TanMenu.Wpf/Views/Settings/BehaviorCard.axaml` + `.axaml.cs`
- Modify: `src/TanMenu.Wpf/Views/SettingsPage.axaml` + `.axaml.cs`

**Interfaces:**
- Consumes: `ConfigService`, `IAutoStartService`, `TaskbarPinService`, `GlobalHotkeyService`, `AppEvents`.
- Produces: `BehaviorCard` with `void ReloadFromConfig()`.

- [ ] **Step 1: Build the group**

Cards, in order: 失焦自动隐藏 (`ToggleSwitch`), 窗口置顶 (`ToggleSwitch`), 开机自启 (`ToggleSwitch`), 固定到任务栏 (`Button` + status line), 启用全局热键 (`ToggleSwitch`), 热键 (read-only `TextBox` + 清除 `Button`).

Port from `SettingsWindow.axaml.cs`: `Opt_Changed` (570), `AutoStart_Changed` (642), `PinTaskbar_Click` (580), `OpenManualTaskbarPinLocationAsync` (604), `RefreshTaskbarPinStateAsync` (617), `ApplyTaskbarPinState` (623), `UpdateHotkeyEnabledState` (683), `Hotkey_Changed` (685), `Hotkey_KeyDown` (694), `HotkeyClear_Click` (707).

Two behavioural notes to preserve verbatim from the original:

- The hotkey capture handler must stay **tunneling**, or the `TextBox` consumes the key first:

```csharp
        HotkeyBox.AddHandler(InputElement.KeyDownEvent, Hotkey_KeyDown, RoutingStrategies.Tunnel);
```

- `窗口置顶` must apply live, not only on restart:

```csharp
        if (_host.Window is { } w)
            w.Topmost = cfg.Config.General.TopMost;
```

- [ ] **Step 2: Register it on the page**

Add `<cards:BehaviorCard x:Name="Behavior" />` under `AppearanceCard` in `SettingsPage.axaml` and `Behavior.ReloadFromConfig();` to `ApplyStaticText`.

- [ ] **Step 3: Build and verify by hand**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Expected: toggling 失焦自动隐藏 on makes the window hide when it loses focus, and off stops it. 窗口置顶 takes effect without a restart. 开机自启 writes the Run key (check `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run` for `TanMenuWpf`). Setting a hotkey registers it and it summons the window from another app.

- [ ] **Step 4: Commit**

```bash
git add src/TanMenu.Wpf
git commit -m "feat(settings): add the behavior card group"
```

---

### Task 10: 文件夹 card group

**Files:**
- Create: `src/TanMenu.Wpf/Views/Settings/FoldersCard.axaml` + `.axaml.cs`
- Modify: `src/TanMenu.Wpf/Views/SettingsPage.axaml` + `.axaml.cs`

**Interfaces:**
- Consumes: `ConfigService`, `DataLocation`, `IAppDataPaths`, `IShellCommands`, `MessageBox`, `IWindowHost.SuppressHide`.
- Produces: `FoldersCard` with `void ReloadFromConfig()`.

- [ ] **Step 1: Build the group**

Cards: 主文件夹 (read-only path + 打开 / 选择…), 数据文件夹 (read-only path + size info + 打开 / 更改… / 重置为默认), 维护 (清理缓存 / 备份… / 恢复…).

Port from `SettingsWindow.axaml.cs`: `PickRoot_Click` (776), `OpenRoot_Click` (791), `OpenData_Click` (802), `ChangeData_Click` (805), `ResetData_Click` (830), `RelocateDataTo` (879), `ClearCache_Click` (920), `RefreshSizeLabelAfterAsync` (938), `BackupConfig_Click` (949), `RestoreConfig_Click` (978), `UpdateDataLocationInfo` / `…Async` (1011/1013), `FormatSize` (1037), `PickFolderAsync` (1048), `ParseFilter` (1059), `ConfigureDataLocationControls` (650).

**The three destructive actions keep their confirmation dialogs** — 更改数据文件夹, 重置为默认 and 恢复 all call `MessageBox.ShowAsync` before acting, exactly as they do today. Immediate-apply is for settings, not for operations that move or overwrite user data.

Every folder picker must bracket with `SuppressHide`:

```csharp
        _host.SuppressHide(true);
        try
        {
            // … await PickFolderAsync(…) …
        }
        finally
        {
            _host.SuppressHide(false);
        }
```

- [ ] **Step 2: Register it on the page**

Add `<cards:FoldersCard x:Name="Folders" />` and `Folders.ReloadFromConfig();`.

- [ ] **Step 3: Build and verify by hand**

Expected: 选择… picks a new root and the app page rebuilds its categories immediately; 打开 opens Explorer; 清理缓存 shrinks the reported data size; 备份… writes a file and 恢复… reads it back and re-applies settings; 更改数据文件夹 and 重置为默认 both confirm first and then relocate.

- [ ] **Step 4: Commit**

```bash
git add src/TanMenu.Wpf
git commit -m "feat(settings): add the folders card group"
```

---

### Task 11: 常用工具 card group

**Files:**
- Create: `src/TanMenu.Wpf/Views/Settings/ToolsCard.axaml` + `.axaml.cs`
- Modify: `src/TanMenu.Wpf/Views/SettingsPage.axaml` + `.axaml.cs`

**Interfaces:**
- Consumes: `ConfigService`, `GeneralConfig.ShowDefaultTools`, `.DefaultTools` (`List<DefaultTool>`), `AppEvents`.
- Produces: `ToolsCard` with `void ReloadFromConfig()`.

- [ ] **Step 1: Build the group**

One card with the 显示「常用工具」分类 `ToggleSwitch`, then a bordered list of per-tool `CheckBox`es built from `DefaultTools`. Port `BuildToolCheckboxes` (525), `ShowTools_Changed` (547) and `Tool_Changed` (557); each checkbox writes `DefaultTool.Show` and saves immediately. Keep the list inside a `ScrollViewer` with `MaxHeight="240"` as the original did, so a long tool list cannot push the following groups off screen.

- [ ] **Step 2: Register it on the page**

Add `<cards:ToolsCard x:Name="Tools" />` and `Tools.ReloadFromConfig();`.

- [ ] **Step 3: Build and verify by hand**

Expected: unchecking a tool removes it from the 常用工具 category on the app page immediately; the master toggle removes the whole category.

- [ ] **Step 4: Commit**

```bash
git add src/TanMenu.Wpf
git commit -m "feat(settings): add the common-tools card group"
```

---

### Task 12: 更新 card group

**Files:**
- Create: `src/TanMenu.Wpf/Views/Settings/UpdatesCard.axaml` + `.axaml.cs`
- Modify: `src/TanMenu.Wpf/Views/SettingsPage.axaml` + `.axaml.cs`

**Interfaces:**
- Consumes: `IAppUpdateService` — resolved with `App.Services.GetRequiredService<IAppUpdateService>()` — exposing `State` (whose `.Status` is an `AppUpdateStatus`), `event EventHandler StateChanged`, `CheckForUpdatesAsync()`, `DownloadUpdateAsync()` and `ApplyAndRestart()`. Port `Updates_StateChanged` (`SettingsWindow.axaml.cs:311`), `RenderUpdateState` (323), `CheckUpdate_Click` (359), `UpdateAction_Click` (362) and `GetCurrentVersion` (306).
- Produces: `UpdatesCard` with `void ReloadFromConfig()` and an `IDisposable`-style unsubscribe in `OnDetachedFromVisualTree`.

- [ ] **Step 1: Build the group**

One card showing 当前版本, one showing status + a `ProgressBar` + 检查更新 / 下载更新 buttons. Port the five members above verbatim. `UpdateAction_Click` keeps its branch: `ReadyToRestart` → `ApplyAndRestart()`, `Available` → `await DownloadUpdateAsync()`.

**Unsubscribe when the page goes away** — `SettingsWindow` did this on `Closed` (line 120), and a page that is kept alive across navigation needs the same treatment or the update service holds a detached control:

```csharp
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _updates.StateChanged += OnUpdateStateChanged;
        RenderUpdateState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _updates.StateChanged -= OnUpdateStateChanged;
        base.OnDetachedFromVisualTree(e);
    }
```

- [ ] **Step 2: Register it on the page**

Add `<cards:UpdatesCard x:Name="Updates" />` and `Updates.ReloadFromConfig();`.

- [ ] **Step 3: Build and verify by hand**

Expected: 检查更新 reports a result; the app page's update notice and this card agree on the state.

- [ ] **Step 4: Commit**

```bash
git add src/TanMenu.Wpf
git commit -m "feat(settings): add the updates card group"
```

---

### Task 13: Delete SettingsWindow and finish the wiring

**Files:**
- Delete: `src/TanMenu.Wpf/SettingsWindow.axaml`, `src/TanMenu.Wpf/SettingsWindow.axaml.cs`
- Modify: `src/TanMenu.Wpf/Services/ISettingsLauncher.cs`
- Modify: `docs/superpowers/specs/2026-07-31-win11-shell-redesign-design.md` (mark as implemented)
- Modify: `README.md` if it describes the flyout behaviour

**Interfaces:**
- Consumes: `IShellNavigator` (Task 5), `SettingsPage` (Task 8).
- Produces: `SettingsLauncher` that navigates instead of opening a window.

- [ ] **Step 1: Rewrite the launcher**

Replace `SettingsLauncher`'s implementation:

```csharp
public sealed class SettingsLauncher : ISettingsLauncher
{
    private readonly IWindowHost _host;
    private readonly IShellNavigator _navigator;

    public SettingsLauncher(IWindowHost host, IShellNavigator navigator)
    {
        _host = host;
        _navigator = navigator;
    }

    public void Open() => _ = OpenAsync();

    /// <summary>Settings is a PAGE now, not a window: this selects it and brings the main window
    /// forward. Still async and still marshalled to the UI thread — the tray menu calls it from a
    /// shell thread.</summary>
    public Task OpenAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OpenCore();
            return Task.CompletedTask;
        }

        var marshalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                OpenCore();
                marshalled.TrySetResult();
            }
            catch (Exception ex)
            {
                marshalled.TrySetException(ex);
            }
        });
        return marshalled.Task;
    }

    private void OpenCore()
    {
        _navigator.Navigate(ShellPage.Settings);
        _host.ShowAndActivate();
    }
}
```

Update its DI registration if the constructor signature changed (`ConfigService` is no longer needed).

- [ ] **Step 2: Delete the window**

```powershell
Remove-Item src\TanMenu.Wpf\SettingsWindow.axaml, src\TanMenu.Wpf\SettingsWindow.axaml.cs
```

Then grep for stragglers and fix each hit:

```powershell
Select-String -Path src\TanMenu.Wpf\**\*.cs, src\TanMenu.Wpf\**\*.axaml -Pattern 'SettingsWindow'
```

- [ ] **Step 3: Update the docs**

Add a short "Status: implemented in `<commit>`" line at the top of the spec, and correct any README sentence that still describes a hide-on-blur flyout or a separate settings window. Per the repo's documentation rule, verify each claim against the code before writing it.

- [ ] **Step 4: Full verification pass**

```powershell
Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Debug -v minimal
dotnet test tests/TanMenu.Core.Tests/TanMenu.Core.Tests.csproj -v minimal
dotnet publish src/TanMenu.Wpf/TanMenu.Wpf.csproj -c Release
Start-Process 'src\TanMenu.Wpf\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe'
```

Walk the whole disturbed surface, in this order:

1. Tray single click hides, then shows the window.
2. Tray 显示 shows it; tray 选项 lands on the 设置 page with the window in front.
3. Global hotkey (set one in 行为 first) toggles it from another application.
4. Launch the exe a second time — the process count stays at 1 and the window resurfaces.
5. Turn 失焦自动隐藏 on, click elsewhere (hides), turn it off, click elsewhere (stays).
6. Close with the ✕ — the window hides and the tray icon survives; 退出 in the tray menu really exits.
7. Resize/move, exit and relaunch — geometry is restored. Set `windowLeft` to `9999` in `config.json` by hand and relaunch — the window is clamped back on screen.
8. Turn transparency off in Windows settings (`Personalization → Colors → Transparency effects`) and relaunch — the window is opaque `SurfaceBaseBrush`, not a hole.
9. NativeAOT publish completes with no new trim/AOT warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(settings): absorb the settings window into the shell as a page"
```

---

## Self-Review

**Spec coverage.** §1 window model → Tasks 1, 2, 3. §2 shell → Task 5. §3 horizontal categories → Task 6. §4 pin InfoBar → Task 7. §5 settings page → Tasks 8–13. §6 config schema and migration → Task 1. §7 single-instance handshake → Task 4. §8 "what does not change" → nothing to implement; enforced by leaving `LauncherViewModel` and `TanMenu.Core`'s services alone. §9 testing → the unit tests in Tasks 1–2 and the manual passes closing Tasks 3–13. §10 rejected alternatives → informational.

**Naming consistency.** `RestorePlacement` / `SavePlacement` (Task 3) are used under those names in Tasks 3 and 13. `ShellPage` / `IShellNavigator.Navigate` (Task 5) are consumed under those names in Tasks 8 and 13. `ReloadFromConfig` is the single reload entry point on every card group (Tasks 8–12) and `SettingsPage.ApplyStaticText` calls exactly that name. `WindowPlacement.Resolve`'s signature in Task 2 matches its call site in Task 3. `AppConfig.CurrentSchemaVersion` and `GeneralConfig.SchemaVersion` (Task 1) are referenced under those names in the Task 1 tests only.

**Known sequencing constraint.** Task 5 introduces a placeholder for the settings page and Task 8 replaces it; Task 5's step text names this explicitly so a fresh implementer does not go looking for a `SettingsPage` type that does not exist yet. `ISettingsLauncher` keeps opening the old `SettingsWindow` until Task 13, which is what keeps every intermediate commit a working application.
