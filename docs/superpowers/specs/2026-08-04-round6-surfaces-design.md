# TanMenu — Windows 11 shell, Round 6 design: surfaces, the rail's selection, and category panels

> **DEPRECATED — Round 7, Task 10.** Describes the Windows 11 shell, shipped in 0.11.0–0.12.0 and
> replaced by the Aero conversion (`2026-08-04-aero-conversion-design.md`). Kept as the record of that
> work; its measurements were true of the code at the time and are not true of the code now. Nothing
> below was edited to add this header. Three of its four items lost their subject outright: item 2's
> Mica-straddle argument (there is no Mica — the caption band is AcrylicBlur and the client below it is
> opaque), item 3's accent selection pill (deleted with the icon rail at Task 8), and item 4's category
> panel fill (the panel is an unfilled Aero group box since Task 9). Item 1 — the search field shown
> only on 应用 — survives in a stronger form: the field lives *inside* the 应用 page now, so there is
> no visibility rule left to get wrong. All the measurements are in
> `../archive/2026-08-04-windows11-measurement-record.md`.

> **Status when written: DESIGN ONLY.** No source file was edited and nothing was committed while
> this document was written; the repository was clean at `ef79481` throughout.
>
> **Status now (2026-08-03).** **Items 1 and 2 are implemented.** Item 1 shipped in `cc550da`, *feat:
> hide the search field off 应用 instead of disabling it*, and its §1.4 steps 2, 3 and 6 were
> **corrected after that item's review**: two of them were bars a correct build fails, and one rested
> on a measurement its instrument could not support. The corrections are marked in place and dated.
> Item 2 shipped next; `ShowPageCore` now writes `PagePane.Classes.Set("app", page == ShellPage.App)`
> and the two selectors are `Border.tm-page-pane.app` / `:not(.app)`, verified against the tree as this
> note was written. **One correction to §2.5 came out of running it** — step 6, the deliberate
> Mica-refused case, **could not be executed on this machine**; the correction is marked in place at
> that step. **Item 3 shipped next**, in `f1ba7aa`, *feat: match the rail's selected style to Task
> Manager's, measured*, exactly as §3.2 specifies (the `TmNavRowItem` `ControlTheme` verbatim, the
> three old rules and `Classes="tm-nav"` deleted, the glyph rule added). **One correction to §3.5 came
> out of running it** — step 3's cap test is an instrument that is far more sensitive in Dark than in
> Light and reads a correct pill as square-ended there; the correction, with the coverage numbers that
> replace it, is marked in place at that step. Step 9 also needed one sentence of disambiguation
> against §3.2(e), marked in place. **Item 3's review then found one stale sentence** — the
> `TmNavRowItem` block comment still asserted, in the present tense, the 4 px indicator its own change
> had replaced with a 5 px one — so that comment was corrected and §3.3(b) was given the outcome note
> §3.3(e) already had; both are marked in place. **Item 4 shipped last**, in `c4aabcb`, *feat: give
> every 应用 category a panel, measured*, exactly as §4.3 specifies. **Three corrections to §4 came out
> of running it**, all marked in place and all one root cause or its consequences: §4.4's arithmetic
> uses the **authored** border `B = 1`, where at this machine's 150 % a 1 DIP stroke lays out as 2
> physical px = 1.3333 DIP, so the real per-panel chrome is **22.6667** per axis and every figure
> derived from 22.00 is 0.67 per panel low — §4.4, §4.6 steps 1, 2 and 10 and §5.2's predicted window
> line are corrected at each site. §4.6 step 7's "10-11 px" corner bar is also corrected: it is
> instrument-dependent, and the correction records the panel's arc beside the app's own other
> authored-8-DIP corner measured in the same capture. Everything else in §4 held as written, including
> every RGB in §4.2's Dark table, which was computed and measured exactly. **Item 4's review then found
> three defects in that record itself**, all fixed in a follow-up commit and all marked in place:
> `AppPage.axaml`'s categories comment still carried the superseded `5 × 22` / `+62`, so a maintainer
> reading the code — this repo's stated authority — got the wrong figure while the spec had the right
> one; §4.6 step 2's explanation of the one container that reads 23.3333 invoked a "ten-column group"
> that does not exist (a `Group-Object … .Count` bug in `r6i4-groups.ps1` had turned four one-column
> groups into 5 / 10 / 6 / 6, and the residual is instead one device pixel in that container's right
> inset, present in a *gap* on the pre-change build); and `Tokens.axaml`'s `RadiusPanel` block asserted
> a flat "10-11 px of visible arc" that the author's own capture had already measured at 12 in Dark.
> **The build itself was not touched** — the fix is comments and this document, and the shipped page's
> 825-node UIA dump is byte-identical before and after.
>
> **Scope.** The four items the user asked for after reviewing the Round 5 build:
>
> 1. 其他页面不要禁用搜索框，可以直接不显示 — on pages other than 应用 the search box is not disabled,
>    it is simply not shown.
> 2. 除了应用页，其他页面使用和窗口相同的材质面板，类似 windows 的设置页 — every page except 应用 uses
>    a pane made of the same material as the window, the way Windows' own Settings page does.
> 3. 导航条中图标的选中状态样式和任务管理器不一样，需要调整 — the selected-state style of the rail's
>    icons does not match Task Manager's and has to be adjusted.
> 4. 应用页面每个分类增加一个面板样式 — each category on the 应用 page gains a panel.
>
> **This round explicitly overrules Round 4 §5.1(d) and §7.1.** See §2.2. That ruling ("设置 keeps
> `SurfaceBaseBrush`") was correct on its own evidence and is being reversed on new evidence, not
> quietly contradicted.
>
> **Evidence base.** Three reconnaissance reports, all read in full and all re-checked against the
> tree before being cited here:
> `r6-shell.md` (items 1 and 3), `r6-material.md` (item 2), `r6-panels.md` (item 4), in
> `C:\Users\ADMINI~1\AppData\Local\Temp\claude\D--0-Work-TanMenuWpf\f7ead68c-…\scratchpad\`.
> Every claim about current behaviour below carries a `file:line`; every number carries either a
> measurement or the arithmetic that produced it. Where a number is computed rather than observed it
> says so in the same sentence.

---

## 0. Ground truth

### 0.1 The machine, and how every pixel number was obtained

| Quantity | Value | Source |
|---|---|---|
| Scale | 150 %, so **DIP = physical px / 1.5** | all three reports |
| Work area | `0,0 5120x1392` DIP | the app's own fit log line |
| Live window (pid 42444) | raw `2571,1224 2561x863`; client `2539x852` px = 1692.67 x 568 DIP | `GetWindowRect` / UIA, r6-material §0, r6-panels §1 |
| Capture | `PrintWindow(hwnd, hdc, 2)` into a `GetWindowRect`-sized bitmap | all three reports |
| **TanMenu image origin** | UIA screen x **− 2571**; the UIA origin sits **11 px right** of the image origin (the Win11 invisible resize border). Vertical offset **0** | r6-shell §0 |
| **Task Manager image origin** | UIA screen x **− 1526**, offset **0** — TM's provider reports the full frame | r6-shell §0 |
| Mica | **granted** on this machine now (`EnableTransparency = 1`); the strip and rail photograph a wallpaper gradient, not a flat fill | r6-material §0 |

**The 11 px figure is not a shared constant.** Reusing it on a WinUI window puts every measurement
11 px out (r6-shell §0). Any verification step below states which space its coordinates are in.

**Every RGB in this document is a `PrintWindow` composite.** `PrintWindow` composites the DWM backdrop
itself, so a Mica surface photographs as a flat colour whose absolute value is the blurred wallpaper
behind that window's own screen position. **Absolute Mica RGBs are not portable**; the *relationships*
derived from them (fill − backdrop, border − backdrop, and alphas solved against a backdrop measured in
the same capture at the same y) are, and those are what every acceptance criterion below rests on.

### 0.2 Where the three reports disagree — nowhere that matters, and three corrections to the brief

The three reports agree on every fact this design uses. What look like conflicts are not:

* r6-shell reads the rail's Light backdrop as `(239,241,253)`; r6-material reads `(238,241,255)` at
  image x = 78 and `(247,239,245)` at x ≈ 2000. Same surface, different x on a wallpaper gradient.
  Consistent, and it is exactly why the criteria below are expressed as alphas against a
  same-capture backdrop.
* r6-material measures Windows Settings' **row cards** at 4 DIP radius / 4 DIP sibling gap and its
  **large tiles** (存储/显卡) at 8 DIP radius / 12 DIP gap; r6-panels measures the row cards on
  `ms-settings:privacy` at 4 DIP / 4 DIP. Two shapes on the reference, both measured, no conflict.
  §4 uses the large-tile numbers and says why.

Three places where the round brief is wrong and the reports are right:

1. There are **three** command Buttons in the rail footer (`NavOpenRootItem` `MainWindow.axaml:467-473`,
   `NavRefreshItem` `:474-480`, `NavExitItem` `:518-524`), not four. 关于 left the rail in Round 5
   item 2 (r6-shell §3.4; `ApplyRailText`, `MainWindow.axaml.cs:709-732`, labels exactly five rows).
2. AppPage's "two `ItemsControl`s, and why" comment is at `AppPage.axaml:144-150`, not 141-144
   (r6-panels §1).
3. The vertical headroom on the 应用 page is **47.67 DIP**, and it is a function of the data, not a
   constant: `window height request = 120.33 + 40·R` where R is the tallest column's row count
   (r6-panels §3.3). §4.3 restates it after the change.

### 0.3 One hazard that shapes every verification step below

r6-shell §0 records that at least one other agent was driving the same live process during
reconnaissance, and that the app navigated pages by itself between a capture and a UIA dump two
minutes later. **Every verification step in this document therefore re-captures; none of them
compares against a stored PNG from the recon session.**

---

## 1. Item 1 — 其他页面不要禁用搜索框，可以直接不显示

### 1.1 Decision

**Hide the field, do not remove it from the tree, and set `IsVisible` on the `Border`, not on the
`TextBox`.**

`src/TanMenu.Wpf/MainWindow.axaml.cs`, `ShowPageCore` (`:586-638`):

| Line(s) | Change |
|---|---|
| `:619-621` | Replace `var searchable = page == ShellPage.App; SearchField.IsEnabled = searchable; SearchBox.IsEnabled = searchable;` with the single write **`SearchField.IsVisible = page == ShellPage.App;`** |
| `:611-618` | Rewrite the comment. It currently argues that *both* writes are needed because "a plain `Border` has no disabled behaviour of its own"; after the change one write on the Border is the whole mechanism, and the reason the Border rather than the TextBox is the lever is that `SearchField` is the only element with `Grid.Column="1"` (`MainWindow.axaml:234`), i.e. it is what owns the centre column's slot — hiding only the TextBox would leave the bordered box and its `E721` glyph on screen |
| `:449-453` | Rewrite `OnKeyDown`'s remark. It says the filter "can no longer be TYPED there, since `ShowPage` **disables** the box". The sentence stays true with "hides"; the Escape branch at `:461-464` needs no change |

`src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` — **delete two rules and their prose**, because
`ShowPageCore:619-621` is their only producer and there is no other writer of
`SearchField.IsEnabled` / `SearchBox.IsEnabled` anywhere in `src/` (verified by grep this session —
the only remaining writes to either element are `ApplySearchFieldWidth`'s `Width`
(`MainWindow.axaml.cs:775`), `OnSearchFocusChanged`'s class toggle (`:781`) and
`ConsumePendingFocus`'s `Focus()` (`:844`)):

| Line(s) | Change |
|---|---|
| `:377-384` | delete `Border.tm-searchfield:disabled { Opacity 0.6 }` and its comment |
| `:394-402` | delete `TextBox.tm-search:disabled { Foreground, PlaceholderForeground → TextDisabledBrush }` and its comment |

`TextDisabledBrush` keeps a consumer (`TmTileButton`'s `^:disabled`, `Launcher.axaml:120-123`), and
`Fluent2TokenTests` only checks referenced → defined, so removing a reference is free
(`Fluent2TokenTests.cs:57-69`).

**Not changed:** `ApplySearchFieldWidth` (`MainWindow.axaml.cs:769-776`) keeps writing
`SearchField.Width` on every `ClientSize` change even while the field is hidden. That is inert — an
invisible element is never measured — and leaving it means the method needs no knowledge of which
page is showing.

### 1.2 Why

**(a) The centre column collapses to zero, margin included — decompiled, not assumed.**
`Avalonia.Layout.Layoutable.MeasureCore` (`Avalonia.Base 12.1.0`, ilspycmd) is
`if (base.IsVisible) { …; return new Size(num, num2); } return default(Size);`, and the
`num += thickness.Left + thickness.Right` term that adds `Margin` sits **inside** that branch
(r6-shell §1.3). So the hidden `SearchField` contributes 0 **and** its `Margin="12,0"`
(`MainWindow.axaml:239`) contributes 0. The `Auto` column measures 0.

**(b) Neither flanking group can move.** `TitleStripContent` is `ColumnDefinitions="*,Auto,*"`
(`MainWindow.axaml:213`); column 0's content is `HorizontalAlignment="Left"` in a column that always
starts at x = 0 (`:219`) and column 2's is `HorizontalAlignment="Right"` in a column that always ends
at the strip's right edge (`:291`). Two equal-weight star columns split the leftover, so hiding the
middle one merely widens both. Arithmetic at the live size: client 1692.67 DIP, field 575.5 + 24
margin = 599.5, each star 546.6 DIP today → **846.4 DIP if hidden**. The left group needs ≈100 DIP
(16 margin + 20 icon + 10 gap + "TanMenu"), the right group needs 140 (`CaptionSpacer`,
`MainWindow.axaml:303`). At the app's floor (`MinWidth="760"`) the field clamps to ≈253 DIP and each
star still gets ≈234 DIP > 140 — **there is no window width at which the caption spacer is squeezed
today and therefore none at which hiding the field relieves it** (r6-shell §1.3).

**(c) The field is the only page-dependent thing in the strip.** `ShowPageCore`'s complete write set
is `NavRail.SelectedIndex` (`:595`), `NavFooterRail.SelectedIndex` (`:596`), `PageHost.Content`
(`:597-599`), `PagePane.Classes.Set` (`:609` — the content pane, not the strip) and the two
`IsEnabled` writes this item replaces. The app icon (`:221-223`), the name (`:224-226`) and
`CaptionSpacer` (`:303`) carry no bindings and no code-behind writer; `CaptionSpacer` appears exactly
once in the repository (r6-shell §1.5).

**(d) Every behaviour bound to the field survives.** The two-way `Filter` binding
(`MainWindow.axaml:260`, DataContext set at `MainWindow.axaml.cs:110`) is unaffected by visibility, so
`ClearFilter` / `HandleSearchEscape` still work and the box shows the right text when 应用 returns.
Enter → `HandleSearchEnter` (`cs:792-795` → `LauncherViewModel.cs:225-229`), Escape
(`cs:796-799` → `LauncherViewModel.cs:233-239`) and Down → `AppPage.FocusFirstTile`
(`cs:800-802` → `AppPage.axaml.cs:334-341`) are **exactly as unreachable on 设置 when hidden as when
disabled** — neither a hidden nor a disabled element receives key events. Escape on 设置 is already
handled by `MainWindow.OnKeyDown` (`cs:455-467`), which routes straight to `_host.Hide()`.
`ConsumePendingFocus` already returns early unless `PageHost.Content` is `_appPage`
(`cs:838-839`) and `ShowPageCore:630` retries it on the way back, so the deferred-focus path needs no
change (r6-shell §1.4).

**(e) Focus, if the field is hidden while focused: cleared to the top level, and it is the same end
state the current code produces.** `Avalonia.Input.InputElement.OnPropertyChanged` (decompiled) reacts
to `Visual.IsVisibleProperty` going false while `IsKeyboardFocusWithin` by calling
`FocusManager.ClearFocusOnElementRemoved(focused, VisualParent)`, which does
`if (Current == removedElement) Focus(null)`, ending in `SetFocusedElement(null, …)`. The current
path reaches the same place from the other side: `InputElement.IsEffectivelyEnabled`'s setter does
`if (focusManager.GetFocusedElement() == this) focusManager.Focus(null)`. **This item introduces no
new focus case** (r6-shell §1.4). The accent underline self-clears with it: the focus clear raises
`LostFocus` (`MainWindow.axaml:263`) → `OnSearchFocusChanged` (`cs:780-781`) removes the `focused`
class that `Launcher.axaml:360-362` and `374-376` key off, so no stale bar can survive.

**(f) The current "greyed" look has a third contributor this repo never owned, and hiding retires it.**
Beyond the two rules being deleted, Avalonia's own Fluent `TextBox` `:disabled` styling repaints
`Border#PART_BorderElement` with a grey slab — measured on `r6-app-now.png` at image x 898..1694,
y 14..58, coinciding **exactly** with `SearchBox`'s UIA rect `3469,1238,797,45`, at `(219,217,221)`
over a `(245,240,247)` backdrop, which solves to black at α = 0.1997 ≡ 20 %. This repo overrides only
the `:focus` variant (`Launcher.axaml:403-423`) and never the `:disabled` one, so today's disabled
field is partly styled by us and mostly styled by the framework (r6-shell §1.2). Hiding removes the
whole question rather than adding a fourth override.

### 1.3 Blast radius

**Files touched:** `MainWindow.axaml.cs` (one line, two comments), `Themes/Fluent2/Launcher.axaml`
(two rules and their comments deleted).

**Re-fit trigger list:** unchanged, five (§5.1). The title strip is a fixed 48 DIP row
(`MainWindow.axaml:194`) and is not an input to the fit — `RefitToContent` measures `_appPage`
(`MainWindow.axaml.cs:349`) and adds `ChromeHeightDip` as a constant.

**Fit arithmetic:** unchanged.

**Tests:** none. No test anywhere references `SearchField`, `SearchBox`, `tm-searchfield` or `tm-nav`
(grep over `tests/`, r6-shell §1.2).

**Config contract:** unchanged.

**Behaviour change a reader should know about:** on 设置 the `Edit` node with
`AutomationId="SearchBox"` **disappears from the UIA tree** instead of being present with
`enabled=False`. That is the better accessibility signal for a control that does not apply to the
page, and it is what §1.4 asserts on. No test pins it either way.

**Documented invariants that become false:** `MainWindow.axaml.cs:611-618` and `:449-453` (both
rewritten above); `Launcher.axaml:377-381` and `:394-398` (deleted with their rules).

### 1.4 Verification (no keyboard, no mouse)

1. **Source.** `grep -rn "tm-searchfield:disabled\|tm-search:disabled\|SearchBox.IsEnabled\|SearchField.IsEnabled" src/`
   returns **nothing**.
2. **UIA, 应用.** Dump the window's raw tree — **uncapped** (see step 3 for why a depth cap invalidates
   the dump). An `Edit` node with `AutomationId="SearchBox"` must be present with `enabled=True`.
