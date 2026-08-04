# TanMenu — Windows 11 shell, Round 4 design

> **DEPRECATED — Round 7, Task 10.** Describes the Windows 11 shell, shipped in 0.11.0–0.12.0 and
> replaced by the Aero conversion (`2026-08-04-aero-conversion-design.md`). Kept as the record of that
> work; its measurements were true of the code at the time and are not true of the code now. Nothing
> below was edited to add this header. Item 1's five deletions are the one part Round 7 partly
> reversed: Task 8 restored the rail's labels and the StackPanels that paired them with icons, and
> deliberately did **not** restore the second width, the transition or the animation clip. Item 5's
> white-band investigation and both of item 5's 8 DIP corners are archived in
> `../archive/2026-08-04-windows11-measurement-record.md`.

> **Status: IMPLEMENTED, and PARTLY SUPERSEDED BY ROUND 5. The shipped build is the authority, not
> this document.** All five items below shipped in `e1546ec..908967e` (the document itself was first
> tracked mid-round by `0b327f3` and corrected twice afterwards, by `961a78b` and `d493e18`). Where an
> implementation measured something this design had predicted or inherited, the section carries a
> **"Superseded during implementation"** box naming the commit — §3.2, §5(e) and §4 have one. The
> sentence this header used to carry, "DESIGN ONLY. Nothing here is implemented.", was true only while
> the document was being written and contradicted its own body from `0b327f3` onwards.
>
> **Round 5 reversed item 2 outright.** The footer order this document specifies — 退出 first, 关于 in
> the rail — is **not** what ships. See §2's own superseded boxes, and
> `docs/superpowers/specs/2026-08-03-settings-row-spec-design.md` (§C.1, §C.2) for what replaced it.
> Do not execute §2 or its acceptance criteria as written.
>
> **Scope.** The five items the user asked for after reviewing the Round 3 build (0.12.0):
>
> 1. 左侧导航去掉展开功能 — the navigation rail loses its expand capability.
> 2. 导航下面5个按钮顺序调整优化 — the rail's five footer entries get a better order.
> 3. 按钮宽度自适应内容，不要精简文本 — tiles size to their content; no text is shortened.
> 4. 启动位置调整为水平正中，靠下方任务栏 — the startup position becomes horizontally centred, low
>    against the bottom taskbar.
> 5. 任务栏和左侧导航栏应该是背景色相同，右侧内容页是纯白底 — the title strip and the rail share one
>    background; the content page is pure white. (任务栏 in the user's text means the 标题栏 / title
>    strip — settled by the controller.)
>
> **Evidence base.** Every claim about current behaviour carries a `file:line`. Every number that comes
> from a measurement names the measurement. The five reconnaissance reports behind this design are:
> `r4-rail.md`, `r4-tiles.md`, `r4-placement.md`, `r4-theme.md`, `r4-crosscut.md` (scratchpad
> `C:\Users\ADMINI~1\AppData\Local\Temp\claude\D--0-Work-TanMenuWpf\f7ead68c-…\scratchpad\`). References
> below of the form *(r4-tiles §4)* point into them. All five agree with each other on every fact this
> design depends on; the two places where their emphasis differs are ruled explicitly in §7.3.

---

## 0. Ground truth this design is built on

### 0.1 The machine everything was measured on

| Quantity | Value | Source |
|---|---|---|
| Primary monitor | `0,0 7680x2160` physical | `EnumDisplayMonitors`+`GetMonitorInfoW`, r4-placement §5.1 |
| Primary **work area** | `0,0 7680x2088` physical = **`0,0 5120x1392` DIP** | same; the app logs the identical figure (`work area 0,0 5120x1392 at scale 1.5`) |
| Scale | 144 DPI = **1.5** on all three monitors | `GetDpiForMonitor`, r4-placement §5.1 |
| Taskbar | bottom edge, **not** auto-hidden, **not** always-on-top; `Shell_TrayWnd` top edge == work-area bottom (2088) | `SHAppBarMessage(ABM_GETSTATE)=0`, r4-placement §5.2 |
| Live window (pid 33084) | raw `2267,1173–4875,2081` = 2608x908 px; client 2586x897 px = **1724x598 DIP**; DWM frame 2590x899 px | `GetWindowRect`/`GetClientRect`/`DwmGetWindowAttribute`, r4-placement §5.3 |
| Non-client insets | `AdjustWindowRectEx` on the live style `0x16CF0000`/ex `0x240100` = **L11 T45 R11 B11**; DWM inset from raw = **9 px** on L/R/B, 0 on top | r4-placement §5.3 |

Derived constants used throughout, at 150 %: the window's **client** height is 11 px (7.33 DIP) less
than its **raw** height (the extended client area swallows the 45 px caption, leaving only the 11 px
bottom); the **visible** (DWM) rectangle is 9 px (6 DIP) inside the raw rectangle on left, right and
bottom. `Window.Position` is the **raw frame origin**; `Window.Width`/`Height` are the **client** size
(r4-placement §5.3, verified against `config.json`'s `windowLeft 1511.33333 × 1.5 = 2267`).

### 0.2 The current window, in the app's own words

```
11:39:59.682 [INF] Window placement applied: 200,100 1000x688 DIP on work area 0,0 5120x1392 at scale 1.5
11:40:00.214 [INF] Window fit to content: 1724x568 DIP (page 1676x471.3333333333333 + rail 48 + title strip 48)
11:41:49.303 [INF] Window fit to content: 1876x568 DIP (page 1676x471.33 + rail 200 + title strip 48)
```
`…\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Data\cache\logs\log-20260802.txt` (r4-crosscut §0).
Three facts fall out and are load-bearing below:

* the page measures **1676 x 471.33 DIP**; the window is 1724x568 because the rail adds 48, the title
  strip adds 48 and `MinHeight="568"` (`MainWindow.axaml:7`) floors the height;
* the rail toggle really is a live re-fit trigger worth exactly **152 DIP**;
* the fit **requested** 568 DIP of client height but the window is **598 DIP** of client / 605.33 DIP
  outer. Thirty DIP of the window is dead space that nothing asked for. §4 removes it.

### 0.3 The reference

`C:\Users\Administrator\.claude\image-cache\f7ead68c-…\3.png` is a 1:1 native-resolution crop of Task
Manager's DWM frame bounds — 1924x1609, byte-for-byte the `DWMWA_EXTENDED_FRAME_BOUNDS` of the live
process (r4-theme §3.0). It is **not** scaled, so its pixels are physical and DIP = px / 1.5. Its
sibling `1.png` is the same window on its **Settings** page and is measured in §5.

What the reference actually shows, measured rather than eyeballed (r4-theme §3.4, r4-rail §3.2):

| | Task Manager | TanMenu today |
|---|---|---|
| Rail width | frame x 2..73 = 72 px = **48 DIP** | identical, 48 DIP |
| Pane left edge | frame x = **74** | frame x = **74** |
| Title bar vs rail at the same x | **Δ ≤ 1 level** | **Δ = 14,14,6** |
| Content pane fill | **#FFFFFF** | #F3F3F3 |
| Pane border | **2 px (1 DIP)** on **top and left only**, `#0F000000` over the backdrop | none |
| Pane top-left corner | **12 px arc = 8.00 DIP**; top-right and bottom-left measured square | square |
| Pane inset | **0 on all four edges** — flush, not a floating card | flush |
| Rail footer | **one** entry (the Settings gear), pinned to the bottom | five entries |
| Rail commands | **none** — commands live in the page's own command bar | four |

---

## 1. Item 1 — the rail is permanently icon-only

### 1.1 Decision

**Delete the expand path entirely. Keep the persisted field as a legacy deserialize-only property.**

`src/TanMenu.Wpf/MainWindow.axaml`

| Line(s) | Change |
|---|---|
| `:287` | delete `ClipToBounds="True"` on `NavRailPane` — it existed only so the width animation had something to clip |
| `:288-292` | delete the whole `<Border.Transitions>` block (`DoubleTransition Property="Width" Duration="0:0:0.167"`) — a transition on a property that can never change |
| `:283-286` | keep `<Border Grid.Column="0" x:Name="NavRailPane" Width="48" Background="Transparent">`. **48 stays the single source of truth for the rail width** and must stay in sync with the constant introduced in §1.1's code-behind change |
| `:273` | keep `ColumnDefinitions="Auto,*"`; rewrite the comment at `:269-272`, which currently justifies `Auto` by the animation |
| `:302` | `RowDefinitions="Auto,*,Auto"` → **`RowDefinitions="*,Auto"`**; `Classes="tm-nav-rail"` stays, the `collapsed` class is not written anywhere any more |
| `:312-320` | **delete `NavToggleButton`** and its `E700` glyph |
| `:332` | `NavRail` moves from `Grid.Row="1"` to `Grid.Row="0"`; `Padding="0,12"` unchanged |
| `:385` | the footer `StackPanel` moves from `Grid.Row="2"` to `Grid.Row="1"` |
| `:356, :392, :401, :410, :419, :444` | **delete all six `tm-nav-label` TextBlocks**, and with them the six one-child `<StackPanel Orientation="Horizontal" Spacing="12">` wrappers. Each row's content becomes exactly `<TextBlock Classes="tm-icon" Width="16" Text="&#xNNNN;" HorizontalAlignment="Center" VerticalAlignment="Center" />` |

`src/TanMenu.Wpf/MainWindow.axaml.cs`

| Line(s) | Change |
|---|---|
| `:83`, `:86`, `:360` | delete the `_events.SettingsChanged += ApplyNavExpandedState` subscription, its initial call and its unsubscribe |
| `:663-674` | delete `ApplyNavExpandedState` |
| `:687-709` | delete `OnNavToggleClick` |
| `:619-624` | delete the six `Nav*Label.Text = …` writes (their targets no longer exist) |
| `:631-642` | **keep every one of the twelve `AutomationProperties.Name` / `ToolTip.SetTip` writes untouched.** They are now the only naming of every row. Rewrite the comment at `:626-630`, which explains them by reference to the `.collapsed` rule that is being deleted |
| `:327-328` | `RefitToContent` stops reading the config. New body: `(_host as WindowHost)?.FitToContent(_appPage, RailWidthDip + PaneBorderDip);` with `private const double RailWidthDip = 48;` and `private const double PaneBorderDip = 1;` declared beside it, each commented with the XAML line it mirrors (`MainWindow.axaml:285` and the pane border added in §5) |

`src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml`

| Line(s) | Change |
|---|---|
| `:579-581` | delete `Grid.tm-nav-rail.collapsed TextBlock.tm-nav-label { IsVisible False }` — no such TextBlock exists any more |
| `:582-587` | delete both `…collapsed … StackPanel { HorizontalAlignment Center }` rules — no such StackPanel exists any more; the centring is now a local `HorizontalAlignment="Center"` on each icon |
| `:568-578` | rewrite the block comment; it describes a state machine that no longer exists |

`src/TanMenu.Core/Services/AppLanguage.cs:190-196` — delete the `NavExpand` / `NavCollapse` entries and
their comment. Nothing else reads them (`MainWindow.axaml.cs:671` was their only consumer) and no test
enumerates language keys (r4-rail §5.5).

`src/TanMenu.Core/Models/AppConfig.cs:86-100` — **`NavRailExpanded` stays as a property.** Rewrite its
remarks in the `ButtonSize` shape (`AppConfig.cs:172-179`): *"Legacy setting retained only so old config
files deserialize cleanly … Nothing reads or writes this field any more; the rail is permanently
icon-only."* Do **not** coerce it in `ConfigService.Normalize` and do **not** bump
`AppConfig.CurrentSchemaVersion` (`AppConfig.cs:25`) — none of the three existing retirements
(`ShowInTaskbar:106-109`, `WindowWidth`/`WindowHeight:61-71`, `ButtonSize:172-179`) did either, and
`ConfigService.Normalize` (`ConfigService.cs:94-123`) touches no such field.

**Vertical consequence, stated as a number.** The toggle occupies `Margin="8,8,0,4"` + `TmIconButton`'s
`Height=32` (`Launcher.axaml:125-126`) = **44 DIP** of the deleted `Auto` row. 应用's glyph centre
therefore moves from a measured **y = 185.5 px** to **y = 120 px** (client top 72 px + 12 DIP ListBox
padding + 2 DIP item margin + 18 DIP half-row = 32 DIP = 48 px). **No spacer replaces it.** The 44 DIP
existed to hold the control being deleted; preserving it would be a constant disguised as a
placeholder, which is exactly what `MainWindow.axaml:269-272` already documents as a smell for the
`Auto` column. This diverges from the reference on one point — Task Manager still *has* a hamburger
(bars measured at y = 105.5 / 112.5 / 119.5, r4-rail §3.2) — but the user's item 1 is that divergence.

### 1.2 Why

* The rail width feeds exactly one piece of functionality: `WindowHost.FitToContent(page, railWidth)`
  → `WindowPlacement.ResolveContentFit(…, chromeWidth, …)`, which is literally
  `contentWidth + chromeWidth` (`WindowPlacement.cs:142-155`). Nothing else keys off it — the title
  strip and its centred search box live in `Grid.Row="0"` of the outer `RowDefinitions="48,*"` grid and
  span the whole window, and the field width is `ClientSize.Width * 0.34` clamped to [200,600]
  (`MainWindow.axaml:155-160, :172-174`; `MainWindow.axaml.cs:737-739, :746-753`). So removing the
  expanded state removes one term from one formula and nothing else.
* Tooltips do not have to be *added*: `ApplyRailText` already sets `AutomationProperties.Name` and
  `ToolTip.Tip` for all six rows **unconditionally**, on every `SettingsChanged`
  (`MainWindow.axaml.cs:631-642`). Today an expanded rail therefore shows a label *and* a duplicate
  tooltip. What changes is their status, not their existence — which is why they are called out above as
  must-not-touch.
* The labels are deleted rather than left hidden because a permanently `IsVisible=False` TextBlock, kept
  alive by six imperative `.Text` writes per settings change, is dead weight that a future reader would
  have to re-derive as dead. With them gone the three `.collapsed` selectors have no targets and go too,
  and the class stops existing rather than becoming a class that is always on.
* Retiring rather than deleting `NavRailExpanded` keeps `ConfigServiceTests.cs:591-615` and `:617-636`
  and `AppConfigTests.cs:53-62` green, and follows the project's own three-precedent pattern. It also
  matters in practice: **both** config shapes exist on this machine right now — the running instance's
  portable `Data\config.json` carries `"navRailExpanded": false`, and
  `D:\0.Documents\Documents\TanMenu\config.json` (written 2026-08-01 06:05) has no such key at all
  (r4-crosscut §4).

### 1.3 Blast radius

**Files touched:** `MainWindow.axaml`, `MainWindow.axaml.cs`, `Themes/Fluent2/Launcher.axaml`,
`TanMenu.Core/Services/AppLanguage.cs`, `TanMenu.Core/Models/AppConfig.cs`.

**Re-fit trigger list:** trigger 3 (rail expand/collapse, `MainWindow.axaml.cs:699`) **dies**. Five
remain — see §6.1. Removing it is safe precisely because the 152 DIP width change it compensated for
can no longer happen.

**Documented invariants that become false and must be rewritten** (the repo's code-wins rule):
`MainWindow.axaml.cs:233-234`; `WindowHost.cs:571-573` (the `FitToContent` summary lists "the navigation
rail expanding or collapsing" among the triggers); `WindowHost.cs:136` (`_fitBasisStale`'s "a rail
toggle, a 每列个数 change"); `WindowHost.cs:624-627` (`railWidth`'s `<param>`, "48 collapsed / 200
expanded"); `WindowPlacement.cs:133-134` (`chromeWidth`'s `<param>`, same phrase) and `:163-166`
(`IsSamePlacement`'s remarks list the rail toggle); `MainWindow.axaml:18-20, :34-36, :269-282,
:294-311, :322-331, :361-384, :423-434`; `Launcher.axaml:541-542, :558-587`; `MainWindow.axaml.cs:20-31,
:79-83, :324-328, :626-630, :645-674, :676-709`; `AppConfig.cs:86-99`.

**Tests:** none change. `WindowPlacementTests.ResolveContentFit_ExpandingTheRail_WidensTheWindowByTheRailDelta`
(`:265-283`) still compiles and passes because it calls the pure function with 48 and 200 directly — but
its header comment (`:258-262`) then describes an impossible scenario, and §4 rewrites that test anyway.

**Docs:** `docs/superpowers/specs/2026-07-31-win11-shell-redesign-design.md:44-45` and `:348`;
`docs/superpowers/plans/2026-08-01-win11-shell-round3.md:75` ("The rail must stay collapsible") must be
marked superseded by this round.

**Not affected:** tray, single-instance, hotkey, `ShellNavigator`, `SettingsLauncher` —
`grep NavRailExpanded` reaches none of them (r4-crosscut §7). `_inShowPage` (`MainWindow.axaml.cs:63,
:491-499, :582-593`) is index-agnostic and untouched. Tab order is unaffected: `NavToggleButton` was
already `Focusable="False"` (`MainWindow.axaml:317`).

### 1.4 Verification (no keyboard or mouse available)

1. `grep -rn "NavToggleButton\|OnNavToggleClick\|ApplyNavExpandedState\|NavExpand\|NavCollapse\|tm-nav-label\|collapsed" src/` returns **nothing**.
2. `grep -rn "NavRailExpanded" src/` returns exactly one hit: the retired property in `AppConfig.cs`.
3. Launch the app; `PrintWindow(hwnd, hdc, 2)` into a `GetWindowRect`-sized bitmap (flags 0/1 return all
   black; 2 and 3 are identical — r4-theme §7). Scan row y = 400: the rail band must span image
   x = 11..82 (72 px = 48 DIP) exactly as today.
4. Scan the icon glyph bands over x = 20..75. There must be **no glyph** between the title strip
   (y = 0..71) and 应用, and 应用's glyph centre must be **y = 120 ± 3 px** (today: 185.5).
5. Walk the UI Automation tree of the window; every one of the six rail rows must expose a non-empty
   `Name` (应用 / 打开主目录 / 刷新 / 关于 / 退出 / 设置 in the configured language).
   *(Superseded by Round 5 (`871504b`): there are **five** rows and 关于 is not one of them — 应用 /
   打开主目录 / 刷新 / 设置 / 退出. The "non-empty `Name` on every row" requirement itself still holds.)*
6. Write `"navRailExpanded": true` into `Data\config.json`, restart the process: the rail band must
   still measure 72 px and the log's fit line must still report the same chrome term as before. Then
   confirm the key is **not** rewritten by the app on the next save.
7. `dotnet test` — 183 cases, all green, no test file edited for this item.

---

## 2. Item 2 — the footer order

### 2.1 The decision, in one line

**Top to bottom: 退出 · ── separator ── · 关于 · 打开主目录 · 刷新 · 设置.** One separator, immediately
below 退出. 设置 stays bottom-most.

> **Superseded by Round 5 (`871504b`); the shipped build is the authority.** This order shipped in
> `1e0fb94` and was replaced one round later. The footer today is **打开主目录 · 刷新 · 设置 ──
> separator ── 退出**, five rows became four, and **关于 is not in the rail at all** — its row and the
> full-pane overlay it raised were deleted, and 关于 is now the last group on the 设置 page
> (`Views/Settings/AboutCard.axaml`). The user asked for both changes directly (退出移动到最下,
> 关于集成到设置页中); see `docs/superpowers/specs/2026-08-03-settings-row-spec-design.md` §C.1 and
> §C.2, and the ordering block in `MainWindow.axaml` above the footer `StackPanel`, which records why
> the separator travelled down with 退出.
>
> **§2.2's argument is not wrong, it was overruled by the user, and its mitigation survived.** (b)'s
> point — that 退出 must not sit directly above 设置 — is still honoured: the separator now fences 退出
> below 设置 instead of above it. Everything §2.2–§2.4 says about 关于 as "the natural buffer" and as
> "the only row that cannot be reached any other way" is dead: `LauncherViewModel.OpenAbout`,
> `ShowAbout`, `CloseAbout` and `OnAboutClick` no longer exist, so the "one correctness fix" §2.3
> ships with the reorder (navigate to 应用 before `OpenAbout()`) went with them.

### 2.2 Why this order and not another

Four inputs, weighed:

**(a) What the reference actually does.** Task Manager's rail footer holds exactly **one** entry, the
Settings gear, at the very bottom — measured at y = 1569.5, 1036 px (690 DIP) below the last page row
and 39.5 px (26 DIP) above the window's bottom edge (r4-rail §3.2). Its four commands
(运行新任务 / 结束任务 / 效率模式 / ⋯) are **not** in the rail at all; they sit in the page's own command
bar at the top right, including the destructive one. So the reference gives one unambiguous instruction
and one divergence we are choosing to keep:
*instruction* — the gear is bottom-most; *divergence* — commands in the rail at all. The instruction is
cheap to honour and 设置 already satisfies it today. Moving the commands into the page's command bar is
out of scope for "reorder the five" and would be a larger redesign than the user asked for.

**(b) Misclick risk.** 退出 fires immediately with **no confirmation** — `IShellCommands.cs:15-23`'s own
comment says "there is no dialog and nothing to wait for". Today it sits 40 DIP above 设置 and 40 DIP
below 关于, styled identically to both, in a flat run with no grouping signal at all (measured pitch
60 px = 40 DIP, r4-rail §3.1). 设置 is the single most-reached footer entry (it is also on the tray menu,
`App.axaml.cs:247`, and on the 应用 empty state, `AppPage.axaml:227`). A terminal action directly above
the most-clicked row, at a fixed screen coordinate the user aims at repeatedly, is the worst adjacency
available. Any acceptable order must break it.

**(c) Frequency and redundancy** (r4-rail §6): 设置 has two other entry points; 刷新 has one
(`AppPage.axaml.cs:133`); 退出 has one (the tray, `App.axaml.cs:246`); 打开主目录 has a sibling action
inside 设置 → 文件夹 (`FoldersCard.axaml.cs:189`); **关于 has none** — `LauncherViewModel.OpenAbout` has
exactly one caller, `MainWindow.axaml.cs:840`. So 关于 is simultaneously the least-used row and the only
one that cannot be reached any other way: it must stay, and it is the natural buffer.

**(d) Convention.** Menus put 退出 last, and this app's own tray menu is 显示 / 设置 / 退出 with 退出 last
(`TrayService.cs:23-25, :39-43, :108-110`). That is a genuine counter-signal, and it is outweighed: a
tray menu is a transient popup that appears under the cursor and is dismissed after one click, whereas
the rail is persistent chrome at a fixed coordinate. The tray's own ordering also puts 设置 immediately
above 退出 — the very adjacency (b) rules out. Convention loses here on the specific ground that makes
the two contexts different.

With 设置 pinned to the bottom by (a) and 退出 forced away from it by (b), 退出's only remaining
low-risk slot is the **top** of the footer group: its upper neighbour is 290 DIP of empty rail
(measured: 435 px between 应用 and the first footer row, r4-rail §3.1), and its lower neighbour is the
least-used row (c). The two commands then sit closest to 设置, where the pointer already goes.

**Separator: yes, exactly one, below 退出.** Justification, not taste: the current flat run of five is
an accident. It is inherited verbatim from the deleted ⋯ overflow `MenuFlyout`, which encoded **three**
groups — `[打开主目录, 刷新] | [关于] | [退出]` — and the Round 3 move into the rail dropped both
`<Separator/>` elements (`git show d107349^:src/TanMenu.Wpf/MainWindow.axaml` lines 244-252, r4-rail §6).
Re-introducing exactly one restores the only boundary that carries safety meaning (terminal vs
reversible) without re-litigating the other two. It also gives `StrokeDividerBrush` its first consumer —
the token is defined in both variants (`Tokens.axaml:59` `#0F000000`, `:95` `#15FFFFFF`) and referenced
nowhere in the codebase today (r4-theme §2).

### 2.3 Decision, concretely

`src/TanMenu.Wpf/MainWindow.axaml`, inside the footer `StackPanel` (`:385-448`) — reorder the **literal
XAML rows**, keeping every `x:Name` and every `Click` handler exactly as they are:

```
<StackPanel Grid.Row="1">
  NavExitItem       (E7E8, OnExitClick)          ← moved from position 4 to 1
  <Border Classes="tm-nav-separator" />          ← NEW
  NavAboutItem      (E946, OnAboutClick)         ← was 3
  NavOpenRootItem   (E8B7, OnOpenRootClick)      ← was 1
  NavRefreshItem    (E72C, OnRefreshClick)       ← was 2
  <ListBox x:Name="NavFooterRail" …>             ← unchanged, still last
    NavSettingsItem (E713)
  </ListBox>
</StackPanel>
```

New style in `Themes/Fluent2/Launcher.axaml`, beside the other rail rules:

```xml
<Style Selector="Border.tm-nav-separator">
  <Setter Property="Height" Value="1" />
  <Setter Property="Margin" Value="4,4" />
  <Setter Property="Background" Value="{DynamicResource StrokeDividerBrush}" />
</Style>
```

Numbers: horizontal `4` matches the rows' own `Margin="4,2"` (`Launcher.axaml:545`), so the line spans
exactly the **40 DIP item column** that Round 3 measured against Task Manager's own rail
(`MainWindow.axaml:322-331`: 48 pane − 2×4 item margin). Vertical `4` puts 4 DIP of clear space on each
side of the hairline, which with the rows' own 2 DIP margins leaves **6 DIP between the line and each
neighbouring row box**, against the **4 DIP** that separates two ordinary rows — a visible boundary
without a gap that reads as a missing item. The separator block is 9 DIP tall, so the footer grows from
204 DIP to **213 DIP**. That cannot overflow: the rail's whole content then needs 265 DIP against a body
that is at least 519 DIP tall (568 client floor − 48 title strip − 1 pane border), and the rail is never
an input to the window fit at all (`MainWindow.axaml.cs:328` measures `_appPage`, not the rail).

**One correctness fix ships with the reorder.** `OnAboutClick` (`MainWindow.axaml.cs:840`) sets
`ShowAbout = true` on the `LauncherViewModel` (`LauncherViewModel.cs:415`), but the overlay bound to it
is a child of `AppPage` (`AppPage.axaml:256-265`), and while 设置 is showing `PageHost.Content` is the
settings page, so `AppPage` is detached (`MainWindow.axaml.cs:513-515`). Read from the code, pressing
关于 from 设置 therefore displays nothing and leaves the flag set — the overlay surfaces later, when the
user returns to 应用. *(Code-read, **not** live-verified: this environment cannot inject the input to
press the row — r4-rail §6.)* Fix: have `OnAboutClick` navigate to 应用 first, then call `OpenAbout()`,
using the same `ShellNavigator.Navigate(ShellPage.App)` the rail already uses at
`MainWindow.axaml.cs:601-602`. The fix is correct whether or not the defect reproduces — an overlay that
lives on 应用 should be shown on 应用 — and shipping a **reordered** row that silently no-ops on one of
the app's two pages would defeat the point of the item.

**Explicit non-goal:** no confirmation dialog is added to 退出. `IShellCommands.cs:15-23` documents the
no-dialog contract deliberately, and changing it is a behavioural design of its own. The separator plus
the four-row distance from 设置 is the mitigation this round chooses.

### 2.4 Blast radius

**Files touched:** `MainWindow.axaml` (reorder + one new `Border`), `Themes/Fluent2/Launcher.axaml` (one
new style), `MainWindow.axaml.cs` (one line in `OnAboutClick`).

**Why the reorder is inert.** Nothing downstream is index-based: `OnNavSelectionChanged`
(`MainWindow.axaml.cs:565-603`) branches on `ReferenceEquals(rail, NavFooterRail)` (`:598`, `:602`), and
`ApplyRailText` (`:609-643`) addresses every row by `x:Name`. `_inShowPage` (`:63`, `:491-499`,
`:582-593`) only suppresses the `SelectionChanged` that `ShowPageCore`'s own `SelectedIndex` writes raise
(`:511-512`) and is index-agnostic (r4-crosscut §2).

**What would NOT be inert, and is therefore forbidden.** Building the footer from a collection instead of
literal rows would introduce the app's fourth `DataTemplate` and its first over a type that does not
exist yet — compiled bindings are mandatory (`AvaloniaUseCompiledBindingsByDefault=true` +
`PublishAot=true` in `TanMenu.Wpf.csproj`) and need a concrete `x:DataType` per template
(`AppPage.axaml:141-144`). It would also force `ApplyRailText`'s twelve `x:Name`-keyed writes into
bindings on a VM needing `INotifyPropertyChanged`. **Reorder the literal rows.**

**设置 stays a `ListBoxItem` in `NavFooterRail`.** The four commands are `Button`s *because a `Button`
has no `:selected` pseudoclass*, which makes "a command row can never look selected" true by
construction (`MainWindow.axaml:369-377`, `Launcher.axaml:243-254`). Converting 设置 to a `Button` for
uniformity would break `ShowPageCore`'s `NavFooterRail.SelectedIndex` write
(`MainWindow.axaml.cs:512`) and `OnNavSelectionChanged`'s `otherRail` logic (`:598-599`). So "five
buttons" in the user's text means five *rows*, four of which are Buttons.

**`NavFooterRail`'s `Padding="0,0,0,4"`** (`MainWindow.axaml:435-440`) encodes "I am the last child of
the footer". This ordering keeps it last, so the assumption survives untouched — a reason on its own to
prefer it over any order that moves 设置 up.

**Tests:** none change. Nothing under `tests/` references the footer.

### 2.5 Verification

> **Superseded by Round 5 (`871504b`); do not run these as written.** Every criterion below asserts the
> order this round shipped and Round 5 replaced. Steps 1, 3 and 4 assert a 关于 row that no longer
> exists and a 退出 that is no longer first, so all three fail against the current build **by design**.
> The equivalents that hold today: the footer's five `Nav*` rows are 应用 · 打开主目录 · 刷新 · 设置 ·
> 退出, with 退出 bottom-most and the hairline immediately above it; the pitch exception (49 DIP rather
> than 40) is now on 设置→退出, not 退出→关于. Step 2's separator geometry is unchanged and still holds —
> `Border.tm-nav-separator` was moved, not restyled. Step 5 holds (the footer is still untested prose;
> nothing under `tests/` references it). See
> `docs/superpowers/specs/2026-08-03-settings-row-spec-design.md` §C.1's own verification for the
> criteria that were actually run.

1. `PrintWindow` capture; detect glyph bands over x = 20..75 in the footer region. Top to bottom the
   centres must be: 退出 (`E7E8`, power), then a 1 DIP rule, then 关于 (`E946`), 打开主目录 (`E8B7`),
   刷新 (`E72C`), 设置 (`E713`), with 设置 last and its glyph centre 6 DIP (9 px) above the pane's bottom
   edge (2 DIP item margin + 4 DIP ListBox padding, plus half the row).
2. The separator must appear as a horizontal run of `StrokeDividerBrush` over the backdrop spanning
   image x = client+6 .. client+65 (4 DIP inset each side of the 48 DIP pane, at 1.5 px/DIP), 1 DIP tall
   (2 physical rows after layout rounding at 150 %).
3. UI Automation: enumerate the footer's children in tree order and assert the `Name` sequence matches
   退出 / 关于 / 打开主目录 / 刷新 / 设置.
4. Row pitch: 40 DIP (60 px) between every adjacent pair **except** 退出→关于, which must be 49 DIP
   (73–74 px).
5. `dotnet test` green with no test edited.

---

## 3. Item 3 — tiles size to their content

### 3.1 Decision

**Delete one Setter and one attribute.**

| File:line | Change |
|---|---|
| `Themes/Fluent2/Launcher.axaml:44` | **delete** `<Setter Property="Width" Value="168" />` |
| `Views/AppPage.axaml:179` | **delete** `TextTrimming="CharacterEllipsis"` |

Nothing else. In particular:

* **Do not** add `HorizontalAlignment="Left"` to the tile `Button`. Leaving it off is what makes the
  result **per-column uniform** rather than per-tile ragged: `ItemsControl.CreateContainerForItemOverride`
  produces a `ContentPresenter` whose own `HorizontalAlignment` and `HorizontalContentAlignment` both
  default to `Stretch`, and `Layoutable.HorizontalAlignmentProperty` defaults to `Stretch` — all three
  verified by reflection and IL against the shipped `Avalonia 12.1.0` assemblies (r4-tiles §3). So each
  tile stretches to its column's width, and a vertical `StackPanel`'s width is its widest child.
* **Do not** add a `MinWidth` or a `MaxWidth` to the tile. See §3.2.
* **Keep** `TextWrapping="NoWrap"` (`AppPage.axaml:178`). It is load-bearing twice over: it guarantees
  the label's desired width is the full single-line advance, and it keeps every row's height identical,
  which is what `FindAdjacentTile`'s `sameRow` test keys off (`AppPage.axaml.cs:370`, `Bounds.Height / 2`).
* **Keep the sizing on the element-local `TmTileButton` ControlTheme's territory** — i.e. do not move any
  tile geometry into a `Button.tm-tile` class style or into the DataTemplate. r4-crosscut §2 is explicit:
  a ControlTheme applied as `Theme="{StaticResource …}"` is a `FrameType.Theme` frame that survives a
  logical-tree detach, whereas class-scoped styles are `FrameType.Style` frames that do not.

**The resulting arithmetic.** A column is `48 + max(label advance in that column) + 4` DIP, where 48 is
the tile's horizontal chrome (2 border + 16 padding + 20 icon box + 10 icon margin —
`Launcher.axaml:40, :43, :414-415`, `AppPage.axaml:174`) and 4 is the tile's right margin
(`AppPage.axaml:163`). The page is `32 + Σ columns + 24 × (groups − 1)` (`SurfacePadding` 16 ×2,
`Tokens.axaml:130`; group `Spacing="24"`, `AppPage.axaml:105`; columns abut with no spacing,
`AppPage.axaml:148`).

> **Superseded by Round 6, item 4 — see
> `docs/superpowers/specs/2026-08-04-round6-surfaces-design.md` §4.4 and §4.6. The shipped code is the
> authority.** The tile arithmetic above is untouched; the *page* line has two terms it no longer has.
> Every category is now wrapped in a `Border Classes="tm-group-panel"`, and the inter-category
> `Spacing` went **24 → 12** because the panels supply that separation themselves. The page is
> `32 + Σ columns + N × panel chrome + 12 × (groups − 1)`, where the panel chrome is **22.6667** per
> axis on this machine — `12 + 8` of padding plus two 1 DIP strokes that each lay out as 2 physical px
> = 1.3333 DIP at 150 %, so re-derive it with the authored 1 at another scale. Measured on the shipped
> build: the page went `1643.3333` → `1708.6667` wide, `+65.3333 = 5 × 22.6667 − 4 × 12`. The line
> reference is dead too — the categories `ItemsPanel` is not at `AppPage.axaml:105` any more; grep
> `Spacing=` inside the `ItemsPanelTemplate` rather than trusting the number.

### 3.2 The cost, quantified — and the bound

Measured on the user's real data (74 items, 5 groups, `每列个数 = 10`, `rootFolder = D:\0.Documents\Desktop`)
with `FormattedText.WidthIncludingTrailingWhitespace` at em 14 for the face the app resolves
(`Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI` — `AppFonts.cs:32`, config `fontFamily=""`),
validated against `testbuild.png` ink to within ~1.4 DIP (r4-tiles §4):

| Model | page DIP | **window DIP** | vs today |
|---|---|---|---|
| today (fixed 168) | 1676.0 | **1724.0** | — |
| **per-column (chosen)** | 1627.2 | **≈1675 ± 10** | **−49** |
| per-tile ragged | 1627.2 | ≈1675 ± 10 | −49 (identical) |
| one global width (203.08 everywhere) | 1991.7 | ≈2040 ± 10 | +316 |
| per-column with a 168 MinWidth floor | 1762.5 | ≈1810 | +86 |

Per-column widths that produce it: 常用工具 118.0 · 工具 142.4 / 175.3 / **203.1** / 167.0 / 142.9 ·
开发 195.9 · 临时文件 184.2 · 游戏 134.4. The single widest label is `TeleportUltraPortable.exe` at
155.08 DIP of text → a 203.08 DIP tile (+35 vs today).

> **Superseded during implementation (commit `beff39e`); the shipped build is the authority.**
> The chosen row's `≈1675 ± 10` and its `−49` are both wrong. The app's own fit line reads
> `1713.8333,816 1692.3333x568 DIP (page 1643.3333x471.3333 + chrome 49x49)`: the page is
> **1643.33 DIP** and the window **1692.33 DIP**, a narrowing of **31.67 DIP**, not 49 — 17.33 DIP
> outside the ±10 band the table claimed.
>
> **The formula is not what missed; the input is.** Feeding the *measured* column widths through
> §3.1's own arithmetic — `32 + Σ tiles + 4 × 9 + 24 × 4` — gives `32 + 1478.67 + 36 + 96 = 1642.67`
> against the logged 1643.33, one physical pixel apart. Every column came out **wider** than the
> `FormattedText` model predicted (measured tile widths from the shipped build; the widest label per
> column re-derived by re-running §3.2's own harness, extended to chunk each group into columns of
> `每列个数 = 10`):
>
> | # | Group | Widest label in the column | Predicted tile | Measured tile | Δ DIP | Δ px @150 % |
> |---|---|---|---|---|---|---|
> | 1 | 常用工具 | `任务管理器` | 118.00 | 118.67 | +0.67 | 1.0 |
> | 2 | 工具 | `CooDesker-x64` | 142.42 | 143.33 | +0.91 | 1.4 |
> | 3 | 工具 | `FontViewOK_x64.exe` | 175.34 | 176.67 | +1.33 | 2.0 |
> | 4 | 工具 | `TeleportUltraPortable.exe` | 203.08 | 204.00 | +0.92 | 1.4 |
> | 5 | 工具 | `Win11右键菜单切换` | 167.00 | **172.67** | **+5.67** | 8.5 |
> | 6 | 工具 | `重启进BIOS.bat` | 142.88 | 143.33 | +0.45 | 0.7 |
> | 7 | 开发 | `Navicat Premium Lite 17` | 195.88 | **199.33** | **+3.45** | 5.2 |
> | 8 | 临时文件 | `SignalRClientTest.html` | 184.24 | 185.33 | +1.09 | 1.6 |
> | 9 | 游戏 | `Battle.net国服` | 134.37 | 135.33 | +0.96 | 1.4 |
>
> Seven of the nine land within 1.33 DIP = **2 physical px**, which is what layout rounding at 150 %
> costs and is not a modelling error at all. The two that miss — 5.67 and 3.45 DIP — are the real
> defect, and it is **not** simply a CJK effect: `Win11右键菜单切换` is mixed Latin + CJK, but
> `Navicat Premium Lite 17` is pure Latin. The harness is WPF's `FormattedText` over
> `PresentationCore` (`Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI` at em 14, culture
> zh-CN); the app shapes that same nominal family list through Avalonia's own text stack, and the
> two disagree by up to 5.67 DIP on one label. **A design that needs a label advance to ±2 DIP has to
> measure it in the framework that will render it, not in a neighbouring one.**
>
> What does hold: `TeleportUltraPortable.exe` is the single widest label (155.08 DIP of text →
> 203.08 predicted, 204.00 measured), the narrowing is real, and the direction of every one of the
> nine errors is the same. The paragraph below keeps its conclusion for the same reason — but its
> `10 → 1675 vs 1724` row is really `1692.33 vs 1724`, and the rest of its `[3,30]` sweep came out of
> the same harness, so those rows carry the same understatement and were not re-measured.

**Content-sizing makes this user's window narrower, not wider** — six of the nine columns are set by
labels shorter than today's ~120 DIP budget, and those savings outweigh the three that grow. The
narrowing holds at every `每列个数` in [3,30] for this data set (r4-tiles §4: 3 → 4194 vs 4648;
10 → 1675 vs 1724; 30 → 1203 vs 1208). It does **not** hold as a general law — a folder full of long
names would widen the window — which is why the controller's "if that makes the window wider, that is
the accepted cost" still governs.

**The bound: none is added, and that is a decision, not an omission.**

* A **`MaxWidth`** is ruled out on its face: it reintroduces trimming for exactly the labels the user is
  complaining about. It cannot be part of a design whose requirement is "不要精简文本".
* A **`MinWidth="168"`** floor is ruled out on the numbers: it converts a 49 DIP narrowing into an 86 DIP
  widening while buying nothing the user asked for. A 118 DIP tile is as tall, as clickable and as
  legible as a 168 DIP one; it is simply not padded.
* The **bound that already exists** is the right one: `WindowPlacement`'s size clamp caps the window at
  the work area (`WindowPlacement.cs:45`), and `ScrollViewer x:Name="Body"` with
  `HorizontalScrollBarVisibility="Auto"` (`AppPage.axaml:91`) makes anything past the clamp reachable.
  That is a real, tested bound on the only quantity that can hurt the user (a window wider than the
  screen), applied at the only place that can see the screen.

### 3.3 Why per-column and not per-tile ragged

They produce the **same** window width — a column is as wide as its widest tile either way — so the
choice is purely visual and navigational, and per-column wins both:

* **Arrow keys.** `FindAdjacentTile` scores `Down`/`Up` as `dy*1000 + |dx|` (`AppPage.axaml.cs:379-380`).
  All groups' rows share a `y`, so `dy` ties across every column and `|dx|` decides. Under per-column
  sizing `dx` is exactly 0 within a column, so **the scoring is bit-for-bit what it is today**. Under
  ragged sizing `dx` becomes non-zero inside a column, and three of this user's nine columns land inside
  the worst-case ambiguity bound (工具 c2: 48.2 vs ≥43.5; 开发 c1: 54.1 vs ≥47.8; 临时文件 c1: 54.1 vs
  ≥42.0). A simulation over all 74 tiles found **0** actual mis-navigations today — but only because of
  which tile happens to sit above which (r4-tiles §6). Per-column removes the hazard by construction.
* **Hit targets.** Ragged widths make the click target's width vary row by row inside one column.
* **Edit size.** Per-column is the *absence* of an attribute; ragged requires adding one.

### 3.4 Blast radius

**Files touched:** `Themes/Fluent2/Launcher.axaml` (delete one Setter, rewrite three comments),
`Views/AppPage.axaml` (delete one attribute), plus comment rewrites listed below.

**Fit-to-content arithmetic.** The `desired.Width` term in `WindowHost.FitToContent`
(`WindowHost.cs:683-684`, unbounded `Measure` on both axes) stops being `32 + 172·N + 24·(G−1)` and
becomes `32 + Σ(48 + maxLabel_col + 4) + 24·(G−1)`. It remains finite, deterministic and **independent of
the probe budget**: nothing on the page wraps horizontally (`TextWrapping="NoWrap"`; columns and groups
are horizontal `StackPanel`s), and `ScrollContentPresenter.MeasureOverride` measures its child with
`double.PositiveInfinity` on the scrollable axis and returns `Child.DesiredSize.Constrain(availableSize)`
— with an infinite available size that is the child's full desired width (decompiled, r4-tiles §5). The
height-axis rationale for the data-layer column cap (`MenuLayout.cs:11-16`, `WindowHost.cs:603-616`)
**has no width analogue** and is unaffected.

**The re-fit skip test now compares fractional widths.** `WindowHost.cs:709-723` compares against the
previous fit's request with `WindowPlacement.IsSamePlacement`'s 0.5 DIP tolerance
(`WindowPlacement.cs:189`). Today the widths are exact integers (1676/1724); content widths are
fractional and `UseLayoutRounding` defaults to true, so each column can snap to a physical pixel (±0.67
DIP at 150 %). The values stay deterministic for a given label set and font, so the skip still fires —
but the tolerance stops being a formality. **Not measured** (r4-tiles §5 could not build).

**Comments that become false and must be rewritten** — this is the largest doc-sync item in the round:

| Location | What it asserts that stops being true |
|---|---|
| `Launcher.axaml:32-35` | "Width is a Setter here (168 DIP) … removed the only reason a tile's width ever varied" |
| `Launcher.axaml:407-412` | "TmTileButton's own Width Setter above carries the tile width and FontSize" |
| `Launcher.axaml:589-595` | the stated reason `StackPanel.tm-group` has **no `MaxWidth`**: "The tile has one fixed width now (168 DIP, TmTileButton), so a group's honest desired width is exactly columns × column width — bounded". **The conclusion survives, the premise does not**: rewrite it as "a column is as wide as its own widest label, so a group's desired width is a finite sum of finite column widths — still bounded, still what the scroller needs" |
| `MainWindow.axaml.cs:270-283` | the detach-guard's rationale, "the tiles do NOT lose their 168 DIP width, because that width is a `FrameType.Theme` frame that survives detach; what IS lost is app-level Style frames such as `Image.tm-tile-icon`'s 20x20 box". **This inverts.** The tile's width now *derives from* that lost 20x20 box plus a `TextBlock` whose font is inherited through the logical tree, so a detached measurement would corrupt **width as well as height**. The guard at `:321-322` still prevents it; the paragraph must say the guard became *more* load-bearing, not less |
| `MainWindow.axaml.cs:294-299` | retires an older "192 DIP" observation on the same now-false premise |
| `MainWindow.axaml.cs:339`, `LauncherViewModel.cs:667` | "every tile is now a fixed 168 DIP regardless of any setting" |
| `AppConfig.cs:172-179` | the retired `ButtonSize` doc, "tiles are always the one size that used to be called 小 (Small, 168 DIP)" |
| `docs/…/reports/2026-08-02-win11-shell-round3-findings.md:122`, `docs/…/specs/2026-07-30-fluent2-redesign-design.md:162`, `docs/…/specs/2026-07-31-win11-shell-redesign-design.md:72` | the same 168 |

**Rejected hardening, recorded so it is not re-proposed.** Moving the 20x20 icon box from
`Image.tm-tile-icon` (`Launcher.axaml:414-415`) onto local attributes on the `Image`
(`AppPage.axaml:171-176`) would make part of the tile's geometry survive a detach. It was rejected as
**incomplete**: the label's font family is inherited from the window (`ApplyWindowFont`) and inheritance
breaks on detach anyway, so the measurement would still be corrupt and the guard would still be the
real protection. Two attributes for a half-fix is worse than one honest comment.

**New behaviour to expect, not to file as a bug.** Filtering deliberately does **not** re-fit the window
(`LauncherViewModel.cs:155-161`); `ContentMetricsChanged` is raised only from `ReloadAsync`
(`LauncherViewModel.cs:658`). `ApplyFilter` → `RecomputeColumns` (`MenuModels.cs:209, :226-232`) re-chunks
on every keystroke, so each chunk's widest label — and hence each column's width — changes as the user
types, inside a window that does not resize. Today only the *number* of columns changes. **Accept it.**
Re-fitting per keystroke would resize the window under the user's hands; computing widths from unfiltered
items would push a width into the data layer, which is precisely the leak `MenuLayoutTests` and
`MenuModelsTests` exist to catch (r4-crosscut §3).

**Tests:** none change. Nothing under `tests/` matches `168`, `TmTileButton` or `tm-tile`
(`Fluent2TokenTests.cs:25` reads `Launcher.axaml` but asserts only colour-token hygiene). **If any
`MenuLayoutTests` or `MenuModelsTests` case breaks, item 3 has leaked into the data layer** — that is the
signal to stop and re-read this section.

**AOT:** neutral. Deleting a Setter changes no binding. Do **not** collapse the two nested
`ItemsControl`s into one panel over a bare list — `AppPage.axaml:141-144` records why they exist.

### 3.5 Verification

1. Launch; read the app's own fit line. It must report `page ≈1627x471.33` and a window of **≈1676 DIP**
   (1627.2 + 48 rail + 1 pane border from §5), not 1676/1724.
2. `PrintWindow` capture; for each of the nine columns, scan the right edge of every tile in that column:
   **all tiles in a column must share one right edge**, and **different columns must have different
   widths** (expected tile widths, ±2 px: 118 / 142 / 175 / 203 / 167 / 143 / 196 / 184 / 134 DIP).

   > **Steps 1 and 2's numbers were superseded during implementation (commit `beff39e`); the shipped
   > build is the authority — see §3.2's box for the full table and the cause.** Step 1: the fit line
   > reads `page 1643.3333x471.3333`, window **1692.33**, not ≈1627/≈1676. Step 2: the *shape* of the
   > assertion held — every column does have exactly one tile width and the nine differ — but the
   > expected list is outside its own ±2 tolerance on **two of nine**: column 5 measured 172.67
   > against 167 and column 7 measured 199.33 against 196. The measured list is
   > **118.67 / 143.33 / 176.67 / 204 / 172.67 / 143.33 / 199.33 / 185.33 / 135.33 DIP**; the other
   > seven land within 1.7. Re-run this step against those figures, and note that ±2 is too tight for
   > a prediction made in a different text stack from the one that renders.
3. The seven labels that render trimmed today — `FontViewOK_x64.exe`, `Win11右键菜单切换`,
   `TeleportUltraPortable.exe`, `tinyMediaManager.exe`, `Navicat Premium Lite 17`,
   `SignalRClientTest.html`, `WIL编辑器(64位).exe` (r4-tiles §2) — must render with **no ellipsis**.
   Grep the capture for the "…" glyph is unreliable; instead assert each of those tiles' label ink
   extends to within 8 DIP of its tile's right padding edge and that the tile is wider than 168 DIP.
4. No horizontal scrollbar at the default `每列个数` — the `Body` ScrollViewer's own scrollbar must not
   render (scan the bottom 12 DIP of the pane for the scrollbar track).
5. Set `"colButtonCount": 3` in `Data\config.json` and restart: the fit line must report a much wider
   window (≈4194, vs 4648 today) and the window must clamp to the 5120 DIP work area with a horizontal
   scrollbar — proving the work-area clamp is still the bound.
6. `dotnet test` green, `MenuLayoutTests`/`MenuModelsTests` untouched.

---

## 4. Item 4 — startup position: horizontally centred, low against the taskbar

This is the item that actually collides with the rest of the system. The design has four parts: where
the rule lives, what "low" means numerically, what happens to the remembered position, and one
pre-existing platform defect that must be removed first or the rule cannot be correct.

### 4.1 Decision

**(a) The rule lives in Core, in the one function that resolves every placement.**

`src/TanMenu.Core/Services/WindowPlacement.cs` is restructured so the app has exactly one placement
rule and the module has exactly one resolver. `Resolve` and `ChooseWorkArea` are **deleted**;
`PlacementRect` and `IsSamePlacement` are unchanged; two functions replace the old pair:

```csharp
/// Size clamps, then horizontal centring, then bottom anchoring — in that order.
public static PlacementRect ResolveBottomCentred(
    double width, double height, PlacementRect workArea,
    double minWidth, double minHeight, double bottomGap)
{
    var w = Clamp(width,  Math.Min(minWidth,  workArea.Width),  workArea.Width);
    var h = Clamp(height, Math.Min(minHeight, workArea.Height), workArea.Height);
    var left = workArea.Left + (workArea.Width - w) / 2;
    var top  = Clamp(workArea.Bottom - bottomGap - h, workArea.Top, workArea.Bottom - h);
    return new PlacementRect(left, top, w, h);
}

public static PlacementRect ResolveContentFit(
    double contentWidth, double contentHeight, double chromeWidth, double chromeHeight,
    PlacementRect workArea, double minWidth, double minHeight, double bottomGap) =>
    ResolveBottomCentred(contentWidth + chromeWidth, contentHeight + chromeHeight,
                         workArea, minWidth, minHeight, bottomGap);
```

The size clamps are byte-for-byte the ones `Resolve` used (`WindowPlacement.cs:45-46`), including
`Clamp`'s inverted-range rule (`:195-196`, `max < min ? min : …`) that makes a work area smaller than the
minimum prefer fitting the screen. The `top` clamp degenerates correctly: when the content fills the work
area, `workArea.Bottom - h == workArea.Top` and the window top-aligns.

**(b) Both callers use it. The anchor is NOT restore-only.**

`src/TanMenu.Wpf/Services/WindowHost.cs`:

| Line(s) | Change |
|---|---|
| `:502-504` | `RestorePlacement` calls `WindowPlacement.ResolveBottomCentred(width, height, work, MinWindowWidthDip, MinWindowHeightDip, BottomGapDip)` — the saved `g.WindowLeft`/`g.WindowTop` arguments are gone |
| `:500` | `ResolveTargetScreen(SavedRect(...))` → `PrimaryScreen()`: `var s = _window?.Screens?.Primary ?? _window?.Screens?.All.FirstOrDefault(); return s is null ? (UnknownWorkArea, 1.0) : (WorkAreaOf(s), SafeScale(s));` |
| `:402-419` | delete `ResolveTargetScreen` (replaced by `PrimaryScreen`) |
| `:437-443` | delete `SavedRect` — nothing computes a saved rectangle any more |
| `:689-693` | `FitToContent` calls the new `ResolveContentFit(desired.Width, desired.Height, chromeWidth, ChromeHeightDip, work, MinWindowWidthDip, MinWindowHeightDip, BottomGapDip)`; `currentLeft`/`currentTop` are no longer read or passed |
| `:129-130` | replace `_lastFitWidthDip`/`_lastFitHeightDip` with a single `private PlacementRect? _lastFitPlacement;` |
| `:497-498`, `:709-723`, `:748-750` | read/compare/write that one field instead of the pair (see (e)) |
| `:35` | `TitleStripHeightDip = 48` stays; add `ChromeHeightDip = TitleStripHeightDip + 1` (the pane's own top border, §5), `MinWindowWidthDip = 760`, `MinWindowHeightDip = 568`, `BottomGapDip = 8` |
| `:199` | delete `window.PositionChanged += … SchedulePlacementSave()` — a move no longer changes anything persistable |
| `:200-208` | narrow the property-changed handler to `Window.WindowStateProperty` only; `Width`/`Height` no longer feed any persisted field either |
| `:800-805` | delete the `g.WindowLeft`/`g.WindowTop` writes from `SavePlacement`. It keeps writing `WindowMaximized` (`:793`) and keeps the Minimized early-return (`:789-790`) |

`src/TanMenu.Wpf/MainWindow.axaml:7` — **delete `MinHeight="568"`**, keep `MinWidth="760"`. See (d).

`src/TanMenu.Core/Models/AppConfig.cs:73-82` — retire `WindowLeft`/`WindowTop` **in place**: keep the
properties, rewrite the remarks in the `ButtonSize`/`WindowWidth` shape (`:61-71`, `:172-179`) stating
that the window is always placed centred-low on the primary and nothing reads or writes these any more.
No `Normalize` coercion, **no schema bump** (`ConfigService.cs:117-122` touches no window field, and a
bump would silently defuse `ConfigServiceTests.Load_MigratedConfig_KeepsDeliberateAutoCloseChoice`, whose
fixture interpolates `AppConfig.CurrentSchemaVersion` at `:456`).

**(c) "Low" = an 8 DIP gap, measured on the resolved placement rectangle.**

`BottomGapDip = 8`, matching the app's own shipped 0.11 flyout, which used
`topPx = work.Bottom - heightPx - offsetPx` with `offsetPx = GeneralConfig.PositionOffset × scale` and
`PositionOffset = 8` by default (`git show 921d7f7:src/TanMenu.Wpf/Services/WindowHost.cs`,
`921d7f7:src/TanMenu.Core/Models/AppConfig.cs:54` — r4-placement §5.7).

The **visible** result is not 8 DIP, and the design states it rather than pretending otherwise. The
placement rectangle's `Top` is the **raw frame origin** and its `Height` is the **client** height; the
raw rectangle is 11 px taller than the client at the bottom and the DWM-visible rectangle is 9 px inside
the raw one (§0.1). So at 150 % on this machine, with an 8 DIP (12 px) nominal gap:

```
rawTop      = round((1392 − 8 − 568) × 1.5) = round(816 × 1.5) = 1224 px
rawBottom   = 1224 + (568×1.5 + 11)          = 1224 + 863      = 2087 px   (1 px inside the work area)
visible bottom = 2087 − 9                     =                  2078 px   (10 px = 6.67 DIP of visible gap)
```

**6.67 DIP of visible clearance above the taskbar, and never an overlap.** Flush-on-the-visible-edge —
what Windows' own tray-overflow flyout and Edge's half-snap do (measured: both put the DWM bottom exactly
at 2088, r4-placement §5.6) — was rejected because those are popups whose DWM frame *is* their raw rect,
whereas this window carries a 9 px invisible resize border; reproducing "flush" for us means deliberately
overhanging the work area by ~6 DIP, and any error in the non-client estimate becomes a real taskbar
overlap. 8 DIP is the app's own precedent and is robust to the ±2 px uncertainty.

**(d) `MinHeight="568"` must go, or the anchor is wrong by 37.33 DIP.**

This is the pre-existing defect the anchor exposes. Avalonia's `Win32.WindowImpl` `WM_GETMINMAXINFO`
handler does, decompiled verbatim (r4-placement §5.4):

```csharp
if (_minSize.Height > 0.0)
    structure2.ptMinTrackSize.Y = (int)(_minSize.Height * RenderScaling
                                        + BorderThickness.Top + BorderThickness.Bottom);
```

`BorderThickness.Top + Bottom` is `AdjustWindowRectEx`'s T45 + B11 = **56 px** on this window's style,
so `ptMinTrackSize.Y = 568 × 1.5 + 56 = 908` physical — the exact observed outer height. The real
non-client vertical inset is only 11 px (the extended client area swallows the caption), so the client
floor is 897 px = **598 DIP** against a request of 568. *(This supersedes
`docs/…/reports/2026-08-02-win11-shell-round3-findings.md:36-44`, which offered
`GetSystemMetricsForDpi(SM_CYMINTRACK, 144) = 56` as an explicitly unverified inference. Both numbers are
56 on this machine; the decompiled code adds `BorderThickness`, not `SM_CYMINTRACK`.)*

Consequence for a bottom anchor computed from the **requested** height: `Top = 1392 − 568 = 824` DIP, but
Windows renders 605.33 DIP outer, so the raw bottom lands 56 px (37.33 DIP) **behind the taskbar** — and
`Resolve`'s own clamp cannot catch it because it clamps with the same wrong height (r4-placement §5.5).

The floor is a *function of* `MinHeight`, so raising `MinHeight` cannot escape it. Removing the property
does: with `_minSize.Height == 0` the handler's guard fails and Windows falls back to its own default
minimum track size (`SM_CYMINTRACK` = 56 px outer here), far below anything the app requests, so
**requested height == real height** and the anchor arithmetic is exact. The app's 568 DIP floor is not
lost — it moves to `MinWindowHeightDip`, passed into the same `minHeight` parameter the placement
functions already clamp with (`WindowHost.cs:504, :693`). `MinWidth="760"` is left alone: horizontally
the non-client inset is the full 22 px that `AdjustWindowRectEx` reports, so the client floor already
equals `MinWidth` exactly and there is nothing to correct.

Two side effects, both accepted: the user can now drag the window shorter than 568 DIP (that latches
`_userResized`, and since no size is persisted, the next process start comes back at the fitted size);
and the window becomes **30 DIP shorter** than today at the same content, removing dead space nobody
asked for (§0.2).

**(e) The re-fit skip basis becomes a rectangle.**

`FitToContent`'s skip test builds its comparison as `new PlacementRect(currentLeft, currentTop,
previousWidthDip, previousHeightDip)` (`WindowHost.cs:712`) — the window's **live** position with the
**previous request's** size, so today it only ever compares size. Once position is a function of size,
that mixed basis can report "same placement" for a rectangle whose position needs to move. Fix:
`_lastFitPlacement` stores the whole previously-**requested** rectangle and the skip compares
`IsSamePlacement(placement, _lastFitPlacement.Value)`. This preserves the rule
`WindowPlacement.cs:170-179` insists on — compare against what was asked for, never against the live
geometry — and is now *more* correct, because with (d) the platform no longer clamps the request at all.

`_lastFitPlacement` is still written **only after all three property assignments return**
(`WindowHost.cs:731-750`, whose reasoning about partial applies is unchanged), and `RestorePlacement`
still does **not** update it (`:506-539`). That NOTE's three-case proof is about SIZE and stays valid
verbatim; it needs one added paragraph for POSITION: *`RestorePlacement` now computes its position with
the same function and the same primary work area a subsequent fit will use, so when the size is unchanged
the two agree exactly and skipping is correct; when the size differs, the skip cannot fire because the
size differs.*

**(f) A window the user has DRAGGED is not re-anchored until its content changes or it is re-summoned.
This is a decision, not an oversight, and it is the one place where "one predictable place" is not
literally true.** It follows from (e): a pure move — a title-bar drag or an external `SetWindowPos` —
reaches `PositionChanged`, never `Resized` (decompiled `Win32.WindowImpl`: `WM_MOVE` raises
`PositionChanged`, only `WM_SIZE` raises `Resized`), so it sets neither `_userResized` nor
`_fitBasisStale`. A later re-fit whose content is unchanged therefore computes the same rectangle it
last *requested*, matches the basis, and skips — leaving the window where the user put it.
*Reproduced on the shipped build:* `SetWindowPos(200,200)` followed by 设置 → 应用 left the window at
`200,200`, with the skip line in the log. The alternative — comparing against the window's LIVE
position — would snap a dragged window back to the anchor on the next page switch, which is worse, and
the pre-Round-4 code did not do that either (its resolver derived the position by clamping the live
one, so the live position appeared on both sides of the comparison and cancelled out). The anchor is
re-applied unconditionally on the next summon, because `RestorePlacement` always runs before `Show`.
It is decided here rather than asked of the user, so it is stated here, in `WindowHost.FitToContent`'s
own skip comment, and in `README.md`'s window paragraph.

### 4.2 Why

* **The anchor cannot live in `RestorePlacement` alone.** `FitToContent` feeds the window's own current
  Left/Top back in (`WindowHost.cs:686-693`) and `Resolve` only ever clamps (`WindowPlacement.cs:48-53`)
  — it never re-centres. Measured against this machine's 5120x1392 DIP work area and the real logged
  sizes: a centred 1000-wide placeholder leaves the fitted 1724-wide window **362 DIP off centre**
  (2060 + 1724/2 = 2922 vs 2560, and no clamp fires because 3784 < 5120); a bottom-anchored 688-tall
  placeholder leaves the fitted 568-tall window **120 DIP above the taskbar** (r4-crosscut §5a). Putting
  the rule in the resolver both callers share is the only shape that survives the fit.
* **Which screen: the primary.** With the saved position retired, `SavedRect` returns null, so
  `ChooseWorkArea` would return the primary unconditionally anyway (`WindowPlacement.cs:104-105`) —
  deleting it just removes a branch nothing can reach. The primary is also what the app's own 0.11
  flyout used (`TryGetPrimaryDisplay`, r4-placement §5.7), and it is the monitor whose 下方任务栏 the
  user's sentence names. "The screen under the cursor" was rejected: nothing in `src/` reads the cursor
  (`grep -rn "ScreenFromPoint|GetCursorPos|MousePosition" src/` returns nothing), so it is new input
  plumbing for a behaviour the user did not ask for. *`FitToContent` keeps using `CurrentScreen()`
  (`WindowHost.cs:676`)* — so a window dragged to another monitor mid-session re-anchors to **that**
  monitor's bottom centre on the next re-fit, and returns to the primary on the next summon. That is
  coherent and needs no new code.
* **Why the saved position is retired rather than kept.** "Always start centred low" and "come back where
  you dragged it" are mutually exclusive, and `RestorePlacement` cannot tell a cold start from a summon —
  the same method runs from `App.axaml.cs:205` and from `WindowHost.cs:885`. Given the app is a
  tray/hotkey-summoned launcher that hides rather than closes, the user's sentence reads as "it should
  appear in one predictable place", which is what 0.11 shipped and deliberately did not persist. Keeping
  a field that is written but never read is the exact stale-data failure `AppConfig.cs:61-69` warns about
  and that this machine's live config demonstrates (`"windowWidth": 918.666666666667` from a pre-Round-2
  build, round-tripping forever). Retiring the properties **in place** keeps
  `ConfigServiceTests.cs:559-589` and `:638-660` green and needs no migration.
* **Why the cold-start jump gets better, not worse.** The process's first show has no
  `_lastFitPlacement`, so it uses the 1000x688 placeholder and is corrected ~330 ms later
  (11:39:59.682 → 11:40:00.214). Today that correction moves the **bottom** edge up by 120 DIP.
  Bottom-anchored, the anchored edge is the one that does not move: the window shrinks from 688 to 568
  by moving its **top** edge down. Horizontally it grows symmetrically about the centre. The visible
  correction is strictly smaller than today's.

### 4.3 Blast radius

**Files touched:** `TanMenu.Core/Services/WindowPlacement.cs` (restructured),
`TanMenu.Wpf/Services/WindowHost.cs`, `TanMenu.Wpf/MainWindow.axaml` (one attribute),
`TanMenu.Core/Models/AppConfig.cs` (comments only).

**Re-fit triggers:** all five surviving triggers (§6.1) now **re-position** the window as well as
re-sizing it. `IsSamePlacement`'s "a whole DIP on any edge is a different placement" theory rows
(`WindowPlacementTests.cs:315-321`, including `[101,200,…]` and `[100,201,…]`) guarantee those moves are
applied rather than skipped — which is what makes the anchor hold.

**Position lands before size on the wire.** `Window.Position`'s setter is `PlatformImpl?.Move(value)` —
immediate — while `Width`/`Height` reach the platform on the next layout pass via `ArrangeSetBounds`
(decompiled, r4-placement §3.3). So a `Top` computed from the new height is applied to a window that
still has the old one, for at most one frame. This is pre-existing (the same is true of today's
horizontal re-clamp) and is not worth restructuring the assignment order for; the cold-start path is
exempt because `ShowCore` pushes the size before `PlatformImpl.Show`.

**Hidden-window re-fit becomes harmless.** `WindowHost.Hide` navigates to 应用 *after* hiding
(`:862-876`), so hiding from 设置 posts a `RefitToContent` against a hidden window, whose only state
guard is `WindowState != Normal` (`:670`) — which a hidden window passes. Today that fit can write a
Position that `SavePlacement` then persists. After this change it writes the same anchor the next summon
would compute, and nothing persists it. Pre-existing oddity, defused.

**Tray 选项 path.** `SettingsLauncher.OpenCore` navigates to 设置 then shows the window
(`ISettingsLauncher.cs:59-63`), so `RefitToContent`'s guard (`MainWindow.axaml.cs:321-322`) returns
immediately and no fit runs — the window appears at whatever `RestorePlacement` applied, at the previous
fit's size. Because `RestorePlacement` now applies the same anchor, the later 设置→应用 switch
(`MainWindow.axaml.cs:542-543`) re-fits to the same place unless the size changed. Under a
restore-only anchor this sequence would have visibly jumped.

**Maximize:** unchanged. `SavePlacement` still writes `WindowMaximized` unconditionally (`:793`);
`RestorePlacement` still applies normal-state geometry first and `WindowState` last (`:540-545`), so the
OS restore rectangle is the anchored placement; `FitToContent` still refuses while maximized (`:670`) and
`MainWindow.OnPropertyChanged` still re-fits on the Maximized→Normal edge
(`MainWindow.axaml.cs:437-439`). The pre-existing hazard that dragging a maximized title bar down latches
`_userResized` (`WindowHost.cs:250-270`) is unchanged and would freeze the anchor too — documented, not
fixed here.

**Tests — this is where item 4's cost lands.** `tests/TanMenu.Core.Tests/Services/WindowPlacementTests.cs`,
26 cases today:

| Case (line) | Action |
|---|---|
| `Resolve_SavedRectInsideWorkArea_IsKept` (:11) | **delete** — the feature is gone |
| `Resolve_RectOffTheRightEdge_IsPulledBackInside` (:28) | **delete** |
| `Resolve_RealNegativePosition_OnLeftHandMonitor_IsKept` (:132) | **delete** |
| `ChooseWorkArea_*` (:96, :110, :122) | **delete** — the function is gone |
| `ResolveContentFit_ContentBetweenMinimumAndWorkArea_FitsExactly` (:168) | **retarget**: size assertion kept; position becomes the anchor, not "preserved" |
| `ResolveContentFit_UnsetPosition_CentresOnWorkArea` (:201) | **merge** into the new anchor test — "unset" no longer exists |
| `IsSamePlacement_ShrinkingFit_DoesNotMoveTheWindow` (:324) | **replace** with its inverse: a shrinking fit moves the window **down by exactly the height delta**, because the bottom edge is anchored |
| `ResolveContentFit_GrowthPastTheEdge_MovesByExactlyTheOverflow` (:345) | **replace**: growth can no longer overflow — assert the grown window is still centred and still gapped |
| `ResolveContentFit_ExpandingTheRail_WidensTheWindowByTheRailDelta` (:265) | **rewrite** as `ResolveContentFit_ChromeWidthIsAddedToTheContentWidth` with the single chrome value; the rail can no longer expand (item 1) |
| `Resolve_UnsetPosition_CentresOnWorkArea` (:19), `Resolve_RectLargerThanWorkArea_IsShrunkToFit` (:38), `Resolve_SizeBelowMinimum_IsRaisedToMinimum` (:49), `Resolve_WorkAreaSmallerThanMinimum_PrefersFittingTheScreen` (:58), `Resolve_WorkAreaWithOffset_PositionsRelativeToIt` (:71), `ResolveContentFit_SmallContent_IsRaisedToTheAppMinimum` (:153), `ResolveContentFit_ContentFarLargerThanWorkArea_Clamps…` (:186), `ResolveContentFit_TallNarrowCategory_HeightNotArtificiallyCapped` (:227), `ResolveContentFit_CategoryTallerThanWorkArea_…` (:243) | **retarget** to `ResolveBottomCentred`/the new `ResolveContentFit`: every size assertion survives verbatim; each gains the anchored position |
| `IsSamePlacement_*` (:291, :299, :315 ×4) | **unchanged** |

**New cases to add** (all pure arithmetic, no UI thread): the bottom gap is honoured exactly; a gap
larger than the work area's spare height clamps `Top` to `workArea.Top` rather than going negative;
horizontal centring on a negative-origin work area; content taller than the work area top-aligns and
fills. Also fix the misleading fixture comment at `WindowPlacementTests.cs:86-93`, which labels this
machine's **physical** three-monitor numbers as "Work areas are DIPs" (r4-placement §2) — the arithmetic
is fine, the comment is wrong.

`ConfigServiceTests` (`:559-589`, `:638-660`) and `AppConfigTests` (`:37-51`) all keep passing unchanged
because the properties remain; only their comments need a note that nothing reads them any more.

**Comments that become false:** `WindowPlacement.cs:11-12` (the class summary, "turns a saved … placement
into one that is guaranteed to be visible"), `:15-34` (`Resolve`'s whole `<param>`/`<remarks>` block),
`:58-94` (`ChooseWorkArea`'s remarks), `:122-141` (`ResolveContentFit`'s `currentLeft`/`currentTop`
params), `:170-179` (`IsSamePlacement`'s "cannot render below roughly 605 DIP outer" — no longer true
once `MinHeight` is gone, though the request-based basis stays right for the truncation reason at
`:180-187`); `WindowHost.cs:353-360` (`CurrentScreen`'s "do not use at restore time" — still true, and
now the only restore-side screen answer is `PrimaryScreen`), `:396-401`, `:421-436`, `:445-462`
(`SchedulePlacementSave`'s maximize-ordering rationale, still valid but now only for `WindowState`),
`:465-486` (`RestorePlacement`'s summary, "Apply the saved POSITION"), `:506-539` (extend, per (e)),
`:567-583` (`FitToContent`'s summary, "Position is preserved"), `:640-666` (the deliberate
move-on-growth decision), `:795-799` (`SavePlacement`'s "Position is still saved only while Normal");
`AppConfig.cs:73-82`; and `docs/…/reports/2026-08-02-win11-shell-round3-findings.md:36-44`, which this
design supersedes with the decompiled mechanism.

### 4.4 Verification

Everything below is doable with no keyboard or mouse. Note the lever that makes summon-path testing
possible: launching a **second** instance broadcasts `TanMenuWpf.ShowFirstInstance` via
`PostMessage(HWND_BROADCAST)` (`Program.cs:52-58, :91-113`), which summons the first instance — a
programmatic summon.

1. **Unit tests first.** `dotnet test` — the rewritten `WindowPlacementTests` are the primary proof of
   the arithmetic, and they run without a display.
2. **Cold start.** Delete nothing; launch. The log must show
   `Window placement applied: {L},{T} 1000x688 DIP on work area 0,0 5120x1392` with
   `L = (5120 − 1000)/2 = 2060` and `T = 1392 − 8 − 688 = 696`, then
   `Window fit to content: {W}x568 DIP` with `L' = (5120 − W)/2` and `T' = 1392 − 8 − 568 = 816`.
   Assert `T + H == 1384` on **both** lines.
3. **The real window.** `GetWindowRect`: raw bottom must be **≤ 2088** and **≥ 2070** (i.e. inside the
   work area, within one non-client height of it); raw horizontal centre must be within **±12 px** of
   3840. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`: visible bottom must be **2078 ± 3 px**.
   *(The ≤12 px horizontal tolerance is not slop: `PlacementRect` centres the **client** width using the
   **raw** origin, which leaves the visible window 11 px right of true centre at 150 % — a known,
   pre-existing property of the coordinate conflation, unchanged by this design.)*
4. **The height floor is gone.** `GetClientRect` must report **852 px** (568 DIP), not 897 px, and
   `GetWindowRect` **863 px** outer, not 908.
5. **Summon re-anchors.** `SetWindowPos` the window to an arbitrary corner (a programmatic move, not
   user input), then launch a second instance to trigger the broadcast summon. The window must return to
   the anchored rectangle, and `Data\config.json`'s `windowLeft`/`windowTop` must be **unchanged from
   before the move** (wait > 500 ms for the debounce, `WindowHost.cs:167`, before reading).
6. **Re-fit re-anchors.** With the window on screen, edit `"colButtonCount"` in `config.json` and summon
   again (the summon rebuilds and raises `ContentMetricsChanged`). The new fit line's `T + H` must again
   equal 1384, at a different `H`.
7. **Work-area edge case.** Temporarily set the primary work area shorter than the content (not possible
   without changing display settings) — skip; instead assert the Core test `content taller than the work
   area top-aligns and fills` covers it.
8. `grep -rn "WindowLeft\|WindowTop" src/` must return only the retired properties in `AppConfig.cs`.

---

## 5. Item 5 — one background for the title strip and the rail, a white content pane

### 5.1 Decision

**(a) A new themed token.** `src/TanMenu.Wpf/Themes/Fluent2/Tokens.axaml`, in **both** `ThemeDictionaries`,
declared immediately after `SurfaceBaseBrush` (`:41` Light / `:80` Dark):

```xml
<SolidColorBrush x:Key="SurfacePageBrush" Color="#FFFFFF" />   <!-- Light,  :41-ish -->
<SolidColorBrush x:Key="SurfacePageBrush" Color="#2C2C2C" />   <!-- Dark,   :80-ish -->
```

Light `#FFFFFF` is **measured**: the reference's content pane is exactly `(255,255,255)` at (1700,100)
and (100,600) in `3.png`, and at (1709,100) in my own `PrintWindow` capture of the live Task Manager
(r4-theme §3.1). Dark `#2C2C2C` is **derived, not measured**: the Light pane sits 12 levels above the
rail (255 vs 243), and `#202020` + 12 = `#2C2C2C` preserves that relationship. r4-theme §6 could not
measure it — doing so needs the machine's Windows theme switched to Dark and Task Manager recaptured. It
is the one value in this design that is arithmetic rather than observation, and it is flagged as such in
the token's own comment.

**(b) A new shape token**, in the variant-independent section beside `RadiusOverlay` (`Tokens.axaml:113`),
following the precedent `RadiusControlInner` (`:115-122`) set for a derived literal declared next to its
source:

```xml
<CornerRadius x:Key="RadiusPaneTopLeft">8,0,0,0</CornerRadius>
```

8 DIP is **measured**: the arc spans 12 physical px in each axis (the vertical border straightens at
frame y = 89, the horizontal at frame x = 86, from outer edges at x = 74 / y = 77) — 12 / 1.5 = 8.00 DIP,
identical on `3.png` and on my own capture. Only the **top-left** corner is rounded: the top-right block
(x 1898..1923, y 72..96) and the bottom-left block (x 68..92, y 1585..1608) measure flat and square
(r4-theme §3.4).

**(c) The content pane.** `src/TanMenu.Wpf/MainWindow.axaml:462`:

```xml
<Border Grid.Column="1"
        x:Name="PagePane"
        Background="{DynamicResource SurfacePageBrush}"
        BorderBrush="{DynamicResource StrokeControlBrush}"
        BorderThickness="1,1,0,0"
        CornerRadius="{DynamicResource RadiusPaneTopLeft}"
        ClipToBounds="True">
  <ContentControl x:Name="PageHost" />
</Border>
```

`StrokeControlBrush` (`Tokens.axaml:57`, `#0F000000`) is **not a guess**: α = 15/255 = 0.0588 multiplies
whatever is behind it by 0.9412, and 243 × 0.9412 = 228.7 → 229, measured `(229,229,229)` exactly on the
`PrintWindow` capture; over the Mica `#F6F1F8` the screen capture shows, it predicts `(232,227,233)` and
`3.png` measures `(232,227,233)` exactly (r4-theme §3.2). **No new stroke token is needed.**
Thickness is authored as **1 DIP**, not the measured 1.33: the reference's 2 physical px is a 1 DIP stroke
layout-rounded at 150 %, and authoring 1.33 would be wrong at every other scale.
Inset is **zero on all four edges** — the reference is flush against the rail (UIA `PaneRoot` ends at
frame x 73, the border's outer edge is x 74), flush at the body's top, and runs to the last client column
and row with **no right or bottom border**. It is *not* a floating card; r4-theme §3.4 refutes that
explicitly.

> **Superseded by Round 6, item 2 — see
> `docs/superpowers/specs/2026-08-04-round6-surfaces-design.md` §2.1-§2.5. The shipped code is the
> authority.** 设置 no longer keeps `SurfaceBaseBrush`. Every page that is not 应用 now paints
> **nothing** — `Background="Transparent"`, no border, no corner — so the window's own surface (Mica,
> or `ApplyBackdrop`'s `SurfaceBaseBrush` when the platform refuses it) runs unbroken from the rail
> and the title strip through the content. The class was also flipped from `settings` to `app`, so the
> selectors are now `Border.tm-page-pane.app` and `Border.tm-page-pane:not(.app)` and `ShowPageCore`
> writes `PagePane.Classes.Set("app", page == ShellPage.App)`.
>
> **The Δ0 argument below is correct and does not apply.** It is an argument about **white**, and
> white is not what the pane became. Card minus backdrop, three sources, each labelled — an earlier
> version of this box called all of them "measured", which two of them were not:
>
> * **Measured on the shipped Round 6 build**, real 设置 cards on the live Mica pane: Light fill −
>   backdrop `(12,9,4)`, border − backdrop `(−13,−14,−15)`; Dark `(9,8,6)` and `(16,16,15)`.
> * **Measured on `Border.tm-searchfield` at rest** — the only instance of the card's three tokens that
>   already sat on this window's real Mica before the change: Light `(8,11,5)` and `(−14,−14,−15)`;
>   Dark `(8,9,7)` and `(16,16,16)`. Taken in the Round 6 reconnaissance and re-taken on the shipped
>   build. *At rest* matters: `.focused` swaps the fill to `SurfaceLayerActiveBrush` and the app focuses
>   that field on show, so a cold-start capture measures the wrong token (Dark reads `(43,42,44)`
>   focused against `(41,40,42)` at rest).
> * **Composited, not photographed** (r6-material §3.2, whose heading says so): for the pane region,
>   Light `(12,10,0)` at the window's left edge and `(6,11,7)` at its right, border `(−14,−14,−15)` /
>   `(−14,−14,−14)`; Dark `(8,8,6)` / `(8,8,7)` and `(16,16,15)` / `(16,16,16)`. These are the card
>   tokens' alphas over Mica backdrops that *were* measured. No settings card on Mica could be
>   photographed before the change, because the pane was opaque.
>
> Against `(8,8,8)` / `(−14,−14,−14)` Light and `(8,8,8)` / `(16,16,16)` Dark over the old grey pane,
> every one of those is the same relationship to within a few levels. Mica measures 238-247 Light /
> 30-34 Dark under this window, i.e. it straddles `SurfaceBaseBrush` (243 / 32) and is nowhere near
> `SurfacePageBrush` (255 / 44).
>
> The 应用 half of this item — (a), (b), (c), (e), (f) — is **unchanged and still shipping**.
> Re-measured on the Round 6 build: 应用 pane `(255,255,255)` Light / `(44,44,44)` Dark, inner border
> column `(240,240,240)` / `(59,59,59)`, top-left arc present, no right or bottom border.

**(d) 设置 keeps the base surface.** `ShowPageCore` (`MainWindow.axaml.cs:513-515`) is already the single
authority for which page is on screen; it gains one line —
`PagePane.Classes.Set("settings", page == ShellPage.Settings);` — and `Launcher.axaml` gains one rule:

```xml
<Style Selector="Border.settings">
  <Setter Property="Background" Value="{DynamicResource SurfaceBaseBrush}" />
  <Setter Property="BorderThickness" Value="0" />
  <Setter Property="CornerRadius" Value="0" />
</Style>
```

This is measured from the user's **other** reference capture. `1.png` is the same Task Manager window on
its Settings page: its content pane is `(243,243,243)` at (74..111, 600) — the *same* value as its rail
— with **no border and no rounded corner** (the whole block x 68..96 / y 70..96 is flat 243, and y = 77
sampled at x = 200/600/1200/1800/1900 is 243 everywhere), and its cards are `#FBFBFB` with `#E5E5E5`
borders — i.e. exactly our `SurfaceLayerBrush` over `SurfaceBaseBrush` and our `StrokeControlBrush` over
`SurfaceBaseBrush` (r4-theme §5.3). **The reference does not make its two pages agree, and neither should
we**: our settings cards use `SurfaceLayerBrush` `#B3FFFFFF` (`Launcher.axaml:604`), which composites to
`#FBFBFB` on a 243 pane (Δ8, measured) but to **`#FFFFFF` on a white pane (Δ0)** — a card that is
literally the same colour as the page it sits on. This is the one place the design diverges from a
literal reading of the controller's "the content pane is pure white"; see §7.

**(e) The title strip.** Add one Setter to the existing `Window.WindowDecorationsTheme`
(`MainWindow.axaml:83-153`), which today overrides only `PART_TitleTextPanel`'s opacity,
`PART_FullScreenButton`'s visibility and three `VerticalAlignment`s and **never touches `Background`**:

```xml
<Setter Property="Background" Value="Transparent" />
```

Why: our title strip renders **pure `#FFFFFF` over exactly the client's top 72 rows (48 DIP) at full
client width** while the rail one row below renders Mica `#F1F1F9` — reproduced on three independent
captures (`testbuild.png`, a fresh capture taken today, and the older `01-coldstart-tall.png`), with
72 px matching `ExtendClientAreaTitleBarHeightHint="48"` (`MainWindow.axaml:15`) exactly. Task Manager
under the identical capture method shows **no** such band (r4-theme §3.3). The **cause is not verified**:
either Avalonia's `WindowDrawnDecorations` overlay paints an opaque background (its markup is compiled to
IL in `Avalonia.Themes.Fluent.dll` 12.1.0 and could not be read), or it is a `PrintWindow` artefact of the
`DwmExtendFrameIntoClientArea` band. The Setter is **correct either way** — the app wants the backdrop to
show through the strip — and it is the only lever available without instrumenting a build.

> **Superseded during implementation (commit `3581976`); the code is the authority.** The Setter above
> does not compile — `WindowDrawnDecorations` has no `Background` property (`AVLN2000`) — and a
> `^ /template/ Panel#PART_TitleBar` rule setting `Background="Transparent"` was then tried and
> **measured to fail** (the part still reported `bg=White`; an `Opacity` probe in the same rule applied,
> so the selector matched). The cause **is** now verified and the `PrintWindow` artefact is ruled out:
> the IL *was* read (`ilspycmd` over `Avalonia.Themes.Fluent.dll` 12.1.0), and a live
> `GetVisualDescendants()` dump found `Panel name=PART_TitleBar bounds=0,0,1000,48 bg=White` in the
> drawn-decorations underlay, its `Background` bound to `{DynamicResource TitleBarBackgroundBrush}`.
> What shipped is a `TitleBarBackgroundBrush` override in `Tokens.axaml`, whose block comment carries the
> full write-up.

**(f) Make the Mica fallback follow the theme.** `MainWindow.axaml.cs:445-449` resolves its brush with
`FindResource` and assigns the resulting object, so the fallback is a snapshot of whichever variant was
live at `OnOpened` and does not follow a later `AppTheme.Apply`
(`Services/AppTheme.cs:69-70`, which the settings window calls on every selection change). Replace the
assignment with a resource-observable binding on `BackgroundProperty` keyed to `SurfaceBaseBrush`, so the
window's fallback surface tracks the variant. Item 5's whole requirement is "title strip and rail share
one background"; that must hold in both variants **and** in both backdrop states, and today it does not.

### 5.2 Why

> **Partially superseded by Round 6, item 2 (`docs/superpowers/specs/2026-08-04-round6-surfaces-design.md`
> §2.2). Stated precisely rather than blanket-marked, because most of this section is still true.** The
> ruling this section justifies is §5.1(d), and §5.1(d)'s 设置 half is overruled — see the box there.
> **Four of the five bullets below stand unchanged**: they are about the 应用 pane, the token test's
> coverage gap, and the tiles on the white 应用 page, all of which still ship exactly as written. **The
> fourth bullet's closing clause is falsified** and carries its own box; its conclusion — do not change
> `SurfaceLayerBrush` — survives, but the reason given for it does not. What the reader must not carry
> forward is the *conclusion* this reasoning was assembled for, namely that the app's two pages should
> be given two different opaque surfaces. Round 6 gives 应用 an opaque surface and every other page the
> window's own.

* **The user's complaint, as a number.** At the same x, Task Manager's title bar and rail differ by
  **≤ 1 level**; ours differ by **14, 14, 6** (x = 35 and x = 60, two independent captures — r4-theme
  §3.1). By the code they already share one background (both `Background="Transparent"` over the window
  backdrop — `MainWindow.axaml:158-160`, `:283-287`), so the fix is not in the layering but in whatever
  paints that white band, which is (e).
* **The pane's geometry is free.** Our rail width and our pane's left edge already land on the *identical*
  pixel as the reference (frame x 2..73 and 74 in both). Only the fill, the border and the corner differ,
  so items (b) and (c) reproduce the reference exactly with no layout change at all.
* **A token, not a literal.** `Fluent2TokenTests.LauncherStyles_ContainNoColourLiterals`
  (`Fluent2TokenTests.cs:43-55`) scans **`Launcher.axaml` only** (`Regex.Matches(LauncherXaml, …)`), so a
  hardcoded `#FFFFFF` typed into `MainWindow.axaml` would pass CI — this is the exact gap item 5 is most
  likely to fall into. The other two window tests do bite: `:71-85` requires every key `MainWindow.axaml`
  references to exist in `Tokens.axaml`, and `:87-101` requires every themed key to exist in **both**
  dictionaries. `RadiusPaneTopLeft` goes in the variant-independent section, so only the first applies to
  it.
* **Tiles on white: accepted, with the numbers.** `SurfaceLayerBrush` over `#FFFFFF` computes to
  `#FFFFFF` (Δ0 from the pane) against `#FBFBFB` (Δ8) today, leaving only the 1 DIP `StrokeControlBrush`
  outline, which on white is `255 × 0.9412 = 240` → `#F0F0F0` (Δ15 — *more* contrast than the Δ14 it has
  today). Hover and press still read: `SubtleFillSecondaryBrush` `#0A000000` over white = 245 (`#F5F5F5`,
  Δ10) and `SubtleFillTertiaryBrush` `#06000000` over white = 249 (Δ6). The tile becomes an *outlined*
  card rather than a *filled* one — which is exactly what the reference's white pane shows (it has no
  filled cards at all). `SurfaceLayerBrush` is **not** changed, because it is also the settings cards'
  fill and those stay on the grey pane where it works.

  > **The last clause went stale on 2026-08-03, Round 6 item 2.** The settings cards do **not** stay on
  > the grey pane — that is exactly what that item changed; they now sit on the window's own Mica. The
  > bullet's *ruling* is unaffected and still shipping: `SurfaceLayerBrush` is not changed. Only its
  > stated reason is dead, and the replacement reason is in §5.1(d)'s box above — over Mica the card's
  > fill − backdrop stays positive on at least two channels and its outline stays Δ13-16, so the token
  > still works where it now lands. Do not carry "those stay on the grey pane" forward as current fact.
* **Dark is not damaged the same way**: `#4D3A3A3A` (α 0.302) over `#202020` gives ~`#282828`, and over a
  `#2C2C2C` pane gives ~`#303030` — still separated.

### 5.3 Blast radius

**Files touched:** `Themes/Fluent2/Tokens.axaml` (two brush entries + one CornerRadius),
`MainWindow.axaml` (`:462` and the decorations theme at `:83-153`), `MainWindow.axaml.cs`
(`ShowPageCore` one line, `ApplyBackdrop` rewritten), `Themes/Fluent2/Launcher.axaml` (one
`Border.settings` rule).

**⚠ This item changes item 4's arithmetic.** A Border's `BorderThickness` is drawn inside its bounds and
its child is laid out inside it, so the 1 DIP left and top borders take 1 DIP each away from the page.
`FitToContent` composes the window as `contentWidth + chromeWidth` and `contentHeight + chromeHeight`
(`WindowPlacement.cs:152-155`), where `chromeWidth` is the rail and `chromeHeight` is
`TitleStripHeightDip = 48` (`WindowHost.cs:35, :691`). **Without compensating, the page would be given
1 DIP less than it measured and `Body`'s horizontal scrollbar would return** — the very defect Round 3
removed. Hence the constants in §4.1(a)/§1.1: `chromeWidth = RailWidthDip + PaneBorderDip = 49` and
`ChromeHeightDip = TitleStripHeightDip + 1 = 49`. The invariant to write down beside them: *chromeWidth
must equal the rail's width plus whatever left border the **应用** pane carries.* (The settings pane's
border is 0 under (d), and the fit never runs on 设置 anyway — `MainWindow.axaml.cs:321-322`.)

**The detach guard is unaffected by (d).** The class toggle lands on the pane `Border`, which is the
always-attached host, not on anything inside `_appPage`. `RefitToContent` measures `_appPage`
(`MainWindow.axaml.cs:328`) and the Border's background is not an input to that measurement.

**Corner clipping.** `ClipToBounds="True"` is set defensively. Whether Avalonia's `Border` clips its child
to the *corner radius* (as opposed to the bounds rectangle) was **not verified**, and the design does not
depend on it: `AppPage`'s content starts 16 DIP inside the pane (`SurfacePadding`, `Tokens.axaml:130`,
applied at `AppPage.axaml:94-96`), which clears the 8 DIP arc entirely; `SettingsPage` has no radius.

