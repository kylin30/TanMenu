# The Windows 11 measurement record

> **Status: ARCHIVE.** This file holds measurements that were taken against a real Windows 11 machine,
> were true of the code at the time, and describe code that no longer exists. Nothing here is a
> statement about the current tree. It exists because the measurements themselves are evidence worth
> keeping, and because this project's rule is that a comment which no longer describes the code is a
> defect — so the prose had to leave the source file, and deleting real evidence outright is worse than
> filing it.
>
> Created by **Round 7, Task 1** (the Aero caption), which is the first task to falsify one of these
> blocks. The Aero conversion design (`../specs/2026-08-04-aero-conversion-design.md`, §4 bucket C)
> lists the other blocks that land here as their own tasks reach them. **Sections are added by the task
> that deletes the code the section describes; nothing is moved ahead of that.**

---

## Round 4, item 5 — the white band across the top of the launcher window

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Tokens.axaml`, where it justified a
`TitleBarBackgroundBrush` = `Transparent` override.
**Stopped being true of the code at** Round 7, Task 1, which replaced Avalonia's
`WindowDrawnDecorations` template wholesale — so the part that read the key stopped existing and the
key lost its only consumer.
**Instrument:** `PrintWindow` (flag 2) into a `System.Drawing.Bitmap`, sampled with pixel reads; plus a
temporary `GetVisualDescendants()` dump through `TopLevelHost` on the live process.
**Machine:** this repository's development machine, Windows 11 Pro 10.0.28000, primary monitor at 150 %.

### What was measured

On a `PrintWindow` capture of the live Debug build, before the override existed, the title strip read
**(255, 255, 255)** and the nav rail one row below it read **(241, 241, 249)** at the same x — sampled
at x = 35 and x = 60. That 14/14/6 difference is what the user's item 5 ("任务栏和左侧导航栏应该是背景色
相同") was about. After the override, all four sample points read (241, 241, 249), i.e. the Mica
backdrop.

### Where the band came from

Avalonia's drawn-decorations template (`WindowDrawnDecorationsContent`, instantiated because MainWindow
set `ExtendClientAreaToDecorationsHint`) built an **underlay** — the layer `TopLevelHost` inserts behind
the window's own content — containing a `Panel` named `PART_TitleBar`, `VerticalAlignment=Top`, `Height`
bound to `TitleBarHeight` (48 in the app at the time), `Background` bound to
`{DynamicResource TitleBarBackgroundBrush}`. Read out of the compiled XAML with
`ilspycmd -t "CompiledAvaloniaXaml.!AvaloniaResources"` on Avalonia.Themes.Fluent 12.1.0, then confirmed
on the **running** app with a temporary `GetVisualDescendants()` dump reached through `TopLevelHost`:

```
Panel name=PART_TitleBar bounds=0,0,1000,48 vis=true bg=White
```

immediately before `MainWindow` in the same tree. That dump is what ruled OUT the alternative
explanation the project had carried until then — that the band was a `PrintWindow` artefact of
`DwmExtendFrameIntoClientArea`. The band was really on screen.

### The finding that outlived its subject

A `Style` setter inside `Window.WindowDecorationsTheme` **cannot** beat a value the template applies as
a **binding on the part itself**. Tried and measured: a `^ /template/ Panel#PART_TitleBar` rule setting
`Background="Transparent"` left the part reporting `bg=White` and the band on screen. The selector was
not the problem — an `Opacity` setter added to the *same* rule as a probe reported back as `op=0.5` on
the same run, so the rule matched. Overriding the **resource key** the template names is the seam that
works.

That precedence fact is theme-independent and still true. It is the reason Round 7, Task 1 replaced the
whole `Template` rather than continuing to patch parts of Fluent's.

### Scope note, as it stood

Only `MainWindow` ever set `ExtendClientAreaToDecorationsHint` (grep: it appeared in no other `.axaml`),
so this was the only window in the app with drawn decorations to tint at all. The override was
deliberately variant-independent, because the requirement — that the strip show the window backdrop
through it exactly as the rail beside it does — held in Light and Dark alike.

