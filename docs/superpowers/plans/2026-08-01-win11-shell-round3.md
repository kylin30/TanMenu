# Windows 11 Shell — Round 3 (measured Task Manager parity)

> Third round, from the user's review of the Round-2 build on 2026-08-01. Nine items. The user
> supplied two Task Manager reference screenshots and said explicitly: **截图任务管理器来做** — match
> it by measurement, not by eye.

## Reference material — use it, do not guess

- User's Task Manager settings page: `C:\Users\Administrator\.claude\image-cache\f7ead68c-a134-48f2-8a0f-b49a1232942b\1.png`
- User's Task Manager process page (title bar + search): `C:\Users\Administrator\.claude\image-cache\f7ead68c-a134-48f2-8a0f-b49a1232942b\2.png`
- Live capture taken by the controller: `<scratchpad>/taskmgr-live.png`

**Better still: capture Task Manager yourself.** `Start-Process taskmgr`, then `PrintWindow` (flag `2`)
into a `System.Drawing.Bitmap`, and read element geometry through UI Automation. That is how the
numbers below were obtained, and it is how a claim about Task Manager should be supported.

### Controller measurements, live, at 1.5 scale (physical px; DIP = ÷1.5)

| Element | Physical | DIP |
|---|---|---|
| Window / client | 1942×1618 / 1920×1607 | — |
| Search box | x=613 y=14 **w=647 h=49** | x=409 y=9.3 **w=431 h=32.7** |
| Title text 任务管理器 | x=95 y=27 h=24 | x=63 y=18 h=16 |

So Task Manager's title band is ~48 DIP with a ~33 DIP search box inset ~9 DIP from the top — the
same 48 DIP this app already asks for. **Our strip is taller than that because its content forces it,
not because the hint is wrong.** Measure ours the same way and find what is pushing it.

## Global Constraints

Unchanged from rounds 1-2 and still binding:

- **Kill the app before building**: `Get-Process -Name TanMenu.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force`, or `MSB3027`.
- **Never change the machine's taskbar pin state.** Never trigger an update install or restart.
- **Restore any real config touched**, and be accurate about it in the report.
- Shipped identity frozen: `TanMenu.Wpf.exe`, the `TanMenuWpf` mutex, `Software\TanMenuWpf`, the `TanMenuWpf` Run-key value.
- PowerShell 7. Baseline **162 tests**. No new NuGet packages. English code and comments. Commit on `main`, no branch.
- NativeAOT: compiled bindings; `dotnet publish -c Release` at 0 warnings (needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH).
- Settings contract from round 1 stands: `ISettingsCard.ReloadFromConfig()`, `_loading` guards, all config writes through `SettingsWrite.CommitAsync`, OS-backed state refreshed on `OnAttachedToVisualTree`.
- The decorations override in `MainWindow.axaml` suppresses Avalonia's own drawn title/fullscreen button and is pinned to Avalonia 12.1.0 internals — do not disturb it casually.
- **No real keyboard or mouse input in this environment**, and `GetForegroundWindow()` returns 0. Use UI Automation, logs and `PrintWindow`; `CopyFromScreen` fails here.

---

### Task 1 — Title bar geometry and search-box focus (user items 2, 3)

**Item 3 — the strip is too tall and the caption buttons sit wrong.** Measure our title band and the
caption buttons the same way the table above was produced, compare against Task Manager, and make
ours match: a ~48 DIP band, the caption buttons hard against the top-right corner at their native
size, the search box ~33 DIP tall inset ~9 DIP from the top. Find what is inflating the band (padding,
a control's `MinHeight`, the icon, the strip's own `Margin`) rather than forcing a height.

**Item 2 — the focus style is wrong.** Windows 11's `TextBox` focus is **not** a full accent ring.
It keeps its 1 px resting border and gains a **2 px accent underline on the bottom edge only**; the
fill lightens slightly. Ours currently draws a heavy accent rectangle around the whole field.
Reproduce Windows 11's treatment for both the resting and focused states, taking the resting fill,
border and radius from the measured Task Manager box. Keep the magnifier glyph and placeholder.

Screenshot both states side by side against Task Manager's, in light and dark.

---

### Task 2 — Navigation rail parity, and the overflow menu moves into it (user items 4, 5)

**Item 5 — the rail does not look like Task Manager's.** Match it by measurement: rail width when
collapsed, icon size and centring, the selected item's treatment (Task Manager uses a left accent bar
plus a subtle rounded fill behind the icon), row height and spacing, and the hamburger's placement.
Note Task Manager pins its settings gear to the **bottom** of the rail, separated from the page list.

**Item 4 — move the `···` overflow's functions into the rail.** 打开主目录 / 刷新 / 关于 / 退出 leave
the title strip. Follow Task Manager's shape: page entries at the top, utility entries pinned to the
bottom. Decide whether each becomes its own rail row or whether they collapse into one bottom entry,
and justify the choice against the reference — but the `···` button itself goes away.

The rail must stay collapsible with the same persisted `NavRailExpanded` state, and every entry needs
a tooltip when collapsed plus an `AutomationProperties.Name`.

> **Superseded by Round 4, item 1** (`docs/superpowers/specs/2026-08-02-win11-shell-round4-design.md`
> §1). "The rail must stay collapsible" no longer holds: the hamburger toggle, the 200 px expanded
> width and the six row labels are deleted and `NavRailExpanded` is retained only as a legacy
> deserialize-only field. The tooltip + `AutomationProperties.Name` requirement survives and is
> strengthened — they are now the only label any rail row has.

---

### Task 3 — Settings page parity (user items 7, 8, 9)

**Item 7 — cards must fill the available width**, not sit at a fixed one. Task Manager's cards
stretch to the content area's width with a consistent gutter. Remove the fixed cap and let them
stretch; keep them readable at very wide window sizes (a generous max is acceptable if the reference
shows one — measure before deciding).

**Item 9 — the section headings are the wrong size.** Measure Task Manager's 外观 / 常规 / 高级
headings against its page title and its card titles, and match the relationship. Ours are currently
too close to the page title.

**Item 8 — 固定到任务栏 must be disabled when the app is already pinned**, not accent-styled. A
completed action is not a call to action. Keep the status line explaining why it is disabled.

---

### Task 4 — Items per column becomes a setting; button size goes away (user items 1, 6)

**Item 1 — the number of shortcuts per column is user-configurable, default 10.** `GeneralConfig`
already carries `ColButtonCount` (default 10), deliberately kept vestigial since the Fluent 2 rebuild
and asserted by existing tests — **revive it as the real thing** rather than adding a second field.
Each category's items fill a column up to that many, then wrap to the next column.

This interacts with Round 2's window fit, which currently sizes the window so the tallest category
renders in a single column. With a column cap the height rule becomes: tall enough for
`min(tallest category count, ColButtonCount)` items. Re-check that the window no longer grows to
near-full-screen for a 30-item category, and that no horizontal scrollbar appears at the default.

Add the setting to the 外观 card group, following the established card contract.

**Item 6 — remove the 按钮大小 setting; tiles are always the small size.** Delete the card row and the
size-class plumbing (`IsSmallButtons`/`IsMediumButtons`/`IsLargeButtons`, the `size-*` classes and the
Medium/Large tile widths). Keep `GeneralConfig.ButtonSize` as a retained legacy field so old configs
still deserialize — the same treatment `ColButtonCount` had and `WindowWidth`/`WindowHeight` have —
and say so in a comment. Existing tests that assert the field round-trips must keep passing.