> **Answered after the round (shipped code unchanged): a `Border` DOES clip its child to the radius.**
> The premise above is also incomplete — the 关于 overlay carries no margin and
> `OverlayPanel.ArrangeOverride` arranges every child over the whole panel rect, so unlike the tile
> body and the docked notice it *does* reach the corner. Its `SplashPanel` card is a square `Border`
> with an accent gradient and no `CornerRadius` of its own, yet on a `PrintWindow` capture of the
> running app its first accent pixel per row traces 94, 91, 90, 89, 88, 87, 86, 86, 86, 85, 85 down
> the top eleven rows — an arc, flush with the pane's inner edge. Both candidate clippers (the
> overlay's own `RadiusOverlay` `Border` and this pane) are `Border`s with the same 8 DIP arc, so the
> conclusion holds either way. Recorded in `MainWindow.axaml`'s own pane comment.
>
> *Round 5 (`871504b`) deleted the 关于 overlay, `RadiusOverlay` included, so this measurement is no
> longer reproducible from this tree — do not treat "re-run the capture" as an open action.
> `MainWindow.axaml`'s pane comment says so and enumerates why no current `OverlayPanel` child restages
> it.*

**Tests:** all four `Fluent2TokenTests` keep passing and two of them actively guard this change. No test
file is edited.

