# Windows 11 Shell — Round 2 (Task Manager fidelity)

> **DEPRECATED — Round 7, Task 10.** Describes the Windows 11 shell, shipped in 0.11.0-0.12.0 and
> replaced by the Aero conversion (`../specs/2026-08-04-aero-conversion-design.md`). Kept as the
> record of that work; its measurements were true of the code at the time and are not true of the code
> now. Nothing below was edited to add this header. `src/TanMenu.Wpf/Themes/Fluent2/` is named
> `Themes/Shell/` from Task 10 on, and the Windows 11 measurements several of these sections rest on
> are collected in `../archive/2026-08-04-windows11-measurement-record.md`.

> Follow-up to `2026-07-31-win11-shell-redesign.md`, from the user's review of the shipped build on
> 2026-08-01. Seven items, all about matching Task Manager more closely — plus one real defect the
> first round's review wrongly dismissed as a screenshot artifact.

**Goal:** Bring the shell the rest of the way to Windows 11 / Task Manager conventions: search in the
title bar, a collapsible icon rail, a solid content surface, real button-styled tiles, and a window
that fits its content.

> **Two of this plan's decisions were overturned by Round 4**
> (`docs/superpowers/specs/2026-08-02-win11-shell-round4-design.md`); the text below is kept as the
> record of what Round 2 decided, not as a description of the code.
>
> - **The rail is no longer collapsible** (Round 4, item 1). The hamburger toggle, the 200 px expanded
>   width, the width transition and the six row labels are deleted; the rail is permanently 48 DIP
>   icon-only and `GeneralConfig.NavRailExpanded` is a legacy deserialize-only field.
> - **The window position is no longer remembered** (Round 4, item 4), so item 1's "size is recomputed
>   from content on every start; position is still remembered" and its use of
>   `WindowPlacement.ChooseWorkArea` are both void — that function and `Resolve` are deleted, and the
>   window is placed horizontally centred on the PRIMARY screen's work area with its bottom edge 8 DIP
>   above the work-area bottom on every launch, every summon and every re-fit.
>   `GeneralConfig.WindowLeft`/`WindowTop` joined `WindowWidth`/`WindowHeight` as legacy fields.

## Global Constraints

Inherited from round 1 and still binding:

- **Kill the app before building.** `Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force`, or the build fails with `MSB3027`.
- **Never change the machine's taskbar pin state.** Never trigger a real update install or restart.
- **Restore any real config, registry value or folder touched**, and say what was touched.
- Shipped identity frozen: `TanMenu.Wpf.exe`, the `TanMenuWpf` mutex, `Software\TanMenuWpf`, the `TanMenuWpf` Run-key value.
- PowerShell 7. Baseline **144 tests passing**. No new NuGet packages. English code and comments. Commit on `main`, no branch.
- NativeAOT: compiled bindings are on; every `DataTemplate` needs `x:DataType`. `dotnet publish -c Release` must stay at 0 warnings (needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH for the ILC link step).
- The settings contract from round 1 stands: `ISettingsCard.ReloadFromConfig()`, `_loading` guards, all config writes through `SettingsWrite.CommitAsync`, OS-backed state refreshed on `OnAttachedToVisualTree`.
- **This environment delivers no real keyboard or mouse input** and `GetForegroundWindow()` returns 0. Verify with logs, UI Automation and `PrintWindow` (flag `2`); `CopyFromScreen` fails here.

## Avalonia 12.1 facts for the title bar, probed 2026-08-01

Do not rediscover these, and do not substitute the Avalonia 11 names:

| API | Status |
|---|---|
| `Window.ExtendClientAreaToDecorationsHint` | Exists. Extends the client area under the caption |
| `Window.ExtendClientAreaTitleBarHeightHint` | Exists. `-1` = OS default height; any positive value forces that height |
| `Avalonia.Controls.Chrome.WindowDecorationProperties.ElementRole` | Attached property of type `Avalonia.Input.WindowDecorationsElementRole`, for non-client hit-testing. **Documented as having effect only while `ExtendClientAreaToDecorationsHint` is true** |
| `WindowDecorationsElementRole.TitleBar` | "The element acts as a titlebar drag area. Clicking and dragging initiates a platform window move" |
| `WindowDecorationsElementRole.User` | "An interactive element set by user code that should receive input even when overlapping chrome areas" |
| `ExtendClientAreaChromeHints` | **Does not exist** in 12.1 — Avalonia 11 name |

