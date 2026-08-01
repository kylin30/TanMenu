# TanMenu — Windows 11 app shell (nav + pages, replaces the flyout launcher)

> **Status: implemented in `28158a8`..`e7f4797`, corrected by `41c6529`** (Task 13 of 13 deletes
> `SettingsWindow` and finishes the wiring this spec describes; `e7f4797` is a `MenuService`
> null-safety fix found while verifying it; `41c6529` is the single fix wave clearing the
> whole-branch review). Every section below now describes shipped behaviour, not a plan.
>
> **Outstanding human verification.** Five behaviours could not be exercised by any automated session:
> the execution environment delivers no real keyboard or mouse input (raw `SendInput`, `SetCursorPos`
> and `SetForegroundWindow` all silently no-op, proven with a Notepad control test) and has no
> foreground window at all (`GetForegroundWindow()` returns 0 unconditionally). They need a person at
> the machine:
>
> 1. **Tray left-click while TanMenu is the foreground window must HIDE it.** `WindowHost.Toggle`
>    gates the hide on `GetForegroundWindow() == our HWND`; if clicking the notification area makes
>    `Shell_TrayWnd` foreground first, the gate reads false and the click re-raises instead of
>    hiding. This is the one plausible functional regression left in the branch. If it misbehaves,
>    the remedy is a "was foreground immediately before this click" latch rather than a live query.
> 2. Geometric arrow-key navigation across tiles: `Down` from the search box into the grid,
>    `Left`/`Right` across column and category boundaries, `Up` back to the search box.
> 3. The tile hover sound, and that a disabled tile stays silent.
> 4. Physical key-combo capture into the 热键 box, and a registered hotkey summoning the window from
>    another application.
> 5. Restoring onto a secondary monitor by actually dragging the window there, exiting and relaunching
>    (the saved-rectangle path was verified by writing the coordinates into `config.json` directly).
>
> Verified by the controller against the running app: NativeAOT `dotnet publish -c Release` produces a
> 28.6 MB native executable with 0 warnings and 0 errors, and a cold start with
> `windowMaximized: true` restores maximized.
>
> Design spec, 2026-07-31. Every claim about the current code was grep/read-verified against the
> tree at `ef79481`, and every Avalonia claim against the `Avalonia 12.1.0` reference assembly
> documentation in the local NuGet cache — not recalled. Where this file and the code disagree, the
> code wins and this file is the bug.

## Goal

TanMenu is currently a light-dismiss flyout: a content-sized, undecorated, acrylic popup that hides
on blur, stays out of the taskbar, and stacks its categories vertically. This spec turns it into an
ordinary Windows 11 application window — Task Manager's shape: a system-decorated Mica window with a
left navigation rail and a content area.

Four requirements drive it:

1. The taskbar-pin prompt stops being a modal dialog and becomes an in-page notice.
2. Categories go back to being laid out **horizontally**, as they were before the Avalonia rewrite.
3. The window becomes a Task-Manager-style shell: navigation on the left, content on the right.
4. The whole app conforms to Windows 11 app conventions.

Requirements 2 and 3 are reconciled as follows (decided with the user on 2026-07-31): the left rail
holds exactly **two** entries — 应用 and 设置. The 应用 page is where categories sit side by side; the
设置 page absorbs today's separate settings window.

## What Avalonia 12.1 actually gives us

Verified against `~/.nuget/packages/avalonia/12.1.0/ref/net10.0/Avalonia.Controls.xml`:

| API | Status |
|---|---|
| `Window.WindowDecorations` | Exists. Enum `WindowDecorations` = `None` \| `BorderOnly` \| `Full` |
| `Window.ExtendClientAreaToDecorationsHint` | Exists |
| `Window.ExtendClientAreaTitleBarHeightHint` | Exists |
| `Window.WindowDecorationsTheme` | Exists — "the theme used to render the window decorations when they are not drawn by the system" |
| `Avalonia.Controls.Chrome.WindowDrawnDecorations` | Exists (12.x chrome model) |
| `ExtendClientAreaChromeHints` | **Does not exist.** That is the Avalonia 11 name; 12 replaced it with the `Chrome` types above |
| `WindowTransparencyLevel.Mica` | Exists, and is already used in production by `SettingsWindow.axaml:24` |

