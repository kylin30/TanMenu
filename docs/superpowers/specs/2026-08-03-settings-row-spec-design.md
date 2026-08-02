# TanMenu — the 设置 row spec, and the rail/关于 move (Round 5 design)

> **Status.** Written as design only. **Part A and Part B are now implemented** — task A shipped the
> style classes and the source guard (`64fc424`, `4427589`), task B migrated all sixteen rows. Part C
> (items 1 and 2) is not. Where the implementation measured something this document had inherited
> rather than measured, the document has been corrected in place and the correction is marked
> **[task B, measured]**. Two such corrections exist: §A.5/§6.1(b) (the three-button rows trail — the
> 760 DIP constraint was re-taken and no longer binds) and §A.8's `Padding`. Card-height predictions in
> §B and §5.4 that followed from the old §A.5 are corrected with the heights actually measured.
>

> **Scope.** The three items the user asked for after reviewing the Round 4 build (0.12.0):
>
> 1. 退出移动到最下 — 退出 moves to the bottom of the rail footer.
> 2. 关于集成到设置页中 — 关于 is folded into the 设置 page.
> 3. 设置页每个设置项统一设计规范 — every settings row obeys one design spec. Five screenshots name
>    the defects: `4.png` (主文件夹 / 数据文件夹), `5.png` (维护), `6.png` (update status),
>    `7.png` (固定到任务栏), `8.png` (热键).
>
> **Evidence base.** Two reconnaissance reports, both read in full:
> `r5-settings.md` (row inventory + divergence audit, 16 cards, measured live on PID 43424 by UI
> Automation and `PrintWindow`) and `r5-about-rail.md` (the rail footer and the 关于 surface). Both were
> taken at HEAD `d493e18` against a Debug build of commit `908967e`. References of the form
> *(r5-settings §3.5)* point into them. **The two reports agree on every fact this design uses**; the
> only two corrections either of them makes are to the *brief*, not to each other — see §0.3.
>
> Three facts below were established in *this* session and are marked **[new]**: the Avalonia
> reflection result in §A.2, the decompiled `ToggleSwitch` column in §A.7, and the test count in §5.3.

---

## 0. Ground truth

### 0.1 What the page is today

`SettingsPage.axaml:50-59` — `ScrollViewer` → `StackPanel x:Name="CardStack" MaxWidth="1250"
Margin="24,16,24,24"` → `PageTitle` + five card groups (`:53-57`). Sixteen cards in five groups
(r5-settings §2). Every card is `Border Classes="tm-card"` (`Launcher.axaml:712-719`); ten wrap a
`Grid ColumnDefinitions="Auto,*,Auto"`, six wrap a `StackPanel` holding that Grid plus sibling rows
below it (r5-settings §3.1).

Measured, and genuinely uniform on all sixteen cards: content origin **17.33, 13.33** DIP relative to
the card, content right edge **1232.67**, card-to-card pitch **66.67** for a **62.67** DIP card
(r5-settings §2, §3.9). Display scale 1.5; a 1 DIP border layout-rounds to 2 physical px = 1.33 DIP,
which is why the authored `1 + 16` padding measures 17.33.

### 0.2 What the reference gives us, and what it does not

No Windows 11 Settings window was measured in Round 5 and **this design claims no number from one.**
The reference measurements that exist in this repo are Task Manager's, taken in Rounds 3 and 4:

* its settings pane is `(243,243,243)`, flat, borderless, square-cornered, carrying `(251,251,251)`
  cards with `(229,229,229)` outlines — which is exactly what `Border.tm-card` over `SurfaceBaseBrush`
  already composites to (`Launcher.axaml:658-671`, Round 4 §5.1(d));
* its section headings and its card titles measure the **same ink height** and are told apart by weight
  alone; its page title's ink is ~1.4× its section headings' — the measurement behind
  `tm-group-head` / `tm-page-title` (`Launcher.axaml:730-743`).

Everything numeric in §A therefore comes from one of three places, and each value below says which:
**(a)** the Fluent 2 type/space ramp already in `Tokens.axaml`, **(b)** a live measurement in
`r5-settings`, or **(c)** arithmetic over the two. Nothing is copied from a Windows 11 screenshot that
nobody measured.

### 0.3 Two corrections to the brief (neither is a disagreement between the reports)

1. **`绿色版 · 34.7 MB` is not in the update card.** It is `DataInfoText` in 文件夹 → 数据文件夹,
   composed at `FoldersCard.axaml.cs:139-141` from `CurrentDataSize` = `当前：{0} · {1}`
   (`AppLanguage.cs:114`) with `DataKindPortable` = 绿色版 (`AppLanguage.cs:110`). Both reports say so;
   `4.png` shows it under 数据文件夹 and `6.png` shows the update card without it.
2. **Round 4 did not add a second 关于 entry point.** It inserted
   `ShellNavigator.Navigate(ShellPage.App)` before `OpenAbout()` so the same rail row switches page
   first (`MainWindow.axaml.cs:866-894`). `LauncherViewModel.OpenAbout()` still has exactly one caller
   (`:893`).

---

# Part A — the row spec

One anatomy. Every settings row obeys it; the variants below are the only legal shapes.

## A.1 The anatomy, in one picture

```
┌ Border Classes="tm-card" ───────────────────────────────── padding 16,12, margin 0,0,0,4 ┐
│                                                                                          │
│  ┌ Grid ColumnDefinitions="Auto,*,Auto"  [RowDefinitions="Auto,Auto"] ────────────────┐  │
│  │ col 0 (16)     col 1 (star)                                    col 2 (auto)       │  │
│  │                                                                                    │  │
│  │  [icon 16]     TITLE      ← tm-card-title,  14/20, primary      [ the control ]    │  │ row 0
│  │   ↕ centre     subtitle   ← tm-card-sub,    12/16, secondary    ↕ centre, right    │  │
│  │   ←— 14 —→                                            ←— 12 —→                     │  │
│  │                                                                                    │  │
│  │                value line                        ← tm-card-value, left             │  │ row 1
│  │                value line                                                          │  │ (optional)
│  │                                                     [ btn ] [ btn ] [ btn ]        │  │
│  └────────────────────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────────────────────┘
   ↑17.33                ↑47.33                                                    1232.67↑
```

**Three invariants. Everything else in this part exists to make them true.**

| # | Invariant | Value |
|---|---|---|
| **I1** | Every row's header is **exactly one title line and one subtitle line**. | header box = **36 DIP** on every card |
| **I2** | Every row's icon box occupies the same rectangle relative to its card. | **x 17.33, y 23.33, 16×16** |
| **I3** | Every interactive control's **right edge** is the card's content right edge — whether it sits in column 2 or on the expansion row. | **1232.67 DIP** |

I1 is what makes I2 free: with a two-line header the icon's `VerticalAlignment="Center"` lands at the
same y on all sixteen rows. Today it lands at four different offsets (−0.33 / +8.33 / +19.00 / +28.33
DIP from the title's centre) purely because some headers carry three or four lines (r5-settings §3.7).

## A.2 The vehicle: seven style classes in `Themes/Fluent2/Launcher.axaml`, and one thing that cannot be a style

**[new — verified this session by reflection over the shipped `Avalonia.Controls.dll` 12.1.0 in
`src/TanMenu.Wpf/bin/Debug/net10.0-windows10.0.19041.0/win-x64/`]** `Grid` exposes
`ShowGridLinesProperty`, `RowSpacingProperty`, `ColumnSpacingProperty`, `ColumnProperty`, `RowProperty`,
`ColumnSpanProperty`, `RowSpanProperty`, `IsSharedSizeScopeProperty` — and **no
`ColumnDefinitionsProperty` / `RowDefinitionsProperty` at all**; `ColumnDefinitions` and
`RowDefinitions` are plain CLR properties with no `AvaloniaProperty` behind them. A `Setter` needs an
`AvaloniaProperty`, so **the column template cannot be pushed into a style or a `ControlTheme`.** It is
repeated literally in every card and enforced by a source test instead (§5.3). `RowSpacing` *is* a
`StyledProperty` and could carry the header→expansion gap — it is deliberately not used, because it is
charged between row definitions even when row 1's only child is `IsVisible="False"` (the update card's
`ProgressBar`, `UpdatesCard.axaml:39`), which would leave 8 DIP of dead space on that card in its normal
state. The gap lives on the expansion child's own `Margin` instead, and an invisible child contributes
neither.

Everything else *can* be a style, and is. Add this block to `Launcher.axaml` immediately after the
existing `Border.tm-card` / `tm-card-title` / `tm-card-sub` rules (`:709-729`), so declaration order
puts every new rule after the two it refines — the same ordering the file already relies on for
`TextBlock.tm-icon.subtle` overriding `TextBlock.tm-icon`'s `Foreground` (`:315-327`):