3. **UIA, 设置.** Navigate with `SelectionItemPattern.Select()` on `NavSettingsItem` (UIA patterns are
   programmatic and work where `SendInput` no-ops). Re-dump: **no `Edit` node with
   `AutomationId="SearchBox"`.**

   *Corrected 2026-08-03, after item 1's review.* The bar as first written — "no `Edit` node anywhere
   in the tree" — is one a **correct build fails**: 设置 owns `Edit` nodes of its own. Measured on the
   shipped build, an uncapped walk of that page finds 558 nodes and two of them are `Edit`s —
   `BehaviorCard`'s `HotkeyBox` at raw depth 20 (`enabled=False`, UIA rect `3814,2761 144x29`) and a
   realized `PART_TextBox` at raw depth 30 — while `Edit` nodes with `AutomationId="SearchBox"` number
   0, which is the assertion this item is actually making. A second run of the same page measured 286
   nodes and one `Edit`: **the realized node set is not fixed either**, so the bar must name the
   `AutomationId` rather than count `Edit`s.

   **Walk the raw tree uncapped** (a runaway guard at 200 is fine). Item 1's first instrument capped at
   `if ($depth -gt 14) { return }`; on 设置 that reaches **100 of the 558 nodes** and cuts everything
   from depth 16 down (depth histogram, measured), so it reports "0 `Edit` nodes" on a page that has
   two. A capped walk that happens to answer the question correctly still says far less than it looks
   like it says.
4. **The centring proof, and it needs no pixels.** In both dumps, the title strip's left `Image`
   (20x20 DIP) and `CaptionSpacer` (140 DIP) must report **byte-identical screen rects**. If either
   moves, the collapse of the `Auto` column disturbed a flank and §1.2(b) is wrong.