**Decision: use the system title bar (`WindowDecorations` left at its default `Full`), not custom
chrome.** Task Manager itself ships a system title bar over a Mica backdrop; custom-drawn chrome is
for apps that put controls *in* the title bar (Explorer, Edge), and this design puts search in the
content area instead. `SettingsWindow` already proves the "system title bar + Mica + Fluent 2
tokens" combination works in this codebase, so this is the verified-cheap path and it removes any
dependency on Avalonia 12's still-changing `Chrome` API.

## Architecture

| Action | File |
|---|---|
| Rewrite | `MainWindow.axaml` (+ code-behind) — shell only: title bar, nav rail, page host |
| Add | `Views/AppPage.axaml` (+ code-behind) — search, pin notice, horizontal categories, empty states |
| Add | `Views/SettingsPage.axaml` (+ code-behind) — the five settings card groups |
| Add | `Views/Settings/*.axaml` — one control per card group (外观 / 行为 / 文件夹 / 常用工具 / 更新) |
| Add | `Controls/InfoBar.axaml` — Fluent 2 InfoBar, used by the taskbar-pin notice |
| Delete | `SettingsWindow.axaml` + `SettingsWindow.axaml.cs` (184 + 1079 lines) |
| Rewrite | `Services/WindowHost.cs` — placement replaces content-measurement |
| Edit | `Services/ISettingsLauncher.cs` — navigate instead of opening a window |
| Edit | `Program.cs` — broadcast-message handshake replaces `FindWindow` by caption |
| Edit | `Services/GlobalHotkeyService.cs` — match the registered message id |
| Edit | `App.axaml.cs` — drop the modal pin prompt; simplify the reveal path |
| Edit | `Themes/Fluent2/Launcher.axaml` — add nav / card / InfoBar themes |
| Edit | `Models/AppConfig.cs`, `Services/ConfigService.cs` — new fields + one-time migration |

`ViewModels/LauncherViewModel.cs` is **not** rewritten. `Groups`, `VisibleItems`,
`HasVisibleItems`, `ShowSplash`, `ShowEmpty`, `ShowNoMatches`, `Filter` and the localized-text
properties already express everything the app page needs; only the view that consumes them changes.

## 1 · Window model

`MainWindow.axaml` today (verified, lines 8–15): `WindowDecorations="None"`,
`TransparencyLevelHint="AcrylicBlur"`, `SizeToContent="WidthAndHeight"`, `ShowInTaskbar="False"`,
`CanResize="False"`, `Opacity="0"`, `WindowStartupLocation="Manual"`.

After:

| Aspect | Value |
|---|---|
| Decorations | System (`WindowDecorations` default `Full`) |
| Material | `TransparencyLevelHint="Mica"`, `Background="Transparent"` |
| Fallback | If `ActualTransparencyLevel != Mica`, paint `SurfaceBaseBrush` — the same guard `SettingsWindow.axaml.cs:148` already uses |
| Size | 1000×640 initial, `MinWidth=760`, `MinHeight=520`, `CanResize=True` |
| Taskbar | `ShowInTaskbar="True"` |
| Startup | `Manual`, restored from config and clamped to the work area of the monitor the SAVED rectangle belongs to (not the monitor the unplaced window happens to sit on), applied by `App.ShowLauncherWindow` BEFORE `Show()` |
| Close | Hides to tray (already the semantics of `ShutdownMode.OnExplicitShutdown` in `Program.cs:82`; unchanged) |

`ExperimentalAcrylicBorder` and the `OpaqueFallback` border are deleted from the visual tree — with
a system-decorated Mica window neither has a job. `Opacity="0"` and the reveal-fade go too: they
exist only so a cold-start flyout never flashes an unsized rectangle at screen top-left, and a
window whose size AND position are applied before it is ever shown cannot do that. That last clause
is a requirement on the startup order, not a free consequence of having a restored size: the
placement was originally applied from `MainWindow.OnOpened`, i.e. ~30 ms AFTER `Show()`, which
painted one frame at the OS cascade position and then jumped. `App.ShowLauncherWindow` now calls
`WindowHost.RestorePlacement()` between `Attach` and `Show`; Avalonia realizes the native window in
`Window`'s constructor, so `Screens` is already available there.

### WindowHost