So: extend the client area, mark the title strip `TitleBar`, mark the search box and any buttons in
it `User`.

### CORRECTION, established by decompiling Avalonia 12.1 during Task 1's review

Two lines above were wrong when this plan was written. The corrected facts:

- **The caption buttons are NOT the system's — Avalonia draws its own.** Setting
  `ExtendClientAreaToDecorationsHint` makes `WindowImpl.NeedsManagedDecorations` true, so
  `TopLevelHost` instantiates a `WindowDrawnDecorations` and fills it from the Fluent theme's default
  `ControlTemplate` for that class. That template draws the window title (a `TextBlock` inside
  `PART_TitleTextPanel`) and Close/Minimize/Maximize **plus a `PART_FullScreenButton`** — which is why
  the shipped Task 1 build showed a doubled title and a fourth, non-Windows-11 caption button. An app
  that draws its own title strip **must override that template/theme** to suppress the default title
  and fullscreen button. Contrary to the original note, supplying a `WindowDrawnDecorations` override
  is therefore REQUIRED here, not forbidden.
  *Verified by decompiling `Avalonia.Themes.Fluent` 12.1.0: the template contains no icon element at
  all — only the title `TextBlock` and the caption buttons. An earlier revision of this note claimed
  it drew an icon; that was inferred from a screenshot, not from the template, and was wrong.*
- **`WindowDecorationMargin` cannot measure the caption buttons.** The Win32 backend hardcodes
  `Left`/`Right`/`Bottom` to `0.0` (`WindowImpl.UpdateExtendMargins`), and when
  `ExtendClientAreaTitleBarHeightHint` is a positive literal, `Top` reduces algebraically to that same
  literal — so it echoes back the app's own XAML value and carries no information about the real
  buttons or the real DPI. Any reservation derived from it is a hardcoded constant wearing a
  disguise. Reserve space from the overridden decorations template's own measured buttons instead, or
  state plainly in a comment that the width is a constant and why that is acceptable.

---

### Task 1 — Search and overflow move into the title bar

**Items 3.** Delete the app page's header row entirely (the search field and the `···` overflow
button and the `✕` hide button). Rebuild them in the window's title strip, Task Manager style: app
icon and name at the left, the search box centred-left in the strip, the overflow menu to its right,
and clear space at the right for the system caption buttons.

- `MainWindow` sets `ExtendClientAreaToDecorationsHint="True"` and an explicit
  `ExtendClientAreaTitleBarHeightHint` (48 is the Windows 11 title bar height at 100%).
- The strip gets `WindowDecorationProperties.ElementRole="TitleBar"` so dragging it moves the window;
  the search box and the overflow button get `="User"` so they take input instead.
- **Reserve the caption-button width on the right.** It is not a constant — read it from
  `Window.WindowDecorationMargin` (exists in 12.1) rather than hardcoding, or the buttons will sit on
  top of the content at a different DPI or with a different system setting.
- The search box keeps every current behaviour: placeholder, two-way `Filter` binding, Enter launches
  the first match, Escape clears then hides, Down enters the tile grid, focus ring on the field.
  `AppPage` still owns the keyboard model, so the box now lives in the shell while the view model that
  backs it lives in the page — decide how they talk and say why in the report. Do NOT duplicate the
  view model.
- The overflow menu keeps 打开主目录 / 刷新 / 关于 / 退出.
- The Mica backdrop must still be visible behind the strip.

**Verify:** the strip drags the window; the search box takes focus and filters; Escape/Down still
work; the caption buttons are not overlapped at 100% and at 150% scaling; nothing of the old header
remains on the app page.