5. **Pixels, 设置.** `PrintWindow` capture. Scan image row y = 36 across x = 300..2350 (between the app
   name's ink and the caption cluster). Today that scan crosses a `(230,226,233)` hairline pair at
   x ≈ 848 / 1710 and a `(219,217,221)` slab from x 898..1694. After the change **no run of ≥ 8
   consecutive pixels may differ from the linear interpolation between its neighbours 100 px away by
   more than 2 levels on any channel** — i.e. only the Mica gradient is left.
6. **Focus — state honestly what is and is not provable here.** *Rewritten 2026-08-03, after item 1's
   review: the first version set a bar a correct build fails, and rested on a reading that cannot
   carry the argument.* Two rules, both measured on the shipped build:

   * **A rail-driven navigation moves keyboard focus to the rail row itself, before `ShowPageCore`
     runs.** `SelectionItemPattern.Select()` on `NavSettingsItem` leaves that row reporting
     `HasKeyboardFocus = True` — measured in three consecutive single-pass readings, corroborated by
     the system-wide `FocusedElement` naming `NavSettingsItem` in our own pid. The row sat at **raw
     depth 16** in those readings and at depth 15 in others: **the rail rows' raw depth is not a
     constant**, which is the second reason no walk may carry a cap. So "after step 3 the focused element
     must be the window (or none)" is **not** the bar — a correct build parks focus on the row. The
     bar is that no `Edit` with `AutomationId="SearchBox"` is present (step 3) and that step 5 still
     passes. It also means step 3's navigation does **not** exercise the "hidden while focused" path
     of §1.2(e): focus has already left `SearchBox` before `IsVisible` is written. Do not claim it
     does.
   * **Never read `HasKeyboardFocus` without an activation probe in the same pass.**
     `ControlAutomationPeer.HasKeyboardFocusCore()` returns `Owner.IsFocused` (Avalonia.Controls
     12.1.0, decompiled), and `TopLevel.PlatformImpl_LostFocus` calls
     `KeyboardDevice.SetFocusedElement(null, …)` when the platform window loses focus — so **every**
     element in the window answers `False` while another process holds the foreground, which on this
     machine happens within about 3 s of any activation (traced: our window active at t = 1.1-2.6 s,
     pid 16176 takes the foreground at t = 3.2 s and every answer flips to `False`). A bare "no
     element reports `HasKeyboardFocus`" reading therefore measures **which window was active**, not
     the app's focus logic. Probe activation in the same pass with the non-client `SystemMenuBar`
     element (it reports focus only while the window is active) or with the system-wide
     `FocusedElement`'s pid.

   Do not substitute a capture: with the field hidden, a stale `focused` class and a cleared one are
   pixel-identical, so a capture cannot distinguish them. What a capture *can* pin is the round trip
   应用 (focused) → 设置 → 应用, where the accent underline must be gone — measured 2472 accent pixels
   (image rows y 54-56, `(0,103,192)`, the field's own band) before and **0** after. Hold that
   invariant, but do not read it as isolating the hide: deactivation clears focus too.

   The basis for the "hidden while focused" behaviour is therefore decompilation, all three links
   re-read against the shipped Avalonia 12.1.0: `InputElement.OnPropertyChanged:1517-1531`
   (`IsVisible` false while `IsKeyboardFocusWithin` → `ClearFocusOnElementRemoved`),
   `FocusManager.ClearFocusOnElementRemoved:116-134` (→ `Focus(null)`), and
   `KeyboardDevice.SetFocusedElement:117-182`, whose line 165 raises `LostFocusEvent` on the **old**
   element even when the new one is `null` — which is what drops the `focused` class and with it the
   underline. Plus the fact that the shipping `IsEnabled` path already produces the same end state.
7. `dotnet test` green with no test file edited for this item.

---

## 2. Item 2 — 除了应用页，其他页面使用和窗口相同的材质面板

### 2.1 Decision

**The pane paints nothing on any page except 应用; the window's own surface shows through. The class
is flipped from `settings` to `app` so that "not 应用" is the default rather than the exception.**

`src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml:692-702` — two selectors, same values on the 应用 side:

```xml
<Style Selector="Border.tm-page-pane.app">
  <Setter Property="Background"      Value="{DynamicResource SurfacePageBrush}" />
  <Setter Property="BorderBrush"     Value="{DynamicResource StrokeControlBrush}" />
  <Setter Property="BorderThickness" Value="1,1,0,0" />
  <Setter Property="CornerRadius"    Value="{DynamicResource RadiusPaneTopLeft}" />
</Style>
<Style Selector="Border.tm-page-pane:not(.app)">
  <Setter Property="Background"      Value="Transparent" />
  <Setter Property="BorderThickness" Value="0" />
  <Setter Property="CornerRadius"    Value="0" />
</Style>
```

`src/TanMenu.Wpf/MainWindow.axaml.cs:609` —
`PagePane.Classes.Set("app", page == ShellPage.App);`

**`Background` is `Transparent`, never omitted.** Two reasons, one certain and one standard-but-
unverified-in-this-tree: it states the intent where a missing setter would read as an oversight, and a
null-background `Border` is not hit-testable in Avalonia, so the gaps between settings cards would
stop absorbing pointer input (r6-material §2, flagged there as standard Avalonia semantics rather
than a measurement). `Value="Transparent"` is a named colour, not a hex literal, so
`Fluent2TokenTests.LauncherStyles_ContainNoColourLiterals` (`Fluent2TokenTests.cs:43-55`) passes.

**Both looks stay styles, never local attributes on the Border.** `Launcher.axaml:680-691` records the
reason and it is unchanged by this item: a class selector's setters land at
`BindingPriority.StyleTrigger`, which cannot override a `LocalValue`, so a local `Background` plus a
class toggle would silently never switch. The `Border` at `MainWindow.axaml:582-587` carries no local
paint properties today and must keep carrying none.

**The two selectors stay mutually exclusive by construction** (`:not(.app)` against `.app`), so the
result does not depend on rule order — the property Round 4 deliberately built in and this item keeps.

**Nothing else on the 设置 page changes.** Verified inert (r6-material §5): the root `ScrollViewer`
(`SettingsPage.axaml:50`) sets no `Background` and Avalonia 12.1's Fluent `ScrollViewer` ControlTheme
sets it to `ImmutableSolidColorBrush(16777215u)` = `0x00FFFFFF` = transparent (ilspycmd over
`Avalonia.Themes.Fluent.dll` 12.1.0); its UIA rect `2654,1296 2467x780` is the pane's rect exactly, and
the colour between cards measures the pane token to the level. `CardStack` (`:51`) sets no background.
`tm-page-title` (`Launcher.axaml:1021-1025`) and `tm-group-head` (`:1011-1016`) set only
size/weight/line-height/margin; Light `TextPrimaryBrush` over the pane computes 25.8 and **measures
`(26,26,26)`**, over Mica it computes `(26,25,26)` — a 0-1 level move — and Dark `TextPrimaryBrush` is
fully opaque and cannot move.

### 2.2 Why — and the supersession, recorded rather than implied

**Round 4 §5.1(d) and §7.1 are overruled.** Round 4 ruled that 设置 keeps `SurfaceBaseBrush` because
"our settings cards use `SurfaceLayerBrush`, which composites to `#FBFBFB` on a 243 pane but to
`#FFFFFF` on a white pane (Δ0) — a card that is literally the same colour as the page it sits on"
(Round 4 §5.1(d)), and §7.1 offered "both white" as the alternative and defaulted to no.

**That argument is correct about white and does not transfer to Mica.** From r6-material §3.1
(*Measured*) and §3.2 (*Composited*) — the unbolded rows are arithmetic over the card tokens' alphas
and a Mica backdrop that was itself measured, because no settings card on Mica existed to photograph
while the pane was still opaque:

| Light | card fill − backdrop | card border − backdrop |
|---|---|---|
| over `SurfaceBaseBrush` (today's 设置) | **(8,8,8)** measured | **(−14,−14,−14)** measured |
| over Mica at the window's left edge | (12,10,0) *computed* | (−14,−14,−15) *computed* |
| over Mica at the window's right edge | (6,11,7) *computed* | (−14,−14,−14) *computed* |
| `tm-searchfield` on real Mica `(244,240,248)` | **(8,11,5)** measured | **(−14,−14,−15)** measured |

| Dark | card fill − backdrop | card border − backdrop |
|---|---|---|
| over `SurfaceBaseBrush` (today) | **(8,8,8)** measured | **(16,16,16)** measured |
| over Mica `(30,32,38)` | (8,8,6) *computed* | (16,16,15) *computed* |
| over Mica `(33,31,35)` | (8,8,7) *computed* | (16,16,16) *computed* |
| `tm-searchfield` on real Mica `(33,31,35)` | **(8,9,7)** measured | **(16,16,16)** measured |

Mica's measured luminance under this window is **238-247 Light / 30-34 Dark**, i.e. it straddles
`SurfaceBaseBrush` (243 / 32), not `SurfacePageBrush` (255 / 44). The card-to-page separation is
therefore **unchanged to within 2-4 levels** and the border's is unchanged to within 1. Round 4's Δ0
was a property of white, and white is not what the pane becomes.

**And the measurement is not a projection.** `Border.tm-searchfield` (`Launcher.axaml:340-347`) uses the
*same three tokens* as `Border.tm-card` (`SurfaceLayerBrush` + `StrokeControlBrush` 1 DIP +
`RadiusControl`) and already sits directly on this window's real Mica in the title strip. Measured:
Light backdrop `(244,240,248)` → fill `(252,251,253)`, border `(230,226,233)`; Dark backdrop
`(33,31,35)` → fill `(41,40,42)`, border `(49,47,51)`. Predicted from the token alphas:
`(252,251,253)`/`(230,226,233)` and `(41,39,42)`/`(49,47,50)` — **matching to within 1 level on every
channel**. A rendered instance of a settings card on Mica is on screen right now (r6-material §3.1).

**The reference does exactly this.** Windows Settings has **no content pane at all**: at image y = 470
the capture reads exactly `(243,242,249)` at x = 20, 100, 200, 300, 400, 450, 480, 490 and 499 — nine
samples, one value, straight through where a pane edge would be. Nav column and content column are one
continuous Mica surface with no border, no corner and no seam (r6-material §4.1). Its cards use the
*same two token values we do*: border = backdrop × 0.940-0.943 ⇒ ≈ `#0F000000` = our
`StrokeControlBrush`; fill α ≈ 0.667-0.70 white against our `SurfaceLayerBrush`'s 0.70196. Its header
search box measures `(252,251,253)` / `(230,227,234)` against our `(252,251,253)` / `(230,226,233)`.

**The seam this removes, as a number** (r6-material §4.4). The vertical rail→pane step at image
x 82 → 83, Light: `(238,241,255)` → `(243,243,243)` = Δ`(+5,+2,−12)` at y = 150, Δ`(+5,+1,−12)` at
y = 400, Δ`(+4,+1,−10)` at y = 700. Dark: Δ`(+2,0,−6)` … `(+2,0,−7)`. The horizontal strip→pane step at
y 71 → 72, x = 1500: Light Δ`(−2,+4,−4)`, Dark Δ`(−2,+1,−2)`. Small per channel, but a straight,
780 px, perfectly vertical line between a neutral grey and a tinted gradient. **In Dark it is close to
invisible — the payoff is almost entirely a Light-theme payoff, and the design says so rather than
overselling it.**

**Why the class is flipped to `app`.** The user's sentence makes 应用 the exception
(*除了应用页，其他页面…*). `ShellPage` has exactly two members (`Services/IShellNavigator.cs:3-7`), so
"not 应用" and "== 设置" select the same thing today — but the current encoding makes
white-with-a-border the **default**, so a hypothetical third page would silently come up white, which
is the opposite of what was asked. The flip costs one word in two selectors and one word in one line,
and it makes the default match the instruction (r6-material §7). **Ruled: flip.**

### 2.3 The Mica-refused fallback — decided, and how it is selected

**The fallback is the window's own `Background`, and it already exists. No new code.**

`ApplyBackdrop` (`MainWindow.axaml.cs:526-533`) paints the **`Window`**, not the pane:

```csharp
if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
    return;

_backdropFallback ??= this.GetResourceObservable("SurfaceBaseBrush")
    .Subscribe(new AnonymousObserver<object?>(v => Background = v as IBrush ?? Brushes.White));
```

So on a machine that refuses Mica the window's `Background` becomes `SurfaceBaseBrush` and everything
transparent above it shows that colour — which is already how the rail (`MainWindow.axaml:328`) and the
title strip (`Tokens.axaml:269`, `TitleBarBackgroundBrush = Transparent`) work today. **A `Transparent`
settings pane on such a machine therefore renders flat `#F3F3F3` Light / `#202020` Dark — pixel-
identical to what 设置 looks like today**, with cards at `(251,251,251)`/`(40,40,40)` and borders at
`(229,229,229)`/`(48,48,48)`. The item degrades to the status quo, not to a hole.

**How the fallback is selected, and when it is re-evaluated.** `ApplyBackdrop` is called from
`OnOpened` only (`MainWindow.axaml.cs:135`), and **`OnOpened` re-fires on every un-hide** — the file
says so in two places and the mechanism is `WindowHost.ShowAndActivate` calling `Show()` again
(`MainWindow.axaml.cs:149`, `:174-175`, `:334-336`). This launcher hides to the tray constantly, so:

* Mica granted at every summon → the early return fires, `_backdropFallback` is never created, the
  window keeps the XAML `Background="Transparent"` (`MainWindow.axaml:10`), the pane shows Mica. ✔
* Mica lost mid-session (user turns off transparency effects) → picked up at the **next summon**, when
  `ApplyBackdrop` runs again, sees non-Mica and creates the subscription. Between the loss and the next
  summon the pane is unpainted — but so are the rail and the title strip, **today**. This item enlarges
  a pre-existing window-of-defect region; it does not create one.
* Mica restored later → **not** picked up, because `_backdropFallback ??=` memoises and nothing ever
  disposes it. Pre-existing (`MainWindow.axaml.cs:531`, and `:43` documents the memoisation
  deliberately), unchanged by this item, and out of scope.

**The pane must not paint its own fallback.** A second fallback brush on the pane would be a second
source of truth for "what colour is this window's surface", and it would be wrong in exactly the case
it exists for: the window's surface is `Window.Background`, whatever that resolves to. One source,
already correct, already theme-following (the resource-observable form Round 4 §5.1(f) shipped for
precisely this reason).

**`SurfaceBaseBrush` stays defined and stays referenced** after the pane rule stops naming it — three
consumers survive, verified by grep this session: `Border.tm-toast` (`Launcher.axaml:541`),
`Dialogs/MessageBoxWindow.axaml:9`, and `ApplyBackdrop` (`MainWindow.axaml.cs:531`). No test would have
caught its removal (`Fluent2TokenTests` never checks defined → referenced), which is a reason to state
this here rather than a reason to relax.

### 2.4 Blast radius

**Files touched:** `Themes/Fluent2/Launcher.axaml` (two selectors, plus the whole 设置 half of the pane
block comment), `MainWindow.axaml.cs` (one line plus its comment), `MainWindow.axaml` (the pane block
comment), `Themes/Fluent2/Tokens.axaml` (one sentence).

**Re-fit trigger list:** unchanged, five (§5.1). **Fit arithmetic:** unchanged, and it cannot be
reached — `RefitToContent` (`MainWindow.axaml.cs:344-350`) returns before measuring anything unless
`PageHost.Content` is `_appPage`, and all five call sites (`:229`, `:392`, `:420`, `:504`, `:637`) pass
through that guard. `PaneBorderDip = 1` (`:388`) and `WindowHost.ChromeHeightDip = 49`
(`WindowHost.cs:52`) encode the **应用** pane's borders only, and this item leaves the 应用 rule
untouched, so both stay correct as written.

**The detach guard is unaffected.** The class toggle lands on `PagePane`, the always-attached host,
never inside `_appPage` — same reasoning Round 4 recorded at `MainWindow.axaml.cs:601-608`, still true.

**Tests:** **none change**, and none guards this. `LauncherStyles_ContainNoColourLiterals` greps the
whole of `Launcher.axaml` **including prose**, so the rewritten block comment must quote decimal RGB
triples and never a hex literal — exactly the constraint the existing comment already works under
(`Launcher.axaml:639-641` says so in the file). The other three facts are unaffected. **Nothing in CI
would catch a regression here; the only net is §2.5.**

**Config contract:** unchanged. **NativeAOT:** unchanged (two `Setter`s over `DynamicResource`).

**Comments and docs that become false and must be rewritten against the code:**

| Where | What goes stale |
|---|---|
| `MainWindow.axaml:529` | the pane block's headline, "Content pane: a solid surface, not Mica" |
| `MainWindow.axaml:538-547` | "设置 keeps `SurfaceBaseBrush` … because our settings cards would otherwise composite to the exact colour of a white page" — the arithmetic is right, the conclusion was drawn against white |
| `MainWindow.axaml:581` | "设置 still has no radius at all under the `.settings` rule" |
| `MainWindow.axaml.cs:601-608` | the `ShowPageCore` comment naming the two looks |
| `Launcher.axaml:630`, `:665-679` | the block headline and the whole 设置 half, including the `1.png` reading and the "Verified after the change" paragraph |
| `Tokens.axaml:53-54` | `SurfacePageBrush`'s "设置 keeps `SurfaceBaseBrush` (see that pane style's own remarks for the measured reason)" |
| `docs/…/2026-08-02-win11-shell-round4-design.md` §5.1(d), §5.2, §5.4 step 7, §7.1 | the reversed ruling and its acceptance criterion, which literally demands the pane measure `(243,243,243)`. Mark each with a **"Superseded by Round 6"** box naming this document, in the shape Round 4 already uses for its own Round 5 supersessions (§2.1, §2.5) |

### 2.5 Verification — and what `PrintWindow` can and cannot prove

**What it can prove: continuity.** Both seams must disappear, and the criterion is quantitative.

1. `PrintWindow` capture of the 设置 page, Light. **Vertical seam:** sample image x = 82 and x = 83 at
   y = 150, 400 and 700. `|Δ|` must be **≤ 1 on every channel at every y**. Today it is
   `(+5,+2,−12)` / `(+5,+1,−12)` / `(+4,+1,−10)`.
2. **Horizontal seam:** sample y = 71 and y = 72 at x = 400, 1500 and 2400. `|Δ|` must be **≤ 1 on
   every channel**. Today at x = 1500 it is `(−2,+4,−4)`.
3. **Cards still read.** In the same capture, at a y through a card's fill, sample the card fill, the
   card's left border column, and the pane 40 px to the left of the card. Require, Light:
   `fill − backdrop` positive on at least two channels and within `(0..14)` on each;
   `border − backdrop` = `(−14 ± 1, −14 ± 1, −14 ± 2)`. Dark: `fill − backdrop` = `(8 ± 2, 8 ± 2,
   6 ± 2)`; `border − backdrop` = `(16 ± 1, 16 ± 1, 15 ± 2)`. **Assert the relationships, never the
   absolute RGBs** — the absolutes move with the wallpaper and the window's screen position.
   Note explicitly: the fill's blue-channel delta can legitimately reach **0** near the window's left
   edge (*computed*, r6-material §3.2 — that section's heading says "Composited", and it is a ceiling
   effect: Mica measures 255 blue there, which a white-at-alpha fill cannot exceed). That is the
   reference's own behaviour too and is not a failure; the border is what draws the card and it is
   Δ14-16 everywhere.

**What it cannot prove: that the surface is Mica.** If the platform refused Mica, `ApplyBackdrop`
paints `SurfaceBaseBrush` on the window, the pane is transparent over it, and **steps 1-3 pass
identically**. A continuity test proves "the pane matches the window's surface", which is exactly the
item, but it says nothing about which surface that is. So:

4. **Disambiguate, and report which case was observed rather than iterating.** In the same capture,
   sample the title strip at y = 6 across x = 100, 700, 1300, 1900, 2400.
   * If those five samples vary by **> 2 levels on any channel** across the row, Mica is live. The pane
     at y = 400 must then show **the same kind of variation** across the same five x — not a flat
     value.
   * If they are flat within 1 level, Mica was refused. The correct assertion is then the **fallback**
     one: pane, rail and strip must all read exactly `(243,243,243)` Light / `(32,32,32)` Dark with no
     step at either seam.
   * Both are passes. Record which.
5. **Dark.** Write `"appearance": "Dark"` into `Data\config.json`, restart, repeat 1-4, restore the
   file byte-identically. Expect the seam deltas to be small before the change too (max Δ7 in blue) —
   this step confirms no regression, it is not where the payoff is.
6. **The refused case, deliberately.** Back up `Data\config.json` first: a hide/summon cycle routes
   through `WindowHost.RestorePlacement`, which can persist a new window position. Then set
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency = 0`, hide the
   window and re-summon it (that re-raises `Opened`, which is what re-runs `ApplyBackdrop` — §2.3).
   Capture: pane, rail and strip must all read `(243,243,243)` with no step at either seam, and a card
   must read `(251,251,251)` behind a `(229,229,229)` outline. Restore `EnableTransparency` and the
   config backup.

   > **Corrected 2026-08-03, while implementing item 2: this step does not work on this machine, and
   > the item shipped with its refused path UNTESTED.** The procedure above was carried out in full and
   > **the platform kept granting Mica throughout**, so it never reached the branch it exists to
   > exercise. What was actually done, in order: `EnableTransparency` read as `1` (DWord) and set to
   > `0`; the window hidden with a posted `WM_CLOSE` (which `MainWindow.OnClosing` cancels into
   > `_host.Hide()` — confirmed, `IsWindowVisible` went `True → False` with the process alive) and
   > re-summoned by re-launching the exe, so `Opened` and therefore `ApplyBackdrop` genuinely re-ran;
   > then, when that changed nothing, `WM_SETTINGCHANGE`/`ImmersiveColorSet` broadcast to
   > `HWND_BROADCAST` and the process killed and restarted cold, so `ApplyBackdrop` ran on a brand-new
   > window. **All three captures still photograph the wallpaper gradient**: title strip y = 6 reads
   > `(239,241,253) (243,240,249) (245,240,248) (246,239,245) (244,240,247)` across x = 100..2400 — a
   > 14-level spread — and the rail at x = 40, y = 400 reads `(238,241,255)`, not the flat
   > `(243,243,243)` the fallback would paint. `ActualTransparencyLevel` was therefore still
   > `Mica`, `ApplyBackdrop` still took its early return, and `_backdropFallback` was never created.
   > `EnableTransparency` was restored to `1` (DWord) and re-broadcast.
   >
   > **Conclusion to carry forward, and it is a limitation, not a pass.** On this machine
   > (Windows 11 26xxx) DWM does not withdraw the Mica backdrop when `EnableTransparency` is cleared in
   > the registry, so **the refused path cannot be exercised here at all** and nobody should write
   > that it was. What *is* established: the mechanism the fallback relies on — a `Transparent` element
   > showing `Window.Background` — is not a new claim, because the rail (`MainWindow.axaml`) and the
   > title strip (`TitleBarBackgroundBrush = Transparent`, `Tokens.axaml`) already work exactly that
   > way, and steps 1 and 2 measured the pane continuous with both to `(0,0,0)` at every sampled point.
   > The untested part is narrower than the whole step: it is whether `ActualTransparencyLevel` reports
   > non-`Mica` on a machine that really refuses it. That is pre-existing shipped code
   > (`MainWindow.axaml.cs`, unchanged by item 2), not something this item introduced.
7. **Source.** `grep -rn 'Classes.Set("settings"' src/` returns nothing;
   `grep -n "tm-page-pane" src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` shows `.app` and `:not(.app)`.
   `grep -n "#[0-9A-Fa-f]\{3,8\}" src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` returns nothing.
8. **The 应用 page is unchanged.** Navigate back and re-capture: pane `(255,255,255)`, inner border
   column `(240,240,240)`, the top-left arc still present, and the app's fit log line byte-identical to
   before the change **for items 1-3** (item 4 changes it — §5.2).
9. **One thing to look at with a human's eyes, once.** Avalonia's Fluent `ScrollBar` expands only on
   pointer-over, and on `IsExpanded` its theme swaps `Grid#Root`'s `Background` to
   `ScrollBarBackgroundPointerOver` and `Rectangle#TrackRect`'s `Fill`/`Stroke` to
   `ScrollBarTrackFillPointerOver` / `ScrollBarTrackStrokePointerOver` (decompiled; the references are
   at `fluent-xaml.txt:21787, 21805, 21816`). **Those three keys' values could not be resolved** — they
   are not defined in the decompiled resource type, and this environment has no pointer, so the
   scrollbar is invisible in every capture (`col 2541` reads the pane colour for all 780 rows). If any
   is opaque, an expanded scrollbar paints a band down the right edge of a Mica settings page.
   See §6.2 for the ruling and the remedy if it turns out to matter.

---

## 3. Item 3 — 导航条中图标的选中状态样式和任务管理器不一样

### 3.1 What was measured, ours against Task Manager, side by side

Every row is a measurement from `r6-shell` §3.2, taken with both windows' rails occupying **the same
image columns 11..82** so the numbers are directly comparable. Rows marked **no change** are properties
where no difference was measured and which this design therefore does not touch.

| Property | **Ours, measured** | **Task Manager, measured** | Decision |
|---|---|---|---|
| Rail pane | image x 11..82 = 72 px = 48 DIP | identical | **no change** |
| Selected row box | 60 x 54 px = **40 x 36 DIP** | 60 x 54 px = **40 x 36 DIP** | **no change** |
| Row pitch | 60 px = 40 DIP | 60 px = 40 DIP | **no change** |
| Row corner arc | first-ink Δx 4,2,1,0 (`RadiusControl` = 4 DIP) | 4,2,1,1,0 — indistinguishable at 150 % | **no change** |
| Unselected row fill | none | none | **no change** |
| Accent hue, Light | `(0,103,192)` = `#0067C0` | `(0,103,192)` — **identical** | **no change** |
| Accent hue, Dark | `(76,194,255)` = `#4CC2FF` | `(76,194,255)` — **identical** | **no change** |
| **Indicator height** | y 96..143 solid, 93..146 with the arcs → **the full 36 DIP row** | y 163..186 = 24 px = **16 DIP**, centre 174.5 = row-box centre (148+201)/2 = **174.5 exactly** | **→ 16 DIP, centred** |
| **Indicator shape** | square-ended strip, clipped only by the row's 4 DIP corners | **pill**: 5 px wide at y 164..185, narrowing to 3 px (x 19..21) at y 163 and y 186 ⇒ cap radius ≈ 2.5 px ≈ 1.67 DIP | **→ pill, `CornerRadius` 1.5** |
| **Indicator displaces content** | **yes** — icon `TextBlock` UIA x = **2608** on the selected row against **2606** on all four unselected rows (2 px = 1.33 DIP) | **no** — driving `SelectionItemPattern.Select()` on `PerformanceItem` and re-capturing, the 进程 glyph measures ink x 37..56, cx 46.5 in **both** states | **→ overlay, zero layout cost** |
| **Indicator width** | 4 px = 2.67 DIP rendered from a declared 3 DIP `BorderThickness` | 5 px = 3.33 DIP | **authored value unchanged at 3 DIP** — see §3.3 |
| **Selected row fill** | Light `(233,235,248)` on `(239,241,253)` → α **6.4/255**; Dark `(40,42,47)` on `(30,32,38)` → α **11.3/255**. Both are exactly `SubtleFillTertiaryBrush` (`Tokens.axaml:64`, `:107`) | Light `(234,234,234)` on `(243,243,243)` → α **9.4/255**; Dark `(44,44,49)` on `(31,32,36)` → α **14.5/255** | **→ `SubtleFillSecondaryBrush`** (α 10/255 Light, 15/255 Dark) |
| **Glyph contrast** (identical in both states in **both** apps) | Light darkest `(92,93,98)` → α 0.605 ⇒ `#9B` = `TextSecondaryBrush`; Dark `(208,209,210)` ⇒ `#C8FFFFFF` | Light darkest `(25,25,25)` → α 0.893 ⇒ `#E4`; Dark pure `(255,255,255)` | **→ `TextPrimaryBrush`, scoped to the rail** |

Two of those TM values are, to the level, tokens this app already defines:
`#0A000000`/`#0FFFFFFF` = `SubtleFillSecondaryBrush` (`Tokens.axaml:63`, `:106`) against TM's measured
α 9.4 / 14.5, and `#E4000000`/`#FFFFFFFF` = `TextPrimaryBrush` (`Tokens.axaml:66`, `:109`) against TM's
measured α 0.893 / 1.000. **Neither value is chosen; both are the token whose value was measured on the
reference.**

### 3.2 Decision

**(a) A new `ControlTheme` for the two rail `ListBoxItem`s.** The current selected look is three
Setters on a template part (`Launcher.axaml:576-580`), and a `/template/ ContentPresenter` Setter can
only set properties of a part that already exists. A 16 DIP, vertically centred, pill-shaped indicator
that does **not** displace the icon cannot be a `BorderThickness`: a border always runs the full edge
and always consumes layout (r6-shell §3.4). It needs a template with an overlay element — which is
WinUI's own `NavigationViewItem` shape and the shape the measurements above describe.

In `Launcher.axaml`'s `<Styles.Resources>`, immediately after `TmNavRowButton` (`:273-307`), so the two
rail row kinds sit together:

```xml
<ControlTheme x:Key="TmNavRowItem" TargetType="ListBoxItem">
  <Setter Property="Background"                Value="Transparent" />
  <Setter Property="Padding"                   Value="12,10" />
  <Setter Property="Margin"                    Value="4,2" />
  <Setter Property="CornerRadius"              Value="{DynamicResource RadiusControl}" />
  <Setter Property="HorizontalContentAlignment" Value="Center" />
  <Setter Property="VerticalContentAlignment"   Value="Center" />
  <Setter Property="FocusAdorner">
    <Template>
      <Border BorderBrush="{DynamicResource FocusStrokeOuterBrush}"
              BorderThickness="2" CornerRadius="{DynamicResource RadiusControl}" Margin="-2" />
    </Template>
  </Setter>
  <Setter Property="Template">
    <ControlTemplate>
      <Panel>
        <Border x:Name="Root"
                Background="{TemplateBinding Background}"
                CornerRadius="{TemplateBinding CornerRadius}" />
        <ContentPresenter Content="{TemplateBinding Content}"
                          Padding="{TemplateBinding Padding}"
                          HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                          VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}" />
        <Border x:Name="SelectionIndicator"
                Background="Transparent"
                Width="3" Height="16" CornerRadius="1.5"
                HorizontalAlignment="Left" VerticalAlignment="Center"
                IsHitTestVisible="False" />
      </Panel>
    </ControlTemplate>
  </Setter>
  <Style Selector="^:pointerover /template/ Border#Root">
    <Setter Property="Background" Value="{DynamicResource SubtleFillSecondaryBrush}" />
  </Style>
  <Style Selector="^:pressed /template/ Border#Root">
    <Setter Property="Background" Value="{DynamicResource SubtleFillTertiaryBrush}" />
  </Style>
  <Style Selector="^:selected /template/ Border#Root">
    <Setter Property="Background" Value="{DynamicResource SubtleFillSecondaryBrush}" />
  </Style>
  <Style Selector="^:selected /template/ Border#SelectionIndicator">
    <Setter Property="Background" Value="{DynamicResource AccentDefaultBrush}" />
  </Style>
</ControlTheme>
```

Every number in it is carried over or measured:
`Padding 12,10` and `Margin 4,2` are the current values (`Launcher.axaml:568-569`) and are deliberately
identical to `TmNavRowButton`'s (`:276-277`) — the two row kinds must measure alike, which is the file's
own stated principle (`:263-266`). `CornerRadius = RadiusControl` is today's value. `Width 3` is today's
authored `BorderThickness` value (`:579`). `Height 16` is TM's measured 24 px / 1.5. `CornerRadius 1.5`
is half the width, and TM's measured cap radius is ≈ 1.67 DIP. `HorizontalAlignment="Left"` is flush
with the row box's left edge, which both apps already are (ours image x 17 = box left 17; TM's x 18 =
box left 18). The three numbers stay literals in the rule rather than becoming tokens, following
`Border.tm-nav-separator`'s precedent (`Height 1`, `Margin 4,4` literal with the derivation in prose,
`:624-628`): they are one indivisible measured shape, not a scale step.