`Services/WindowHost.cs` is 448 lines, most of it the measure-then-place machinery for a
content-sized popup. Deleted: `ResizeToContentAndPlaceAsync`, `TryMeasureNaturalSize`,
`ApplyReportedSizeAndPlace`, `ApplyWithoutScreenInfo`, `ContentVersion`, `RevealSequenceAsync`,
`RefreshPlacementAfterRevealAsync`, `CompleteReveal`, and the `ContentMaxWidthDip` /
`MaxHeightFraction` / `SetWindowPos` apparatus.

Added: `RestorePlacement()` (read config, pick the target monitor from the saved rectangle, clamp,
apply) and `SavePlacement()` (write size/position/maximized back). Kept unchanged in contract:
`Attach`, `Window`, `ShowAndActivate`, `Hide`, `Toggle`, `SuppressHide` — the tray
(`TrayService.cs:40,59`), the global hotkey (`GlobalHotkeyService.cs:69,146`), the second-instance
path, `MessageBox`, `IUpdatePromptService` and `ISettingsLauncher` all depend on them.

Three of those behaviours are sharper than "unchanged" implies, and each exists because this is an
ordinary app window rather than a focused flyout:

- **`Toggle` asks who is in FRONT, not merely whether the window is visible.** With 失焦自动隐藏 off by
  default the window routinely sits visible behind other apps, so `IsVisible` no longer implies
  "focused"; treating it as such made the hotkey and tray left-click hide the window the user was
  summoning. It hides only when `GetForegroundWindow()` is our HWND, and raises otherwise.
- **`Hide` resets the shell to 应用** (see §2), after the window is off screen so the page swap is
  never seen. Reset on hide rather than on show, because `ISettingsLauncher.OpenCore` navigates to
  设置 and *then* calls `ShowAndActivate`.
- **`FlushPlacementBlocking()`** is what 退出 calls. The fire-and-forget flush yields at the first
  await inside `SaveAsync`, and `desktop.Shutdown()` then ends the dispatcher loop its continuation
  was posted to, losing a geometry change made inside the 500 ms debounce window. `SaveAsync` awaits
  with `ConfigureAwait(false)` throughout so this blocking wait cannot deadlock.

`IWindowHost.ContentVersion` disappears from the interface, so `LauncherViewModel`'s
`_host.ContentVersion = HashCode.Combine(...)` block (`LauncherViewModel.cs:680`) is deleted with
it. That hash exists only to force a re-measure when rendered size could change; nothing re-measures
now.

### Behaviour defaults

`GeneralConfig.AutoClose` (失焦自动隐藏) and `GeneralConfig.TopMost` both default to `true` today
(`AppConfig.cs:39-40`). A window that lives in the taskbar and vanishes when you click elsewhere is
broken behaviour, so both defaults become `false`. Both settings stay in the UI for users who want
the old feel. See §6 for how existing configs — which carry `true` explicitly — are migrated.

## 2 · Shell: navigation rail + page host

```
┌────────────────────────────────────────────────────┐
│ ▣ TanMenu                              ─  □  ✕    │  system title bar over Mica
├──────────┬─────────────────────────────────────────┤
│ ▸ 应用    │  🔍 搜索快捷方式…                  ···  │
│   设置    │  ┌ ⓘ 固定到任务栏… [固定][不再提示] ✕ ┐ │
│          │  └────────────────────────────────────┘ │
│          │  ┌常用工具──┐┌工具─────┐┌开发────┐      │
│          │  │计算器    ││aida64   ││Apifox  │  →   │
│          │  │记事本    ││ARen命名 ││Bitvise │      │
│          │  └──────────┘└─────────┘└────────┘      │
└──────────┴─────────────────────────────────────────┘
```

- The rail is 200 px, fixed. Each entry is an icon (Segoe Fluent Icons, the family
  `Themes/Fluent2/Launcher.axaml` already uses for `tm-icon`) plus a label, and the selected entry
  carries Fluent 2's 3 px accent bar on its leading edge. No collapse-to-icons mode: with two
  entries it would be decoration, and `MinWidth=760` guarantees room. It is declared inline in
  `MainWindow.axaml` (a `ListBox` with two `ListBoxItem`s, lines 26–46) rather than as a separate
  `Controls/NavigationRail.axaml`: two static rows do not justify a control.
- The page host is a `ContentControl` swapping two `UserControl`s. `MainWindow.axaml` keeps only the
  shell so it does not grow into the 300-line file it is today.
- The overflow menu (`···`) keeps 打开主目录 / 刷新 / 关于 / 退出 and **loses 选项** — the rail is now
  that entry point. It moves into the app page's header, right of the search field.