```xml
<!-- The icon box. Shared by the glyph rows and by 关于's product image, hence :is(Control) rather
     than a TextBlock-scoped selector. Width 16 fixes the column independently of the glyph: all
     nine glyphs on the page measure exactly 16 DIP wide today, so this changes nothing and stops a
     future glyph from moving the title. Height is deliberately NOT set — the measured 16 DIP box is
     Segoe Fluent Icons' own line box at FontSize 16, and pinning a height would clip the glyph if
     the Windows 10 fallback face (Segoe MDL2 Assets) measures taller. -->
<Style Selector=":is(Control).tm-card-icon">
  <Setter Property="Width" Value="16" />
  <Setter Property="Margin" Value="0,0,14,0" />
  <Setter Property="VerticalAlignment" Value="Center" />
</Style>

<!-- The header text column. 12 DIP is the gutter to the control; Center is what pairs with the
     icon's own Center to satisfy I2. -->
<Style Selector="StackPanel.tm-card-text">
  <Setter Property="VerticalAlignment" Value="Center" />
  <Setter Property="Margin" Value="0,0,12,0" />
</Style>

<!-- A value line: a subtitle that is one ellipsized line rather than a wrapping paragraph. Used
     WITH tm-card-sub (Classes="tm-card-sub tm-card-value"), so it inherits size, colour and line
     height and states only the four things that differ. HorizontalAlignment=Left is the fix for the
     accidental centring; MaxWidth is a desired-width bound, not a display cap — see A.6. -->
<Style Selector="TextBlock.tm-card-value">
  <Setter Property="TextWrapping" Value="NoWrap" />
  <Setter Property="TextTrimming" Value="CharacterEllipsis" />
  <Setter Property="HorizontalAlignment" Value="Left" />
  <Setter Property="MaxWidth" Value="260" />
</Style>

<!-- The expansion row's own top gap. On the child itself, not on the Grid, so an IsVisible=False
     child costs nothing (see A.2's note on RowSpacing). -->
<Style Selector=":is(Control).tm-card-expander">
  <Setter Property="Margin" Value="0,8,0,0" />
</Style>
<Style Selector="StackPanel.tm-card-expander">
  <Setter Property="Spacing" Value="8" />
</Style>
<Style Selector="StackPanel.tm-card-values">
  <Setter Property="Spacing" Value="2" />
</Style>

<!-- Every action strip on the page, in column 2 AND on the expansion row. HorizontalAlignment=Right
     is inert in column 2 (the column is Auto-sized to the strip) and is what puts an expansion-row
     strip on the content edge, so one class makes invariant I3 true in both places. -->
<Style Selector="StackPanel.tm-card-actions">
  <Setter Property="Orientation" Value="Horizontal" />
  <Setter Property="Spacing" Value="8" />
  <Setter Property="HorizontalAlignment" Value="Right" />
  <Setter Property="VerticalAlignment" Value="Center" />
</Style>

<!-- The settings toggle. Blanking On/OffContent moves out of the six cards that repeat it today.
     The negative right margin is explained in A.7 — do not remove it without reading that. -->
<Style Selector="ToggleSwitch.tm-card-toggle">
  <Setter Property="OnContent" Value="" />
  <Setter Property="OffContent" Value="" />
  <Setter Property="VerticalAlignment" Value="Center" />
  <Setter Property="Margin" Value="0,0,-12,0" />
</Style>
```

and two `Setter`s added to rules that already exist:

```xml
<Style Selector="TextBlock.tm-card-title">
  <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}" />
  <Setter Property="LineHeight" Value="{DynamicResource LineHeightBody}" />   <!-- NEW -->
</Style>
<Style Selector="TextBlock.tm-card-sub">
  <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}" />
  <Setter Property="LineHeight" Value="{DynamicResource LineHeightCaption}" /><!-- NEW -->
  <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}" />
  <Setter Property="TextWrapping" Value="Wrap" />
</Style>
```

`tm-card-title` and `tm-card-sub` are the only two text classes in the whole theme that skip the
line-height half of the ramp — `tm-group-head` (`:744-749`) and `tm-page-title` (`:754-758`) both set it
(r5-settings §1). The measured cost of skipping it: a title line box of **19.33** instead of the token's
20, a subtitle box of **16.67** instead of 16, **and 0.67 DIP of drift between rows of the same class**
— the two pure-ASCII value lines measure 16.0 while every CJK subtitle measures 16.67 (r5-settings §3.3).
Setting the tokens removes the drift and makes the header exactly 20 + 16 = 36, which is what
invariants I1/I2 rest on. It costs no card height: 19.33 + 16.67 is already exactly 36.

## A.3 The numbers, each with its basis

| Slot | Value | Token | Basis |
|---|---|---|---|
| card fill / outline / radius | `SurfaceLayerBrush` / `StrokeControlBrush` 1 DIP / `RadiusControl` | existing | `Launcher.axaml:712-716`; composites to the reference's own `(251,251,251)` card on a 243 pane (§0.2) |
| card padding / gap | `16,12` / `0,0,0,4` | `Space4`,`Space3`,`Space1` | `Launcher.axaml:717-718`; measured content origin 17.33,13.33 and 66.67 pitch on **all 16** cards (r5-settings §2) |
| icon box | 16 wide, `Margin 0,0,14,0`, centre | — | measured 16×16 on all nine distinct glyphs; margin already identical on all 16 rows (r5-settings §3.7) |
| title | 14 / **20** / `TextPrimaryBrush` (inherited) | `FontSizeBody`, `LineHeightBody` | `Tokens.axaml:194-195`; ramp declared at `:175-177` |
| subtitle | 12 / **16** / `TextSecondaryBrush` / Wrap | `FontSizeCaption`, `LineHeightCaption` | `Tokens.axaml:192-193` |
| **header block** | **36** | — | 20 + 16; equals today's measured 36 exactly (r5-settings §2) |
| text → control gutter | 12 | `Space3` | `Margin="0,0,12,0"`, already on 13 of 16 rows |
| control | `VerticalAlignment="Center"`, right edge 1232.67 | — | measured flush on the ComboBox / NumericUpDown / Button / TextBox rows (r5-settings §3.8) |
| action strip gap | 8 | `Space2` | `Spacing="8"` on all four existing strips (`FoldersCard.axaml:42,67,88`, `UpdatesCard.axaml:45`) |
| header → expansion gap | **8** | `Space2` | the two existing below-row text margins are 8 (`BehaviorCard.axaml:87,120`); the four action strips use 10 — normalised **down** to 8, because 8 is a ramp step and 10 is not (`Tokens.axaml:168-172`) |
| expansion left edge | column 1's origin, **47.33** | — | measured: the hand-written `Margin="30,…"` indent lands at exactly 47.33 = 17.33 + 16 + 14, i.e. at column 1's origin (r5-settings §3.6). The spec replaces the constant with the column. |
| value → value gap | 2 | — | existing `Margin="0,2,0,0"` (`FoldersCard.axaml:63`) |
| standard card height | **62.67** measured (62 authored + 2 × 0.33 border rounding) | — | 12 + 36 + 12 + 2 border; equals the 62.67 that nine cards already measure (r5-settings §3.9) |

**No `MinHeight` is added to `tm-card`, deliberately.** With I1 enforced, every simple row is 62.67 by
construction; a `MinHeight` would silently absorb a violation of I1 instead of letting the source test
(§5.3) fail on it. This project has already recorded its dislike of constants that stand in for a rule
(`MainWindow.axaml:269-272`, quoted in Round 4 §1.1).

## A.4 Variant 1 — one trailing control (the base case)

Ten rows. This is the shape `AppearanceCard` already ships (`AppearanceCard.axaml:24-34`), minus three
attribute triplets that become class names.

```xml
<Border Classes="tm-card">
  <Grid ColumnDefinitions="Auto,*,Auto">
    <TextBlock Grid.Column="0" Classes="tm-icon tm-card-icon" Text="&#xE774;" />
    <StackPanel Grid.Column="1" Classes="tm-card-text">
      <TextBlock x:Name="LanguageTitle" Classes="tm-card-title" />
      <TextBlock x:Name="LanguageSub"   Classes="tm-card-sub" />
    </StackPanel>
    <ComboBox Grid.Column="2" x:Name="LanguageCombo" Width="220" VerticalAlignment="Center"
              SelectionChanged="OnLanguageChanged" />
  </Grid>
</Border>
```

A `ToggleSwitch` in column 2 is written `<ToggleSwitch Grid.Column="2" x:Name="…"
Classes="tm-card-toggle" IsCheckedChanged="…" />` — the class carries `OnContent`, `OffContent`,
`VerticalAlignment` and the margin from §A.7.

## A.5 Variant 2 — several actions

**[task B, measured — this section's rule was rewritten. The original said: "up to two peer actions
trail in column 2; three or more go on the expansion row, right-aligned." The 760 DIP constraint it
rested on was re-measured and no longer binds; see §6.1(b) for the numbers.]**

**Rule: every action strip trails in column 2, however many buttons it holds. The expansion row is for
full-width content — value lines, a progress bar, a bordered list — not for actions.** The strip is a
`StackPanel Classes="tm-card-actions"` either way, so invariant I3 holds wherever it sits, and the
per-button `VerticalAlignment="Center"` that all eleven buttons carried becomes unnecessary (a
horizontal `StackPanel` stretches its children to its own height, and the panel is centre-aligned).

```xml
<!-- two, three, however many: column 2 -->
<StackPanel Grid.Column="2" Classes="tm-card-actions">
  <Button x:Name="ClearCacheBtn" Theme="{StaticResource TmButton}" Click="OnClearCacheClick" />
  <Button x:Name="BackupBtn"     Theme="{StaticResource TmButton}" Click="OnBackupConfigClick" />
  <Button x:Name="RestoreBtn"    Theme="{StaticResource TmButton}" Click="OnRestoreConfigClick" />
</StackPanel>
```