---

### Task 2 — Collapsible navigation rail, collapsed by default

**Item 2.** Task Manager's rail: a hamburger toggle at the top, icon-only when collapsed, icon+label
when expanded, the selection indicator visible in both states.

- Collapsed is the **default** and is what a fresh config gets.
- Persist the expanded/collapsed choice in `GeneralConfig` (new field, defaulting to collapsed) and
  write it through `SettingsWrite.CommitAsync`'s contract if it is changed from settings, or directly
  through `ConfigService` + `SettingsChanged` if it is only toggled from the rail — pick one and say why.
- Collapsed width should match Windows 11's rail (48 is the shell convention); expanded stays 200.
- Tooltips show the label when collapsed, and the icon-only rows keep their `AutomationProperties.Name`.
- The 3 px accent selection bar stays visible when collapsed.
- Transition between the two widths should animate, as the shell does.

**Verify:** default-collapsed on a fresh config; toggle expands and collapses; the choice survives a
restart; selection stays visible collapsed; tooltips appear.

---

### Task 3 — Visual pass: content surface, tiles, buttons

**Items 5, 6, 7.**

- **6 — the app content area is a solid surface, not Mica.** Task Manager's shape: Mica shows through
  the rail and the title strip; the content pane is a solid layer. Use the existing Fluent 2 tokens so
  light and dark both work — white-equivalent in light, the dark surface token in dark. Do not
  hardcode `#FFFFFF`. The settings page gets the same treatment.
- **7 — shortcuts render as buttons.** They are currently borderless icon+text rows. Give them the
  Fluent 2 standard-button surface: subtle fill, hairline border, 4 px radius, real hover/pressed/
  focus states. Keep the existing size classes (Small/Medium/Large) working and keep the tile width
  fixed so the column layout from round 1 still measures correctly.
- **5 — the buttons on the settings cards are wrong.** 打开 / 更改… / 重置为默认 / 清理缓存 / 备份… /
  恢复… currently render as default Avalonia Fluent buttons. Give them the Fluent 2 standard button:
  correct height (32 at 100%), padding, radius, border, and the accent style for the one default
  action per card if any. Check them in both light and dark.

**Verify:** screenshots of the app page and the settings page, in light AND dark, showing the solid
content surface, button-styled tiles with a visible hover state, and corrected card buttons.

---

### Task 4 — Window fits its content; settings bottom is reachable

**Items 1 and 4.**

**4 is a confirmed defect, measured by the controller on 2026-08-01:** with the settings page scrolled
to `VerticalScrollPercent = 100`, the 检查更新 button's bottom edge sits **47 px below the window's
bottom edge** — that content can never be reached. Round 1's review dismissed the same symptom as a
`PrintWindow` capture artifact; that was wrong. Find the real cause (a padding or margin outside the
`ScrollViewer`'s measured extent is the usual culprit) and fix it so the last card is fully reachable
at every window height, including at `MinHeight`.

**1 — the window fits its content at every launch.** Decided with the user: **size is recomputed from
content on every start; position is still remembered.**

- On startup, measure the natural content width/height and size the window to it, clamped to the work
  area of the target screen (the saved-position screen, using round 1's `WindowPlacement.ChooseWorkArea`).
- Scrollbars must appear only when the content genuinely exceeds the work area.
- `GeneralConfig.WindowWidth`/`WindowHeight` stop being restored. Decide whether to keep writing them
  (harmless, and a user resize is still meaningful within a session) or retire them — say which, and
  keep old configs loading either way.
- The user may still resize during a session; that must keep working.
- Interaction with `WindowMaximized`: a maximized window should still restore maximized, and
  fit-to-content applies to the restored (normal) size.

**Verify:** launch with 5 categories and confirm no scrollbar and no wasted space; add a category
externally and relaunch to confirm the window grows; shrink the work area case by writing a huge
content set and confirm it clamps and scrolls; confirm the settings page's last card is fully
reachable at `MinHeight`; confirm a maximized start still restores maximized.