`Background="Transparent"` at rest on the indicator, painted only by the `:selected` rule, is the same
pattern `Border.tm-searchfield-focusbar` already uses (`:371-376`) — no `IsVisible` toggle, no layout
participation, nothing to leave stale.

The `FocusAdorner` is copied verbatim from `TmNavRowButton` (`:283-288`). It is **not** a claim about
what the stock `ListBoxItem` template drew: replacing a template replaces its focus visual too, and a
rail row must stay keyboard-reachable and visibly focused, so the theme that now owns the template
states it. Same reasoning for `^:pressed` — it mirrors `TmNavRowButton:pressed` (`:304-306`) so the two
row kinds keep one vocabulary. **Neither is a match-Task-Manager change; both are "do not silently drop
a state while replacing a template".** TM's hover and press states could not be measured at all in this
environment (no pointer; `SetCursorPos`/`SendInput` are no-ops and `GetForegroundWindow()` returns 0 —
r6-shell §3.3), which is also why `^:pointerover` keeps today's brush unchanged (see §3.3(c)).

There is **no `^:selected:pointerover` rule**, and none is needed: after this change selected and hover
resolve to the same brush, so the combined state is already correct.

**(b) The two `ListBoxItem`s adopt it, in `MainWindow.axaml`.** Add
`Theme="{StaticResource TmNavRowItem}"` to `NavAppsItem` (`:380`) and `NavSettingsItem` (`:504`) —
exactly how the three command rows already carry `Theme="{StaticResource TmNavRowButton}"` in the same
file (`:468`, `:475`, `:519`).

**(c) Delete the three old rules** `Launcher.axaml:567-580` (`ListBox.tm-nav ListBoxItem`,
`…:pointerover /template/ ContentPresenter`, `…:selected /template/ ContentPresenter`) and rewrite the
block comment at `:557-566`, which describes the `BorderThickness="3,0,0,0"` mechanism being replaced.

**(d) Remove `Classes="tm-nav"`** from `NavRail` (`MainWindow.axaml:354`) and `NavFooterRail` (`:499`).
With (c) done, no selector keys off it, and the repo's own precedent is to delete a name that has lost
its last reader rather than leave it defined for nobody (`Tokens.axaml:135-140` on `RadiusOverlay`).
The row identity now lives in `TmNavRowItem`, symmetric with `TmNavRowButton`.

**(e) One rule for the rail's glyph contrast**, beside the deleted ones:

```xml
<Style Selector="Grid.tm-nav-rail TextBlock.tm-icon">
  <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
</Style>
```

`NavRailContent` carries `Classes="tm-nav-rail"` (`MainWindow.axaml:339`) and is the ancestor of all
five rows — the two `ListBoxItem`s and the three Buttons — so one rule covers the rail and nothing
outside it. This is precisely the use the class was kept for: `MainWindow.axaml:335-338` says it is
"kept as this element's own name for anything that needs to scope a rule to the rail root", and notes
that as of Round 4 nothing did. **That comment must be updated: something does now.** The descendant
combinator reaches through `ListBox → ListBoxItem → TextBlock` and `Button → TextBlock` on the logical
tree, the same shape `Border.tm-section-head.clickable:pointerover TextBlock.tm-folder`
(`Launcher.axaml:471-473`) already ships.

`TextBlock.tm-icon`'s own `Foreground = TextSecondaryBrush` (`:320`) is untouched, so the search glyph
(`tm-icon subtle small`, which overrides to `TextTertiaryBrush` at `:327-329`), the folder hint and
every settings-card icon are unaffected.

### 3.3 Why these and not others

**(a) Why the glyph is in scope.** The user's sentence is 导航条中**图标**的选中状态样式 — it names the
icons. The glyph is the second of the two reasons the rail does not read like Task Manager's
(r6-shell §3.2, summary item 6), it is measured in both variants, and TM's measured values *are* our
`TextPrimaryBrush` to the level in both. Changing it is therefore a lookup, not a taste call. It is
scoped to the rail because the rest of the app's `tm-icon` uses were not measured against anything.

**(b) Why the authored indicator width does not change.** Ours renders 4 px from a declared 3 DIP
(*pre-change, and see the outcome note below*); TM's renders 5 px. 3 DIP at 150 % is 4.5 px, so both
readings are consistent with the same authored 3 DIP resolving differently under layout rounding, and
pixels alone cannot separate 3.0 from 3.33 (r6-shell, open questions). Authoring 3.33 would be wrong
at every other scale — the identical argument
Round 4 used to author the pane border at 1 DIP against a measured 1.33
(`Launcher.axaml:652-654`). **So the authored number stays 3, the render may shift by a pixel as a side
effect of moving from a `BorderThickness` to an element, and §3.5 accepts 4 **or** 5 px.** The
acceptance criteria that carry this item are height, centring, shape and non-displacement.

> **Outcome, 2026-08-03, added after item 3's review.** The anticipated shift happened, so the
> present tense above describes only the **pre-change** build. Measured on the shipped build with the
> coverage instrument §3.5 step 3 settles on: the indicator is **5 px** wide through its centre in
> **both** Light and Dark (image x 17..21), against **4 px** before the change (`x 17..20` on the body
> scanlines of `r6i3-before-app-light.png`, re-measured with the same instrument at the same box
> geometry). UIA independently reports the `SelectionIndicator` `Border` at **5x24 px** on both rail
> rows. So ours now renders the same 5 px TM does — **from the authored 3 that did not move**, which
> is the point of this paragraph and not an invitation to reopen it: the agreement is a rounding
> coincidence at 150 %, and authoring 3.33 to *guarantee* it would still be wrong at every other
> scale. `TmNavRowItem`'s block comment in `Launcher.axaml` asserted the pre-change "ours renders
> 4 px" as a present-tense fact and was corrected to say both numbers.

**(c) Why hover is not touched, and what follows from that.** The fill change makes the selected row
`SubtleFillSecondaryBrush` — which is what `:pointerover` already uses
(`Launcher.axaml:574`, and `TmNavRowButton:pointerover` at `:302`). **So after this change a hovered
unselected row and a resting selected row have the same fill.** That is a real consequence and it is
accepted deliberately: TM's hover was not measurable in this environment, the controller's rule is not
to change a property no difference was measured in, and the accent pill — which hover does not draw —
is what distinguishes selection in both apps. Note also the ordering this fixes: today our *hover*
(α 10) is **stronger** than our *selection* (α 6), which is backwards; after the change they are equal
and the pill carries the distinction, exactly as measured on TM.

**(d) Why the `ListBoxItem`/`Button` split is not a constraint.** `TmNavRowItem` is a `ControlTheme`
for `ListBoxItem`; a `Button` has no `:selected` pseudoclass at all and its `TmNavRowButton` template
has a `Border#Root` and a `ContentPresenter` that sit inside no `ListBoxItem`. "A command row can never
look selected" therefore stays true **by construction**, which is the property
`MainWindow.axaml:396-405` and `Launcher.axaml:266-269` were built to guarantee (r6-shell §3.4). The
three Buttons need no change at all.

**(e) Composited predictions for the new fill**, so §3.5 can check arithmetic rather than eyeball:
Light, over the measured rail backdrop `(239,241,253)`, α 10/255 black →
`(229.6, 231.5, 243.1)` → **`(230,232,243)`**. Dark, over `(30,32,38)`, α 15/255 white →
`(43.2, 45.1, 50.8)` → **`(43,45,51)`**. These are predictions on this wallpaper; the criterion is the
**alpha**, not the triple.

> **Outcome, 2026-08-03.** Light measured `(230,232,244)` — blue one level above the prediction,
> because the fill and the backdrop are sampled at different x on a wallpaper gradient (backdrop
> `(239,242,253)` at image x 14 in that same capture, against the `(239,241,253)` §3.1 recorded).
> Dark measured `(43,45,51)`, exactly as predicted. Both alphas are inside §3.5 step 4's band.

### 3.4 Blast radius

**Files touched:** `Themes/Fluent2/Launcher.axaml` (one new `ControlTheme`, three rules deleted, one
rule added, two comment blocks rewritten), `MainWindow.axaml` (two `Theme=` attributes added, two
`Classes="tm-nav"` removed, three comments corrected: `:335-338` — `tm-nav-rail` now has a reader;
`:414-420` — it claims both `tm-nav-rail` and `tm-nav-row` have none, and only `tm-nav-row` still does;
`:482-489` — it describes `NavSettingsItem` as sharing the `"tm-nav"` ListBox class and names the
deleted `ListBox.tm-nav ListBoxItem:selected` selector as what paints it).

Also `Launcher.axaml:582-590`, the comment explaining why `NavRail`/`NavFooterRail` set `Padding` with
a zero horizontal component: it points at "`ListBoxItem`'s own `Margin="4,2"` above", which after this
change lives in `TmNavRowItem`. Re-point the reference; the fact and the 40 DIP item column are
unchanged.

**Re-fit trigger list:** unchanged, five. **Fit arithmetic:** unchanged — the rail is never measured,
it enters the fit only as the constant `RailWidthDip = 48` (`MainWindow.axaml.cs:349`, `:371`), and this
item does not touch the pane's 48 DIP width (`MainWindow.axaml:325-327`).

**Rail height:** unchanged. `Padding` and `Margin` carry over identically, so the row box stays
40 x 36 DIP on a 40 DIP pitch and the footer stays 173 DIP (`Launcher.axaml:615-618`).

**Tests:** none change. Nothing under `tests/` references `tm-nav`, `NavSettingsItem` or the rail
(grep, r6-shell §1.2). `Fluent2TokenTests.ReferencedKeys` filters out keys beginning with `Tm`
(`Fluent2TokenTests.cs:110-113`), so `{StaticResource TmNavRowItem}` in `MainWindow.axaml` is exempt
from the referenced → defined check by the existing filter — the same exemption `TmNavRowButton`,
`TmTileButton` and `TmIconButton` already rely on.

**Config contract:** unchanged. **NativeAOT:** neutral — one more `ControlTheme` of pure `Setter`s over
`DynamicResource` brushes, no `DataTemplate`, no reflection, no new type.

### 3.5 Verification (no keyboard, no mouse)

Re-capture; do not compare against the recon PNGs (§0.3).

1. **Non-displacement, by UIA — the crispest check in this item.** Dump the tree on 应用: the icon
   `TextBlock` rects of `NavAppsItem` (selected), `NavOpenRootItem`, `NavRefreshItem`,
   `NavSettingsItem` and `NavExitItem` must **all report the same x**. Today four report 2606 and the
   selected one reports 2608. Then `SelectionItemPattern.Select()` on `NavSettingsItem` and re-dump:
   all five must **still** report that same x, with `NavSettingsItem` now the selected one.
2. **Indicator height and centring.** `PrintWindow`, Light, 应用 selected. Locate the selected row box
   in this capture from the fill's own extent (expected image y 93..146, x 17..76). Column-scan at the
   box's left edge: accent pixels (Δ from the fill > 30 on the blue channel) must span **24 px ± 1**,
   and the accent run's centre must equal the row box's centre **± 1 px**. Today the accent spans the
   full 51-54 px of the box.