---

## Round 4, item 5 / Round 6, item 4 — the two authored 8 DIP corners

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Tokens.axaml`, where they were the `RadiusPaneTopLeft`
and `RadiusPanel` tokens.
**Stopped being true of the code at** Round 7, Task 7, which deleted both tokens: Aero's content pane
has no rounded corner at all — the vendored `ControlStyles/ListBox.axaml` sets `CornerRadius="0"` — and
the 应用 page's category panel became a group box at the control radius.
**Instrument:** the user's reference capture `3.png` (a 1:1 native-resolution crop of Task Manager's DWM
frame bounds, so its pixels are physical and DIP = px / 1.5), plus `PrintWindow` (flag 2) captures of
this app scanned column-by-column for the row/column at which a border's outer pixel reaches its
straight-run value.
**Machine:** this repository's development machine, Windows 11 Pro 10.0.28000, primary monitor at 150 %.

### The content pane's one rounded corner — 8 DIP, top-left only

8 DIP came from `3.png`. Re-measured during the implementation rather than taken on trust: the pane's
outer edges are at frame x = 74 and y = 77, and scanning for the row/column where the border's OUTER
edge reaches its steady position gives y = 87 and x = 84 — **10 physical px of arc in each axis**. The
Round 4 survey reported 12 px (y = 89 / x = 86) for the same corner; the gap is entirely where one calls
the boundary among the antialiased pixels, and neither reading is a whole-DIP figure on its own. 8 DIP
is the design value that reproduced it: rendered at 8 DIP, this app's own pane measured 10-11 px of
visible arc on a `PrintWindow` capture — the same 10 px as the reference under the identical scan. A
stroked rounded rectangle shows radius minus about half the stroke, which is why a 12 px radius reads as
a ~10-11 px arc rather than 12.

Only the top-left corner was rounded, and that was measured too rather than assumed: in `3.png` the
top-right block (x 1898..1923, y 72..96) and the bottom-left block (x 68..92, y 1585..1608) contain no
arc — the top border and the pane fill run straight to the frame edge in both, so the other three
corners were 0 rather than "not looked at".

### The category panel's corner — the four-criterion comparison

Windows 11 Settings draws two DIFFERENT container shapes, and the Round 6 recon measured both on
`ms-settings:about` at 150 %. Its ROW cards show a 7 px arc (⇒ 4 DIP, which is what `RadiusControl` gave
`Border.tm-card`), and its LARGE side-by-side tiles (存储 / 显卡) show a 10 px arc with an 18 px gutter
(⇒ 8 DIP at a 12 DIP gap). A category panel was taken to be the second kind of object.

What the shipped panel rendered was recorded as instrument-dependent rather than as a single number.
Scanning `PrintWindow` captures for the row and the column at which the border's outer pixel reaches its
straight-run value: 12 px in each axis in Dark, and in Light 12 px on an exact match or 11 px if a
1-level tolerance is allowed. So the panel did NOT sit inside a 10-11 px band in both variants. What
survived the instrument was the comparative reading, taken in the SAME captures: `Border.tm-page-pane`,
the app's other authored-8-DIP corner, landed **within 1 px of the panel in both variants on every
criterion tried**. Under the ±1-level "steady-value" criterion the measuring script implemented, the
panel read 11 Light / 12 Dark against the pane's 12 / 12. Under a STRICT exact match the panel read 12
in both variants, and two runs of that same scan, on captures taken a session apart, put the pane at 12
and at 13 — antialiasing ambiguity, not a disagreement about the shape. The four-criterion table is in
`../specs/2026-08-04-round6-surfaces-design.md`, section 4.6 step 7.

### The finding that outlives its subject

Both blocks say the same thing about the instrument, and it is theme-independent: **an arc measured off
antialiased pixels is a function of where you call the boundary, and a digit quoted from such a scan is
meaningless without naming the criterion.** Round 7, Task 7's own corner check is a use of that lesson
rather than a repeat of the measurement — with the radius at 0 it scans for a CONSTANT first-ink column
down the first twelve rows, which no criterion can move.

---

## Round 6, item 2 — the Mica straddle, and the card-on-Mica separation it protected

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml`, where it was a 147-line argument
attached to `Border.tm-page-pane`.
**Stopped being true of the code at** Round 7, Task 1 for its two premises (the window asks for
`AcrylicBlur`, not Mica, and the search field it cited moved onto the white 应用 pane) and at Round 7,
Task 7 for its subject (there is no card fill any more, and `SurfaceLayerBrush`,
`SubtleFillSecondaryBrush` and `SubtleFillTertiaryBrush` are all deleted).
**Instrument:** `PrintWindow` (flag 2) pixel reads, plus arithmetic over the token alphas where a
photograph was impossible.
**Machine:** this repository's development machine, Windows 11 Pro 10.0.28000, primary monitor at 150 %.