`tm-card-actions` contributes `Orientation`, the 8 DIP gap, the right alignment and the vertical
centring. On the expansion row it composes cleanly with `tm-card-expander` (`Margin="0,8,0,0"` and the
same `Spacing` of 8) — that combination is still legal and is what a future full-width action strip
would use; no card uses it today.

**Why "everything trails", and what replaced the old ≥3 rule. [task B, measured]** The below-row
placement was inherited from a real measurement: three buttons plus the text column were recorded as
clipping past the visible edge at `MinWidth="760"` (`FoldersCard.axaml:22-29`, "confirmed by resizing to
exactly that minimum and re-screenshotting"). That measurement was taken when column 1 *also* held a
`NoWrap` path line with `MaxWidth="260"`, and §A.6 moves that line out. The measurement was therefore
retaken rather than inherited: the live window was shrunk with `SetWindowPos` until the platform clamped
it at its enforced floor (`GetWindowRect` 1162 px = 760 DIP client + the 22 px horizontal inset), and
every control was read from its UIA rect. **Nothing clips.** On 数据文件夹 and 维护 the three-button
strip ends exactly on the card's content right edge with the standard 12 DIP gutter, in Chinese and in
English — English being the worse case, where 重置为默认 (104.67 DIP) becomes Reset to Default (133.33).

What 760 DIP does cost is wrapping: 数据文件夹's subtitle takes two lines there in Chinese and three in
English. That is not the strip's doing and moving the strip would not fix the page — at the same width
每列个数's English subtitle wraps with nothing but a 220 DIP ComboBox beside it, and 主文件夹's wraps with
only two buttons. `tm-card-sub` wraps by design. **A header of exactly two RENDERED lines is a property
of the width the app actually opens at, not of any button placement**, which is why "do they fit" is the
test the rule rests on and "does the header stay two lines at the floor" is not.

**Consequence:** 主文件夹 and 数据文件夹 are now structurally identical in `4.png` — icon, two-line
header, trailing strip, value lines below — which is the user's first complaint, closed rather than
explained.

## A.6 Variant 3 — a value line (a path, a size, a version)

Value lines live on the **expansion row**, never in the header stack. That is what keeps I1 true, and
therefore what keeps the icon (I2) and the trailing control at one y on every row: today
主文件夹's two buttons centre against a three-line block and 数据文件夹's icon centres against a
four-line block, which is where the +19.00 and +28.33 DIP icon outliers come from (r5-settings §3.7).

```xml
<StackPanel Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Classes="tm-card-expander">
  <StackPanel Classes="tm-card-values">
    <TextBlock x:Name="DataPathText" Classes="tm-card-sub tm-card-value" />
    <TextBlock x:Name="DataInfoText" Classes="tm-card-sub tm-card-value" />
  </StackPanel>
  <StackPanel Classes="tm-card-actions"> … </StackPanel>
</StackPanel>
```

**`HorizontalAlignment="Left"` is the whole fix for the floating text in `4.png`.** The cause is proved
in r5-settings §3.5 against Avalonia 12.1.0's own decompiled `Layoutable.ArrangeCore`: `MaxWidth` clamps
the arranged width, and the `Stretch` case then **falls through to `Center`'s centring arithmetic**
(`case HorizontalAlignment.Stretch: case HorizontalAlignment.Center: num += (size.Width -
constraints.Width) / 2.0`). The arithmetic reproduces the measurement to the pixel:
`47.33 + (1029.33 − 260)/2 = 432.0` = `RootPathText`'s measured box x; `47.33 + (1173.33 − 260)/2 =
504.0` = `DataPathText`'s. It is the *absence* of `HorizontalAlignment`, not any alignment the file
sets. The same mechanism is load-bearing 20 lines away, where `SettingsPage.axaml:47-48` uses it
deliberately to centre `CardStack` at its cap.

**`MaxWidth="260"` stays, and it is the one number in this spec that is inherited rather than derived.**
It exists because a `NoWrap` + `CharacterEllipsis` `TextBlock` reports its full *untrimmed* single-line
advance as its measure-time desired width — trimming is a paint-time concern — so without a cap a long
path inflates the card's desired width and pushes the trailing controls past the visible edge
(`FoldersCard.axaml:14-20`, confirmed there by screenshot). Left-aligning removes the centring but not
that. 260 is the shipped, working value; raising it would need the same re-measure at `MinWidth="760"`
that §A.5 defers. The full text stays one hover away — `ReloadFromConfig` already sets a matching
`ToolTip` (`FoldersCard.axaml.cs:138`).

## A.7 Variant 4 — the toggle, and the 12 DIP that is not ours

**[new — decompiled this session]** `Avalonia.Themes.Fluent` 12.1.0's `ToggleSwitch` template builds an
inner `Grid` with three columns: column 0 `Auto`, **column 1 a hard `new GridLength(12.0, Pixel)` with
`MaxWidth = 12`**, column 2 `Auto` holding `PART_OffContentPresenter` / `PART_OnContentPresenter`
(`FluentResources.decompiled.cs:46601-46612`, in the scratchpad). That fixed 12 DIP spacer is charged
whether or not the content presenters are empty, which is exactly the measured gap: the element is
**52 DIP** wide, its visible `OuterBorder` pill is **40**, and the pill's ink stops **18 px = 12 DIP**
short of every other row's control (r5-settings §3.8, `[px]`-confirmed). Blanking `OnContent`/
`OffContent` — done deliberately at `BehaviorCard.axaml:9-12` so an untranslated English "On"/"Off"
never sits beside a Chinese label — collapses column 2, not column 1.

A `ColumnDefinition` is not a `Control`, so no `/template/` selector can reach it. The two real options
are a full `ToggleSwitch` `ControlTheme` (a ~150-line template with knob transitions and eight visual
states, which this project does not have) or a compensating margin. **The design takes the margin:**
`Margin="0,0,-12,0"` in `ToggleSwitch.tm-card-toggle`. Arithmetic: the element's desired width becomes
52 − 12 = 40, so the Auto column measures 40 and spans 1192.67…1232.67; `ArrangeCore` deflates the slot
by the negative margin, giving the element 1192.67…1244.67, so the **pill's right edge lands exactly on
1232.67** and the 12 DIP of empty presenter overhangs into the card's own 16 DIP right padding, where it
draws nothing. The risk is named and bounded: if a future Avalonia changes that column, the five toggle
rows shift by whatever the change is, and §5.4's pixel check catches it.

**[task B, measured]** On a `PrintWindow` scan through each control's vertical centre, bounded inside the
card, the last ink pixel of all fifteen rows' trailing controls — four `ComboBox`/`NumericUpDown`, five
`ToggleSwitch` pills and six `Button`s — is the same image column. The five toggles no longer stop 18 px
short.

## A.8 Variant 5 — a disabled control

No structural difference. What changes is that the page stops shipping **two disabled vocabularies
50 RGB units apart** (r5-settings §3.10):

* app-themed controls dim via `TmButton`'s `:disabled` — `TextDisabledBrush` foreground plus
  `Opacity 0.6` on `Border#Root` (`Launcher.axaml:230-236`) — measured `(253,253,253)` on a
  `(251,251,251)` card;
* `HotkeyBox` carries **no `Classes` at all** (`BehaviorCard.axaml:112-113`) and no `TextBox` style in
  this repo applies to it, so it falls through to Fluent's stock `:disabled` rule, which repaints
  `Border#PART_BorderElement.Background` to `TextControlBackgroundDisabled` →
  `SystemControlBackgroundBaseLowBrush` → `SystemBaseLowColor` = `#33000000`
  (`Avalonia.Themes.Fluent.decompiled.cs:67205 → 19304 → 91483`). 20 % black over 251 = 200.8, and the
  measured fill is `(201,201,201)` — the heavy grey in `8.png`.

The fix uses the precedence this file already proved works: an app-level `Style` targeting a named
template part outranks the `ControlTheme`'s own rule for the same part and the same pseudo-class — the
mechanism written up at `Launcher.axaml:392-421`, where `TextBox.tm-search:focus /template/
Border#PART_BorderElement` beats Fluent's built-in focus repaint.

```xml
<!-- The hotkey capture box. The ONLY stock-Fluent-themed control on the page; these rules bring it
     into the app's own vocabulary in all four states. See Launcher.axaml's tm-search block for why
     targeting the named part is what reaches the screen. -->
<Style Selector="TextBox.tm-keycap">
  <Setter Property="Width" Value="160" />
  <Setter Property="MinHeight" Value="32" />
  <Setter Property="Padding" Value="8,0" />   <!-- [task B, measured] was 8,5 -->
  <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}" />
  <Setter Property="VerticalContentAlignment" Value="Center" />
</Style>
<Style Selector="TextBox.tm-keycap /template/ Border#PART_BorderElement">
  <Setter Property="Background"      Value="{DynamicResource SurfaceLayerBrush}" />
  <Setter Property="BorderBrush"     Value="{DynamicResource StrokeControlBrush}" />
  <Setter Property="BorderThickness" Value="1" />
  <Setter Property="CornerRadius"    Value="{DynamicResource RadiusControl}" />
</Style>
<Style Selector="TextBox.tm-keycap:pointerover /template/ Border#PART_BorderElement">
  <Setter Property="Background" Value="{DynamicResource SurfaceLayerActiveBrush}" />
</Style>
<!-- Focus matters here more than on any other control: "click the box, then press the combination"
     IS the interaction (BehaviorCard's tunneling KeyDown handler). One accent hairline, not Fluent's
     filled accent band — the same conclusion Round 3, Task 1 reached for the search field. -->
<Style Selector="TextBox.tm-keycap:focus /template/ Border#PART_BorderElement">
  <Setter Property="Background"  Value="{DynamicResource SurfaceLayerActiveBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource AccentDefaultBrush}" />
</Style>
<!-- Disabled: the SAME recipe TmButton uses, so the page has one disabled vocabulary. -->
<Style Selector="TextBox.tm-keycap:disabled">
  <Setter Property="Foreground" Value="{DynamicResource TextDisabledBrush}" />
  <Setter Property="Opacity"    Value="0.6" />
</Style>
<Style Selector="TextBox.tm-keycap:disabled /template/ Border#PART_BorderElement">
  <Setter Property="Background"  Value="{DynamicResource SurfaceLayerBrush}" />
  <Setter Property="BorderBrush" Value="{DynamicResource StrokeControlBrush}" />
</Style>
```

Predicted fill when disabled: `SurfaceLayerBrush` `#B3FFFFFF` over the 251 card = 253.8, then 0.6
opacity over the card = 251 + (254 − 251) × 0.6 = **252.8 → 253** — the same `(253,253,253)` a disabled
`TmButton` measures. §5.4 asserts it. **[task B, measured]** It measures `(253,253,253)` in light,
against `ChangeDataBtn`/`ResetDataBtn`'s `(253,253,253)` on the same page, and `(43,43,43)` in dark
against those buttons' `(43,43,43)`; enabled controls measure 254 and 46 respectively.

**[task B, measured]** `Padding` is `8,0`, not the `8,5` first written here. With `8,5` the box's own
content exceeded 32 DIP, so `MinHeight` stopped binding and the box measured 50/50/49/48/49/49 physical
px across the eight scroll stops against a `TmButton`'s flat 48 — and, because a horizontal `StackPanel`
stretches its children, it dragged 清除 to the same 50. `8,0` is what `TmButton` itself states, for
exactly this reason; with it the box reads 49/48/48/47/48/48, the residual ±1 being rect quantisation
that tracks the scroll offset. An explicit `Height="32"` was tried instead and measured identical, so
`MinHeight` stays.

**`TmButton`'s own disabled recipe is NOT changed.** A disabled button being only two RGB levels off the
card is a legitimate complaint, but the fix would repaint every button in the app including 应用's, which
is outside "settings rows obey one spec". Item 3 is satisfied by there being **one** vocabulary; making
that vocabulary louder is its own item. Recorded as settled in §6.2.

## A.9 Variant 6 — a row with no control

Column 2 simply has no child. The Grid is unchanged; the header is still two lines; the card is still
62.67 DIP. `当前版本` is the only such row today (`UpdatesCard.axaml:20-28`); after §C.2 the only such
row is 关于.

## A.10 What is deliberately left alone

* **`ToolsCard`'s tool list is 9.33 DIP in from the title** (r5-settings §3.6) because its wrapping
  `Border` adds `Padding="8,4"` and a 1 px stroke on top of the row indent (`ToolsCard.axaml:33-35`).
  Ruling: **conforming.** The spec constrains where a row's expansion *content* starts — the list
  container's left edge, which lands on 47.33 like everything else. What a bounded, outlined container
  does inside itself is that control's business, and a `CheckBox`'s own template padding makes exact
  glyph alignment unattainable anyway.
* **`SettingsPage`'s 24/16/24/24 `Margin` and 1250 `MaxWidth`** (`SettingsPage.axaml:51`) — measured
  against the reference in Round 3 and carrying a long correctness comment about `ScrollViewer` padding
  (`:12-31`). Untouched.
* **Group heads** (`Launcher.axaml:744-749`) and the page title (`:754-758`). Untouched.

---

# Part B — per-row remediation

Seventeen rows: the sixteen that exist plus the one 关于 adds. "Renders identically" means the pixels do
not move; those rows still swap three literal attribute triplets for the class names in §A.2, which is a
mechanical edit with no visual effect (the icon's measured 16 DIP width becomes an explicit 16; the
20/16 line heights sum to the same 36 the row already measures).

| # | Group · row | file:line | Conforms? | Change |
|---|---|---|---|---|
| 1 | 外观 · 语言 | `AppearanceCard.axaml:24-34` | **yes** | classes only |
| 2 | 外观 · 外观 | `:36-46` | **yes** | classes only |
| 3 | 外观 · 字体 | `:48-63` | **yes** | classes only |
| 4 | 外观 · 每列个数 | `:79-90` | **yes** | classes only |
| 5 | 行为 · 失焦自动隐藏 | `BehaviorCard.axaml:16-26` | no (I3) | toggle → `Classes="tm-card-toggle"`; drop the local `OnContent`/`OffContent`/`VerticalAlignment` (`:23-24`). Pill's right edge moves +12 DIP onto 1232.67 (§A.7) |
| 6 | 行为 · 窗口置顶 | `:28-38` | no (I3) | same, at `:35-36` |
| 7 | 行为 · 开机自启 | `:40-50` | no (I3) | same, at `:47-48`. Disabled state unchanged (portable build) |
| 8 | 行为 · **固定到任务栏** — `7.png` | `:52-89` | no (I1, gap) | **root `StackPanel` deleted**; card becomes a plain Variant-1 row. `PinTaskbarStatusText` (`:86-87`) moves **into the header stack as the `tm-card-sub`**, replacing the bare `TextBlock Grid.Column="1"` at `:56-57` with the standard `StackPanel Classes="tm-card-text"`. Both causes of the measured **14.00 DIP** title→text gap die with it: the `Margin="30,8,0,0"` (cause 1) and the ~6.33 DIP of slack under a 19.33 DIP title centred in a 32 DIP grid row (cause 2, r5-settings §3.4). `PinTaskbarBtn` (`:83-84`) stays in column 2, unchanged, including the run-time `Theme` swap at `BehaviorCard.axaml.cs` — the header is 36 DIP so its 32 DIP box still centres. Card 83.33 → 62.67 |
| 9 | 行为 · 启用全局热键 | `:91-101` | no (I3) | toggle class, at `:98-99` |
| 10 | 行为 · **热键** — `8.png` | `:103-122` | no (I1, gap, disabled fill) | as row 8: root `StackPanel` deleted, `HotkeyHelpText` (`:120`) becomes the header's `tm-card-sub`, the bare title at `:107-108` becomes a `tm-card-text` stack. Measured **14.67 DIP** gap → 0. `HotkeyBox` (`:112-113`) gains `Classes="tm-keycap"` and drops its local `Width`/`Padding`/`VerticalContentAlignment` (now in the class); its disabled fill goes from `(201,201,201)` to `(253,253,253)` (§A.8). The `TextBox`+`Button` group stays in column 2 as a `Classes="tm-card-actions"` strip (`:109`). Card 84 → 62.67 |
| 11 | 文件夹 · **主文件夹** — `4.png` | `FoldersCard.axaml:33-49` | no (I1, I2, centring) | `RootPathText` (`:39-40`) moves out of the header stack to the expansion row as `Classes="tm-card-sub tm-card-value"`; `HorizontalAlignment="Left"` (from the class) moves it from measured **x 432.0 back to 47.33**. Header returns to two lines → icon offset returns from **+19.00** to the page's +8.00, and the two trailing buttons (`:42-47`) return to the same y as every other control. `Spacing="8"` and the three `VerticalAlignment="Center"` attributes fold into `tm-card-actions`. Card 84.67 → **86.67 measured** |
| 12 | 文件夹 · **数据文件夹** — `4.png` | `:51-76` | no (I1, I2, I3, centring) | root `StackPanel` becomes the two-row Grid. `DataPathText`/`DataInfoText` (`:58-63`) → `tm-card-values` on the expansion row, left at **47.33** (from **504.0**). Icon offset **+28.33 → +8.00**. **[task B, measured]** The three buttons (`:67-74`) do not stay below — they move into column 2 as a `tm-card-actions` strip, right edge on **1232.67** (§A.5, §6.1(b)); the `Margin="30,10,0,0"` constant goes with them. Card 146 → **104.67 measured** |
| 13 | 文件夹 · **维护** — `5.png` | `:78-97` | no (I3) | root `StackPanel` → single-row Grid; **[task B, measured]** the three buttons (`:88-95`) move into column 2 as a `tm-card-actions` strip rather than staying below. The empty right half in `5.png` is what this fixes. Card 104.67 → **62.67 measured** — an ordinary row |
| 14 | 常用工具 · 显示"常用工具"分类 | `ToolsCard.axaml:17-41` | no (I3) | toggle class (`:25-26`); root `StackPanel` → two-row Grid with the list `Border` (`:33-39`) as `Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Classes="tm-card-expander"`, replacing `Margin="30,10,0,0"` with column 1 + 8. The list's left edge does not move; its right edge now reaches the content edge instead of stopping wherever its content did. Inner 9.33 DIP inset: conforming by §A.10 |
| 15 | 更新 · 当前版本 | `UpdatesCard.axaml:20-28` | **deleted** | The version and its channel help move: the version to the new 关于 card (§C.2), the channel help (`UpdateHelpText`, `:25`, chosen at `UpdatesCard.axaml.cs:85-90`) onto row 16's expansion row as a wrapping `tm-card-sub`. Deleting it also retires the `E946` glyph clash the recon flagged (three consumers, two of them one card apart) |
| 16 | 更新 · **update status** — `6.png` | `:30-52` | no (I1, I3) | **gains a real title.** `UpdateStatusText` (`:35`) stops being the `tm-card-title` and becomes the `tm-card-sub`; a new `UpdateStatusTitle` `tm-card-title` is added above it, text from a new key `UpdateStatusTitle` = (更新状态, Update status). Its local `TextWrapping="Wrap"` goes — the sub class already wraps. `CheckUpdateBtn`/`UpdateActionBtn` (`:45-50`) move **from the expansion row into column 2** as a `tm-card-actions` strip — two actions, so §A.5's rule puts them trailing; that is what fixes the lone greyed button floating bottom-left in `6.png`. The `ProgressBar` (`:39-40`) stays on the expansion row and keeps `IsVisible="False"`. Channel help joins it there (row 15), so the two share one `Classes="tm-card-expander"` `StackPanel`. **[task B, measured]** Card 88 → **86.67**, not the 62.67 first written here: an always-visible help line on the expansion row costs 8 + 16 whatever the `ProgressBar` does. 62.67 would only have been right had the help line gone somewhere else, which row 15 says it does not |
| 17 | **关于 · TanMenu** (new) | new `Views/Settings/AboutCard.axaml` | n/a | §C.2 |

**Score: 4 of 16 rows conform today. 12 change, and one of those 12 (当前版本) is deleted with its
content re-homed. One row is new.**

**[task B] One consequence this table did not anticipate: three language strings became false and were
reworded.** `HotkeyHelp` said 点击**上方**输入框 / "Click the box **above**" and
`PinToTaskbarNotAllowed` / `PinToTaskbarUnsupported` said 点击**上方**按钮 / "Use the button **above**".
Rows 8 and 10 move those very lines from below the Grid into the header, which puts the control they
point at *beside* the sentence rather than above it. All three now say 右侧 / "on the right", which
invariant I3 makes stable. No test enumerates language keys, so the edit is free.

Every one of the five screenshotted defects, with its fix and its line:

| Screenshot | Defect | Fix | Where |
|---|---|---|---|
| `4.png` | path lines floating in mid-card (measured +384.67 / +456.67 DIP right of the title) | `HorizontalAlignment="Left"` via `tm-card-value`; value lines move to the expansion row | `FoldersCard.axaml:39-40, 58-63` → §A.6 |
| `4.png` | two adjacent folder cards, one with trailing buttons and one with bottom-left buttons | both strips become `tm-card-actions`; both end on 1232.67 | `FoldersCard.axaml:42-47, 67-74` → §A.5 |
| `5.png` | 维护's three buttons bottom-left under an empty right half | `tm-card-actions` in column 2 **[task B, measured — see §A.5]** | `FoldersCard.axaml:88-95` |
| `6.png` | the update card has no title — the dynamic status string wears `tm-card-title` | new static title; status demoted to `tm-card-sub` | `UpdatesCard.axaml:35` |
| `6.png` | a disabled 检查更新 alone on a bottom-left row | two actions ⇒ column 2 | `UpdatesCard.axaml:45-50` |
| `7.png` | 14.00 DIP hole between title and status text | status becomes the header's subtitle; the `30,8,0,0` margin and the centred-bare-title slack both go | `BehaviorCard.axaml:56-57, 86-87` |
| `8.png` | 14.67 DIP hole, and a hard-grey dead capture box beside a live button | as above, plus `TextBox.tm-keycap`'s four template-part rules | `BehaviorCard.axaml:107-108, 112-113, 120` → §A.8 |

---

# Part C — items 1 and 2

## C.1 Item 1 — 退出 moves to the bottom of the rail

### Decision

The footer becomes, top to bottom:

```
<StackPanel Grid.Row="1" Margin="0,0,0,4">          ← the 4 DIP bottom inset moves HERE
  NavOpenRootItem   (E8B7, OnOpenRootClick)
  NavRefreshItem    (E72C, OnRefreshClick)
  <ListBox x:Name="NavFooterRail" … Padding="0">    ← was Padding="0,0,0,4"
    NavSettingsItem (E713)
  </ListBox>
  <Border Classes="tm-nav-separator" />             ← moved from :465
  NavExitItem       (E7E8, OnExitClick)             ← moved from first to last
</StackPanel>
```

`NavAboutItem` (`MainWindow.axaml:467-473`) is deleted by item 2. The separator style itself
(`Launcher.axaml:617-621` — `Height 1`, `Margin 4,4`, `StrokeDividerBrush`) is **unchanged**; only its
position in the markup moves.

### Why

* The user's instruction is explicit and overrides Round 4's ordering argument, which put 退出 first
  precisely to keep it away from 设置 (`MainWindow.axaml:423-429`). That argument's *mitigation* — fence
  the terminal action off with the one group boundary the Round 3 move dropped
  (`MainWindow.axaml:444-451`) — survives the reversal intact, which is why the separator moves with
  退出 rather than staying put or being deleted.
* Reordering is inert with respect to code, verified twice: `ApplyRailText` addresses every row by
  `x:Name` (`MainWindow.axaml.cs:708-734`) and `OnNavSelectionChanged` branches on `ReferenceEquals`
  against the two `ListBox` instances (`:691-695`). Nothing downstream is index-based.
* **The `Padding="0,0,0,4"` must move.** Its own comment says it encodes "I am the last child of the
  footer StackPanel" (`MainWindow.axaml:502-506`), and it no longer is. Moving it onto the footer
  `StackPanel` as a `Margin` rather than onto 退出 puts the inset where the fact actually lives — the
  footer's distance from the pane's bottom edge — so no future reorder has to unpick it again.

### Blast radius

**Files:** `MainWindow.axaml` (reorder inside `:452-518`; rewrite the ordering rationale at `:414-451`
and `NavFooterRail`'s comment at `:489-506`), `Themes/Fluent2/Launcher.axaml` (the separator's comment
block at `:599-616` only — its "the footer measures 213 DIP rather than 204" claim becomes 173).

**Geometry, derived from the measured row box.** Row pitch is 40 DIP (36 box + 2×2 margin), measured
60 px at 150 % (r5-about-rail §1.3). The separator block is 9 DIP (1 + 2×4). Footer =
4 × 40 + 9 + 4 = **173 DIP**, against today's measured **213.3** (5 × 40 + 9 + 4 = 213 ✓). Rail content
total = 64 (page list: 12 + 40 + 12) + 173 = 237 DIP against a body of at least 519 DIP. No overflow, and
**the rail is not an input to the window fit at all** (§5.1).

**Not affected:** the two rails' mutual exclusion (`MainWindow.axaml.cs:594-595`, `:691-692`) and its
three guards (`_navReady` `:672`, `_inShowPage` `:675-686`, `rail.SelectedIndex < 0` `:688-689`); 设置
stays a `ListBoxItem` in `NavFooterRail` for the reason Round 4 §2.4 records (a `Button` has no
`:selected`). Tests: none — nothing under `tests/` references the footer.

### Verification (no keyboard, no mouse)

1. UI Automation tree walk of the footer's children in tree order; the `Name` sequence must be
   **打开主目录 / 刷新 / 设置 / 退出** and nothing else.
2. `PrintWindow(hwnd, hdc, 2)`; scan glyph bands over the 40 DIP item column. 退出's `E7E8` must be the
   **bottom-most glyph in the rail**, its box bottom 4 DIP + 2 DIP margin above the client bottom.
3. The hairline: a run of `StrokeDividerBrush` spanning the 40 DIP item column, 2 physical rows at
   150 %, sitting between 设置's box bottom and 退出's box top. Box-to-box distance across it must be
   **13 DIP (20 px)** against **4 DIP (6 px)** between any ordinary neighbouring pair — the same
   20 px / 6 px the current build measures across its own separator (r5-about-rail §1.3).
4. Footer height, from 打开主目录's margin-box top to the client bottom: **173 DIP (260 px)**.
5. `dotnet test` green with no test file edited.

## C.2 Item 2 — 关于 leaves the rail and becomes a settings card

### Decision

**(a) The overlay is deleted, not hidden.** With everything it orphans, per this project's no-dead-code
rule. The complete list, every entry verified by the recon's own greps (r5-about-rail §2.3, §2.4):

| Deleted | Location |
|---|---|
| `NavAboutItem` | `MainWindow.axaml:467-473` |
| `OnAboutClick` + its 24-line xmldoc | `MainWindow.axaml.cs:866-894` |
| the `aboutText` local and its two writes | `MainWindow.axaml.cs:715, 726, 732` |
| the About `Border` | `AppPage.axaml:269-278` |
| `OnAboutPressed` | `AppPage.axaml.cs:438-442` |
| `_showAbout` / `ShowAbout` / `OpenAbout` / `CloseAbout` | `LauncherViewModel.cs:50, 404-408, 423, 425` |
| the splash's `HintText` binding | `AppPage.axaml:226` |
| `SplashPanel`'s hint `TextBlock`, `ShowHintProperty`, `HintTextProperty`, the ctor mirror and the `OnPropertyChanged` mirror | `SplashPanel.axaml:127-130`; `SplashPanel.axaml.cs:19-20, 31-32, 37, 46-49, 64-67, 74` |
| `LauncherViewModel.ClickToDismissText` + its `RaisePropertyChanged` | `:269, :286` |
| `AppLanguage["ClickToDismiss"]` | `AppLanguage.cs:203` |
| `CornerRadius x:Key="RadiusOverlay"` | `Tokens.axaml:134` |

The hint chain goes because it is already unrenderable without the overlay: the surviving splash usage
binds `HintText` but its `ShowLoading` is bound to `ShowSplash`, and `ShowHint` is `!ShowLoading`, so the
hint is false for the entire time the splash is visible (`AppPage.axaml:220-226`,
`SplashPanel.axaml.cs:37, 74`).

**Survives, and must not be touched:** `SplashPanel` the type (`AppPage.axaml:220-225` still uses it as
the startup splash), `LauncherViewModel.VersionText` / `CopyrightText` (the splash binds both),
`OverlayPanel` (it still hosts the splash, two empty states and the toast — only its class xmldoc, which
names the About panel as its reason for existing, needs a correction), and all `ShellNavigator` /
`ShellPage` plumbing.

**`RadiusOverlay` is deleted** rather than kept: the About `Border` at `AppPage.axaml:271` is its only
consumer anywhere (grepped, r5-about-rail §2.4), and no test bites — `Fluent2TokenTests` checks
referenced→defined (`:57-69`, `:71-85`) and light/dark parity (`:87-101`), never defined→referenced, and
`RadiusOverlay` is variant-independent anyway. Two comments must be rewritten with it:
`Tokens.axaml:132` ("4 for controls, 8 for the overlay/window surface") and `Tokens.axaml:136-137`
("Declared beside `RadiusOverlay` for the same reason `RadiusControlInner` is declared beside
`RadiusControl`") — `RadiusPaneTopLeft` keeps its measurement and re-points that clause at
`RadiusControl`.

**The corner-clipping comment loses its evidence.** `MainWindow.axaml:542-556` says a `Border` clips its
child to its radius and cites the About overlay's traced arc (94, 91, 90, 89, 88, 87, 86, 86, 86, 85, 85
down eleven rows) as what established it. Nothing else inside `AppPage` reaches the pane's top-left arc
— every candidate is inset (`PinNotice` 12,0,12,8; the update banner 16,16,16,0; `Body` 16;
both `tm-empty` panels centred; `tm-toast` centred with a 16 margin), verified element by element in
r5-about-rail §2.5. **Ruling: keep `ClipToBounds="True"` and rewrite the comment** to say the capture
that established it is Round 4's, that the subject of that capture no longer exists in the tree, and
that the observation could not distinguish which of the two `Border`s clipped. Do not restage the
experiment and do not claim it is re-verifiable from the current tree. That answers r5-about-rail's open
question 8 without letting a comment cite evidence that is gone.

**(b) One new card, in its own group, last on the page.**

`Views/Settings/AboutCard.axaml` + `.axaml.cs`, added as the last child of `CardStack`
(`SettingsPage.axaml:57`). No other wiring: `SettingsPage.ApplyStaticText` discovers cards structurally
via `CardStack.Children.OfType<ISettingsCard>()` (`SettingsPage.axaml.cs:34-40`).

```xml
<StackPanel>
  <TextBlock x:Name="GroupHead" Classes="tm-group-head" />
  <Border Classes="tm-card">
    <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,Auto">
      <Image Grid.Column="0" Classes="tm-card-icon" Height="16"
             Source="avares://TanMenu.Wpf/wwwroot/app-tile.png" />
      <StackPanel Grid.Column="1" Classes="tm-card-text">
        <TextBlock Text="TanMenu" Classes="tm-card-title" />
        <TextBlock x:Name="PublisherText" Classes="tm-card-sub" />
      </StackPanel>
      <StackPanel Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2"
                  Classes="tm-card-expander tm-card-values">
        <TextBlock x:Name="VersionText"   Classes="tm-card-sub tm-card-value" />
        <TextBlock x:Name="CopyrightText" Classes="tm-card-sub tm-card-value" />
      </StackPanel>
    </Grid>
  </Border>
</StackPanel>
```

| Slot | Content on this machine | Source |
|---|---|---|
| group head | `关于` / `About` | `AppLanguage["MenuAbout"]` **renamed to `About`** (`AppLanguage.cs:188`) |
| icon | the 16×16 product tile | `avares://TanMenu.Wpf/wwwroot/app-tile.png`, already used at `SplashPanel.axaml:94` and `MainWindow.axaml:219` |
| title | `TanMenu` — a XAML literal, exactly as the overlay had it | `SplashPanel.axaml:98` |
| subtitle | `由 TanSoft 出品` / `by TanSoft` | **new key** `AboutPublisher`. The overlay's literal `by TanSoft` (`SplashPanel.axaml:100`) was never localized; an untranslated English line is acceptable inside a branded splash card and is not acceptable as a settings-row subtitle |
| value 1 | `当前版本：0.12.0` | `AppLanguage["CurrentVersion"]` (`:117`), **key unchanged, consumer moved** off the deleted 当前版本 card |
| value 2 | `© 2026 TanSoft. All rights reserved.` | the same expression the overlay rendered (`LauncherViewModel.cs:274`) |
| control | none | Variant 6 (§A.9) |

`AboutCard` implements `ISettingsCard.ReloadFromConfig()` and takes **no `_loading` guard** — it has no
writable control, the documented reason `FoldersCard` (`FoldersCard.axaml.cs:25-30`) and `UpdatesCard`
(`UpdatesCard.axaml.cs:73-77`) have none either. It needs `ConfigService` only, for
`UiLanguage => _config.Config.General.Language`.

**(c) One small consolidation ships with it: `Services/AppIdentity.cs`.** The version expression exists
twice today, with **different fallbacks** — `LauncherViewModel.cs:54-55` (`?? "1.0.0"`) and
`UpdatesCard.axaml.cs:95-96` (`?? "0.0.0"`) — and the copyright string exists once as C# interpolation
with a live year (`LauncherViewModel.cs:274`) while the assembly carries a *different*, never-read
string (`TanMenu.Wpf.csproj:27`, no year). Adding a third copy in `AboutCard` is not acceptable, so:

```csharp
internal static class AppIdentity
{
    public const string ProductName = "TanMenu";
    public const string Publisher   = "TanSoft";
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string Copyright => $"© {DateTime.Now.Year} {Publisher}. All rights reserved.";
}
```

`LauncherViewModel.AppVersion` (`:54-55`) and `CopyrightText` (`:274`) and `AboutCard` all read it; the
`"1.0.0"` fallback is retired in favour of `"0.0.0"` (a missing version is a broken build either way, and
`0.0.0` sorts below every real one). `UpdatesCard.GetCurrentVersion` (`:95-96`) is deleted with the
当前版本 card. **No assembly *attribute* is read** — `InformationalVersion` carries the commit SHA and
`AssemblyCopyright` carries the year-less string, and `grep` finds zero attribute reads anywhere in
`src/` today (r5-about-rail §2.2). Keeping it that way keeps the AOT/trimming question from ever being
asked (§5.5).

### Why these choices

* **Its own group, last.** Windows-11 convention puts identity at the end, and it is the only group on
  the page with no interactive control — the page ends on information rather than on a half-disabled
  action. The 更新 group keeps the page's last *action*.
* **The version moves rather than duplicating.** `当前版本：0.12.0` is currently the 更新 group's own
  card title (`UpdatesCard.axaml:24`). An About card that also showed a version would show it twice, one
  card apart, and the recon notes that the 当前版本 card is "structurally already an About row" — icon,
  version, an edition/channel line, no control. Moving it is the smaller change than keeping two. What
  stays in 更新 is what 更新 is about: the status, the progress and the check button, with the
  channel-specific help line demoted to that card's expansion row (row 16 in §B).
* **`MenuAbout` is reused, renamed.** Its only remaining consumer after the rail row goes would be this
  group head, and a key named `Menu*` labelling a settings group head is a lie the comment at
  `AppLanguage.cs:181-185` would then have to carry. Rename to `About`; that comment shrinks by one
  entry.
* **No edition label, no repository link, no build date.** The edition is already on the page as
  `当前：绿色版 · 34.7 MB` (`FoldersCard.axaml.cs:139-141`), and the only localized labels that exist
  (`DataKindPortable`/`DataKindApp`/`DataKindDefault`/`DataKindCustom`, `AppLanguage.cs:109-112`) are
  *data-location* labels, not app editions — reusing them for an edition would be a semantic lie and
  minting a third set is scope the item does not ask for. `RepositoryUrl` is a private const
  (`AppUpdateService.cs:42`) and exposing it means a new launch path and a new button. Build date and
  licence text: nothing in `src/` computes either (r5-about-rail §3).

### Blast radius

**Files touched:** `MainWindow.axaml`, `MainWindow.axaml.cs`, `Views/AppPage.axaml`,
`Views/AppPage.axaml.cs`, `ViewModels/LauncherViewModel.cs`, `Controls/SplashPanel.axaml`,
`Controls/SplashPanel.axaml.cs`, `Controls/OverlayPanel.cs` (xmldoc only),
`Themes/Fluent2/Tokens.axaml`, `TanMenu.Core/Services/AppLanguage.cs`,
`Views/SettingsPage.axaml`, `Views/Settings/UpdatesCard.axaml(.cs)`, plus the two new files
(`Views/Settings/AboutCard.axaml(.cs)`, `Services/AppIdentity.cs`).

**Not touched:** `IShellCommands` (one member, `Exit()` — there is no About member to remove,
`Services/IShellCommands.cs:7-11`), `ShellPage` (two values, `Services/IShellNavigator.cs:3-7`),
`TrayService` (three items, no About, `TrayService.cs:37-64, 103-113`). Config: nothing.

**Language keys, net:** rename `MenuAbout` → `About`; add `AboutPublisher` and `UpdateStatusTitle`;
delete `ClickToDismiss`. `CurrentVersion` is unchanged with a moved consumer. No test enumerates keys
(`AppLanguageTests.cs`, read in full — it asserts four specific behaviours and no key inventory).

### Verification (no keyboard, no mouse)

1. `grep -rn "ShowAbout\|OpenAbout\|CloseAbout\|OnAboutClick\|OnAboutPressed\|ClickToDismiss\|ShowHint\|HintText\|RadiusOverlay" src/` returns **nothing**.
2. `grep -rn "MenuAbout" src/` returns nothing; `grep -rn "\"About\"" src/` returns exactly two hits — the key and `AboutCard`'s group-head read.
3. `grep -rn "GetName().Version" src/` returns exactly one hit, in `AppIdentity.cs`.
4. UI Automation on 设置 (reach it with `SelectionItemPattern.Select` on `NavSettingsItem` — patterns are
   programmatic and work where `SendInput` no-ops, the route Round 4 §5.4 step 7 used), then
   `ScrollPattern.SetScrollPercent(100)`: the last group head must read `关于`, and under it a card whose
   Text descendants are exactly `TanMenu`, `由 TanSoft 出品`, `当前版本：0.12.0`,
   `© 2026 TanSoft. All rights reserved.`, in that order.
5. The 更新 group must contain exactly **one** card, and no element anywhere on the page other than the
   About card may contain the string `0.12.0`.
6. The rail must expose exactly five rows (应用 + four footer rows), none of them named 关于.

---

# Cross-cutting

## 5.1 Re-fit triggers, and why the settings page is not one

**Confirmed from the code, not assumed.** `MainWindow.RefitToContent` is:

```csharp
private void RefitToContent()
{
    if (!ReferenceEquals(PageHost.Content, _appPage))
        return;

    (_host as WindowHost)?.FitToContent(_appPage, RailWidthDip + PaneBorderDip);
}
```

`MainWindow.axaml.cs:343-349`. The guard returns before measuring anything whenever 设置 is the hosted
page, and the thing measured is `_appPage`, never the settings page and never the rail — the rail enters
only as the constant `RailWidthDip` (`:356-368`, a literal 48 mirroring `MainWindow.axaml`'s pane width).
**So neither item 1 nor item 3 can reach the window fit at all.** Item 2's only edit inside `_appPage` is
the removal of an `IsVisible="False"` overlay child from `OverlayPanel`, and an invisible child
contributes nothing to a measure, so it cannot move the page's desired size either.

The trigger list is therefore **unchanged from Round 4 §6.1 — five triggers**
(`MainWindow.axaml.cs:219-223`, `:333`/`:391`, `:418-419`, `:635-636`, `:501-503`). One clause in that
section's prose dies with item 2: trigger 5 ("设置 → 应用 switch") is no longer "also newly reached by
关于 from 设置", because there is no 关于 row to reach it from. The trigger itself stays — a real rail
click to 应用 still fires it.

## 5.2 The fit arithmetic

Unchanged from Round 4 §6.2, restated so the zero-delta claim is checkable:

```
pageDesired   = AppPage.Measure(∞, ∞).DesiredSize                    (WindowHost.cs)
window.Width  = pageDesired.Width  + 48 (rail) + 1 (pane left border)
window.Height = pageDesired.Height + 48 (title strip) + 1 (pane top border)
                clamped to [min(568, work.Height), work.Height]
window.Left   = work.Left + (work.Width − Width) / 2
window.Top    = clamp(work.Bottom − 8 − Height, work.Top, work.Bottom − Height)
```

The shipped build logs `1713.8333,816 1692.3333x568 DIP (page 1643.3333x471.3333 + chrome 49x49)`.
**Round 5 changes no term.** The app's log line after the round must be byte-identical for the same data
set and settings; if it is not, something in item 2's deletions reached `AppPage`, and that is the signal
to stop.

## 5.3 Test-suite delta

**[new — counted this session]** 183 cases today: 125 `[Fact]` + 58 `[InlineData]` rows under 11
`[Theory]` attributes, in the single project `tests/TanMenu.Core.Tests`. Nothing exercises `MainWindow`,
`WindowHost`, `SettingsPage` or any card — they need a UI thread — so items 1, 2 and 3 have **zero**
automated behavioural coverage and §§C.1/C.2/5.4 are the only nets they get.

| File | Delta |
|---|---|
| `Themes/Fluent2TokenTests.cs` | **no change**, and two of its four facts actively guard §A. `LauncherStyles_ContainNoColourLiterals` (`:43-55`) greps the **whole file, prose included** — every new rule and every new comment must be token-only, decimal RGB triples in prose (the warning already at `Launcher.axaml:632-634`). `EveryTokenTheLauncherReferences_IsDefinedInTokens` (`:57-69`) covers the new references — `LineHeightBody`, `LineHeightCaption`, `SurfaceLayerActiveBrush`, `AccentDefaultBrush`, `TextDisabledBrush`, `StrokeControlBrush`, `RadiusControl` — all of which `Tokens.axaml` already defines |
| **new** `Themes/SettingsRowTests.cs` | **+3 facts.** Source tests in the same shape, over `src/TanMenu.Wpf/Views/Settings/*.axaml`: (1) every `<Border Classes="tm-card">` is immediately followed by `<Grid ColumnDefinitions="Auto,*,Auto"` — the invariant no style can carry (§A.2); (2) no card file contains `Margin="30,` or `MaxWidth="260"` — the two constants the spec moved into the theme; (3) per file, the count of the exact attribute `Classes="tm-card-title"` equals the count of `Classes="tm-card-sub"` — a cheap proxy for invariant I1 that value lines (`Classes="tm-card-sub tm-card-value"`) do not disturb, because it matches the attribute value exactly |
| `Services/AppLanguageTests.cs` | **no change** — it asserts four behaviours and enumerates no keys, so the rename and the two additions are free |
| everything else | untouched |

Total after: **186**.

## 5.4 Verification for a machine with no keyboard and no mouse

The tools are UI Automation rects and `PrintWindow(hwnd, hdc, 2)` + `GetPixel`, exactly as both recon
reports used them. **Two protocol rules carry over and are not optional:**

* **Scroll every card fully into view before reading its rect.** Avalonia's UIA peers return the
  element's *last arranged* bounds, and a card scrolled out of `SettingsScroll` keeps a stale rectangle
  indefinitely — the first dump of the recon session mixed two coordinate systems (1875 px vs 1125 px
  for sibling cards). Sweep `ScrollPattern.SetScrollPercent` over 0/15/30/45/60/75/90/100 and accept a
  rect only from a stop where the element lies entirely inside the viewport (r5-settings, "How the
  numbers were obtained").
* `PrintWindow`'s image origin is 11 px left of the UIA window origin (the Windows 11 invisible resize
  border); scale is 1.5, so DIP = px / 1.5.

**Row conformance is one assertion repeated over every card** — three coordinates that must be equal
across all of them:

1. **Icon box.** For every card: icon rect = (card.x + 17.33, card.y + 23.33, 16, 16). Today this holds
   for 11 of 16 and fails by up to 28.66 DIP on the folder rows.
2. **Title box.** Title top = card.y + 13.33, height **20.0** (today 19.33). Subtitle top =
   card.y + 33.33, height **16.0** (today 16.67 for CJK, 16.0 for ASCII). Assert the height is the same
   value on every card, not merely close.
3. **Control right edge.** For every card, the right-most interactive descendant's edge = card.x +
   1232.67. For the five `ToggleSwitch` rows the UIA rect will read 1244.67 (§A.7) — assert the *ink*
   instead: on a `PrintWindow` row through the pill's vertical centre, the last pixel that differs from
   the card fill must be at the image x corresponding to 1232.67. Today the toggles' ink stops 18 px
   short.
4. **Expansion left edge.** Every `Grid.Row="1"` child's left edge = card.x + 47.33.
5. **Card height.** Every simple card 62.67 (94 px). **[task B, measured]** After the migration the
   fifteen existing rows are: 62.67 × 11 (four 外观, six 行为, 维护), **86.67** 主文件夹, **104.67**
   数据文件夹, **86.67** 更新状态, and 常用工具 at whatever its tool list needs (**268** with the five
   default tools). 关于 is task C's. The 144 / 102 / 62.67-idle figures this table first carried
   followed from the old §A.5 and from row 16's expansion row being empty; both premises changed.
6. **Disabled vocabulary.** `HotkeyBox`'s fill must measure `(253,253,253)` ± 1 — the same value a
   disabled `TmButton` measures — not `(201,201,201)`.
7. **Dark variant.** Write `"appearance": "Dark"` into `Data\config.json`, restart, repeat 1–5 (they are
   geometric and must be identical) and re-measure 6: the stock disabled `TextBox` brush resolves to
   `#33FFFFFF` in dark (`Avalonia.Themes.Fluent.decompiled.cs:91523`), so the defect inverts rather than
   disappears and the fix must be checked in both. Restore the config afterwards. Neither recon report
   saw dark at all — this is the round's one unmeasured surface.
8. Item 1's four checks (§C.1) and item 2's six (§C.2).
9. `dotnet test` — 186 green, no existing test file edited. `dotnet publish -c Release` (NativeAOT) with
   0 warnings.

## 5.5 Config contract

**No change at all.** No item reads or writes a setting; `AppConfig.CurrentSchemaVersion` stays 1 and
`ConfigService.Normalize` gains nothing. The 关于 deletion orphans **no** config field — had it done so,
the field would have been retired in place with rewritten remarks, joining `ShowInTaskbar`,
`WindowWidth`/`WindowHeight`, `ButtonSize`, `NavRailExpanded`, `WindowLeft`/`WindowTop` (Round 4 §6.4),
and there would still have been no schema bump, because a bump silently defuses
`ConfigServiceTests.Load_MigratedConfig_KeepsDeliberateAutoCloseChoice`, whose fixture interpolates the
constant.

## 5.6 NativeAOT

Neutral, provided three rules hold:

* **No new `DataTemplate`.** `AvaloniaUseCompiledBindingsByDefault` + `PublishAot` are both on
  (`TanMenu.Wpf.csproj`), so every `DataTemplate` needs a concrete `x:DataType`. None is added: the
  settings cards are populated by imperative `x:Name` writes from `ReloadFromConfig`, they have no
  `DataContext`, and `AboutCard` follows that exactly — **no `{Binding}` anywhere in the new card**.
* **No reflection beyond what already ships.** `AppIdentity` uses
  `Assembly.GetEntryAssembly()?.GetName().Version`, which is the expression already running twice today.
  No `GetCustomAttribute`, no `InformationalVersion`, no type lookups.
* **The rail stays literal XAML rows.** Rebuilding the footer from a collection would add a
  `DataTemplate` over a type that does not exist — the constraint Round 4 §2.4 recorded and
  `MainWindow.axaml:416-419` repeats. A reorder is an edit in the markup and nowhere else.

The new style classes are pure `Setter`s over `DynamicResource` brushes; the one `Image` added
(`AboutCard`'s product tile) uses the `avares://` URI already compiled into the assembly for
`SplashPanel` and `MainWindow`.

---

# 6. Open decisions

## 6.1 The two that change the work

**(a) Does the version move out of 更新, or does 关于 not show one?**

*This design says it moves* (§C.2(b)): `UpdatesCard`'s 当前版本 card (`UpdatesCard.axaml:20-28`) is
deleted, its channel help line moves onto the status card's expansion row, and `当前版本：0.12.0` becomes
a value line on the 关于 card. Say **"keep 当前版本 in 更新"** and the alternative is: `UpdatesCard` is
untouched apart from row 16's fixes, `AboutCard` drops its version value line, and the page shows the
version exactly where it does today. That is roughly 40 fewer lines of edit and one fewer moved language
consumer — but it leaves an information card in an action group and the About card with only a
publisher line and a copyright.

**(b) Do the three-button rows keep their buttons below the header?** — **CLOSED: no. They trail.**

*The design said yes* (§A.5), right-aligned, because the trailing-column overflow at `MinWidth="760"`
was measured and re-screenshotted when it was written (`FoldersCard.axaml:22-29`) and neither recon
report could safely re-test it. Task B was ruled to measure first, and did.

**The measurement.** `SetWindowPos` was used to shrink the live window until the platform's own
`WM_GETMINMAXINFO` floor clamped it: `GetWindowRect` 1162 px = 760 DIP client + the 22 px horizontal
inset `MainWindow.axaml:60` records. Every control was then read from its UIA rect, in Chinese and again
in English (the config's `language` was switched to `en-US`, the app restarted, and the file restored
byte-identically afterwards). Card content width at that size is 629.33 DIP; content right edge 2440
DIP (Chinese) / 2432 (English).

| At 760 DIP | 数据文件夹 (3 buttons) | 维护 (3 buttons) |
|---|---|---|
| strip's right edge | 2440.00 zh / 2432.00 en — **on the content edge** | 2440.00 zh / 2432.00 en |
| gutter to the text column | 12.00 DIP | 12.00 DIP |
| clipped? | **no** | **no** |

So the old constraint no longer binds — it was taken when column 1 also carried a `MaxWidth="260"`
`NoWrap` path line, which §A.6 moves to the expansion row. §A.5 collapses to "controls trail, full-width
content goes below", and `4.png`'s two folder cards become structurally identical.

The ruling's second clause — "*and* the header stays two lines" — turned out not to be a discriminator,
and the same measurement is why: at 760 DIP in English, `每列个数`'s subtitle wraps to two lines with
nothing but a 220 DIP ComboBox beside it, and `主文件夹`'s wraps with only two buttons. No button
placement can hold a two-rendered-line header at that width, so the test the rule can actually rest on
is clipping, and there is none. Captures: `b-760-folders.png`, `b-760-folders-en.png`.

## 6.2 Settled here, recorded so they are not re-opened

| Question | Ruling | Where |
|---|---|---|
| Is the row spec enforced by a `ControlTheme` / style, or by copied markup? | **Both, split at a verified line.** Seven style classes carry everything that has an `AvaloniaProperty`; the `Grid`'s column template has none, so it is literal markup guarded by a source test | §A.2, §5.3 |
| Icon pinned to the title line, or centred on the text block? | **Centred — unchanged.** Enforcing "the header is always two lines" makes centring land on one y for all 16 rows and leaves 11 rows byte-identical. Pinning would have changed 13 | §A.1 (I1/I2) |
| Where do path / status / version lines live? | **The expansion row, never the header stack.** That is what keeps the header two lines, which is what keeps the icon and the trailing control at one y | §A.6 |
| Centred path text | **`HorizontalAlignment="Left"`** on `tm-card-value`. Cause proved against decompiled `ArrangeCore`; `Stretch` shares `Center`'s offset arithmetic under a `MaxWidth` | §A.6 |
| Keep `MaxWidth="260"`? | **Yes**, as a measure-time bound, moved from three literals into the class. Raising it would need its own 760 DIP re-measure — 6.1(b)'s does not cover it, because it was taken with the bound in place | §A.6 |
| The update card's missing title | **New static `UpdateStatusTitle` (更新状态 / Update status)**; the dynamic string is demoted to `tm-card-sub` and loses its local `TextWrapping` | §B row 16 |
| 固定到任务栏 / 热键's 14 DIP gap | **Both status lines become the header's subtitle**; the root `StackPanel` and the `30,8,0,0` margin go. The original "the control needs more room" justification does not bind — both controls leave ≥ 350 DIP of text column even at `MinWidth="760"` | §B rows 8, 10 |
| 热键's grey box | **`TextBox.tm-keycap`, four `/template/` rules**, using the precedence `tm-search` already proved. It also gains a real focus state, which the key-capture interaction needs | §A.8 |
| Make disabled `TmButton`s more visible? | **No.** One disabled vocabulary is the requirement, and it is met by bringing the `TextBox` to the button. Repainting every button in the app is a different item | §A.8 |
| The toggles' 12 DIP short ink | **`Margin="0,0,-12,0"`**, against a hard `GridLength(12, Pixel)` column in the stock template. A full `ToggleSwitch` `ControlTheme` was rejected as ~150 lines for one gap | §A.7 |
| A `MinHeight` on `tm-card`? | **No.** It would absorb a violation of the two-line rule instead of surfacing it; the source test surfaces it | §A.3 |
| `ToolsCard`'s 9.33 DIP inner inset | **Conforming.** The spec constrains the container's left edge, not a bounded control's own padding | §A.10 |
| Where does the About card land? | **Its own `关于` group, last on the page, one card, no control** | §C.2 |
| `MenuAbout` reused? | **Reused and renamed to `About`** — its only surviving consumer is the new group head | §C.2 |
| `RadiusOverlay` | **Deleted** with its only consumer; two `Tokens.axaml` comments rewritten | §C.2(a) |
| The corner-clipping comment | **Rewritten, not restaged.** It keeps the conclusion, names Round 4's capture as the evidence, and states that the subject of that capture is gone and that it never distinguished which `Border` clipped | §C.2(a) |
| The separator when 退出 moves | **Moves with it**, immediately above 退出. The style is unchanged; only its position and the "213 DIP" figure in its comment change | §C.1 |
| `NavFooterRail`'s "I am last" padding | **Moves to the footer `StackPanel` as `Margin="0,0,0,4"`**, where the fact actually lives | §C.1 |
| Assembly metadata on the About card? | **No.** Zero attribute reads today; keeping it that way keeps AOT out of it. The version comes from the expression already shipping, consolidated into `AppIdentity` | §C.2(c), §5.6 |