**Comments that become false:** `MainWindow.axaml:452-461` (the pane comment, which argues at length that
reusing `SurfaceBaseBrush` "keeps the app's one opaque surface colour meaning exactly one thing" and that
the single Border stands in for both pages) — both halves change and it must be rewritten with the
measured basis; `MainWindow.axaml.cs:442-444` (`ApplyBackdrop`'s summary).

### 5.4 Verification

1. `PrintWindow(hwnd, hdc, 2)` capture, Light theme. Scan a vertical profile down **x = 35** (inside
   the rail's own band) in 8 px steps from the top of the client area to y ≈ 400, i.e. from the title
   strip, across the strip/rail seam at **y = 72**, and well down into the rail. **There must be no
   step at the seam**: the last strip row (y = 66) and the first rail rows (y = 74 / 82 / 90) must be
   equal on every channel, and the profile as a whole must drift monotonically rather than jump.
   Repeat at x = 60. This is item 5's headline assertion.

   > **The bar this step originally set is one a correct build fails; rewritten after the round
   > (shipped code unchanged).** It used to demand that `GetPixel(35,12)` and `GetPixel(35,400)` — two
   > points 388 px apart — agree "within 1 level on every channel". The shipped build reads
   > **(240,241,252)** and **(238,241,255)** there in Light, Δ **(2,0,3)**, and Δ **(1,0,2)** in Dark,
   > so anyone re-running the old wording verbatim reads a pass as a fail. Across the *seam itself*
   > both variants are pixel-identical — Light strip y = 66 and rail y = 74 / 82 / 90 all
   > **(239,241,253)** at x = 35; Dark all **(30,32,38)** — and the 8 px profile shows the value
   > drifting **continuously** through y = 72 with no step at all. What remains is the **Mica
   > gradient**, which by construction varies down the window and which the title strip and the rail
   > now share; it is not a layering seam. Before the round the same two points differed by
   > **(14,14,6)** *with a hard step at y = 72*, and it is the step, not the difference, that item 5
   > was about. (Values measured on the shipped build in the whole-round review; the Dark figures
   > required `"appearance": "Dark"` in `Data\config.json`, which was restored afterwards.)
2. `GetPixel(2400,100)` must be `(255,255,255)`.
3. Left border: the pane's first two image columns (x = 83,84) at y = 600 must be
   `#F0F0F0` = `(240,240,240)` — `StrokeControlBrush` over white. Top border: y = 72,73 across the pane,
   same value.
4. Corner: map the block x = 83..111, y = 72..102. The vertical border must straighten at y = 84 and the
   horizontal at x = 95 (12 px of arc in each axis from the outer edges at x = 83 / y = 72).

   > **Steps 3 and 4 measured differently, and the expectations above are what is wrong** (commit
   > `3581976`, re-measured in the fix round). Step 3: only the pane's *inner* column/row reads
   > `(240,240,240)`; the outer one (x = 83 / y = 72) is the same stroke composited over the Mica
   > backdrop instead of over white — `(226,227,236)` — because a `Border` paints its background inside
   > the stroke. The reference capture `3.png` does the same thing, measuring `(232,227,233)` on its own
   > left border. Step 4: the arc is **10–11 px**, not 12 — and `3.png` measures **10 px** under the
   > identical scan, so the 8 DIP token reproduces the reference to within a pixel; the 12 came from a
   > looser antialias threshold in the survey. Step 6's `~(48,48,48)` is likewise the Dark stroke over
   > the *rail* (`#202020`), not over the `#2C2C2C` pane, where it computes and measures `(59,59,59)`.
5. Right and bottom edges: no border pixel — the white must run to the last client column and row.
6. **Dark theme.** Write `"appearance": "Dark"` into `Data\config.json`, restart. The pane must be
   `(44,44,44)`, the title strip and rail must again match each other within 1 level, and the border must
   be ~`(48,48,48)` (`#12FFFFFF` over `#2C2C2C`).
7. **Settings page.** Select the 设置 rail row through UI Automation (`SelectionItemPattern.Select` on
   `NavSettingsItem`; UIA patterns are programmatic and work where `SendInput` no-ops). The pane must
   then measure `(243,243,243)` with **no** border pixel at x = 83 and **no** arc at the top-left, and
   the cards must measure `#FBFBFB` with `#E5E5E5` borders. If the provider refuses the pattern, fall
   back to asserting the `Border.settings` rule by source inspection plus (6)'s token check.

   > **Superseded by Round 6, item 2; do not run this step as written — a correct build fails it.**
   > `Border.settings` no longer exists (the selectors are `Border.tm-page-pane.app` and
   > `:not(.app)`), and the pane no longer measures `(243,243,243)`: it paints nothing, so it measures
   > whatever the window's surface measures at that pixel. On a Mica-granted machine that is a
   > wallpaper gradient with **no portable absolute value**. The equivalent that holds today is
   > `docs/superpowers/specs/2026-08-04-round6-surfaces-design.md` §2.5: assert **continuity** across
   > the rail→pane seam at image x 82→83 and the strip→pane seam at y 71→72 (|Δ| ≤ 1 per channel), and
   > assert the card's **relationships** to the backdrop measured in the same capture, never its
   > absolute RGB. The `(243,243,243)` figure survives only as the **Mica-refused fallback's**
   > expectation, where `ApplyBackdrop` paints `SurfaceBaseBrush` on the window and the transparent
   > pane shows it. Measured on the shipped Round 6 build in Light: both seam deltas `(0,0,0)` at every
   > sampled point.
8. **If the white title band survives step 1**, do not iterate blindly: the remaining candidate cause is
   a `PrintWindow`/`DwmExtendFrameIntoClientArea` artefact (r4-theme §3.3), which means the band was never
   on screen at all. Confirm against a human looking at the actual window before doing more work.
9. `grep -n "#[0-9A-Fa-f]\{3,8\}" src/TanMenu.Wpf/MainWindow.axaml` must return **nothing** — the rule no
   test enforces for that file.
10. `dotnet test` green; `dotnet publish -c Release` (NativeAOT) with 0 warnings.

---

## 6. Cross-cutting

### 6.1 The re-fit trigger list after Round 4

Everything still routes through `MainWindow.RefitToContent` (`MainWindow.axaml.cs:319-329`) →
`WindowHost.FitToContent` (`WindowHost.cs:668`), and `RefitToContent` still bails unless
`PageHost.Content` is `_appPage` (`:321-322`).

| # | Trigger | Site | Round 4 effect |
|---|---|---|---|
| 1 | Startup build finished — on **every** summon | `MainWindow.axaml.cs:219-223`, posted at `Loaded` from `OnOpened:160` | unchanged; now also re-anchors |
| 2 | Docked notice opened/closed | `:333` (sub. `:117`; sources `AppPage.axaml.cs:81-85`, `:87-91`) | unchanged; now also re-anchors |
| ~~3~~ | ~~Rail expand/collapse~~ | ~~`:699`~~ | **deleted by item 1** |
| 4 | Launcher rebuild — 每列个数 / 字体 / 语言 / 刷新 / summon | `:353-354` (sub. `:124`; raised at `LauncherViewModel.cs:658`) | unchanged trigger; item 3 makes it change column **widths** as well as counts |
| 5 | 设置 → 应用 switch | `:542-543`, gated on `wasSettings` (`:509`) | unchanged; also newly reached by 关于 from 设置 (§2.3) — *that second path went with 关于 in Round 5 (`871504b`); the rail-click path is unchanged* |
| 6 | Maximized → Normal | `:437-439` | unchanged; now also re-anchors |

**Five triggers, and every one of them now moves the window as well as resizing it.**

### 6.2 The fit arithmetic after Round 4

```
pageDesired          = AppPage.Measure(∞, ∞).DesiredSize            (WindowHost.cs:683-684)
window.Width         = pageDesired.Width  + 48 (rail) + 1 (pane left border)
window.Height        = pageDesired.Height + 48 (title strip) + 1 (pane top border)
                       clamped to [min(568, work.Height), work.Height]
window.Left          = work.Left + (work.Width  − Width)  / 2
window.Top           = clamp(work.Bottom − 8 − Height, work.Top, work.Bottom − Height)
```

On this machine, with this user's data: page 1627.2 x 471.33 → window **1676.2 x 568** at
**Left 1721.9, Top 816** DIP. Today: page 1676 x 471.33 → window 1724 x 568 at Left 1511.33, Top 782,
rendered 30 DIP taller than requested.

> **Superseded during implementation (`c34c65b..908967e`); the shipped build is the authority.**
> The composition above is exactly right and the predicted numbers are not. The app logs
>
> ```
> Window fit to content: 1713.8333333333335,816 1692.3333333333333x568 DIP
>   (page 1643.3333333333333x471.3333333333333 + chrome 49x49) on work area 0,0 5120x1392 at scale 1.5
> ```
>
> so the shipped figures are **1692.33 x 568 at Left 1713.83, Top 816**. Height and Top are exact.
> Width came out **16.13 DIP higher** than predicted and Left **8.07 DIP lower** — which is one
> error, not two: Left is `(work.Width − Width)/2`, so half of any width error appears in it with the
> opposite sign.
>
> Every line of the box above then checks out against those numbers: `1643.33 + 49 = 1692.33`,
> `471.33 + 49 = 520.33` clamped up to 568, `(5120 − 1692.33)/2 = 1713.83`, `1392 − 8 − 568 = 816`.
> **The formula was right and the input was wrong** — the page came in at 1643.33 rather than the
> predicted 1627.2, because item 3's per-column label-advance model under-measured. §3.2's box has
> the per-column table and the reason. Nothing about items 4 or 5 contributed to the miss.

> **Those shipped figures are Round 4's, and Round 6, item 4 moved them — see
> `docs/superpowers/specs/2026-08-04-round6-surfaces-design.md` §4.4 and §5.2.** The composition above
> still holds term for term (`page + chrome 49 × 49`, the 568 floor, the placement formula); only the
> page input changed, because every category gained a panel and the inter-category gap halved. The
> current build logs
>
> ```
> Window fit to content: 1681.1666666666665,816 1757.6666666666667x568 DIP
>   (page 1708.6666666666667x494 + chrome 49x49) on work area 0,0 5120x1392 at scale 1.5
> ```
>
> i.e. **1757.6667 × 568 at Left 1681.1667, Top 816** — `+65.3333` in width and `−32.6667` in Left,
> which is again one error appearing twice with opposite signs. Do not quote `1692.33` as this app's
> width; it is the Round 4 figure.

### 6.3 Test-suite delta

183 cases today = 125 `[Fact]` + 58 `[InlineData]` rows under 8 `[Theory]`, in the single project
`tests/TanMenu.Core.Tests` (r4-crosscut §3). **Nothing exercises `MainWindow` or `WindowHost`** — they
need a UI thread — so items 1, 2, 5 and the `WindowHost` half of item 4 have **zero** automated coverage,
and §§1.4/2.5/3.5/4.4/5.4 are the only nets they get.

| File | Delta |
|---|---|
| `Services/WindowPlacementTests.cs` | **rewritten** per §4.3: 6 deleted, 4 replaced/rewritten, 9 retargeted, 7 unchanged, ≥4 added |
| `Services/ConfigServiceTests.cs` | **no code change**; comments on `:559-589`, `:591-615`, `:617-636`, `:638-660` updated |
| `Models/AppConfigTests.cs` | **no change** (`:37-51`, `:53-62` still pass) |
| `Themes/Fluent2TokenTests.cs` | **no change**; `:71-85` and `:87-101` actively guard item 5 |
| `Services/MenuLayoutTests.cs`, `ViewModels/MenuModelsTests.cs` | **must not change.** If either breaks, item 3 leaked into the data layer |
| everything else | untouched |

### 6.4 Config contract

No schema bump. `AppConfig.CurrentSchemaVersion` stays 1 (`AppConfig.cs:25`); `ConfigService.Normalize`
(`ConfigService.cs:94-123`) gains nothing. Three properties are retired **in place**, joining
`ShowInTaskbar` (`:106-109`), `WindowWidth`/`WindowHeight` (`:61-71`) and `ButtonSize` (`:172-179`):

| Property | Line | New status |
|---|---|---|
| `NavRailExpanded` | `:100` | legacy; the rail is permanently icon-only |
| `WindowLeft` | `:81` | legacy; the window is always placed centred-low on the primary |
| `WindowTop` | `:82` | legacy; same |

A bump would silently defuse `ConfigServiceTests.Load_MigratedConfig_KeepsDeliberateAutoCloseChoice`,
whose fixture interpolates the constant (`:456`) and therefore tracks it instead of testing against it.

### 6.5 NativeAOT

Neutral across all five items, provided two rules hold (r4-crosscut §6): the footer is **reordered as
literal XAML rows**, never rebuilt from a collection (that would add a fourth `DataTemplate` over a type
that does not exist and need an `x:DataType`); and the tile grid keeps its two nested `ItemsControl`s
(`AppPage.axaml:141-144` records why). Brushes stay `DynamicResource`; placement is arithmetic.

---

## 7. Open decisions for the human

Exactly one survives. Everything else in this document is decided.

**7.1 — Does the 设置 page's content pane go white too?**

> **Answered by the user in Round 6, and neither way this question offered.** The user asked for
> *除了应用页，其他页面使用和窗口相同的材质面板* — every page except 应用 uses a pane made of the same
> material as the window. So the 设置 pane is neither the grey this section defends nor the white its
> alternative proposed: it paints **nothing**, and the window's own surface shows through. Round 6,
> item 2 shipped that; see `docs/superpowers/specs/2026-08-04-round6-surfaces-design.md` §2.1-§2.5 and
> the box on §5.1(d) above for the measurements that retire the Δ0 objection below. The question is
> closed — do not re-open it as a binary.

*This design says no* (§5.1(d)): 应用 becomes white with the 1 DIP top+left border and the 8 DIP top-left
corner; 设置 keeps `SurfaceBaseBrush` with no border and no corner. The basis is the user's own second
reference capture — `1.png` measures Task Manager's Settings pane at `(243,243,243)`, flat, borderless,
square-cornered, with `#FBFBFB` cards on it — and the fact that our settings cards use the same
`SurfaceLayerBrush` (`Launcher.axaml:604`) that computes to `#FFFFFF` (Δ0) on a white pane.

*The alternative* is a literal reading of the controller's ruling: one white pane for both pages. It costs
**less** code (delete §5.1(d) entirely — one line in `ShowPageCore`, one style rule) but flattens every
settings card to the colour of the page behind it, leaving only a `#F0F0F0` hairline.

Say **"both white"** and §5.1(d) is deleted; say nothing and the design ships as written.

### 7.2 Settled here, recorded so they are not re-opened

| Question | Ruling | Where |
|---|---|---|
| Column widths: per-column uniform or per-tile ragged? | **Per-column uniform.** Same window width either way; per-column leaves `FindAdjacentTile`'s scoring bit-for-bit unchanged and needs no added attribute | §3.3 |
| Which screen anchors the startup position? | **The primary**, unconditionally, matching 0.11's shipped `TryGetPrimaryDisplay`. `FitToContent` keeps using the window's current screen | §4.2 |
| Is the saved position retired or still written? | **Retired in place** — `WindowLeft`/`WindowTop` keep deserializing, nothing reads or writes them, no schema bump | §4.1(b), §6.4 |
| Flush against the work-area bottom, or a gap? | **8 DIP gap** on the placement rectangle = 6.67 DIP visible at 150 %. Flush was rejected because our window carries a 9 px invisible border that Windows' own popups do not | §4.1(c) |
| Is a window the user has dragged snapped back to the anchor? | **No** — not until its content changes or it is re-summoned. Falls out of the request-based skip basis; snapping back on every page switch is worse, and the pre-Round-4 build did not do it either | §4.1(f) |
| A tile `MinWidth` or `MaxWidth`? | **Neither.** `MaxWidth` reintroduces the ellipsis the user asked to remove; `MinWidth="168"` turns a 49 DIP narrowing into an 86 DIP widening. The work-area clamp plus `Body`'s scrollbar is the bound | §3.2 |
| A spacer where the hamburger was? | **No.** 应用 rises 44 DIP, glyph centre y = 120 px | §1.1 |
| A confirmation on 退出? | **No.** Non-goal; the separator plus four rows of distance from 设置 is the mitigation | §2.3 |
| Does the title strip stay 48 DIP? | **Yes.** The reference's body starts at 52 DIP (frame y 78) but the strip's height is not in this round's scope | §0.3 |
| Does `TextTrimming` stay as a safety net? | **No.** With the tile sized to content it can never fire, and a property that says "this can overflow" when it cannot is a lie in the code | §3.1 |

### 7.3 Where the reports differ, and the ruling

The five reports agree on every fact this design uses. Two differences of **emphasis** are worth ruling
explicitly:

1. **The ~605 DIP height floor.** `r4-crosscut §5b` treats it as a measured constant to design around and
   warns that hardcoding it is a guess at any other scale; `r4-placement §5.4` decompiles the mechanism
   (`ptMinTrackSize.Y = MinHeight × scale + BorderThickness.Top + Bottom`) and shows it is a *consequence
   of `MinHeight`*, not a platform constant. **`r4-placement` is right and this design follows it**: the
   floor is removed at its source (§4.1(d)) rather than compensated for with a margin, which is why no
   scale-dependent constant appears anywhere in the placement arithmetic. Both reports also agree that
   `docs/…/2026-08-02-win11-shell-round3-findings.md:36-44`'s `SM_CYMINTRACK` explanation is a numeric
   coincidence — 56 is both `SM_CYMINTRACK(144)` and `BorderThickness.Top+Bottom` here — and the
   decompiled line supersedes it.
2. **Where tile sizing may live.** `r4-crosscut §2` says "keep the sizing on the element-local
   ControlTheme"; `r4-tiles §5` shows that content sizing means the width derives from the class-scoped
   `Image.tm-tile-icon` box and the label regardless. **Both are satisfied by doing nothing but deleting
   the Setter** (§3.1): no sizing moves to a class style, and the detach-guard comment is rewritten to say
   the guard became more load-bearing rather than less.