### The thesis

Round 4 had argued that a translucent settings card (`SurfaceLayerBrush`, white at alpha 179)
composites to (255,255,255) over a WHITE page — Δ0, a card the literal colour of the page behind it —
and therefore that the 设置 pane could not be transparent. Round 6, item 2 overruled it on new
measurements: **Mica under this window measured 238..247 in Light and 30..34 in Dark**, i.e. it
STRADDLED `SurfaceBaseBrush` (243 / 32) and came nowhere near `SurfacePageBrush` (255 / 44). The card's
own separation therefore survived a transparent pane.

### The table, with its provenance labelled

Card minus backdrop, both terms taken at the same y. Four rows were photographs and two were arithmetic,
because before the change the 设置 pane was opaque and no settings card on Mica existed to photograph:

| | fill − backdrop | border − backdrop |
|---|---|---|
| Light, over `SurfaceBaseBrush` (was) — MEASURED | (8,8,8) | (−14,−14,−14) |
| Light, over Mica, window left edge — computed | (12,10,0) | (−14,−14,−15) |
| Light, over Mica, window right edge — computed | (6,11,7) | (−14,−14,−14) |
| Dark, over `SurfaceBaseBrush` (was) — MEASURED | (8,8,8) | (16,16,16) |
| Light, after the change — MEASURED | (12,9,4) | (−13,−14,−15) |
| Dark, after the change — MEASURED | (9,8,6) | (16,16,15) |

The two computed rows were later supplied with photographs after all, from a control that used the same
three tokens and already sat on real Mica — the search field in the window's title strip. At rest:
Light backdrop (244,240,248) gave fill (252,251,253) and border (230,226,233); Dark backdrop (33,31,35)
gave fill (41,40,42) and border (49,47,51). Predicted from the token alphas: (252,251,253)/(230,226,233)
and (41,39,42)/(49,47,50) — within 1 level on every channel. As card-minus-backdrop that is Light fill
(8,11,5) / border (−14,−14,−15) and Dark fill (8,9,7) / border (16,16,16), inside the same envelope as
the computed pair.

### The two seams the change closed

On the pre-change build the vertical rail→pane step at image x 82 → 83 in Light measured (+5,+2,−12) at
y = 150, (+5,+1,−12) at y = 400 and (+4,+1,−10) at y = 700 — small per channel, but a straight 780 px
line between a neutral grey and a tinted gradient — and the horizontal strip→pane step at y 71 → 72
measured (−2,+4,−4) at x = 1500. In Dark the same steps were (+2,0,−6)..(+2,0,−7) and (−2,+1,−2). After
the change both steps measured (0,0,0) at every one of those points, in both variants.

### The findings that outlive their subject

1. **Absolute RGBs on a backdrop surface are not portable.** `PrintWindow` composites the DWM backdrop
   itself, so a Mica pane photographs as the blurred wallpaper behind that window's own screen position.
   Only relationships travel. Round 7, Task 7 hit the acrylic version of the same wall from the other
   side: a fully transparent client photographs as (0,0,0), which is why that task made the client
   opaque rather than trying to measure ink over a blur.