3. **Pill.** Row-scan through the accent run's vertical centre: the accent must be **4 or 5 px** wide
   (§3.3(b)) starting at the box's own left edge. On the run's **first and last** scanlines the accent
   width must be **at least 1 px narrower** than at the centre. Today the run is the same width on
   every scanline it occupies.

   > **Corrected 2026-08-03, while implementing item 3: the "Δ > 30 on the blue channel" mask this
   > step inherits from step 2 is a bar a correct build fails in Dark, for the same class of reason
   > §0.3 guards against.** Its sensitivity is not comparable between the variants. In Light the
   > accent's blue is 192 against a row fill near 244, a 52-level span, so Δ > 30 needs **58 %** ink
   > coverage to trip. In Dark the accent's blue is 255 against a fill near 51, a 204-level span, so
   > the same Δ > 30 needs **15 %**. An anti-aliased cap pixel at ~27 % coverage is therefore invisible
   > to the Light mask and fully counted by the Dark one, and an identical pill reads pill-shaped in
   > Light and square in Dark. **Measured both ways on the shipped build**: under the literal mask,
   > Light gave first 3 / centre 5 / last 3 and Dark gave 5 / 5 / 5. Under **coverage** — the pixel's
   > blue expressed as a fraction of the accent-minus-fill span, one threshold of 0.577 (= 30/52, the
   > coverage the Light bar itself implies) for both variants — the two are the same shape to within
   > 0.02: the four corner scanlines read `x17 0.27 · x18 0.85 · x19 1.00 · x20 0.85 · x21 0.27` and
   > `x17 0.85 · x18–x20 1.00 · x21 0.85` in **both** Light and Dark. **The bar to use is coverage,
   > not a raw channel delta.** Instrument: `r6i3-pill.ps1` in the scratchpad.
4. **Fill strength, as an alpha against a same-capture backdrop.** Measure the row fill and the rail
   backdrop at the same y, outside the accent. Light: `1 − fill/backdrop` must be
   **0.039 ± 0.004** (today 0.024, TM 0.037). Dark: `(fill − backdrop)/(255 − backdrop)` must be
   **0.059 ± 0.004** (today 0.044, TM 0.057).
5. **Glyph contrast.** Darkest ink pixel inside the row. Light: `1 − v/fill` must be **0.89 ± 0.02**
   (today 0.605, TM 0.893). Dark: the glyph must reach `(255,255,255)` (today ≈ `(208,209,210)`).
   **Assert on a selected row and on an unselected row in the same capture — the two must agree**, as
   they do in both apps today.
6. **Accent hue unchanged.** `(0,103,192)` Light, `(76,194,255)` Dark, on the pill.
7. **Non-displacement, in pixels too.** Capture 应用 and 设置. The 设置 gear glyph's ink x-extent must
   be identical in both captures, and so must 应用's grid glyph's. This is the same controlled test
   r6-shell ran on Task Manager, applied to us.
8. **Dark.** `"appearance": "Dark"` in `Data\config.json`, restart, repeat 2-7, restore.
9. **Command rows unchanged.** The three Buttons' icon rects and their resting pixels must be identical
   before and after, and no command row may show a pill in any capture.

   > **Disambiguated 2026-08-03, while implementing item 3.** "Resting pixels" can only mean the row's
   > own **surface**, not its glyph: §3.2(e) deliberately scopes the glyph rule to
   > `Grid.tm-nav-rail`, which is the ancestor of all five rows, and §3.1's glyph row records that both
   > apps draw every rail glyph at one contrast. Reading it as "including the glyph" would contradict
   > the section it is verifying. **Measured, Light, before against after**: `NavOpenRootItem`
   > `(238,242,254)` → `(238,242,254)`, `NavRefreshItem` `(238,242,254)` → `(238,242,254)`,
   > `NavExitItem` `(239,242,253)` → `(239,242,253)`; icon rects `2606,·,24x24` in both; zero accent
   > pixels in the indicator columns of any command row in any of the six captures taken. Their
   > **glyphs** did change, as intended: `(93,95,100)` → `(25,26,27)`, i.e. contrast 0.608 → 0.894.
10. `dotnet test` green with no test file edited for this item.

---

## 4. Item 4 — 应用页面每个分类增加一个面板样式

### 4.1 The reference gives a scale, not a shape — stated plainly

**Windows 11 does not wrap a labelled group of items in a panel.** On `ms-settings:privacy` the three
group labels (安全性 / Windows 权限 / 应用权限) sit **on the page background above** a stack of
per-row cards — measured ink left edges x = 500 / 500 / 501 against a card outer left edge of x = 498,
i.e. the label aligns with the card's *outer* edge and the group has no container of its own
(r6-panels §6). That is the shape our own 设置 page already copies (`tm-group-head`
`Margin="0,20,0,8"`, `Launcher.axaml:1011-1016`, over a stack of `tm-card`s).

**So this item is a deliberate departure from the reference, not a convergence on it,** and the design
says so rather than dressing an invention as a match. What the reference *does* supply is a measured
**scale for a large container**, distinct from its row-card scale, and that is what the panel is
derived from:

| Reference shape | radius | gap between siblings | source |
|---|---|---|---|
| row card (`ms-settings:privacy`, `ms-settings:about`) | 7 px arc ⇒ **4 DIP** | 4-5 px ⇒ **2.67-3.33 DIP** | r6-panels §6, r6-material §4.2 |
| **large tile** (存储 / 显卡 on `ms-settings:about`) | 10 px arc ⇒ **8 DIP** | 18 px ⇒ **12 DIP** | r6-material §4.2 |

Our `tm-card` (`Launcher.axaml:719-726`) is already a faithful copy of the first row: 4 DIP radius,
1 DIP `StrokeControlBrush`, 4 DIP sibling gap. A category panel is the second kind of object — a large
side-by-side container — so it takes the second row's numbers.

### 4.2 The palette collisions, measured, that decide the fill

On the **white 应用 pane** (`SurfacePageBrush` `#FFFFFF`, an opaque brush, so these are not Mica
composites) r6-panels §2 and §5.2 measured and computed:

| Candidate fill | over the white pane | verdict |
|---|---|---|
| `SurfaceLayerBrush` (what `tm-card` uses) | **(255,255,255)** — *measured*: the pane and a tile's interior both read pure white | **invisible.** A `tm-card`-shaped panel here is an outline-only box drawn around tiles that are themselves outline-only boxes |
| `SubtleFillSecondaryBrush` | (245,245,245) | **collides**: this is exactly the tile's hover colour (`TmTileButton`'s `StateLayer` on `:pointerover`, `Launcher.axaml:111-113`). A panel filled with it rests at the colour a tile turns when hovered |
| `SurfaceBaseBrush` | (243,243,243), opaque | re-introduces the grey pane Round 4 item 5 removed |
| **`SubtleFillTertiaryBrush`** | **(249,249,249)** | **chosen.** Δ6 from the pane, Δ4 from hover, and it equals only the tile's *pressed* wash, which is transient |

The measured tile border on the white pane is `(240,240,240)` — `StrokeControlBrush` `#0F000000` over
255 — which is also what a panel border will be against the pane. Inside the panel the tile's border
composites over 249 instead and lands at `(234,234,234)`. **The two hairlines differ by 6 levels**, so
the "two identical nested outlines" objection r6-panels raises against a `tm-card`-shaped panel does
not apply to a filled one.

**And the panel fixes a defect the recon measured.** Today a tile on the 应用 page has **no fill at
all** — pane and tile interior both `(255,255,255)`, Δ0; it is an outline, not a surface. On a 249
panel the same tile composites to `(253,253,253)`, **Δ4 from the panel it sits on**. Hover still reads
at the same strength as today: `SubtleFillSecondaryBrush` over 253.2 → `(243,243,243)`, Δ10 from rest,
against today's 255 → 245, Δ10. That is an argument for the panel independent of the user's request.