- Navigation state is view-level, not config: the window always opens on 应用. `ShellNavigator.Current`
  is process-lifetime state that survives a hide/show cycle, so "always opens on 应用" is enforced by
  `WindowHost.Hide` resetting it — not merely by the initial value. Without that reset a session that
  visited 设置 re-summons onto 设置, with `AppPage` detached and therefore no search box to focus.

## 3 · App page: horizontal categories

The pre-Avalonia Blazor build (`a41f309:src/TanMenu.Wpf/Components/Index.razor`) laid groups out in
a horizontal flex row and chunked each group's items into columns of `ColButtonCount`. This restores
that shape without the fixed column count:

- Groups run left to right in a horizontal `StackPanel` inside a `ScrollViewer` with
  `HorizontalScrollBarVisibility="Auto"` and `VerticalScrollBarVisibility="Disabled"`.
- Within a group, items fill a column top-to-bottom and wrap to the next column when they reach the
  bottom — `WrapPanel Orientation="Vertical"`, height-constrained by the window. Items per column is
  therefore a function of window height, which is why no setting is needed for it.
- `GeneralConfig.ColButtonCount` stays exactly as it is: vestigial, round-tripped for old configs,
  not resurrected as a setting. The existing assertions in `ConfigServiceTests` / `JsonContractTests`
  / `AppConfigTests` keep passing untouched.
- Group headings keep today's behaviour (`tm-section-head`): click to open the directory, folder
  glyph only for a real directory, inert for the built-in 常用工具 group.
- Filtering, the splash, the empty state and the no-matches state move over unchanged; they bind to
  the same `LauncherViewModel` properties.

## 4 · Taskbar pin: modal dialog → in-page InfoBar

Today `App.PromptForTaskbarPinAfterReveal` (`App.axaml.cs:239`) fires on every `LauncherShown` and
`PromptForTaskbarPinAsync` shows a modal `MessageBox` with OK/Cancel. Both are deleted.

Replacement: a Fluent 2 InfoBar pinned above the categories in the app page.

- **Shown when** `TaskbarPinService.GetStateAsync().Status != Pinned` **and**
  `GeneralConfig.TaskbarPinPromptDismissed == false`.
- **Content**: informational icon, the existing `TaskbarPinRequiredPrompt` string, and three
  actions — `[固定]`, `[不再提示]`, and a close glyph.
- `[固定]` reuses the existing service logic verbatim: `RequestPinAsync()`, with the
  `Unsupported`/`NotAllowed`/`Failed` branch falling back to `OpenManualPinLocation()` and its own
  failure `MessageBox`. Only the entry point changes, not the flow.
- `[不再提示]` sets `TaskbarPinPromptDismissed = true` and saves through the SAME
  `SettingsWrite.CommitAsync` contract as the five settings card groups (§5) — a failed write rolls
  the flag back, re-evaluates the notice, and reports the error — so the bar can never be dismissed
  on screen while the flag never reached disk. The close glyph dismisses for this session only.
- The message string (`TaskbarPinRequiredPrompt`) describes THIS UI: it names the 固定 button, not the
  确定 of the modal it replaced, and it makes no hide-on-blur claim, since that is off by default now.
- Once the state reports `Pinned`, the bar removes itself.
- `SuppressHide(true/false)` around the pin flow is **kept**: Windows' own pin-confirmation dialog
  takes focus, and with 失焦自动隐藏 enabled the window would otherwise disappear mid-flow.

The 行为 settings card group keeps its own 固定到任务栏 button — that is the deliberate, always-available
path, unaffected by the dismissal flag.

## 5 · Settings page: Windows 11 setting cards

`SettingsWindow` (a `Window` + `TabControl` of five tabs + 确定/取消/应用 over a staged-edit model,
184 lines of XAML and 1079 of code-behind) is deleted outright. The five tabs become five card
groups on one vertically scrolling page:

| Group | Contents (unchanged in substance) |
|---|---|
| 外观 | 语言, 外观 (light/dark/system), 字体, 按钮大小 |
| 行为 | 失焦自动隐藏, 窗口置顶, 开机自启, 固定到任务栏 + status, 全局热键 enable/combo/clear |
| 文件夹 | 主文件夹 (打开/选择), 数据文件夹 (打开/更改/重置), 清理缓存, 备份, 恢复 |
| 常用工具 | 显示「常用工具」分类 + the per-tool checkbox list |
| 更新 | 当前版本, 检查更新, 下载/重启 action, progress |