2. **Windows' own Settings has no content pane at all.** Measured on it during the Round 6
   reconnaissance: at image y = 470 its capture read exactly (243,242,249) at x = 20, 100, 200, 300,
   400, 450, 480, 490 and 499 — nine samples, one value, straight through where a pane edge would be.
   That is what the `:not(.app)` rule was written against, and the rule itself is unchanged.
3. **The backdrop-refused branch was never exercised**, and no claim that "the fallback works" may be
   read into any of this. It was attempted in full while item 2 shipped — `EnableTransparency` cleared
   in the registry, the window hidden and re-summoned so `Opened` and therefore `ApplyBackdrop` genuinely
   re-ran, then an `ImmersiveColorSet` broadcast and a cold restart — and the machine kept granting Mica
   through all three.

---

## Round 6, item 4 — the category panel's fill, chosen by measurement

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml`, where it justified
`Border.tm-group-panel`'s `SubtleFillTertiaryBrush`.
**Stopped being true of the code at** Round 7, Task 7, which deleted all three candidate tokens and made
the panel an unfilled Aero group box.
**Instrument:** `PrintWindow` (flag 2) pixel reads on the white 应用 pane, plus arithmetic over the token
alphas for the Dark half.

Three candidates were measured or composited against `SurfacePageBrush` (255,255,255):

| candidate | renders | verdict |
|---|---|---|
| `SurfaceLayerBrush` | (255,255,255) — MEASURED | invisible: the pane and a tile's interior both read pure white |
| `SubtleFillSecondaryBrush` | (245,245,245) | exactly the colour a tile turned on hover — a collision, not a surface |
| `SubtleFillTertiaryBrush` | (249,249,249) | Δ6 from the pane, Δ4 from hover, coinciding only with a tile's transient pressed wash |

`SurfaceBaseBrush` was ruled out on sight: (243,243,243) opaque was the grey pane Round 4, item 5 had
just removed from that page.

The panel also repaired something the recon measured on the way past. A tile had NO fill of its own —
pane and tile interior both (255,255,255), Δ0; it was an outline, not a surface. Over a 249 panel the
same tile composited to (253,253,253), a real Δ4, while hover kept its strength (245 over 255 before,
243 over 253 after — Δ10 either way). The two hairlines differed too, which is the "not two identical
nested outlines" property: `StrokeControlBrush` over the pane's 255 gave the panel's own (240,240,240),
over the panel's 249 it gave the tile's (234,234,234). Dark was computed from the same alphas and
separated more, not less: pane (44,44,44), panel fill (53,53,53), panel border (59,59,59), tile fill
(55,55,55), tile border (67,67,67).

**The finding that outlives its subject:** the whole exercise was necessary only because every surface
in that palette was an ALPHA over another surface, so no two of them could be compared without knowing
what was behind both. Aero's fills are opaque gradients, which is why Round 7, Task 7 replaced the
measurement with an object (a group box) rather than with another candidate.

---

## Round 5, item 3 — the line-height pinning, and what it cost in card height

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml`, where it justified `LineHeight`
setters on `TextBlock.tm-card-title` and `TextBlock.tm-card-sub`.
**Stopped being true of the code at** Round 7, Task 7, which deleted `LineHeightBody`,
`LineHeightCaption` and `LineHeightSubtitle` outright — Windows 11's ramp publishes a line height beside
every size and Aero's does not.
**Instrument:** UI Automation `BoundingRectangle` on the live 设置 page at 150 %, each card scrolled
fully into view first, read in physical px.

Unpinned, the live page measured a title line box of **19.33 DIP** against the token's 20, a subtitle box
of **16.67** against 16, and **0.67 DIP of drift WITHIN the subtitle class** — the two pure-ASCII path
lines measured 16.0 while every subtitle containing CJK measured 16.67. Pinning both put a
title-over-subtitle header block at exactly 20 + 16 = 36 DIP.