**Dark, computed the same way** (r6-panels §7 flags Dark as unexamined by the recon; these are
arithmetic over `Tokens.axaml`'s alphas, not measurements, and §4.6 measures them):

| | pane | panel fill | panel border | tile fill on panel | tile border on panel |
|---|---|---|---|---|---|
| Light | (255,255,255) | **(249,249,249)** | **(240,240,240)** | (253,253,253) | (234,234,234) |
| Dark | (44,44,44) | **(53,53,53)** | **(59,59,59)** | (55,55,55) | (67,67,67) |

Dark separation is Δ9 panel-to-pane and Δ14 border-to-panel, i.e. the panel is *more* visible in Dark
than in Light — the opposite of the failure mode r6-panels warned about ("a design validated in Dark
can look fine and be invisible in Light"). One pre-existing quirk that the panel does not create and
does not fix: in Dark a hovered tile's fill (66) is within a level of a tile's own border (67), and it
already is today (60 against 59).

### 4.3 Decision

**(a) A new radius token**, `src/TanMenu.Wpf/Themes/Fluent2/Tokens.axaml`, in the variant-independent
section immediately after `RadiusPaneTopLeft` (`:163`):

```xml
<CornerRadius x:Key="RadiusPanel">8</CornerRadius>
```

8 DIP is measured — the reference's large tiles show a 10 px arc at 150 %, and this repo's own rule of
thumb, already written down at `Tokens.axaml:155-157`, is that a stroked rounded rectangle shows radius
minus about half the stroke, so 8 DIP renders 10-11 px. It is the same 8 DIP step `RadiusPaneTopLeft`
carries, and `Tokens.axaml:132-133`'s sentence ("the 8 DIP step of that scale is now carried by
`RadiusPaneTopLeft` below and nothing else") must be updated. **`OverlayCornerRadius` (`:235`) is not
reused** even though it is also 8: it is a `FluentTheme` override key that reaches Avalonia's own
flyout and popup templates, and conflating our panel with those would make one key mean two things —
the exact failure `Tokens.axaml:211-233` warns about from the other direction.

**(b) A new style**, `Launcher.axaml`, beside `StackPanel.tm-group` (`:712-714`):

```xml
<Style Selector="Border.tm-group-panel">
  <Setter Property="Background"      Value="{DynamicResource SubtleFillTertiaryBrush}" />
  <Setter Property="BorderBrush"     Value="{DynamicResource StrokeControlBrush}" />
  <Setter Property="BorderThickness" Value="1" />
  <Setter Property="CornerRadius"    Value="{DynamicResource RadiusPanel}" />
  <Setter Property="Padding"         Value="12,12,8,8" />
</Style>
```

**`Padding="12,12,8,8"` is a uniform 12 DIP visible inset**, and the asymmetry in the authored value is
the correction for something already measured: every tile carries `Margin="0,0,4,4"`
(`AppPage.axaml:169`), so the tile grid already contributes **4 DIP on its right and bottom** inside the
group box. This is measured, not assumed — the 常用工具 group's UIA width is 122.67 = its single
column's tile width 118.67 + 4, and the 工具 group's height is 431.33 = 31.33 + 10 x 40 where the 40 is
tile 36 + margin 4 (r6-panels §2). A symmetric `Padding="12"` would render 12 / 12 / 16 / 16.

12 is `Space3` (`Tokens.axaml:177`) and it is the reference's measured large-tile gap, so the panel's
inset and the gutter between panels are one number rather than two.

**No `MaxWidth` on the panel**, for the reason `Launcher.axaml:704-711` already gives for
`StackPanel.tm-group`: a cap would only clamp the width the panel *reports*, while the columns
`ItemsControl` inside keeps laying out however many columns `MenuLayout.ChunkIntoColumns` produced, so
a category wider than the cap would render over its neighbour. Extend that comment to cover the Border.

**No `VerticalAlignment` on the panel**, which is what makes the five panels **equal height**: in a
horizontal `StackPanel` a child with the default `Stretch` is arranged to the row height, so every panel
matches the tallest. `StackPanel.tm-group`'s own `VerticalAlignment="Top"` (`:713`) **stays** — it is
what keeps each category's heading and tiles hugging the top of its panel, leaving the slack at the
bottom of the shorter ones. Ragged panels were the alternative and are rejected: five bordered boxes of
five different heights (measured today: 231.33 / 431.33 / 431.33 / 271.33 / 271.33 DIP inside a
478.67 DIP row) side by side read as an error, and the reference's own large tiles are equal height
(213 px each). **Equal height costs nothing in measure** — the panel's *desired* height is still its own
content, so `FitToContent` still measures the tallest exactly as today.

**(c) `src/TanMenu.Wpf/Views/AppPage.axaml`** — three edits inside the existing `DataTemplate`, no new
template:

| Line(s) | Change |
|---|---|
| `:102` | delete `Spacing="14"` on the outer `StackPanel`. It is **inert today** — that panel has exactly one child, the categories `ItemsControl` at `:108-207` (r6-panels §1) — and it is a landmine precisely for an edit like this one, which would silently activate it if it ever gained a second child |
| `:111` | `Spacing="24"` → **`Spacing="12"`** on the categories `ItemsPanel`. This is the real inter-category gap (measured 24.0 DIP; column pitch across a group boundary = last tile width + 4 + 24, verified on three boundaries). 12 is the reference's measured large-tile gap, and the panels now supply the separation the 24 was doing |
| `:125` | **move `IsVisible="{Binding HasVisibleItems}"` off the `StackPanel Classes="tm-group"` and onto a new wrapping `Border`** |

The wrap:

```xml
<Border Classes="tm-group-panel" IsVisible="{Binding HasVisibleItems}">
  <StackPanel Classes="tm-group">
    <!-- heading Border (129-142) and the columns ItemsControl (151-202), unchanged -->
  </StackPanel>
</Border>
```

**The `IsVisible` move is the one thing this change can get actively wrong, and it was measured.** With
the search box set to `"Steam"` via `ValuePattern`, four of five group containers reported an empty UIA
rect but the items panel still measured 187.33 DIP = 91.33 (the surviving group) + 96 (= 4 x 24) — the
generated container stays visible at zero size while only the inner `StackPanel` collapses
(r6-panels §4.2). If the Border is added as a wrapper and the binding is left where it is, **every
filtered-out category renders as an empty bordered box of 22 DIP.**

The 96 DIP dead lead-in under a filter is pre-existing (Avalonia charges `Spacing` for the still-visible
zero-size containers), is not fixed by this item, and becomes **48 DIP** as a side effect of the gap
change. Not in scope; recorded so nobody reads the halving as a fix.

**Class name: `tm-group-panel`, not `tm-card`.** Reusing `tm-card` would pass CI today only because
`SettingsRowTests.CardsDir` is hardcoded to `src/TanMenu.Wpf/Views/Settings`
(`SettingsRowTests.cs:248-249`) and `Views/AppPage.axaml` is outside it. But the class encodes a
specific anatomy — the Border must wrap one `Auto,*,Auto` Grid with exactly one `tm-card-title` and one
`tm-card-sub` in column 1 (`SettingsRowTests.cs:78-103`, `:150-167`) — which a category panel has none
of, and it would put the 应用 panel one careless `CardsDir` widening away from failing four tests at
once (r6-panels §5.3). And visually `tm-card`'s fill does not transfer at all (§4.2).

### 4.4 The cost, as arithmetic, before the numbers were chosen

From the measured decomposition (r6-panels §3.2), which reproduces today's logged page size exactly:

```
page W = 16 (SurfacePadding L) + Σgroups + (N−1)·G + 16 (SurfacePadding R)
       = 16 + 1515.33 + 4·24 + 16 = 1643.33                              ← logged
page H = 8 (closed PinNotice band) + 16 + Htallest + 16
       = 8 + 16 + 431.33 + 16 = 471.33                                    ← logged, exact
```

With N visible categories, per-side padding `P_h`/`P_v`, border `B` and a new gap `G`:

```
ΔW(page)  = N·(P_left + P_right + 2B) + (N−1)·(G − 24)
ΔH(page)  =      P_top + P_bottom + 2B          ← only the TALLEST category drives the measure
```

At the chosen numbers (`P = 12,12,8,8`, `B = 1`, `G = 12`, `N = 5`):

```
ΔW = 5·(12 + 8 + 2) + 4·(12 − 24) = 110 − 48 = +62.00
ΔH = 12 + 8 + 2                              = +22.00
```

> **Corrected 2026-08-03, while implementing item 4: `B = 1` is the AUTHORED value and the layout
> value on this machine is 4/3.** At 150 % a 1 DIP stroke lays out as 2 physical px = 1.3333 DIP, so
> `2B` is **2.6667**, not 2, and the per-panel chrome is **22.6667 DIP per axis**, not 22. This is not
> a new mechanism — it is the same rounding `Launcher.axaml`'s pane-border remark and `PaneBorderDip`
> already record for the content pane's own 1 DIP border, and the design failed to carry it into this
> arithmetic. Corrected:
>
> ```
> ΔW = 5·(12 + 8 + 2.6667) + 4·(12 − 24) = 113.3333 − 48 = +65.3333
> ΔH =      12 + 8 + 2.6667                             = +22.6667
> ```
>
> **Measured on the shipped build**, and the model reproduces it exactly: the logged page went
> `1643.3333x471.3333` → `1708.6667x494`, i.e. **+65.3333 / +22.6667**. The five UIA container widths
> sum to **1628.6667** against `Σgroups 1515.33 + 5 × 22.6667 = 1628.6667`, and the measured insets are
> **L 13.3333, T 13.3333, R 9.3333** (12 + 1.3333 and 8 + 1.3333) rather than 13 / 13 / 9. Every row of
> the alternatives table below is therefore `N × 0.6667` low in ΔW and 0.67 low in the height request;
> the **ordering of the five rows is unchanged**, so the choice the table was built to justify stands.

Alternatives evaluated, so the choice is visible rather than asserted (r6-panels §3.5 supplies rows a-d;
the chosen row is computed the same way):

| | P | B | G | ΔW | window W | height request | window H |
|---|---|---|---|---|---|---|---|
| a | `tm-card` 16,12 verbatim | 1 | 24 | +170.0 | 1862.33 (+10.0 %) | 546.33 | 568 |
| b | 12 all round | 1 | 12 | +82.0 | 1774.33 (+4.8 %) | 546.33 | 568 |
| c | 8 all round | 1 | 8 | +26.0 | 1718.33 (+1.5 %) | 538.33 | 568 |
| d | `tm-card` 16,12 | 1 | 8 | +106.0 | 1798.33 (+6.3 %) | 546.33 | 568 |
| **chosen** | **12,12,8,8** | **1** | **12** | **+62.0** | **1754.33 (+3.7 %)** | **542.33** | **568** |

Row (a) is `tm-card` verbatim and costs 10 % of the window's width for decoration; it is also the row
whose fill is invisible (§4.2). Row (b) is the chosen shape without the tile-grid correction and pays
20 DIP for 4 DIP of invisible right/bottom padding. Row (c) is cheapest but puts the panel's hairline
8 DIP from the tiles' own. **Width is the whole cost and it scales with the number of categories, not
with the number of tiles** (`2N(P_h+B)` dominates): this user's five categories pay +62; ten categories
would pay +124 net of the gap credit. Break-even in width would need `P_h ≈ 7` with the gap nearly
closed, so no Fluent-plausible padding is free.

**Height is free today and the threshold moves.** The window height request is
`120.33 + 40·R` today and `142.33 + 40·R` after, where R is the tallest column's row count
(`GeneralConfig.ColButtonCount`, default 10) and 568 is the floor (`WindowHost.cs:89`):

| R (每列个数) | request today | request after | window after |
|---|---|---|---|
| 10 (default) | 520.33 | **542.33** | **568** (floor) |
| 11 | 560.33 | 582.33 | **582.33** — grows by 14.33 |
| 12 | 600.33 | 622.33 | 622.33 |

**So panels move the point at which the window starts growing vertically from 每列个数 = 12 to
每列个数 = 11.** That is the item's only height cost and it is stated rather than discovered later.

> **Outcome, 2026-08-03.** The conclusion held and the numbers moved by the 0.67 above. Measured by
> setting 每列个数 through 设置 on the shipped build and reading the fit line: `R = 10` logs
> `page …x494` ⇒ request **543** ⇒ window **568** (floor), and `R = 11` logs `page 1719.3333x534` ⇒
> **window 583**, not 568. The corrected model is `142.9999 + 40·R` (= `120.33 + 22.6667 + 40·R`),
> which gives 543 and 583 exactly. The threshold moves to 每列个数 = 11 as predicted.

### 4.5 Blast radius

**Files touched:** `Views/AppPage.axaml` (three edits, one new `Border`), `Themes/Fluent2/Launcher.axaml`
(one new style, one comment extended), `Themes/Fluent2/Tokens.axaml` (one new token, one comment
corrected).

**The fit arithmetic** (§5.2): unchanged in form, three terms different in value —
`Σgroups` gains `N x 22`, the gap term goes from `4 x 24` to `4 x 12`, and `Htallest` gains 22. The
composition `window = page + chrome 49 x 49`, the 568 floor and the placement formula are untouched.

**The re-fit trigger list** (§5.1): unchanged, five. This item changes what trigger 4 (launcher rebuild
— 每列个数 / 字体 / 语言 / 刷新 / summon) *produces*, not the list. It adds no trigger: a filter change
is still not one (measured — the window did not re-fit while filtered, r6-panels §4.2), so **"the window
resizes" cannot be used as feedback when testing panels against a filter.**

**Arrow-key navigation: safe, with one condition that the design satisfies.** `FindAdjacentTile`
(`AppPage.axaml.cs:350-395`, read this session) scores from
`CenterOf(v) = v.TranslatePoint(centre, Body)` — coordinates resolved relative to `Body` through
whatever ancestor chain exists — and `Tiles()` walks `Body.GetVisualDescendants()`, so **extra nesting
is irrelevant by construction**. What matters is geometry:

* `sameRow` is `Math.Abs(dy) < current.Bounds.Height / 2` = **< 18 DIP** at the measured 36 DIP tile
  height (`AppPage.axaml.cs:370`). Today every category's first tile row is at the same y (measured:
  all nine columns start at image y = 1381). **Uniform** panels shift every row by the same 13 DIP
  (12 padding + 1 border) and preserve that. **This is why the design forbids panelling only some
  categories and forbids per-panel vertical padding** — a 18 DIP difference kills cross-category
  Left/Right.
* Left/Right score is `|dx|`, nearest wins; a constant inset added to every cross-category dx preserves
  the ordering and therefore the winner.
* Up/Down score is `dy·1000 + |dx|`; the dy quantum is 40 DIP = 40 000 points and the largest |dx|
  perturbation a panel can add is a few tens. Cannot flip a decision.

**The horizontal-scrollbar threshold moves by exactly ΔW = +62 DIP.** `Body` is
`HorizontalScrollBarVisibility="Auto"` (`AppPage.axaml:98`) and today its content extent
(1611.33 + 32) equals its viewport (1643.33) exactly — that equality *is* what `FitToContent` produces,
and no `ScrollBar` element exists under `Body` in the UIA tree. After the change both sides grow by 62
and the equality holds. The bar can still only appear when the fit does not get the width it asked for:
(a) the work-area clamp (5120 DIP here, against a new 1754.33 — a very long way off), (b) `_userResized`
latched (`WindowHost.cs:759`), (c) maximized, (d) a manual drag to `MinWidth="760"`. Cases (a) and (d)
come 62 DIP closer.

> **Outcome, 2026-08-03: it holds, at 65.33 rather than 62 (§4.4's correction).** Measured on the
> shipped build at the default: the page's desired width is 1708.6667 and `Body`'s viewport is
> `window 1757.6667 − chrome 49` = **1708.6667**, equal to the digit, and an uncapped raw UIA walk of
> the whole window finds **no `ScrollBar` element anywhere**. Same reading with the filter applied and
> after it was cleared. Whether a shown horizontal bar steals layout height from `Body` or overlays it was
**not determined** (r6-panels §4.4) and this item does not depend on it.

**Empty state and both docked notices: untouched.** `ShowEmpty` (`AppPage.axaml:233-243`) and
`ShowNoMatches` (`:250-257`) are `OverlayPanel` children outside the categories `ItemsControl`,
centred, contributing no layout size; when the page-level `HasVisibleItems` is false the whole
`ItemsControl` (`:108`) collapses so no panels are generated at all. `PinNotice` and the update banner
are `DockPanel.Dock="Top"` **above** `Body` and outside `SurfacePadding`, so a panel cannot swallow
them, and `MainWindow.axaml.cs:392` already re-fits when one opens. Their **open** heights were not
measured (neither can be opened without changing machine state), so the combination "panels + an open
notice" has no number here beyond "the two are additive in the `DockPanel`".

**Tests: +1 fact, 187 → 188.** A new source test, in the shape `SettingsRowTests` established, guarding
the one silent failure mode measured above:

`tests/TanMenu.Core.Tests/Themes/AppPageGroupTests.cs` —
`GroupPanel_CarriesTheVisibilityBinding`: load `src/TanMenu.Wpf/Views/AppPage.axaml` with `XDocument`;
assert (i) exactly one `Border` with `Classes="tm-group-panel"` exists, (ii) it carries
`IsVisible="{Binding HasVisibleItems}"`, and (iii) no `StackPanel` with `Classes="tm-group"` carries an
`IsVisible` attribute. Rationale for adding a test where the other three items add none: this failure is
invisible until a filter is typed, no capture in CI would see it, and the recon *measured* that the
generated container stays visible at zero size, so the failure is certain rather than hypothetical.

**Config contract:** unchanged; `每列个数` is read exactly as before, only its consequence for the window
height threshold moves (§4.4).

**NativeAOT:** neutral. **No new `DataTemplate`** — the `Border` is wrapped *inside* the existing
`DataTemplate x:DataType="vm:MenuGroupVm"` (`AppPage.axaml:115`), so the two nested `ItemsControl`s and
their `x:DataType`s (`:144-150` records why they exist) are untouched, and
`AvaloniaUseCompiledBindingsByDefault` + `PublishAot` need nothing new. The moved binding targets the
same property on the same `x:DataType`. No reflection, no new type.

**Comments that must be rewritten:** `Tokens.axaml:132-133` (the 8 DIP step now has two carriers),
`Launcher.axaml:704-711` (the no-`MaxWidth` rule now covers the Border too),
`AppPage.axaml:104-107` (the categories comment, which should name the panel).

### 4.6 Verification (no keyboard, no mouse)

1. **The fit line.** `Data\cache\logs\log-<date>.txt` must read
   `page 1705.3333…x493.3333… + chrome 49x49` and the window `1754.3333…x568 DIP` at
   `1682.8333…,816`. **This is a prediction — see §5.2 for its basis and its error bars.**

   > **Outcome, 2026-08-03. The prediction missed by +3.33 DIP in page width and +0.67 in page height,
   > and the cause is fully diagnosed — see §4.4's correction: the arithmetic used the authored 1 DIP
   > border where the layout value at 150 % is 1.3333.** Measured, and logged byte-for-byte:
   > `Window fit to content: 1681.1666666666665,816 1757.6666666666667x568 DIP (page
   > 1708.6666666666667x494 + chrome 49x49) on work area 0,0 5120x1392 at scale 1.5`. Against the
   > prediction: page W 1708.6667 vs 1705.3333, page H 494 vs 493.3333, window 1757.6667 vs 1754.3333,
   > Left 1681.1667 vs 1682.8333 (exactly −ΔW/2), Top 816 and height 568 (the floor) both exact. The
   > corrected model reproduces all five to the last logged digit. The **pre-change** line measured in
   > the same session, from a build of `4bd1b13` with only these three files reverted, was
   > `1713.8333333333335,816 1692.3333333333333x568 DIP (page 1643.3333333333333x471.3333333333333 +
   > chrome 49x49)` — byte-identical to §5.2's "today", so the whole difference is this item's, and the
   > `page 1658.6667` jitter §5.2 warns about did not occur in any of the six runs taken.
2. **Panel chrome, by UIA.** Walk the raw tree with a depth cap of at least 80 (the tile Buttons sit at
   depth 33-39; a depth-30 walk silently finds none — r6-panels §0). For each of the five group
   containers: `container.Width − (Σ over its columns of (tile width + 4))` must be **22.00 DIP**, and
   the same for height on the tallest.

   > **Corrected 2026-08-03, while implementing item 4, twice over.**
   >
   > * The width bar is **22.6667 DIP**, not 22.00 (§4.4's correction). Measured on the shipped build,
   >   the five containers give **22.6666 / 22.6666 / 23.3333 / 22.6667 / 22.6667**; the bar is
   >   **22.6667 ± 0.7**.
   >
   >   **Re-corrected 2026-08-03 by item 4's review, which caught both the explanation of the 23.3333
   >   and the instrument that produced it.** The first correction called that container "the
   >   ten-column group … the accumulation of ten independently px-rounded column rects". **There is no
   >   ten-column group.** It holds ten tiles at a *single* x (image 4230, one tile width 198.6667 DIP);
   >   the page has **nine** columns across its five categories — 1 / 5 / 1 / 1 / 1 — which is the same
   >   nine §5.2 and step 5 both count. The false count came from `r6i4-groups.ps1:62-67`, which does
   >   `$cols = $mine | Group-Object x` and then reads `$cols.Count`: when `Group-Object` yields a
   >   *single* `GroupInfo`, `.Count` is that group's **member** count, so the script printed
   >   cols = 5 / 10 / 6 / 6 for the four one-column groups (each equal to its own tile count, which is
   >   the tell). `@()` around the pipeline is the fix; `r6i4fix-cols.ps1` re-runs both dumps corrected.
   >
   >   What the extra 0.6667 actually is, measured on the same two dumps: **one device pixel, and it
   >   sits in that container's RIGHT inset.** Container-to-inner-`tm-group` insets, in px:
   >   L = 20 and T = 20 on all five (13.3333 DIP = 12 + 1.3333), R = **15** on that container against
   >   **14** on the other four (9.3333 DIP = 8 + 1.3333) — an arranged rect of 339 px where the
   >   arithmetic its four siblings satisfy asks for 338. **The panel did not create the pixel.** On the
   >   reverted build the identical odd pixel is in the *gap* between that group and the next (37 px,
   >   against 36 on the other three); after the change all four gaps are exactly 18 px and the pixel
   >   has moved inside the new Border. Both pages balance to the digit — before,
   >   `Σ containers 2272 px + Σ gaps 145 px = 2417 px = 1611.3333 DIP` (+ 2 × 16 = the logged
   >   1643.3333); after, `2443 + 72 = 2515 px = 1676.6667 DIP` (+ 32 = the logged 1708.6667) — so the
   >   growth is 98 px = **+65.3333 DIP exactly**, and no term of §4.4's corrected model moves. The
   >   sub-pixel origin of the pixel itself is *not* claimed: unrounded desired sizes are not observable
   >   through UIA. Instruments: `r6i4fix-cols.ps1`, `r6i4fix-inner.ps1`, `r6i4fix-gaps.ps1`.
   > * **The height half of this step cannot be read off the arranged rect at all, and a correct build
   >   fails it as written.** The window's height is clamped UP to the 568 floor from a 543 request, so
   >   `Body` hands its content ~24.67 DIP more height than it asked for and the panels, being
   >   `Stretch`, take it: every container measures **478.6667** where the tallest category's own
   >   content is 431.3333, a 47.33 gap of which only 22.6667 is chrome. The bar that does hold is the
   >   **desired** height, and the fit line is the instrument for it: page H moved 471.3333 → 494, i.e.
   >   exactly +22.6667 on the tallest category (step 1). Measured insets corroborate it per panel:
   >   L 13.3333, T 13.3333, R 9.3333, and B 9.3333 **plus** that panel's share of the clamp slack.
3. **Equal height.** All five group containers must report the **same** height. Today they are
   231.33 / 431.33 / 431.33 / 271.33 / 271.33.

   > **Outcome, 2026-08-03.** All five measure **478.6667 DIP**, one value, on the shipped build.
4. **The gap.** For each adjacent pair, `container[i+1].x − (container[i].x + container[i].Width)` must
   be **12.00 DIP** (18 px). Today 24.00.
   > **Outcome, 2026-08-03.** **12 / 12.0001 / 12 / 12 DIP**, 18 px at every one of the four boundaries.
5. **Arrow-key invariant.** All nine columns' first tile must report the **same y**. Today all report
   image y = 1381; after, all must report one value ≈ 1400.

   > **Outcome, 2026-08-03, and it was strengthened into the check the item actually needs.** All nine
   > columns' first tile report **y = 1400**, one value, and every tile centre falls on one of exactly
   > **10** distinct y values both before and after, on a 40 DIP row pitch against an 18 → 18.3333 DIP
   > `sameRow` bar. That is only the geometric precondition; the decision itself was verified directly.
   > `FindAdjacentTile`'s scoring (`AppPage.axaml.cs`, transcribed verbatim, including that `<` keeps
   > the first candidate on a tie) was replayed over **two measured tile geometries** — the shipped
   > build's, and a build of `4bd1b13` with only these three files reverted, both dumped by raw UIA
   > walk in the same session, the same 74 tiles in the same document order — for all four directions
   > from every tile. **All 296 decisions are identical** (Left 74/74, Right 74/74, Up 74/74,
   > Down 74/74, agreeing also on which 10 / 10 / 9 / 5 of them are "no move"). Instrument:
   > `r6i4-arrows.ps1` in the scratchpad.
6. **Pixels, Light.** `PrintWindow`. Scan a row through a panel between its heading and its first tile
   row, crossing a panel boundary: the sequence must be pane `(255,255,255)` → panel border
   `(240,240,240)` → panel fill `(249,249,249)`. Then scan a row through a tile: panel fill
   `(249,249,249)` → tile border `(234,234,234)` → tile fill `(253,253,253)`. **The two hairlines must
   differ** (240 against 234) — that is the "not two identical nested outlines" criterion.

   > **Outcome, 2026-08-03: every value exact, and one fact the design did not anticipate.** Measured
   > on the shipped build at image row y = 172 (between the heading and the first tile row): pane
   > `(255,255,255)` through x = 108, panel border at x = 109, panel fill `(249,249,249)` from x = 111.
   > At y = 200 (through a tile): panel fill `(249,249,249)` through x = 128, tile border at x = 129,
   > tile fill `(253,253,253)` from x = 131. The unanticipated part: a 1 DIP stroke is **2 physical px**
   > here (the same rounding §4.4's correction is about), and the two pixels are NOT the same colour —
   > the panel's read `(240,240,240)` then `(234,234,234)`, the tile's `(234,234,234)` then
   > `(238,238,238)`. That is `StrokeControlBrush` composited over what lies under each pixel, 255 and
   > 249 respectively, and it is arithmetic the design already has; it just did not say the stroke
   > would show both. **The criterion as written holds at the outer pixel of each stroke: 240 against
   > 234, Δ6.** The gutter between two panels scans 18 px of pure `(255,255,255)`.
7. **Corner.** Block-dump the panel's top-left 16x16: the arc must span **10-11 px** in each axis, the
   same figure `RadiusPaneTopLeft` was validated against (`Tokens.axaml:154-157`) and the same the
   reference's large tiles measure.

   > **Corrected 2026-08-03, while implementing item 4: "10-11 px" is not a bar a single reading can
   > pass or fail, because the number depends entirely on where the antialiased boundary is called —
   > which is what `Tokens.axaml`'s own `RadiusPaneTopLeft` remark already says (it records 10 px for
   > one instrument and Round 4's 12 px for another on the SAME corner).** So the panel's arc was
   > measured at four criteria and, in the same captures, **so was the app's own other authored-8-DIP
   > corner**, `Border.tm-page-pane`'s. That comparison is the claim that actually matters and no
   > instrument artefact can fake it. Coverage bars are `|G − outside| / |border − outside|`;
   > "steady-value" is the criterion `Tokens.axaml` describes, the row/column at which the border's
   > outer pixel reaches its straight-run value:
   >
   > | corner, same capture | 0.05 | 0.50 | 0.90 | steady-value |
   > |---|---|---|---|---|
   > | `tm-group-panel`, **Light** | 8 px | 9 px | 11 px | **11 px** |
   > | `tm-page-pane`, **Light** | 8 px | 10 px | 12 px | 12 px |
   > | `tm-group-panel`, **Dark** | 8 px | 10 px | 12 px | **12 px** |
   > | `tm-page-pane`, **Dark** | 8 px | 10 px | 12 px | 12 px |
   >
   > Identical in each axis for every row. **In Dark the two corners agree exactly at all four
   > criteria**; in Light the panel reads 1 px shorter at three of them, and the coverage metric is
   > biased against it there because a pure-fill pixel inside the panel is 249 against a 255 outside
   > and a 240 border, i.e. already 0.4 "covered" before any ink — the pane, whose inside is 255, has
   > no such term. **Ruled: the panel renders the same 8 DIP arc the pane does, to within the
   > instrument's own 1 px ambiguity, and the design's 10-11 px band is met at the steady-value
   > criterion in Light (11) and missed by 1 px in Dark (12) by exactly as much as the pane misses it.**
   > No change to the authored 8. Instrument: `r6i4-arc.ps1` in the scratchpad.
   >
   > **Re-measured 2026-08-03 by item 4's review, all four rows reproduced, plus the one thing the
   > table hides.** Fresh captures of the shipped build (`r6i4fix-light.png`, `r6i4fix-dark.png`, panel
   > corner at image 109,110 and pane corner at 83,72) give the table above digit for digit. The
   > "steady-value" column, though, is `r6i4-arc.ps1:62-64`'s criterion, which accepts the border's
   > outer pixel **within 1 level** of its straight-run value. On an exact match the Light panel reads
   > **12 / 12**, not 11: the block dump of that corner runs
   > `y118 = 252, y119 = 247, y120 = 244, y121 = 241, y122 = 240` down the outer column and
   > `x117 = 252 … x120 = 241, x121 = 240` along the outer row, so 241 at 11 px is inside the tolerance
   > and 240 first appears at 12. Dark is 12 either way (`59` first at y = 122 / x = 121). **So the
   > 10-11 px band is not met in either variant on the strict criterion, and the comparative claim is
   > the one to keep** — the pane's corner reads 12 / 12 in both variants in the same captures.
   > `Tokens.axaml`'s `RadiusPanel` block was rewritten to say exactly this instead of asserting the
   > band. Instruments: `r6i4-arc.ps1` plus the block dumps recorded in this session's report.
   >
   > **One digit moved again on the whole-round review's own captures (2026-08-03), and it is the pane's,
   > not the panel's.** The four rows above reproduced digit for digit under the ±1-level criterion, but
   > on a strict exact match that review read `tm-page-pane` at **13** in both variants against the
   > panel's 12 — where the run above read it at 12. Two runs of the same scan, one level apart, on
   > captures taken a session apart: that spread IS the antialiasing ambiguity, and it is the reason the
   > comparative claim must be stated with its criterion attached. **The bound that survives every
   > reading taken so far is "the two 8 DIP corners land within 1 px of each other in both variants";
   > "they agree exactly in Dark" holds only under the ±1-level criterion.** `Tokens.axaml`'s
   > `RadiusPanel` block now says that, and names the criterion beside every digit it quotes.
8. **Filtering — the failure mode the test in §4.5 guards, checked once on screen too.**
   `ValuePattern.SetValue` on `SearchBox` = a string that matches exactly one category (the recon used
   `"Steam"`). Capture. Scan the row at the surviving panel's vertical centre from the items panel's
   origin rightwards: the whole **48 DIP lead-in must be pure pane colour** — `(255,255,255)` Light —
   with **no border pixel and no fill**. If four empty 22 DIP boxes appear, the `IsVisible` binding did
   not move. Restore the filter to `""` and re-verify 74 tiles / 5 groups.

   > **Outcome, 2026-08-03.** With the box set to `"Steam"` through `ValuePattern`, **four of the five
   > generated containers report an empty UIA rect** and the fifth is 114 DIP wide with one tile. It
   > starts **48.0000 DIP** after the items panel's own origin, and the pixel scan of that lead-in —
   > 72 px at the surviving panel's vertical centre — is **`(255,255,255)` on every one of them**, with
   > the panel's border appearing at exactly the 73rd. Clearing the filter restored **74 tiles in 5
   > groups** with byte-identical container geometry.
   >
   > One thing this step incidentally exposed and which is **NOT this item's**: with a filter that
   > matches *nothing*, the 未找到匹配项 overlay renders **zero pixels** — the three UIA nodes
   > (`未找到匹配项`, `没有名称包含该关键词的快捷方式。`, `清除搜索`) exist in the tree with empty rects.
   > **Measured identically on a build of `4bd1b13` with these three files reverted**, so it predates
   > item 4 and item 4 neither causes nor fixes it. Hypothesis, stated as one and not verified:
   > `OverlayPanel.MeasureOverride` measures every overlay against `children[0].DesiredSize`, and with
   > the categories `ItemsControl` collapsed that first child's desired size falls to the
   > `SurfacePadding`, leaving the centred overlay nothing to measure into. The `ShowEmpty` state could
   > not be exercised at all here — it needs `HasGroups` false, i.e. a machine with no shortcuts.
9. **Dark.** `"appearance": "Dark"` in `Data\config.json`, restart, repeat 6-7 with pane `(44,44,44)` →
   panel border `(59,59,59)` → panel fill `(53,53,53)`, and tile border `(67,67,67)` → tile fill
   `(55,55,55)`. These are **computed**, not measured — accept ± 2 levels. Restore.

   > **Outcome, 2026-08-03: all five computed values measured EXACTLY, zero levels of error on every
   > channel.** Pane `(44,44,44)` → panel border `(59,59,59)` → panel fill `(53,53,53)`; panel fill
   > `(53,53,53)` → tile border `(67,67,67)` → tile fill `(55,55,55)`. The panel's second stroke pixel
   > reads `(67,67,67)` and the tile's `(69,69,69)`, the Dark counterpart of step 6's finding, and the
   > two outer hairlines differ by 8 (59 against 67). Gutter: 18 px of `(44,44,44)`. `Data\config.json`
   > was backed up before the edit and restored from that backup afterwards; SHA-256
   > `DFEFF028…46F65F` before and after.
10. **每列个数 = 11**, set through 设置, then re-read the fit line: the window height must be
    **582.33**, not 568 (§4.4). Restore the setting.

    > **Outcome, 2026-08-03.** Driven through 设置 for real — `SelectionItemPattern.Select` on
    > `NavSettingsItem` from a side runspace (2.08 s), `ValuePattern` on `ColCountUpDown`'s inner
    > `Edit`, committed by `SetFocus` off it, then back to 应用 — so this exercised re-fit **trigger 4**,
    > not a cold start. Logged: `1675.8333333333335,801 1768.3333333333333x583 DIP (page
    > 1719.3333333333333x534 + chrome 49x49)`. **The window height is 583, not 568** — the threshold
    > moved to 每列个数 = 11 as designed. The absolute figure is 583 against the predicted 582.33,
    > the same +0.67 border term §4.4's correction covers. The setting was returned to 10 the same way
    > and the fit line came back byte-identical to step 1's.
11. `dotnet test` — **188** green. `dotnet publish -c Release` (NativeAOT) with 0 warnings.

    > **Outcome, 2026-08-03.** `dotnet test -c Release`: **188 passed, 0 failed, 0 skipped** (187 at
    > `4bd1b13`, counted in the same session before the change). `dotnet publish -c Release`:
    > **0 warnings, 0 errors**, ILC link step included.

---

## 5. Cross-cutting

### 5.1 The re-fit trigger list after Round 6

**Five, unchanged from Round 4 §6.1 and Round 5 §5.1.** Everything still routes through
`MainWindow.RefitToContent` (`MainWindow.axaml.cs:344-350`) → `WindowHost.FitToContent`
(`WindowHost.cs:757`), and `RefitToContent` still returns before measuring anything unless
`PageHost.Content` is `_appPage`.

| # | Trigger | Site (verified by grep this session) | Round 6 effect |
|---|---|---|---|
| 1 | Startup build finished — on **every** summon | `MainWindow.axaml.cs:229`, posted at `Loaded` | unchanged |
| 2 | Docked notice opened/closed | `:392` (`OnNoticeVisibilityChanged`) | unchanged |
| 4 | Launcher rebuild — 每列个数 / 字体 / 语言 / 刷新 / summon | `:420` | unchanged trigger; **item 4 changes what it produces** |
| 5 | 设置 → 应用 switch, gated on `wasSettings` | `:637` | unchanged |
| 6 | Maximized → Normal | `:504` | unchanged |

(Trigger 3, the rail expand/collapse, was deleted by Round 4 item 1; the numbering is kept so the three
documents line up.)

Why no item adds one: **item 1** touches the title strip, which is a fixed 48 DIP row
(`MainWindow.axaml:194`) and is never measured — the fit measures `_appPage` and adds
`ChromeHeightDip` as a constant. **Item 2** touches only the pane's paint, and the guard means 设置 can
never drive a fit. **Item 3** touches the rail, which enters the fit only as the literal
`RailWidthDip = 48`. **Item 4** changes `_appPage`'s desired size, which is what trigger 4 already
exists to re-measure.

### 5.2 The fit arithmetic after Round 6, and the predicted window

Composition unchanged (Round 4 §6.2, Round 5 §5.2):

```
pageDesired   = AppPage.Measure(∞, ∞).DesiredSize                (WindowHost.cs:757-867)
window.Width  = pageDesired.Width  + 48 (rail) + 1 (pane left border)
window.Height = pageDesired.Height + 48 (title strip) + 1 (pane top border)
                clamped to [min(568, work.Height), work.Height]
window.Left   = work.Left + (work.Width − Width) / 2
window.Top    = clamp(work.Bottom − 8 − Height, work.Top, work.Bottom − Height)
```

Today, logged: `1713.8333,816 1692.3333x568 DIP (page 1643.3333x471.3333 + chrome 49x49)` on
`work area 0,0 5120x1392 at scale 1.5`.

After Round 6, on **this machine with this user's data** (5 categories, 74 tiles, 9 columns,
每列个数 = 10):

```
page W = 16 + (Σgroups 1515.33 + 5·22) + 4·12 + 16 = 16 + 1625.33 + 48 + 16 = 1705.33
page H = 8 + 16 + (431.33 + 22) + 16                                        =  493.33
window = 1705.33 + 49  ×  max(568, 493.33 + 49 = 542.33)      = 1754.33 × 568 DIP
Left   = (5120 − 1754.33)/2 = 1682.83        Top = 1392 − 8 − 568 = 816
```

**PREDICTED: `1682.8333,816 1754.3333x568 DIP (page 1705.3333x493.3333 + chrome 49x49)`.**

> **Outcome, 2026-08-03: MEASURED `1681.1667,816 1757.6667x568 DIP (page 1708.6667x494 + chrome
> 49x49)`. The prediction missed by +3.3333 DIP in page width and +0.6667 in page height, and the
> cause is the one modelled term.** The two paragraphs below were right that the *measured* inputs
> (`Σgroups`, `Htallest`, the closed-`PinNotice` band) would hold — they reproduced to the third of a
> DIP — and right to name "the panel chrome, pure arithmetic over authored constants" as the only
> modelled term. That is exactly where the miss is: the arithmetic used **the authored 1 DIP border
> where this machine lays out 2 physical px = 1.3333 DIP**, so every panel costs 22.6667 rather than
> 22, and `5 × 0.6667 = +3.3333` in width, `+0.6667` in height. §4.4 carries the corrected arithmetic.
> Corrected, the model reproduces all five logged figures exactly:
>
> ```
> page W = 16 + (1515.33 + 5·22.6667) + 4·12 + 16 = 16 + 1628.6667 + 48 + 16 = 1708.6667  ← logged
> page H = 8 + 16 + (431.33 + 22.6667) + 16                                  =  494.0000  ← logged
> window = 1708.6667 + 49 × max(568, 494 + 49 = 543)         = 1757.6667 × 568            ← logged
> Left   = (5120 − 1757.6667)/2 = 1681.1667                  Top = 816                    ← logged
> ```
>
> Neither error source this section names actually fired: the `page 1658.6667` width jitter did not
> occur in any of the six runs taken (every pre-change run logged `1643.3333`), and the two 0.67
> residuals it flags are a different quantity from the 0.67-per-panel border term above — the five UIA
> container rects still sum 0.67 short of the page arithmetic for the reason given there.
>
> **Items 1, 2 and 3 did not move this line**, as this section required: a build of `4bd1b13` with only
> item 4's three files reverted logged `1713.8333,816 1692.3333x568 DIP (page 1643.3333x471.3333 +
> chrome 49x49)`, byte-identical to the "today" recorded above.

**This is a prediction, and Round 4's equivalent missed by 16.13 DIP.** The difference in confidence,
stated so it can be judged rather than trusted: Round 4's miss came from a *modelled input* — its
per-column label-advance model under-measured the page width (Round 4 §6.2's superseded box). Here the
input is **measured on the running build** (`Σgroups`, `Htallest`, the closed-`PinNotice` band, all
reproduced to the third of a DIP against the logged line), and the only modelled term is the panel
chrome, which is pure arithmetic over authored constants (`5 x 22 + 4 x (12 − 24) = +62`).

Two known error sources, both bounded:

* **The page's desired width is not perfectly stable.** 16 of 20 fit lines logged `page 1643.3333`
  and 4 logged `page 1658.6667`, a **+15.33 DIP** jitter with no established cause; the height never
  varied (r6-panels §7.1). The prediction inherits it, so a run logging `page 1720.67` /
  `window 1769.67` is the same pass.
* **Two unexplained 0.67 DIP residuals**, both flagged rather than chased: the five UIA group rects sum
  to 1514.67 where the page arithmetic requires 1515.33 (five independently px-rounded rects), and the
  heading `Border` measures 25.33 where the tokens imply 26 (the same sub-DIP line-box effect Round 5
  measured on `tm-card-title`).

  > **The first of the two is explained, 2026-08-03, by item 4's review** (§4.6 step 2's re-correction,
  > which measures the same pixel on the other side of the change). It is not spread over five rects:
  > on the pre-change dump the group rects are 184 / 1290 / 305 / 284 / 209 px = 2272 px = **1514.6667**
  > and the four gaps are 36 / 36 / **37** / 36 px = 145 px = **96.6667**, i.e. the 0.6667 the rects are
  > short is the same 0.6667 the gaps are long, in one specific gap. The span is
  > `2272 + 145 = 2417 px = 1611.3333`, which is the logged `page 1643.3333` less `2 × 16` of
  > `SurfacePadding` — exact, so the page arithmetic never actually lost anything. The second residual
  > is untouched by this.

**Items 1, 2 and 3 must not move this line at all.** If the fit line changes for those three, something
leaked into `_appPage` and that is the signal to stop.

### 5.3 Test-suite delta

**187 today** — counted this session: 129 `[Fact]` + 58 `[InlineData]` rows, in the single project
`tests/TanMenu.Core.Tests`. Nothing exercises `MainWindow`, `WindowHost`, `SettingsPage` or `AppPage`
(they need a UI thread), so **items 1, 2 and 3 have zero automated behavioural coverage and §§1.4, 2.5
and 3.5 are the only nets they get.**

| File | Delta |
|---|---|
| `Themes/Fluent2TokenTests.cs` | **no change**, and two facts actively guard this round. `LauncherStyles_ContainNoColourLiterals` (`:43-55`) greps the whole of `Launcher.axaml` **including prose** — every rewritten comment must quote decimal RGB triples, never hex. `EveryTokenTheLauncherReferences_IsDefinedInTokens` (`:57-69`) covers the new `RadiusPanel` reference; `EveryTokenTheLauncherWindowReferences_IsDefinedInTokens` (`:71-85`) covers `MainWindow.axaml`'s new `{StaticResource TmNavRowItem}`, which the existing `Tm*` filter (`:110-113`) exempts. `EveryThemedTokenIsDefinedInBothVariants` (`:87-101`) is unaffected — `RadiusPanel` goes in the variant-independent section |
| `Themes/SettingsRowTests.cs` | **no change.** `CardsDir` stays `src/TanMenu.Wpf/Views/Settings` (`:248-249`) and item 4 deliberately does not put a `tm-card` outside it (§4.3) |
| **new** `Themes/AppPageGroupTests.cs` | **+1 fact**, `GroupPanel_CarriesTheVisibilityBinding` (§4.5) |
| `Services/WindowPlacementTests.cs`, `Services/MenuLayoutTests.cs`, `ViewModels/MenuModelsTests.cs` | **must not change.** If any breaks, an item leaked into the placement arithmetic or the data layer |
| everything else | untouched |

**Total after: 188.**

Three tests were considered and rejected. A source test asserting `Border.tm-page-pane:not(.app)` sets
`Transparent` restates the implementation in a second place and breaks on any reasonable refactor. A
source test on the rail indicator's geometry would pin literals a capture already checks better. A
source test forbidding `#RRGGBB` in `AppPage.axaml` is the rule no test enforces for that file — real,
but a Round-6-unrelated gap that should not be smuggled in here.

### 5.4 Config contract

**No change at all.** No item reads or writes a setting. `AppConfig.CurrentSchemaVersion` stays 1
(`AppConfig.cs:25`), `ConfigService.Normalize` gains nothing, and no property is retired — a bump would
silently defuse `ConfigServiceTests.Load_MigratedConfig_KeepsDeliberateAutoCloseChoice`, whose fixture
interpolates the constant (Round 4 §6.4).

Two verification steps write `Data\config.json` (`"appearance": "Dark"`) and one triggers a hide/summon
cycle that can persist a window position through `WindowHost.RestorePlacement`. **Back the file up
before either and restore it byte-identically afterwards** (§2.5 step 6, §3.5 step 8, §4.6 step 9).

### 5.5 NativeAOT

Neutral across all four items, provided three rules hold — the same three every round since Round 4:

* **No new `DataTemplate`.** `AvaloniaUseCompiledBindingsByDefault` + `PublishAot` are both on
  (`TanMenu.Wpf.csproj`), so every `DataTemplate` needs a concrete `x:DataType`. Item 4 wraps a Border
  *inside* the existing `MenuGroupVm` template and adds none; the two nested `ItemsControl`s stay
  (`AppPage.axaml:144-150` records why they must).
* **The rail stays literal XAML rows.** Item 3 adds a `ControlTheme` and two `Theme=` attributes;
  rebuilding the footer or the page list from a collection would add a `DataTemplate` over a type that
  does not exist (`MainWindow.axaml:424-429`).
* **Brushes stay `DynamicResource`; no reflection is added.** Every new Setter in this round resolves a
  token by key. `RadiusPanel` and `TmNavRowItem` are resource lookups, not types.

### 5.6 Light and dark, in one place

| | Light | Dark |
|---|---|---|
| **Item 2** — 设置 pane | Transparent over Mica; card fill − backdrop `(6..12, 10..11, 0..7)`, border − backdrop `(−14..−15)` uniformly. **This is where the payoff is** (the seam is Δ12 in blue today) | Transparent over Mica; fill − backdrop `(8,8,6..7)`, border − backdrop `(15..16)`. The seam it removes is at most Δ7 in blue, i.e. **close to invisible — Dark gains little and loses nothing** |
| **Item 2** — Mica refused | flat `(243,243,243)`, cards `(251,251,251)` / `(229,229,229)` — identical to today's 设置 | flat `(32,32,32)`, cards `(40,40,40)` / `(48,48,48)` — identical to today's 设置 |
| **Item 3** — selected fill | α 10/255 black; over the measured rail backdrop → `(230,232,243)` | α 15/255 white; over `(30,32,38)` → `(43,45,51)` |
| **Item 3** — pill | `(0,103,192)`, unchanged hue | `(76,194,255)`, unchanged hue |
| **Item 3** — rail glyph | `TextPrimaryBrush` α 0.894, matching TM's measured 0.893 | `TextPrimaryBrush` opaque white, matching TM's measured `(255,255,255)` |
| **Item 4** — panel | fill `(249,249,249)` on a `(255,255,255)` pane (Δ6), border `(240,240,240)` (Δ15); tile gains a Δ4 fill it does not have today. **All four measured exactly, 2026-08-03** | fill `(53,53,53)` on `(44,44,44)` (Δ9), border `(59,59,59)` (Δ15); tile fill Δ2, tile border Δ14. Written as *computed*; **§4.6 step 9 measured all five with zero levels of error on every channel, 2026-08-03** |
| **Item 1** | nothing to compute — the field is absent on 设置 in both variants | same |

One pre-existing divergence this round neither creates nor fixes, recorded so it is not rediscovered:
Windows Settings outlines its **dark** cards with a hairline *darker* than the backdrop
(`(28,29,32)` on `(32,32,35)` ⇒ ≈ `#19000000`) where our `StrokeControlBrush` Dark `#12FFFFFF` gives a
*lighter* one. Both read; ours is what preserves the measured Δ16 separation over dark Mica
(r6-material §4.3).

---

## 6. Open decisions

### 6.1 For the human

**None.** Every question this round raised is settled below with its basis. The two candidates that
were weighed and closed rather than escalated:

* *The rail's hover, once selection moves to the same brush* — closed by the controller's own rule
  (do not change a property no difference was measured in); TM's hover is unmeasurable in this
  environment (§3.3(c)).
* *The Mica settings page's expanded scrollbar* — closed as "ship and look once" (§6.2), because both
  readings lead to the same work now.

### 6.2 Settled here, recorded so they are not re-opened

| Question | Ruling | Basis |
|---|---|---|
| Item 1: `IsVisible=false` or remove from the tree? | **`IsVisible=false`, on `SearchField` (the Border)** | The Border owns the centre column's slot; `MeasureCore` is decompiled to contribute 0 including `Margin`; the two-way `Filter` binding and the placeholder must survive, which removal would complicate for nothing. §1.1, §1.2 |
| Item 1: keep `SearchBox.IsEnabled` as well? | **No, one write** | A hidden element receives no input; the second write would be a redundant statement of the same fact. §1.1 |
| Item 1: delete the two `:disabled` rules? | **Delete both** | `ShowPageCore:619-621` was their only producer; the repo's own precedent is to delete a rule that has lost its last consumer (`RadiusOverlay`, Round 5). §1.1 |
| Item 2: which class, `settings` or `app`? | **`app`** — `.app` and `:not(.app)` | The user's sentence makes 应用 the exception; the current encoding makes white the default, so a third page would silently come up white. §2.2 |
| Item 2: `Transparent` or no `Background` setter? | **`Transparent`** | States intent; keeps the pane hit-testable; passes the no-hex test. §2.1 |
| Item 2: does the pane paint its own Mica fallback? | **No — `Window.Background` is the single source** | `ApplyBackdrop` already paints it, already follows the theme variant, and already re-evaluates at every summon. A second fallback would be a second truth. §2.3 |
| Item 2: Round 4 §5.1(d)/§7.1 | **Overruled, with the measurement recorded** | The Δ0 objection is a property of white; Mica measures 238-247 / 30-34 and the card separation is unchanged to within 2-4 levels. §2.2 |
| Item 2: the expanded scrollbar over Mica | **Ship as designed; no pre-emptive override.** If a human with a pointer sees an opaque band, override `ScrollBarBackgroundPointerOver` / `ScrollBarTrackFillPointerOver` / `ScrollBarTrackStrokePointerOver` in `Tokens.axaml`'s existing "Overrides that reach Avalonia's own control templates" block (`:211-236`) — the seam already proved for `TitleBarBackgroundBrush` | The three keys' values could not be resolved (decompiled references at `fluent-xaml.txt:21787/21805/21816`, no definitions), and overriding a brush whose current value is unknown to fix a symptom nobody has observed is exactly what this repo's rules forbid. §2.5 step 9 |
| Item 3: vehicle for the indicator | **A `ControlTheme` for the two rail `ListBoxItem`s with a `Panel`-based template** | A 16 DIP centred pill that does not displace content cannot be a `BorderThickness` — a border always spans the full edge and always eats layout. The alternative (a pill inside each item's content) makes the two row kinds' markup diverge and needs the padding rebalanced. §3.2(a) |
| Item 3: authored indicator width | **Stays 3 DIP** | 4 px vs 5 px is consistent with one authored 3 DIP rounding differently; authoring 3.33 would be wrong at every other scale — Round 4's own argument for the 1 DIP pane border. §3.3(b) |
| Item 3: hover | **Untouched** | Unmeasurable in this environment on either app; the controller's rule forbids changing an unmeasured property. The consequence (selected fill = hover fill) is accepted and the pill carries the distinction, as on TM. §3.3(c) |
| Item 3: the glyph colour | **Changed to `TextPrimaryBrush`, scoped to `Grid.tm-nav-rail`** | The user's sentence names 图标; the difference is measured in both variants (α 0.605 vs 0.893 Light; `#C8` vs opaque Dark); and TM's measured values *are* our `TextPrimaryBrush` to the level in both. §3.2(e), §3.3(a) |
| Item 3: `:pressed` and `FocusAdorner` on the new theme | **Included, copied from `TmNavRowButton`** | Not a match-TM change — "do not silently drop a state while replacing a template", and the two rail row kinds must share one vocabulary (`Launcher.axaml:263-266`). §3.2(a) |
| Item 3: `Classes="tm-nav"` | **Removed from both ListBoxes** | No selector keys off it once the three rules move into the ControlTheme; the repo deletes names that lose their last reader. `tm-nav-rail` gains a reader in the same edit. §3.2(d) |
| Item 4: reuse `tm-card`? | **No — a new `tm-group-panel`** | Semantic collision (`tm-card` encodes an `Auto,*,Auto` Grid with one title and one subtitle) and one `CardsDir` widening from failing four tests; and its fill is invisible on the white 应用 pane. §4.3 |
| Item 4: the fill | **`SubtleFillTertiaryBrush`** | `SurfaceLayerBrush` measures (255,255,255) on the pane — invisible; `SubtleFillSecondaryBrush` is exactly the tile's hover colour; `SurfaceBaseBrush` re-introduces the grey Round 4 removed. §4.2 |
| Item 4: radius | **8 DIP, new `RadiusPanel` token** | Measured on the reference's large side-by-side tiles (10 px arc at 150 %); a category panel is that shape, not the 4 DIP row-card shape. `OverlayCornerRadius` is not reused — it is a FluentTheme key for Avalonia's own popups. §4.3(a) |
| Item 4: padding | **`12,12,8,8`** = a uniform 12 DIP visible inset | 12 is `Space3` and the reference's measured large-tile gap; the right/bottom are 8 because the tile grid already contributes 4 DIP there, measured on the live build. §4.3(b) |
| Item 4: the inter-category gap | **24 → 12 DIP** | The reference's measured large-tile gap, and the panels now supply the separation the 24 was doing. It also cuts the cost from +110 to +62 in authored DIP — measured on the shipped build, +113.33 to **+65.33**, §4.4's correction. §4.3(c), §4.4 |
| Item 4: equal-height or ragged panels? | **Equal height** | Five different-height bordered boxes side by side read as an error; the reference's large tiles are equal height; and it costs nothing in measure because the panel's *desired* height is still its own content. Achieved by giving the Border no `VerticalAlignment` and leaving `StackPanel.tm-group`'s `Top` in place. §4.3(b) |
| Item 4: panel some categories only, or vary vertical padding? | **Forbidden** | `FindAdjacentTile`'s `sameRow` test is `< 18 DIP`; every category's first tile row is at one y today and must stay there. §4.5 |
| Item 4: the inert `Spacing="14"` | **Deleted** | It has one child today and would silently activate on exactly this kind of edit. §4.3(c) |
| Item 4: the 96 DIP filtered lead-in | **Not fixed; becomes 48 as a side effect** | Pre-existing — Avalonia charges `Spacing` for the still-visible zero-size containers — and out of this item's scope. §4.3(c) |
| Item 4: a test? | **+1 source fact** | The `IsVisible` move is a *measured*-certain failure mode that is invisible until a filter is typed and that nothing in CI would see. §4.5 |
| All items: the reference is a departure for item 4 | **Stated, not disguised** | Windows 11 draws no wrapping panel around a labelled group; measured on `ms-settings:privacy`. §4.1 |