- Each row is a `SettingsCard`: leading icon, title, secondary line, trailing control.
- **Edits apply immediately.** A dropdown or toggle change writes through `ConfigService` and raises
  `SettingsChanged` on the spot. The `_staged` clone and the Ok/Cancel/Apply buttons are gone.
- Exception — three destructive actions keep an explicit confirmation dialog: 更改数据文件夹,
  重置为默认, and 恢复 (restore from backup). "Immediate" is wrong for operations that move or
  overwrite the user's data.
- The 1079-line code-behind is split one control per card group under `Views/Settings/`, so no single
  file inherits the whole thing.
- **Anything read from the OS is re-read on `OnAttachedToVisualTree`, not once at construction.**
  `SettingsWindow` re-queried on every `Opened`; the page is cached for the process lifetime and has
  no such moment, and navigating 应用↔设置 is what detaches and re-attaches it. This covers both OS-backed
  rows in 行为 — 固定到任务栏 (shell COM) and 开机自启 (the Run key / StartupTask, which Task Manager's
  启动应用 tab changes behind the app's back).
- Localization keeps working the way it does now: `AppLanguage` has no change notification, so the
  page re-labels itself imperatively when the language card changes, exactly as
  `SettingsWindow.ApplyStaticText()` does today.

`ISettingsLauncher` keeps its `Open()` / `OpenAsync()` shape — the tray menu (`TrayService.cs:42`)
and the app page both call it — but the implementation becomes "select the 设置 page, then
`ShowAndActivate()`". The `SuppressHide` bracketing in `SettingsLauncher` disappears with the
separate window: there is no second window to lose focus to.

## 6 · Config schema and migration

New fields on `GeneralConfig`:

| Field | Default | Purpose |
|---|---|---|
| `SchemaVersion` | `0` | Migration marker; the current schema is `1` |
| `WindowWidth` / `WindowHeight` | `1000` / `640` | Legacy — retired by Round 2's task 4 (`docs/superpowers/plans/2026-08-01-win11-shell-round2.md`). Window size is now recomputed from content on every summon (`WindowHost.FitToContent`); these two fields are kept only so pre-Round-2 config files still deserialize, and nothing reads or writes them any more |
| `WindowLeft` / `WindowTop` | `null` | Restored position; `null` = no remembered position, centre instead. Nullable rather than a negative sentinel: monitors left of the primary have negative coordinates, so `< 0` cannot mean "unset" |
| `WindowMaximized` | `false` | Restored maximized state |
| `NavRailExpanded` | `false` | Added by Round 2's task 2. Whether the navigation rail is pinned open (200 px, icon+label) rather than collapsed (48 px, icon-only). Written directly through `ConfigService.SaveAsync` from `MainWindow.OnNavToggleClick` — shell chrome the rail owns, like `WindowMaximized`/`WindowLeft`/`WindowTop`, not a setting any 设置 card exposes, so it raises no `SettingsChanged` |
| `TaskbarPinPromptDismissed` | `false` | §4's 不再提示 |

`AutoClose` and `TopMost` change default from `true` to `false`.

Migration runs in `ConfigService.Normalize` (`ConfigService.cs:94`), which already canonicalizes
freshly loaded configs and is the right seam. It has two call sites — `LoadAsync` (line 58) and
`RestoreFromAsync` (line 219) — and both want the migration: restoring a 0.11-era backup should
produce app-window behaviour just as loading a 0.11-era config does.

```
if (config.General.SchemaVersion < 1)
{
    // 0.11 and earlier wrote AutoClose/TopMost = true for a flyout that hid on blur and floated
    // above everything. This build is a taskbar-resident app window, where both are wrong.
    config.General.AutoClose = false;
    config.General.TopMost = false;
    config.General.SchemaVersion = 1;
}
```

`SchemaVersion` must default to `0`, not `1`: `System.Text.Json` leaves an absent property at its
initializer value, so a field defaulting to `1` would make a pre-0.12 config — which has no
`schemaVersion` at all — indistinguishable from a migrated one and skip the migration entirely.

Defaulting to `0` collapses "old config" and "brand-new config" into one case, which is safe because
the migration is a no-op on a fresh config: `AutoClose` and `TopMost` already default to `false`
there, so it only stamps the version. Once `Normalize` has written `1`, a user who deliberately
re-enables either setting keeps that choice across restarts.

## 7 · Single-instance handshake

`Program.MainWindowTitle = "TanMenuWpf"` is currently protocol: `SignalFirstInstance` locates the
running instance with `FindWindowW(null, "TanMenuWpf")` (`Program.cs:96`) and posts
`WM_APP+1`, which `GlobalHotkeyService.WndProcHook` handles (`GlobalHotkeyService.cs:65`). The
caption is set on the window in `App.ShowLauncherWindow` (`App.axaml.cs:179`).

With the window now in the taskbar and Alt+Tab, that caption is user-visible and "TanMenuWpf" is the
wrong name to show. Renaming the caption to "TanMenu" while keeping `FindWindow` would be unsafe:
the data folder is itself called TanMenu, so an Explorer window could match first.

The handshake therefore stops depending on the caption:

- Both sides call `RegisterWindowMessageW("TanMenuWpf.ShowFirstInstance")`, which returns the same
  system-wide id for the same string.
- The second instance posts that id to `HWND_BROADCAST` (`0xFFFF`) instead of to a located HWND.
- `GlobalHotkeyService.WndProcHook` compares against the registered id. The hook is installed via
  `Win32Properties.AddWndProcHookCallback` on the main window (`GlobalHotkeyService.cs:51`), which is
  top-level and therefore receives broadcasts.
- `Window.Title` becomes "TanMenu". `Program.MainWindowTitle` is deleted.
- Best-effort semantics are unchanged: if the first instance's window does not exist yet, the message
  is dropped and the second process exits, exactly as documented today.

`SingleInstanceMutexName` ("TanMenuWpf"), the `RegistryAutoStartService` value name, the
`Software\TanMenuWpf` registry key and the `TanMenu.Wpf.exe` filename are **untouched** — they are
the shipped identity that Velopack updates, the MSIX Executable entry and existing taskbar pins all
reference.

## 8 · What does not change

- All of `TanMenu.Core`: `MenuService`, `ConfigService` (beyond the fields above), `IconProvider`,
  `LaunchService`, `SoundService`, `ShortcutResolver`.
- `LauncherViewModel`'s data model and filtering.
- Tray icon, tray menu, global hotkey registration, Velopack update flow, MSIX packaging, autostart.
- `Themes/Fluent2/Tokens.axaml` — the new surfaces consume the existing tokens rather than adding a
  parallel palette.

## 9 · Testing

- **Unit (`TanMenu.Core.Tests`)**: round-trip of the new `GeneralConfig` fields; the
  `SchemaVersion < 1` migration flipping `AutoClose`/`TopMost` on an old config and leaving a
  `SchemaVersion = 1` config alone, pinned on BOTH `Normalize` call sites (`LoadAsync` and
  `RestoreFromAsync`); `WindowPlacement.ChooseWorkArea` picking the monitor that contains the saved
  rectangle's centre and falling back to the primary when that monitor is gone;
  `ConfigService.SaveAsync` completing while the calling thread is blocked on it (the property 退出's
  flush depends on); existing `ColButtonCount` assertions must keep passing unchanged.
- **Manual, in the running app** — the paths this design disturbs: tray single-click toggle, tray
  显示, tray 选项 landing on the settings page, global hotkey toggle, second-instance activation
  (new handshake), 失焦自动隐藏 on and off, close-to-tray, size/position surviving a restart, window
  restored off a disconnected monitor being clamped back, and the Mica fallback with transparency
  disabled.

## 10 · Rejected alternatives

- **Custom-drawn title bar.** Avalonia 12 replaced 11's `ExtendClientAreaChromeHints` with a `Chrome`
  API this project has never exercised, and nothing in the design needs controls in the title bar.
  Rejected in favour of the system title bar, which is also what Task Manager ships.
- **Categories in the left rail.** Reconciles requirement 3 but abandons 2 — the rail holds 应用/设置,
  and categories stay horizontal in the content area.
- **Keeping the settings tabs and Ok/Cancel/Apply inside the page.** Cheapest, but a tab strip nested
  in a navigation page with dialog buttons on a page is the opposite of the Windows 11 convention
  requirement 4 asks for.
- **Reviving 每列按钮数 as a setting.** A resizable window makes items-per-column a function of
  height; a fixed count would fight the window.