The cost, over the sixteen cards the page had at the time: twelve did not change, three lost 1-2 px and
one gained 1. Two effects cancel on a conforming row — the title gains 0.67 and a CJK subtitle loses
0.67 — and a row whose column 2 held a 32 DIP button or ToggleSwitch absorbed the title's gain outright.
The three that lost: `PinTaskbarStatusText`'s card 125 → 124, `HotkeyHelpText`'s 126 → 125, and
`DataFolderTitle`'s 219 → 217. The one that gained: `UpdatesCard`'s status card, 132 → 133 — a bare
title, no subtitle to cancel it, and an empty column 2.

**The finding that outlives its subject:** the invariant the pinning protected — one
`VerticalAlignment="Center"` putting every row's icon and trailing control on the same y — never
depended on the block measuring 36. It depends on every header being the same TWO LINES, which is
`SettingsRowTests.EveryCardHeader_IsOneTitleLineAndOneSubtitleLine`'s job and which no card violates.
That is why deleting the tokens changed no layout invariant.

---

## Round 3 Task 2 / Round 4 item 1 / Round 6 item 3 — the icon rail, matched to Task Manager's own

**Deleted from** `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml`, where 55 lines of preamble justified
`TmNavRowItem`'s selected-state accent pill and 9 more explained why no rule in the file made the rows
icon-only, and from `src/TanMenu.Wpf/MainWindow.axaml`, where 27 lines derived the 40 DIP item column
and each glyph's local `Width="16"`.
**Stopped being true of the code at** Round 7, Task 8, which replaced the 48 DIP icon rail with a
160 DIP wide labelled Windows 7 navigation pane: the pane width, the row box, the row pitch, the item
column and the accent indicator all stopped existing in the same commit, and `TmNavRowItem` became the
vendored Aero `ListBoxItem` theme with four setters on top.
**Instrument:** live UI Automation `BoundingRectangle` on BOTH applications at once — this app and
Windows 11's own Task Manager, with the two windows arranged so their rails occupied the same image
columns — plus `PrintWindow` (flag 2) pixel reads on the same pair of captures.
**Machine:** this repository's development machine, Windows 11 Pro 10.0.28000, primary monitor at 150 %.

### The pane and the item column

Task Manager's `PaneRoot` measured **72 physical px = 48 DIP**, which is the width this app had always
opened at, so the rail's own 48 was carried over unchanged rather than chosen. Inside it, TM's item
column measured **40 DIP** where this app's measured **32** — the difference was this app's own
`ListBox` horizontal `Padding`, which stacked a second 4 DIP inset on top of the `ListBoxItem`'s
`Margin="4,2"` and ate 16 DIP of the 48 (48 − 16 = 32) instead of the 8 the item margin alone accounts
for (48 − 8 = 40). Both rails were then set to a zero horizontal `Padding` locally, which fixed the item
column at 40 DIP with no other change.

### The icon box, and the finding that produced the local `Width="16"`

TM's rail glyphs measured a FIXED, IDENTICAL 16 DIP box at the same x on every row — seven different
glyphs (folder, chart, clock, wrench, people, list, puzzle) all reported **x = 5322** — which only
happens if the glyph sits in a fixed-size container rather than being centred on its own advance. This
app's did not: before the fix, 应用's grid glyph and 设置's gear glyph centred **4 physical px apart**
from each other purely from Segoe Fluent Icons' per-glyph advance differences, even though both rows'
content was correctly centred. A local `Width="16"` on each glyph fixed it. That local width SURVIVES
into the labelled pane and is the one thing in this section that does; its citation changed from TM's
measured icon box to `vis-layout`'s published "A control size of 16x16 relative pixels is a good minimum
size for any input device", because a labelled row's icon column is not TM's icon box.

### The selected row, and the pill

The pre-Round-6 selected look was `BorderThickness="3,0,0,0"` on the stock template's
`ContentPresenter`, and a border always runs the full length of its edge AND always consumes layout. It
therefore painted the accent down the whole 36 DIP row box and pushed the row's own glyph sideways.
Measured against TM:

- TM's accent is **24 px tall = 16 DIP**, centred on the row box rather than spanning the full row.
- TM's accent is a **pill**: 5 px wide through the middle, narrowing to 3 px on its first and last
  scanlines — a cap radius near half its width, about 1.67 DIP.
- **TM's glyph does not move when the row is selected.** Driving `SelectionItemPattern.Select()` on its
  性能 row and re-capturing, the 进程 glyph's ink sat at the same x in both states. This app's moved its
  icon `TextBlock` 2 px (1.33 DIP) right on selection — the border's layout cost, reported by UI
  Automation on the glyph itself.

Round 6 item 3 replaced the border with an ELEMENT: a 3x16 DIP `Border` at `CornerRadius="1.5"`, laid
over the content in a `Panel`, hit-test invisible, transparent until `:selected` painted it. The move
from a `BorderThickness` to an element shifted the render by a pixel — 4 px wide before, 5 px after, the
same 5 px TM draws — from an authored number that never moved, because 3 DIP at 150 % is 4.5 and layout
rounding covers 4 and 5 alike. The width was deliberately NOT "corrected" to 3.33 to chase TM's 5 px:
that would have been wrong at every other scale.

Everything the survey found IDENTICAL was left alone and is recorded here for the same reason: the
48 DIP pane, the 40x36 DIP row box, the 40 DIP row pitch, the 4 DIP corner arc, the empty unselected
row, and the accent hue — the two apps' indicators sampled the same triple in each variant, so
`AccentDefaultBrush`, which `Services/AppTheme.cs` republishes from Windows personalization, was already
right.

### The state fills, and the alpha solve

TM's selected row solved to **alpha 9.4/255 black in Light** and **14.5/255 white in Dark**, against
which this app's `SubtleFillSecondaryBrush` was exactly 10/255 and `SubtleFillTertiaryBrush` exactly
15/255 — so the state fills were a LOOKUP, not a colour choice. Round 7, Task 7 deleted both tokens
(Aero paints a list row's states as opaque two-stop gradients, not alpha washes) and the figures ceased
to describe anything.

### The rail's glyph contrast

Measured on both apps, both variants, and identical in the selected and unselected state in each: TM's
rail glyphs solve to **alpha 0.893 black over their row in Light** and reach **pure white in Dark** —
which is this app's `TextPrimaryBrush` to the level in both. This app's were `TextSecondaryBrush`, alpha
**0.605** in Light and **(208,209,210)** in Dark. `Grid.tm-nav-rail TextBlock.tm-icon` was written to
close that gap and is the one rule of this whole survey that SURVIVES into the labelled pane — re-derived
rather than re-measured, because under a labelled row the reason a glyph is at full ink is that the
label beside it is, not that Task Manager's was.

### The footer's geometry

TM's rail footer holds exactly ONE entry, its Settings gear, pinned to the very bottom of the pane, and
its `FooterMenuItemsHost` fills a `"*"`-sized **1390 physical px** even though its 7 items occupy only
414 of them — the same "leftover space pushes the footer down" mechanism this app's own `RowDefinitions`
uses, and the reason that Grid needs no explicit height. The **~4 DIP** gap measured between TM's
`FooterMenuItemsHost` and the window edge is where this app's footer `StackPanel` `Margin="0,0,0,4"`
comes from; that margin survives Task 8 unchanged, and it is the only geometric number in this section
that does.

**The finding that outlives its subject:** every number above was chosen by matching a Windows 11
application, and the reference itself is what Round 7 retired. The two things that survived — the local
16 DIP glyph width and the full-ink rail glyph — survived because each had a second, non-comparative
justification available; the pill, the pane width, the row box, the pitch and the alpha solve had only
the comparison, and went with it.
