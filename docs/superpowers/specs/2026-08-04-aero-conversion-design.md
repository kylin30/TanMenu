# TanMenu — the Aero conversion, design

> **Status: DESIGN.** No source file was edited, no package added, no build run and nothing committed
> while this was written. Tree at `e6d031b` with the feasibility document untracked. This document is
> the plan; `docs/superpowers/specs/2026-08-04-aero-feasibility.md` is the decision record it executes
> against, and it is Option B of that document's §4, extended by the six decisions the user settled
> afterwards.

## 0. How to read this

**Every number in this document is one of four kinds, and each says which.**

| tag | meaning |
|---|---|
| **MEASURED** | read off a real capture or a live process, with the coordinates or the API named |
| **PUBLISHED** | quoted from a Microsoft Learn page, with the URL |
| **VENDORED** | a value inside `aero-theme-for-avalonia` at upstream `main` = `a705b7c0`, with the file and key |
| **AUTHORED** | chosen by this design because no source states it. Always in capitals, always with the reasoning |

The Windows 7 navigation pane's geometry is **entirely AUTHORED** and says so in every place it
appears. Microsoft never published it: the Windows 7 UX Guide's table of contents
(`https://learn.microsoft.com/en-us/windows/win32/uxguide/toc.json`, 73 slugs) has no Explorer article
and no navigation-pane article, and the glossary has no "navigation pane" entry. There is no Windows 7
machine and no Aero reference capture anywhere in this tree. Six rounds' numbers were measured; this
round's pane numbers cannot be, and pretending otherwise would be the exact failure this project's
comment rule exists to prevent.

**Two corrections to the feasibility document, established since it was written, that change the plan.**

1. §3.2 says "of 46 keys, 6 survive unchanged" — the `Space1..Space5` ramp and `SurfacePadding`.
   **That is wrong.** `vis-layout` publishes Aero's own spacing ladder (11 / 7 / 5 relative pixels)
   and it is not 4 / 8 / 12 / 16 / 20. **Of 46 keys, zero survive unchanged.** §3 below is key by key.
2. §3.2 says "~14 style rules target Avalonia Fluent's own template part names". The correct live
   census is **24** `/template/` rules, of which **14 name parts we author ourselves and are immune**,
   **5** name Fluent's `TextBox` part and break, and **5** name Avalonia 12's `WindowDrawnDecorations`
   parts and are deleted with the caption. §5 enumerates all 24 with file:line.

---

## 1. The settled decisions, and what each one forecloses

These six are the user's, are not re-opened, and the design executes them literally.

1. **A hand-authored Aero window frame, through `Window.WindowDecorationsTheme`.** The Windows 11 title
   bar goes. This is Task 1 and it is the one item that could still prove impossible; §7.1 states what
   "impossible" looks like and what the fallback is.
2. **The search box returns to the 应用 page's content area.** It cannot survive Task 1 in place —
   Task 1 deletes the client-side title strip that hosts it — so the two land in the same commit.
   Microsoft rules on it directly (`win-window-frames`, verbatim): *"Don't add controls to a window
   frame. Put the controls within the window instead."* **PUBLISHED.**
3. **The navigation rail becomes a Windows 7 wide labelled pane.** One width, not two. This is a revert
   of the first two of Round 4 item 1's five deletions (the label `TextBlock`s and the `StackPanel`s
   that paired them with icons) and **not** of the other three (the 200 DIP second width, the
   `DoubleTransition`, the `ClipToBounds`). Keeping one width is what keeps `RailWidthDip` a constant
   rather than a config read, and it keeps the sixth re-fit trigger deleted.
4. **Dark mode is kept**, as a documented anachronism. Windows 7 shipped High Contrast, an
   accessibility mode, not a design variant; the library's Dark palette is its author's invention. It
   costs nothing mechanically — `Variant/Light.axaml` and `Variant/Dark.axaml` hold **168 `<Color>`
   keys each and their key sets are identical, zero diff in either direction** (VENDORED, verified by
   `comm` on the two sorted key lists) — so `EveryThemedTokenIsDefinedInBothVariants` is satisfied by
   the vendored files on day one.
5. **Glass is approximated with Acrylic, stated as a substitution.** Note one thing the feasibility
   document got right for the wrong reason: its §2.1 blocker (*"Beginning with Windows 8, calling this
   function doesn't result in the blur effect"*, on `DwmEnableBlurBehindWindow`) **does not apply to
   Avalonia's `AcrylicBlur`**, which is a WinUI composition gaussian blur (σ = 30) over
   `ICompositor3::CreateHostBackdropBrush()` and never calls that API. The substitution is still a
   substitution — Aero glass had a specular sweep, a colour tint bar, a bright inner reflection and a
   1 px light rim, and Avalonia's acrylic is a plain blur with no tint layer at all — but it is a live
   blur of what is behind the window, not a flat fill. The missing tint is ours to draw, and §7.1
   draws it.
6. **Control visuals come from copying individual MIT files, never a package dependency.**
   `Window.axaml` is dropped. `ComboBox`/`ComboBoxItem` are authored by us.

**Two things the six decisions did not settle and this design settles anyway**, both recorded with
their basis rather than left to drift:

- **The settings page keeps immediate-apply cards.** Round 5's row spec is information architecture,
  not paint, and it is the most survivable thing in `Launcher.axaml`. What changes is the container:
  the per-row card fill and hairline die, and the five groups (外观 / 行为 / 文件夹 / 常用工具 / 更新)
  become Aero group boxes, which is what a Windows 7 property page drew around a labelled group. This
  adds `ControlStyles/HeaderedContentControl.axaml` to the port set (**§7.9** — Task 4's decision
  vendors only `TextBox`, `NumericUpDown` and `ButtonSpinner`; the group-box file lands with the
  category panel in Task 9, which is where the rest of this paragraph's decision is executed).
- **The accent keeps being published, and gains exactly one new job: the caption glass tint.** Aero's
  user colour was `DwmGetColorizationColor` — a single *frame* colour. That is precisely the role the
  caption tint needs, and it is the only role Aero ever had for a user colour. So
  `Services/AppTheme.cs` is untouched, all 11 `AppThemeTests` stay meaningful, and the accent goes
  from six flat-fill consumers to two: the caption glass and the splash gradient.

---

## 2. Cross-cutting contracts

Everything in §7 is bound by these. They are stated once here so no task restates them.

### 2.1 The fit arithmetic, with the new terms

Four constants in three files mirror four XAML values. Nothing else in the fit changes.

| term | constant | file:line | mirrors | today | after |
|---|---|---|---|---|---|
| **R** rail pane width | `MainWindow.RailWidthDip` | `MainWindow.axaml.cs:371` | `MainWindow.axaml:327` `NavRailPane Width` | 48 | **160** |
| **B** pane LEFT border | `MainWindow.PaneBorderDip` | `MainWindow.axaml.cs:388` | `Launcher.axaml:872` left component | 1 | **1** (unchanged) |
| **T** caption height | `WindowHost.TitleStripHeightDip` | `WindowHost.cs:39` | `MainWindow.axaml:15` **and** `:194` row 0 | 48 | **30** |
| **Bt** pane TOP border | the literal `+ 1` | `WindowHost.cs:52` | `Launcher.axaml:872` top component | 1 | **1** (unchanged) |

```
chromeWidth  = R + B  = 161        MainWindow.axaml.cs:349
chromeHeight = T + Bt = 31         WindowHost.cs:52
W    = Clamp(Pw + chromeWidth,  Min(Wmin, work.Width),  work.Width)
H    = Clamp(Ph + chromeHeight, Min(Hmin, work.Height), work.Height)
Left = work.Left + (work.Width - W) / 2
Top  = Clamp(work.Bottom - gap - H, work.Top, work.Bottom - H)
```

`Wmin = MinWindowWidthDip` **760 → 872**. `Hmin = MinWindowHeightDip` **568, unchanged** — it is the
app's own height floor and has nothing to do with chrome. `gap = BottomGapDip` 8, unchanged.
`PlaceholderWidthDip` and `MainWindow.axaml:7 Width` **1000 → 1112**, so the cold-start first frame
does not visibly jump wider when the fit lands. `PlaceholderHeightDip` / `Height` stay 688.

**Why 872 and not 760.** At the floor the content pane gets `Wmin − chromeWidth`. Today that is
`760 − 49 = 711` DIP. With R = 160 and `Wmin` left at 760 it would be `760 − 161 = 599`, a 112 DIP
loss, and **four measurement records elsewhere in the tree would become false**:
`Launcher.axaml:1078-1079` and `:1140` (the settings overflow rule, re-measured at "GetWindowRect
1162 px = 760 DIP client, card 664.00 DIP, content box 629.33"), `FoldersCard.axaml:27-37` (what wraps
in Chinese at that width) and `MainWindow.axaml.cs:789`. Setting `Wmin = 760 + (160 − 48) = 872`
**holds the content pane's floor at exactly 711 DIP**, so those four records stay true in substance
and each needs only its premise line rewritten from "760 DIP window" to "872 DIP window, 711 DIP
content pane". That is a one-line edit per block instead of four re-measurements.

**The height will not move, and that is not a bug.** `Ph + chromeHeight` is below `Hmin`:
today `494 + 49 = 543 < 568`, 25 DIP of slack. After the conversion the caption is *shorter*
(31 vs 49) and the search header adds roughly 38 DIP to `Ph` (§7.9), giving `≈ 532 + 31 = 563`,
still below 568. **Any `chromeHeight ≤ 74` produces the identical 568 DIP window.** Nobody may verify
the caption height by reading the fit log line's `H`. Verify it by the `page {W}x{H}` term, by the
`chrome {cw}x{ch}` term (which *does* change, `49x49 → 161x31`), and by a PrintWindow scan of the
strip.

**The failure mode is silent and is closed by a new test.** All 23 `WindowPlacementTests` take the
chrome terms as parameters, so they keep passing through any chrome change. Nothing asserts
`chromeWidth == RailWidthDip + PaneBorderDip`, nor that those constants mirror the XAML. Task 1 adds
`tests/TanMenu.Core.Tests/Themes/ShellConstantTests.cs` (§2.6).

### 2.2 The re-fit trigger list — unchanged, five entries

1. every summon; 2. a docked notice opening or closing; 3. a 每列个数 / 字体 / 语言 change;
4. a switch back to 应用 from 设置; 5. the Maximized→Normal transition.

The rail's expand/collapse was a sixth until Round 4 item 1 deleted it, and **this round does not
bring it back**: decision 3 is one width, just not 48. The search header returning to the page adds no
trigger either — §7.9 deletes `ApplySearchFieldWidth`, its three constants and the
`ClientSizeProperty` subscription at `MainWindow.axaml.cs:106`, because the field's width stops
tracking the client. Net: one subscription and one method fewer, same five triggers.

### 2.3 The config contract — nothing on disk may break

**No JSON key is added, removed, renamed or retyped.** `general.appearance` keeps its three values
(decision 4), so `AppAppearance` (`AppAppearance.cs:15-19`, where `System` is both the default and
what a key-less older config deserializes to) is untouched and `ToVariant_MapsEverySetting` stays
green. `ConfigServiceTests` (23) and `JsonContractTests` (8) are not touched by any task in §7.

One behaviour change worth stating rather than discovering: a persisted window width below 872 comes
back **clamped up to 872** by `WindowHost.RestorePlacement`'s existing `MinWindowWidthDip` clamp. That
is the clamp doing its job, not a regression, and it is exactly what happened the last time a minimum
moved.

### 2.4 NativeAOT — the rules, and the bar

The bar is a convention, not a gate (`Directory.Build.props:11`: *"Findings are warnings, not
build-breaking"*), and it has held for six rounds. Rules for this round:

- **Zero `{ReflectionBinding}` in the tree at the end of every task.** The port's entire liability is
  two lines, `ControlStyles/ProgressBar.axaml:255-256`, and they are on `Border x:Name="ShineBorder"`
  binding `PART_Indicator` — the **determinate** path, so deleting the indeterminate machinery does
  not remove them. Both convert to `{Binding #PART_Indicator.Bounds.Width|Height, Mode=OneWay}`, the
  idiom the same file already uses four lines away.
- **Zero `DataTemplate` without `x:DataType`.** No task adds one.
- Converters are `x:Static` singletons or `x:Key`'d instances; no `Activator`, `typeof`, `Assembly`,
  `dynamic` or `MakeGeneric`. The four vendored converters already satisfy this.
- `AvaloniaUseCompiledBindingsByDefault=true` stays (`TanMenu.Wpf.csproj:45`), redundant on Avalonia 12
  and kept as documentation of intent.
- **Every task ends with `dotnet publish -c Release` and the IL2xxx/IL3xxx count recorded.** The count
  must not rise. `TrimmerSingleWarn=false` (`:43`) reports each one individually, so this is readable.

The one genuinely unvalidated item is whether the vendored files' 24 non-ProgressBar `{Binding}`
expressions compile under compiled-bindings-by-default. Nobody has built them. Task 2 and Task 3 are
sequenced so that this is discovered at the earliest possible moment, on the smallest possible diff.

### 2.5 Light and dark, for everything

Every task delivers both variants or it is not done. Concretely, in every task's verification:

- a PrintWindow capture in Light **and** in Dark, at this machine's 150 % scale;
- the same pixel scan run against both;
- `EveryThemedTokenIsDefinedInBothVariants` green.

The one place dark needs its own thought is the **caption glass**, because acrylic is a blur of an
arbitrary desktop and the tint has to keep the title legible over both. §7.1 gives the two gradients
and the verification that reads them.

### 2.6 The test delta, by name

Ground truth today: **141 `[Fact]`/`[Theory]` attributes** across `tests/`.

> **This ledger counts ATTRIBUTES; `dotnet test` reports CASES, and the two numbers are not the same.**
> A `[Theory]` expands to one case per `[InlineData]`, so the runner's total is always the larger.
> Measured with `git grep -E '^\s*\[(Fact|Theory)' <rev> -- tests/`: `e6d031b` = 141 (this census),
> `33a758f` = 145 (Task 1's four), `eb46249` = 147 (Task 2's two), `6e60d03` = 147 — the projected
> final count, met exactly. At `6e60d03` those 147 attributes are the **194** cases the runner
> reports. Reconcile a task's result against this table, not against the runner's headline number;
> Round 7 Task 3's report mistook the 147/194 gap for a stale census and dismissed the ledger, which
> is the one place a silently dropped test would show up.

**Survive untouched (137).** `ConfigServiceTests` 23, `MenuModelsTests` 16, `MenuLayoutTests` 10,
`JsonContractTests` 8, `AppLanguageTests` 6, `ShellLinkTests` 6, `SoundServiceTests` 5,
`Win32IconExtractorTests` 5, `AppConfigTests` 4, `LaunchServiceTests` 4, `PortableRuntimeTests` 4,
`MenuDataServiceTests` 3, `MenuServiceTests` 3, `FontUtilTests` 2, `ShortcutResolverTests` 2,
`AppDataPathsTests` 1, all 23 `WindowPlacementTests`, all 7 `AppThemeTests`,
`Fluent2TokenTests.LauncherStyles_ContainNoColourLiterals`,
`Fluent2TokenTests.EveryThemedTokenIsDefinedInBothVariants`,
`SettingsRowTests.EveryCardBorder_WrapsTheStandardColumnGrid`,
`SettingsRowTests.EveryCardHeader_IsOneTitleLineAndOneSubtitleLine`,
`SettingsRowTests.NoHeaderSubtitleString_ContainsAHardLineBreak`,
`AppPageGroupTests.GroupPanel_CarriesTheVisibilityBinding`.

Two of those deserve a note because a careless plan would have broken them:

- `EveryThemedTokenIsDefinedInBothVariants` hardcodes `x:Key="Light"`. **We keep the `"Light"` /
  `"Dark"` convention and do not adopt the library's `"Default"`.** The vendored `Variant/*.axaml`
  files are merged *by us*, inside a wrapper we write, so the outer key names are ours to choose. Zero
  test change, and no red build with a parser message instead of a parity message.
- `GroupPanel_CarriesTheVisibilityBinding` asserts `panels.Count == 1` for
  `Border Classes="tm-group-panel"`. **The class name `tm-group-panel` is kept.** The look changes
  completely; the class does not. The test's underlying finding — a filtered-out category's
  `ItemsControl` container stays visible at zero size, so a `Border` outside the binding paints an
  empty 22 DIP box — is theme-independent and measured, and it is the only guard against a bug nobody
  sees until they type in the search box.

**Re-targeted (4), each in the task that falsifies it.**

| test | change | task |
|---|---|---|
| `Fluent2TokenTests.EveryTokenTheLauncherReferences_IsDefinedInTokens` | union the `defined` set with `Themes/Aero/Variant/Light.axaml`, `Variant/Dark.axaml`, `AVMAP/Brushes.axaml`, `AVMAP/Metrics.axaml`. **Widen the defined set, never the `"Tm"` prefix filter** — a prefix filter would let a typo'd `AeroFoo` through, unioning the real files still catches it | 2 |
| `Fluent2TokenTests.EveryTokenTheLauncherWindowReferences_IsDefinedInTokens` | same | 2 |
| `SettingsRowTests.NoCardFile_RestatesAConstantTheThemeOwns` | re-point its constant list from Fluent 2's to Aero's | 7 |
| `AppThemeTests` (no code change) | its five `ResolveAccent_*` facts stay meaningful because the accent keeps a consumer (§1) | — |

**New (6), all source tests of the `Fluent2TokenTests` kind, all in
`tests/TanMenu.Core.Tests/Themes/ShellConstantTests.cs` unless noted.** Final count **147**.

| test | what it closes | task |
|---|---|---|
| `CaptionHeight_IsTheSameNumberInAllThreePlaces` | `MainWindow.axaml:15` hint, `MainWindow.axaml:194` row 0, `WindowHost.cs` `TitleStripHeightDip` — three unlinked copies today | 1 |
| `RailWidth_MatchesTheRailPaneWidth` | `MainWindow.axaml` `NavRailPane Width` vs `MainWindow.axaml.cs` `RailWidthDip` | 1 |
| `MinWindowWidth_IsTheSameNumberInBothPlaces` | `MainWindow.axaml:7 MinWidth` vs `WindowHost.cs` `MinWindowWidthDip` | 1 |
| `NoTemplatePartSelector_NamesAPartOutsideTheAllowList` | **the dead-selector class of bug, permanently.** Scans every `.axaml` under `src/` for `/template/ <Type>#<Name>`; each `<Name>` must appear as `x:Name=` or `Name=` inside some `ControlTemplate` in the tree, or be on a short allow-list of Avalonia-owned names we deliberately target. An Avalonia selector that matches nothing does not error; this is the only thing that will ever catch it | 1 |
| `EveryVendoredAeroFile_CarriesTheUpstreamCopyrightHeader` (`Themes/AeroAttributionTests.cs`) | MIT's notice requirement, mechanically | 2 |
| `NoVendoredFile_ReferencesTheInterFontFamily` (same file) | the CJK regression `AVMAP/Metrics.axaml:13` would cause | 2 |

### 2.7 The verification harness

Every step below is provable with **no keyboard and no mouse**. Four instruments, all already used by
previous rounds:

- **`dotnet build` / `dotnet test` / `dotnet publish`** output.
- **UI Automation `BoundingRectangle`** on named elements. The three caption buttons get
  `AutomationProperties.AutomationId` = `Close` / `Minimize` / `Maximize` **for free** —
  `WindowDrawnDecorations.AttachCaptionButtons()` sets them on whatever it finds under the `PART_*`
  names, and it also sets `AutomationId="AvaloniaTitleBar"`, `Name="TitleBar"` and
  `IsControlElementOverride=true` on `PART_TitleBar`.
- **`PrintWindow` captures** with flag 2, sampled with `System.Drawing` pixel reads — the instrument
  that produced every colour number in the tree, including the Mica `(241,241,249)` reading at
  `Tokens.axaml:274-279`.
- **`SendMessage(hwnd, WM_NCHITTEST, 0, MAKELPARAM(x, y))`** — returns the hit-test code for any
  screen point with no input at all. This is the rigorous, mouse-free proof that the Aero caption is
  really non-client, and it is new to this round.
- **Serilog lines.** Task 1 adds one:
  `Backdrop: requested {Requested}, granted {Granted}` in `ApplyBackdrop`, so the acrylic grant is a
  log fact forever instead of an in-process read nobody can take.

---

## 3. The token strategy

### 3.1 Which layer is the source of truth — and why the obvious answers both fail

**Neither "re-derive our tokens from Aero values" nor "our tokens become a shim over theirs" works as
stated. The seam is `AVMAP/Brushes.axaml`, not `Tokens.axaml`.**

- Our tokens are **flat `SolidColorBrush`es**. Aero's fills are **vertical two-stop
  `LinearGradientBrush`es** built from paired `…Color0`/`…Color1` (up to `…Color3` for buttons). A
  token layer of solid brushes has no gradient to give. Re-deriving our 21 themed keys to Aero colours
  produces a flat-Aero-coloured Fluent app, not an Aero app.
- Their 168 variant keys are **raw `<Color>`, not `Brush`**. Every `.tm-*` rule in `Launcher.axaml`
  sets `Background` / `BorderBrush` / `Foreground`, all brush-typed. A shim would have to *be*
  `Brushes.axaml`, and that file already exists and is theirs.

**The shape that works, and it is the smallest of the three:**

```
Themes/Aero/Variant/Light.axaml   168 <Color>, variant-specific     ← VENDORED verbatim
Themes/Aero/Variant/Dark.axaml    168 <Color>, identical key set    ← VENDORED verbatim
Themes/Aero/AVMAP/Brushes.axaml   110 brushes, VARIANT-INDEPENDENT, merged into BOTH  ← VENDORED verbatim
Themes/Aero/AVMAP/Metrics.axaml   8 keys (2 dropped, §7.2)          ← VENDORED minus 2 lines
Themes/Fluent2/Tokens.axaml       our semantic names, as StaticResource ALIASES onto the above,
                                  plus the things Aero has no opinion about
```

That is the library's own designed position for each file, it is structurally the same shape
`Tokens.axaml` already has (one Light block, one Dark block, identical key sets), and it means
`Launcher.axaml` and the 147 tests do not have to be rewritten in one commit — **our names survive,
the values come from Aero.**

`<StaticResource x:Key="X" ResourceKey="Y" />` as an alias is not invented here: `AVMAP/Metrics.axaml`
itself uses exactly that form for `ControlContentThemeFontSize` (VENDORED). Because Aero's brushes are
variant-independent, the aliases sit **outside** the `ThemeDictionaries`, and the two dictionaries keep
only the handful of keys that genuinely need a per-variant value Aero does not supply.

**The file name `Themes/Fluent2/` becomes a lie and is renamed to `Themes/Shell/` in Task 10** — after
every other task has landed, because renaming it earlier means re-pointing `Fluent2TokenTests`'
hardcoded `ThemesDir` in the middle of the round for no benefit. `Themes/Aero/` (the vendored layer)
and `Themes/Shell/` (ours) is the end state.

### 3.2 Key by key — all 46

**Themed (21 keys × 2 variants).** `A→` means a `<StaticResource>` alias onto a vendored Aero brush.

| # | key | fate | value / basis |
|---|---|---|---|
| 1 | `SurfaceBaseBrush` | **A→ `AeroWindowBackground`** | `#FCFCFC` / `#373737` VENDORED. Its only consumer becomes `ApplyBackdrop`'s opaque fallback |
| 2 | `SurfacePageBrush` | **A→ `AeroListBoxBackground`** | `#FFFFFF` / `#4A4A4A` VENDORED. Consumer: `Border.tm-page-pane.app`. Explorer's own content area is the list background, so the 应用/其他 pane distinction survives with an Aero justification instead of a Windows 11 Settings one |
| 3 | `SurfaceLayerBrush` | **DELETE** | the settings card's fill. Cards lose their fill; the five groups become Aero group boxes (§7.7) |
| 4 | `SurfaceLayerActiveBrush` | **DELETE** | "focused fill lightens slightly" is Fluent's idea. Aero's TextBox focus is a `BorderBrush` change to `AeroTextBoxFocusBorderBrush` |
| 5 | `SubtleFillSecondaryBrush` | **DELETE** | the rail's selected fill. Replaced by the Aero `ListBoxItem`'s four-state gradients |
| 6 | `SubtleFillTertiaryBrush` | **DELETE** | the category panel fill and the update-progress track. Replaced by the Aero group box and the Aero `ProgressBar` |
| 7 | `TextPrimaryBrush` | **A→ `AeroForeground`** | `#FF000000` / `#FFFFFFFF` VENDORED |
| 8 | `TextSecondaryBrush` | **DELETE** | replaced by a mechanism, not a colour: a subtitle is `AeroForeground` at `Opacity="0.6"`. Windows 7 published no secondary-body colour, and inventing a third grey beside `AeroForeground` and `AeroDisabledForeground` is worse than one opacity that follows the variant for free |
| 9 | `TextTertiaryBrush` | **DELETE** | same mechanism |
| 10 | `TextDisabledBrush` | **A→ `AeroDisabledForeground`** | `#FF7F7F7F` in **both** variants VENDORED |
| 11 | `StrokeControlBrush` | **A→ `AeroGroupBoxBorderBrush`** | `#D5DFE5` / `#1B252B` VENDORED |
| 12 | `StrokeSurfaceFlyoutBrush` | **DELETE** | its one consumer is `Border.tm-toast`'s border, which takes `AeroGroupBoxBorderBrush` |
| 13 | `StrokeDividerBrush` | **A→ `AeroGroupBoxBorderBrush`** | a divider and a group-box outline are the same hairline in a Windows 7 dialog |
| 14 | `FocusStrokeOuterBrush` | **A→ `AeroForeground`** | the focus visual becomes the classic single dotted rectangle, §3.4 |
| 15 | `FocusStrokeInnerBrush` | **DELETE** | the two-ring focus rectangle is Windows 11's |
| 16 | `AccentDefaultBrush` | **KEEP, re-purposed** | the caption glass tint stop. Aero's user colour was `DwmGetColorizationColor`, a frame colour — this is that role, restored |
| 17 | `AccentTextBrush` | **DELETE** | published by `AppTheme` and read by nothing in the tree today; `grep` finds it only in `Services/AppTheme.cs`. It stops being published |
| 18 | `TextOnAccentBrush` | **DELETE** | only consumer is `TmAccentButton`, which dies (§7.3) |
| 19 | `AccentColor` | **KEEP** | `SplashPanel.axaml:84` gradient stop + the caption tint's first stop |
| 20 | `AccentColorShaded` | **KEEP** | `SplashPanel.axaml:85` + the caption tint's last stop |
| 21 | `ShadowFlyout` | **DELETE** | `Border.tm-toast`'s `BoxShadow`. Aero drew no soft shadow inside the client area |

**New themed keys (5 × 2 variants), all AUTHORED, all justified in §7.1 and §7.7:**

| key | Light | Dark |
|---|---|---|
| `CaptionGlassActiveBrush` | 3-stop vertical gradient, white-alpha veil tinted by `AccentColor` | same structure, black-alpha |
| `CaptionGlassInactiveBrush` | same gradient at roughly half the tint's alpha | same |
| `CaptionBevelLightBrush` | `#80FFFFFF` | `#40FFFFFF` |
| `CaptionBevelDarkBrush` | A→ `AeroGroupBoxBorderBrush` | same |
| `HeadingBrush` | `#003399` **PUBLISHED** (`vis-fonts`: *"main instructions 12 pt blue #003399"*, *"group headers 11 pt #003399"*) | `#6699FF` **AUTHORED** — Windows 7 had no dark variant; this is `#003399` lifted to keep the same contrast ratio against `#373737` that the original has against `#FCFCFC` |

**Variant-independent (25 keys).**

| # | key | fate | value / basis |
|---|---|---|---|
| 22 | `RadiusControl` | **A→ `AeroControlCornerRadius`** | `3` VENDORED |
| 23 | `RadiusControlInner` | **RE-VALUE 3 → 2** | `RadiusControl − 1`. Kept as a second literal beside the first, for the reason its own comment already gives |
| 24 | `RadiusPaneTopLeft` | **DELETE** | Aero's content pane has no rounded corner; Aero's own `ListBox` sets `CornerRadius="0"` VENDORED. Its 30-line measurement block goes to `docs/` (§4) |
| 25 | `RadiusPanel` | **DELETE** | the category panel becomes an Aero group box at radius 3. Its 33-line block goes to `docs/` |
| 26 | `Space1` | **RE-VALUE 4 → 5** | **PUBLISHED** `vis-layout`: *"target regions … should either be touching or have at least 3 DLUs (5 relative pixels) of space between them"*, and *"label to control 5"* |
| 27 | `Space2` | **RE-VALUE 8 → 7** | **PUBLISHED** `vis-layout`: *"Between related controls — 4 / 7. Between buttons — 4 / 7"* |
| 28 | `Space3` | **RE-VALUE 12 → 11** | **PUBLISHED** `vis-layout`: *"Dialog box margins — 7 DLU / 11 relative pixels on all sides"*, *"Between unrelated controls — 7 / 11"*, *"Between paragraphs — 11"* |
| 29 | `Space4` | **DELETE** | 16 is unpublished. Consumers move to `Space3` or to a stated `2 × Space3 = 22` |
| 30 | `Space5` | **DELETE** | 20 is unpublished. Same |
| 31 | `SurfacePadding` | **RE-VALUE 16 → 11** | the dialog margin, same citation as `Space3` |
| 32 | `FontFamilySmall` | **DELETE** | Segoe UI *Variable* optical sizes are Windows 11. Also already dead: `grep` finds **zero** consumers today |
| 33 | `FontFamilyText` | **DELETE** | same |
| 34 | `FontFamilyDisplay` | **DELETE** | same. Also already dead |
| 35 | `AppFontFamily` | **RE-VALUE** | `Segoe UI, Microsoft YaHei UI`. **PUBLISHED** `vis-fonts`: *"Segoe UI 9 pt is the system font"* — plain Segoe UI, not Variable. Consumers `SplashPanel.axaml`, `Services/AppFonts.cs` are untouched |
| 36 | `FontSizeCaption` | **DELETE** | a Windows 7 property sheet uses one body size; hierarchy comes from colour and weight, not from a fourth size |
| 37 | `LineHeightCaption` | **DELETE** | Windows 11's ramp encodes line heights; GDI text does not. All three go |
| 38 | `FontSizeBody` | **RE-VALUE 14 → 12** | **PUBLISHED** 9 pt at 96 dpi = 12 DIP, and **VENDORED** `AeroFontSizeNormal = 12` agrees exactly |
| 39 | `LineHeightBody` | **DELETE** | natural line height |
| 40 | `FontSizeSubtitle` | **RE-VALUE 20 → 16** | **PUBLISHED** `vis-fonts` main instruction 12 pt = 16 DIP; **VENDORED** `AeroFontSizeLarge = 16` agrees |
| 41 | `LineHeightSubtitle` | **DELETE** | natural line height |
| 42 | `IconFontFamily` | **KEEP unchanged** | `Segoe Fluent Icons, Segoe MDL2 Assets`. A deliberate anachronism, §9 |
| 43 | `ControlCornerRadius` | **RE-VALUE 4 → 3** | A→ `AeroControlCornerRadius`. Still an override so any control we do not restyle matches |
| 44 | `OverlayCornerRadius` | **RE-VALUE 8 → 0** | Windows 7 popups and flyouts were square |
| 45 | `ContentControlThemeFontFamily` | **RE-VALUE** | `Segoe UI, Microsoft YaHei UI`. **This override is load-bearing and must survive**: `AVMAP/Metrics.axaml:13` sets the same key to `avares://AeroAvalonia/Fonts#Inter`, Inter has **no CJK coverage**, and this is a Chinese-facing app. We drop that line when vendoring (§7.2) *and* keep this override — belt and braces, plus a test (§2.6) |
| 46 | `TitleBarBackgroundBrush` | **DELETE** | it exists solely to tint **Fluent's** `PART_TitleBar`, which stops existing the moment Task 1 replaces the template. Its 32-line write-up goes to `docs/` |

**New variant-independent keys (1):** `FontSizeGroupHead` = **15**. **PUBLISHED** `vis-fonts`:
*"group headers 11 pt #003399"*; 11 pt at 96 dpi = 14.667 DIP, rounded up to the nearest whole DIP.

### 3.3 What this means numerically

Of 46 keys: **9 become aliases** onto vendored Aero brushes, **11 are re-valued in place**,
**23 are deleted**, **3 are kept unchanged** (`AccentColor`, `AccentColorShaded`, `IconFontFamily`).
**6 new keys** are added (5 themed + `FontSizeGroupHead`). End state: **29 keys**, down from 46,
of which only 3 carry a value this design invented from nothing (`HeadingBrush` Dark and the two
caption bevels).

### 3.4 The focus visual

Deleting `FocusStrokeInnerBrush` deletes the two-ring rectangle, which is a Windows 11 form. Aero's is
the classic **1 px dotted rectangle**, and it is expressible: a `Rectangle` with
`Stroke="{DynamicResource FocusStrokeOuterBrush}" StrokeThickness="1" StrokeDashArray="1,1"` inside
each `FocusAdorner`. This matters more than it sounds — the vendored
`Button_RepeatButton_ToggleButton.axaml` declares **zero `:focus` rules and zero `FocusAdorner`
setters**, so keyboard focus on a stock Aero button has no indication of its own. Task 3 verifies
Avalonia's default adorner is visible on it and, if it is not, sets the same dotted-rectangle adorner
on the vendored `Button` theme in the same commit.

---

## 4. The comment-prose problem

`Launcher.axaml` is 1,274 lines of which 485 are markup; `Tokens.axaml` 310 of which 80;
`MainWindow.axaml` 597 of which 154. Across all 15 UI `.axaml` files: **3,355 lines, 1,320 of markup**
— roughly **2,000 lines of comment prose, essentially all of it a Windows 11 measurement record**.
Under this project's rule that a comment which no longer describes the code is a bug, all of it comes
due.

**The policy is three buckets and one timing rule.**

**Timing rule, absolute: a comment is deleted or rewritten in the same commit as the code it
describes.** Never a later "cleanup task". There is no cleanup task in §7 that exists to absorb prose;
Task 10 handles repo-level documents only.

**Bucket A — DELETE, in the task that removes the subject.** Prose whose subject stops existing.
Named, with the task that owns each:

| prose | file:lines | lines | task |
|---|---|---|---|
| the `CaptionSpacer` 140 DIP derivation | `MainWindow.axaml:293-302` | 10 | 1 |
| the `PART_TitleTextPanel` / Opacity precedence write-up | `MainWindow.axaml:125-143` | 19 | 1 |
| the `PART_MinimizeButton` VerticalAlignment write-up + its CORRECTION | `MainWindow.axaml:151-181` | 31 | 1 |
| the Round 4 item 5 "NOT here, and that is the finding" block | `MainWindow.axaml:108-123` | 16 | 1 |
| the `TitleBarBackgroundBrush` write-up | `Tokens.axaml:274-304` | 31 | 1 |
| the `"*,Auto,*"` title-strip centring argument | `MainWindow.axaml:200-210` | 11 | 1 |
| the `SearchFieldWidthRatio` five-width Task Manager table | `MainWindow.axaml.cs:779-793` | 15 | 1 |
| the search field's `PART_BorderElement` focus write-up | `Launcher.axaml:498-514` | 17 | 4 |
| `TmNavRowItem`'s 55-line Task Manager preamble | `Launcher.axaml:309-364` | 55 | 8 |
| the rail's "no rule here that makes them icon-only" block | `Launcher.axaml` (rail section) | 9 | 8 |
| the 40 DIP item-column derivation | `MainWindow.axaml:346-353`, `:361-380` | 27 | 8 |
| `RadiusPaneTopLeft`'s arc measurement | `Tokens.axaml:144-163` | 20 | 7 |
| `RadiusPanel`'s four-criterion panel-vs-pane comparison | `Tokens.axaml:166-198` | 33 | 7 |
| the 147-line Mica-straddle content-pane argument | `Launcher.axaml:722-868` | 147 | 7 |
| `Border.tm-group-panel`'s 47-line Windows 11 Settings derivation | `Launcher.axaml:908-954` | 47 | 9 |
| the `ClipToBounds` "OBSERVED ONCE, ON A SURFACE THIS TREE NO LONGER CONTAINS" block | `MainWindow.axaml:556-588` | 33 | 9 |

**Bucket B — REWRITE, in the task that moves the number.** Prose whose subject survives but whose
numbers change. The new text cites the new measurement, the UX Guide URL, or is labelled **AUTHORED**;
it never cites the old one.

| prose | file:lines | what changes | task |
|---|---|---|---|
| `RailWidthDip` / `PaneBorderDip` xmldoc | `MainWindow.axaml.cs:363-388` | 48 → 160; the "single source of truth" argument survives verbatim | 8 |
| `TitleStripHeightDip` / `ChromeHeightDip` xmldoc | `WindowHost.cs:36-52` | 48 → 30; gains a pointer to the new guard test | 1 |
| `MinWindowWidthDip` xmldoc | `WindowHost.cs:54-61` | 760 → 872; the `AdjustWindowRectEx` *declarability* argument survives unchanged, because `ExtendClientAreaToDecorationsHint` stays on | 8 |
| the settings overflow measurement | `Launcher.axaml:1078-1079`, `:1140` | premise "760 DIP client" → "872 DIP window, 711 DIP content pane"; every other figure holds | 8 |
| what wraps in Chinese at the floor | `FoldersCard.axaml:27-37` | same premise line | 8 |
| `Border.tm-nav-separator`'s derivation | `Launcher.axaml:700-720` | horizontal 4 → 2 (the new row `Margin`); item column 40 → 156; footer 173 → 109 DIP | 8 |
| the tile's "48 DIP of chrome" arithmetic | `Launcher.axaml:526-532` | terms stay, numbers move with the type ramp | 9 |
| the five `ToggleSwitch.tm-card-toggle` call sites' alignment prose | `Launcher.axaml:1158-1178` | the `-12` compensation **survives** (Aero copies Fluent 11's `<ColumnDefinition Width="12" MaxWidth="12"/>` verbatim), but the pill grows 40×20 → 56×22 VENDORED, so every measured statement about the trailing column is re-measured | 3 |
| the hotkey box's disabled-vocabulary argument | `Launcher.axaml:1180-1201` | re-targeted from `PART_BorderElement` to `BgElement` and re-measured | 4 |

**Bucket C — ARCHIVE.** Prose that is a genuine record of a real measurement against a real Windows 11
machine, worth keeping as evidence, and no longer about this code. **Destination: one new file,
`docs/superpowers/archive/2026-08-04-windows11-measurement-record.md`**, sectioned by round. Each
section states what was measured, with what instrument, on what machine, and **the commit at which the
code it described stopped existing**. Nothing is moved by copy-paste alone — a paragraph that only
made sense inside a `<Style>` gets a sentence of framing.

What goes there: the Task Manager rail survey (pane width, item column, icon box, footer geometry, the
seven-glyph `x=5322` alignment finding), the Mica straddle measurements (238..247 Light / 30..34 Dark),
the Fluent 2 radius arc scans and their four-criterion table, the Windows 11 Settings tile derivation,
the caption-button UI Automation cluster measurement (140 DIP), and the `PART_TitleBar` white-band
investigation with its `GetVisualDescendants()` dump.

**And the six existing design documents in `docs/superpowers/specs/` are not edited and not deleted.**
Each gains a one-paragraph header: *"Describes the Windows 11 shell, shipped in 0.11.0–0.12.0 and
replaced by the Aero conversion (`2026-08-04-aero-conversion-design.md`). Kept as the record of that
work; its measurements were true of the code at the time and are not true of the code now."* Task 10.

**Rules for prose written during this round.**

1. **Any Aero colour quoted in `Launcher.axaml` must be a decimal triple.**
   `LauncherStyles_ContainNoColourLiterals` greps the whole file, prose included, for
   `#[0-9A-Fa-f]{3,8}\b`. Aero's palette is hex-heavy; pasting one gradient table into a comment
   breaks the build. `Launcher.axaml:739-741` already warns about exactly this.
2. **Any Aero geometry must cite one of three things**: the measured Microsoft screenshot
   (`https://learn.microsoft.com/en-us/windows/win32/uxguide/images/win-window-frames-image2.png`,
   with the pixel coordinates), a `learn.microsoft.com` URL, or the VENDORED file and key. Otherwise
   it is labelled **AUTHORED** in capitals.
3. **The navigation pane's numbers are all AUTHORED** and every comment that states one says so in the
   same sentence.
4. **No comment claims a Windows 7 measurement.** There is no Windows 7 machine here. The one Aero
   capture we have is Microsoft's own published Calculator screenshot and it is cited by URL every
   time it is used.

---

## 5. The dead-selector sweep

**An Avalonia selector that matches nothing does not error, throws nothing, and fails no test.** This
is the round's most dangerous silent failure class, and the sweep below is the complete live census —
24 `/template/` rules, not the feasibility document's "~14".

### 5.1 The 14 that are immune — ours, and they stay ours

`Launcher.axaml:111, 114, 125, 163, 169, 224, 227, 234, 301, 304, 396, 399, 402, 405` — all naming
`Border#Root`, `Border#StateLayer` or `Border#SelectionIndicator`, and all declared **inside the same
`ControlTheme` that declares the template**: `TmTileButton`, `TmIconButton`, `TmButton`,
`TmNavRowButton`, `TmNavRowItem`. The selector and the part are in one file we own, so no theme change
can break them. Four of them (`:396, :399, :402, :405`) are rewritten in Task 8 when `TmNavRowItem` is
rebased, and `:405` (`SelectionIndicator`) is deleted with the pill — that is a deliberate rewrite, not
a breakage.

### 5.2 The 5 that break — Fluent's `TextBox` part, Task 4

Aero's `TextBox` template names its border **`BgElement`**; `grep -c PART_BorderElement` on the
vendored `ControlStyles/TextBox.axaml` is **0** (VENDORED). Fluent 12.1 declares the name at
`Controls/TextBox.xaml:116`.

| file:line | selector | fate |
|---|---|---|
| `Launcher.axaml:515` | `TextBox.tm-search:focus /template/ Border#PART_BorderElement` | **DELETE** — it exists only to beat Fluent's built-in accent focus band, and Aero's TextBox has no accent focus band. It becomes both a no-op *and* unnecessary; deleting it is the correct fix, not re-targeting it |
| `Launcher.axaml:1222` | `TextBox.tm-keycap /template/ Border#PART_BorderElement` | **RE-TARGET → `Border#BgElement`** |
| `Launcher.axaml:1228` | `…:pointerover /template/ Border#PART_BorderElement` | **RE-TARGET** |
| `Launcher.axaml:1231` | `…:focus /template/ Border#PART_BorderElement` | **RE-TARGET** |
| `Launcher.axaml:1239` | `…:disabled /template/ Border#PART_BorderElement` | **RE-TARGET** |

The four re-targets are not cosmetic. Left dead, the hotkey box reverts to Aero's stock text-box paint
in all four states, silently discarding the "one disabled vocabulary per page" decision that
`Launcher.axaml:1180-1201` was written to establish.

> **CORRECTED after Task 4 shipped (`87dd54f`). The DELETE row above is wrong, and the code is the
> authority.** All five selectors were re-targeted onto `Border#BgElement`; none was deleted, and the
> `tm-search` one gained a `:pointerover` twin, so `Launcher.axaml` now carries **six** rules where
> §5.2 counted five. The premise "Aero's TextBox has no accent focus band" is only half the story:
> the vendored `ControlStyles/TextBox.axaml`'s `^:pointerover` and `^:focus` blocks set **`Background`
> as well as `BorderBrush`** on `BgElement`, and `SearchBox`'s local `BorderThickness="0"` means the
> `BorderBrush` half cannot draw at all — so deleting the rule leaves only the background half:
> an opaque `AeroTextBoxFocusBackground` block inset inside the field. It was built both ways and
> measured — Dark interior (93,93,93) deleted vs (49,49,49) present; **Light is identical either way**,
> because that token's Light value is white over a white field, so a Light-only check passes the broken
> build. The A/B table lives at the two rules in `Launcher.axaml`. §7 Task 4's "Why" paragraph below
> carries the same retraction.

### 5.3 The 5 that are deleted with the caption — Task 1

`MainWindow.axaml:144` (`Panel#PART_TitleTextPanel`), `:147` (`Button#PART_FullScreenButton`),
`:182`, `:185`, `:188` (`Button#PART_MinimizeButton` / `MaximizeButton` / `CloseButton`). All five
target parts that belong to **Fluent's** `WindowDrawnDecorations` template. Task 1 replaces that
template wholesale, so all five go with it — along with the 66 lines of comment prose that justify
them (§4 bucket A).

### 5.4 Five more mentions inside comments

`MainWindow.axaml:115`, `:252`; `Launcher.axaml:500`, `:1164`; `Tokens.axaml:294`. All are inside
blocks bucket A already deletes.

### 5.5 The library's own dead selectors — deleted on vendoring, logged per file

These are dead **upstream**, at Avalonia 11 too. The CSS class `aeroState` (without the `2`) is applied
to no element anywhere in the upstream repository; only `aeroState2` is (VENDORED, `grep -rn`).

| file | dead rules | consequence upstream |
|---|---|---|
| `AVMAP/StyleTrailMix.axaml` | `:9, :10, :11, :15, :16, :17, :18, :27, :28, :29, :30` — 11 rules on `aeroState` | the opacity cross-fade machinery never runs |
| `ControlStyles/ScrollBar.axaml` | `:379, :382-383, :434, :442, :446, :454` on `aeroState`; `:153` on `#PART_ContentPresenter` | the track's hover/expand fill and the disabled foreground never run |
| `ControlStyles/ProgressBar.axaml` | `:309, :312, :320, :335, :363, :366, :374, :389, :442` on `#GlowClipHelper` / `#IndeterminateProgressBarIndicator` / `#IndeterminateProgressBarIndicator2`, none of which is ever declared; plus `:156`, a `{Binding #GlowClipHelper.Bounds}` to a name that does not exist | the whole indeterminate glow never runs |
| `ControlStyles/ButtonSpinner.axaml` | `:194` on `#PART_TextPresenter` | one dead rule |

> **CORRECTED after Task 5 shipped, on the `ProgressBar.axaml` row.** Its nine style rules are right
> and all nine are gone. Its tenth item is wrong: `:156` is not a live `{Binding}` — it sits inside
> the XML comment spanning `:152-158` — and a different defect on that file goes unlisted,
> `ShineBorder`'s `CornerRadius="{Binding #PART_Indicator.Margin}"` at `:254`, a `Thickness` bound to
> a `CornerRadius` with no conversion between the two types in Avalonia 12.1. §7 Task 5 carries the
> full retraction and the evidence.

**Policy: we delete them, and each vendored file's header lists exactly what was removed.** The
alternative — keep them for future upstream comparison — buys nothing: there is no upstream to merge
from (zero tags, zero releases, the author's own position is *"this theme is far from complete, and
thus I can't really encourage its use yet"*), and this project's culture treats a rule that matches
nothing as a bug. What survives `StyleTrailMix.axaml` is **3 rules**: the 125 ms `BrushTransition` on
`Border.aeroState2` and the two `PART_ContentPresenter` foreground rules.

### 5.6 The step that makes this verifiable forever

Task 1 adds `NoTemplatePartSelector_NamesAPartOutsideTheAllowList` (§2.6). It scans every `.axaml`
under `src/` for `/template/ <Type>#<Name>` and requires each `<Name>` to be declared as `x:Name=` or
`Name=` inside some `ControlTemplate` in the tree, or to be on a short, explicitly-listed set of
Avalonia-owned part names we deliberately target. Adding a foreign target then requires editing the
test, which is the point.

---

## 6. Attribution

MIT permits copying individual files, modified, **provided the copyright notice and permission notice
travel with them**. `https://raw.githubusercontent.com/Splitwirez/aero-theme-for-avalonia/main/LICENSE`
is the verbatim unmodified MIT text opening `MIT License` / `Copyright (c) 2026 Splitwirez`; the GitHub
API reports `license.spdx_id = "MIT"`. There is no attribution clause beyond stock MIT, no additional
terms and no CLA.

**Exactly where the notice goes, in what form. All of it lands in Task 2, before a single vendored file
is referenced by anything.**

1. **`src/TanMenu.Wpf/Themes/Aero/LICENSE-aero-theme-for-avalonia.txt`** — the upstream `LICENSE` byte
   for byte. Nothing else in that file.
2. **A header comment at the top of every vendored file**, without exception — the 13 `.axaml` (12
   control styles + `HeaderedContentControl`), `AVMAP/Brushes.axaml`, `AVMAP/Metrics.axaml`,
   `AVMAP/StyleTrailMix.axaml`, `Variant/Light.axaml`, `Variant/Dark.axaml`, and the 5 converter
   `.cs`. Fixed four-part form:

   ```
   Copyright (c) 2026 Splitwirez
   Licensed under the MIT License. See LICENSE-aero-theme-for-avalonia.txt in this directory
   for the full text of the copyright notice and permission notice.
   Source: https://github.com/Splitwirez/aero-theme-for-avalonia at main = a705b7c0
           src/AeroAvalonia/<original path>
   Changes from upstream: <numbered list, or "none">
   ```

   The "Changes from upstream" list is not optional and not a summary. It is the numbered record of
   every edit — the dead-selector deletions of §5.5, the two `{ReflectionBinding}` conversions, the six
   `Watermark` → `PlaceholderText` renames, the `HighlightBrush` / `HighlightForegroundBrush` fixes,
   the dropped `Metrics.axaml:13`.
3. **`README.md` gains a "Third-party notices" section** naming the project, its URL, its licence, the
   commit `a705b7c0`, and what was taken (control themes and colour resources; not the fonts, not
   `Window.axaml`).
4. **The shipped payload carries it.** `scripts/package-portable.ps1` and `scripts/package-msix.ps1`
   copy `LICENSE-aero-theme-for-avalonia.txt` into the package as `THIRD-PARTY-NOTICES.txt` beside the
   executable. A licence notice that only exists in the source tree does not travel with the binary,
   and the binary is what users get.
5. **`EveryVendoredAeroFile_CarriesTheUpstreamCopyrightHeader`** (§2.6) asserts (2) mechanically over
   every file under `Themes/Aero/`, so a file added later without the header fails the build.

**The fonts are not taken and that closes a real gap.** The 7 bundled Inter TTFs (2,536,600 bytes)
carry **no font licence anywhere in the upstream tree**; Inter is normally SIL OFL, whose notice
requirement a repo-level MIT `LICENSE` does not satisfy. Exactly one line in the whole upstream
repository references them (`AVMAP/Metrics.axaml:13`), we drop that line, and the question becomes
moot. It also preserves `TanMenu.Wpf.csproj:66-68` verbatim (*"No fonts are embedded … 22.7 MB"*),
keeps CJK rendering through Microsoft YaHei UI, and — the point worth making — **Segoe UI is more
Aero-faithful than Inter anyway**, since Segoe UI 9 pt *is* the Windows 7 system font.

---

## 7. The task sequence

Ten tasks. **Each one ends buildable, runnable and shippable** — that is how six rounds were delivered
and this round does not break it. The riskiest item is first, so it fails cheaply.

---

### Task 1 — The Aero caption *(THE RISKY ONE)*

#### Decision

**The vehicle.** `MainWindow.axaml:105-192`'s existing `Window.WindowDecorationsTheme` block stays and
gains one setter: `Template`. `BasedOn` is **dropped** — a full replacement should not inherit Fluent's
`DefaultTitleBarHeight = 30`, `DefaultFrameThickness = 1` and `DefaultShadowThickness = 8` setters, and
`GetEffectiveTheme()` returns the explicit theme without consulting the implicit
`{x:Type WindowDrawnDecorations}` resource at all. All five existing `Style` rules are deleted (§5.3).

```xml
<Window.WindowDecorationsTheme>
  <ControlTheme TargetType="chrome:WindowDrawnDecorations">
    <Setter Property="Template">
      <WindowDrawnDecorationsTemplate>
        <chrome:WindowDrawnDecorationsContent>
          <chrome:WindowDrawnDecorationsContent.Underlay>  <!-- glass + bevel + icon + title -->
          <chrome:WindowDrawnDecorationsContent.Overlay>   <!-- the three caption buttons -->
        </chrome:WindowDrawnDecorationsContent>
      </WindowDrawnDecorationsTemplate>
    </Setter>
  </ControlTheme>
</Window.WindowDecorationsTheme>
```

`Avalonia.Markup.Xaml.Templates.WindowDrawnDecorationsTemplate` is `public`, carries
`[Content] [TemplateContent(TemplateResultType = typeof(WindowDrawnDecorationsContent))]`, and
`Avalonia.Markup.Xaml`'s `AssemblyInfo` has
`[assembly: XmlnsDefinition("https://github.com/avaloniaui", "Avalonia.Markup.Xaml.Templates")]`, so it
needs no prefix. `WindowDrawnDecorationsContent` is `public` under `Avalonia.Controls.Chrome`, which
`MainWindow.axaml:3` already imports as `xmlns:chrome`.

**The split between the two slots mirrors Fluent's own**, which is the arrangement Avalonia itself
validates: Fluent puts `PART_TitleBar` in the Underlay and the caption buttons in the Overlay. The
Underlay renders behind the window's content, the Overlay in front of it. The buttons must be in front
or a hit test finds our content first.

**Geometry — every number MEASURED from Microsoft's own published Windows 7 screenshot**
(`https://learn.microsoft.com/en-us/windows/win32/uxguide/images/win-window-frames-image2.png`,
Calculator on Windows 7, 278×351, 24-bit RGB, native resolution — self-consistency holds on both axes,
228 − 8 − 8 = 212 and 321 − 30 − 8 = 283, and every border is a crisp single pixel):

| quantity | value | pixel evidence |
|---|---|---|
| caption band height **T** | **30 DIP** | top non-client band y ∈ [14, 43]; client from y = 44 |
| inner bevel at the band's bottom | 1 DIP light over 1 DIP dark | y = 42 light `#D5E7D4`, y = 43 dark `#455C42` |
| caption buttons — height | **19 DIP**, top-flush 1 DIP down | close-button vertical scan at x = 215: y = 15…33 |
| caption buttons — widths | minimize **28**, maximize **27**, close **48** | separators at x ≈ 140, x ≈ 168, x = 195 (close left), x = 242 (close right) |
| button block | **103 DIP**, right edge inset **8 DIP** from the window's outer edge | block x ∈ [140, 242]; window outer right at x = 249 |
| title text left edge | **30 DIP** from the window's outer edge | glyph column histogram, first text column x ≈ 52; window left edge x = 22 |
| title vertical position | centred in the full 30 DIP band | glyph band y = 24…32, centre ≈ 28; band centre (14+43)/2 = 28.5 |
| caption gradient shape | brightest ≈ 11–13 DIP below the top edge, darker toward both ends | x = 110: `#A9CCA4` (y=16) → `#B9DAB3` (y=25–27) → `#A2CB9F` (y=41) |

Cross-check: 30 − 8 = 22 is the widely-quoted Windows 7 `SM_CYCAPTION`, and 8 is the classic
`SM_CXSIZEFRAME(4) + SM_CXPADDEDBORDER(4)`. Two independent numbers agreeing is what makes the capture
trustworthy as unscaled.

The title's icon sits between the frame and the text, so: icon `Margin="8,0,0,0"` at 16×16, title
`Margin="6,0,0,0"` → 8 + 16 + 6 = **30**, reproducing the measurement exactly. The button block's
right inset of 8 DIP is kept even though we draw no side frame, because under
`ExtendClientAreaToDecorationsHint` the client's right edge **is** the window's outer edge — which is
the edge the measurement is relative to.

**The 8 px side and bottom glass frame is NOT drawn.** On Win32 the only decoration part ever enabled
is `TitleBar` — `WindowImpl.RequestedDrawnDecorations => _isClientAreaExtended ?
(PlatformRequestedDrawnDecoration)8 : 0`, against the enum `None=0, Shadow=1, Border=2, ResizeGrips=4,
TitleBar=8` — so `FrameThickness` and `ShadowThickness` are permanently zero and `:has-border` /
`:has-shadow` never fire. Drawing the frame anyway would mean drawing it *inside* the client area,
stealing 8 DIP from the content on three sides and putting a fake glass gutter between the navigation
pane and a real Windows 11 border. The top band is the one part of the Aero frame that Win32 actually
grants us, and it is the part that carries the identity.

**Colour.** The glass is a three-stop vertical `LinearGradientBrush` over the acrylic, its stop
positions taken from the measured gradient shape (brightest at ≈ 40 % of the band's height, darker at
both ends), tinted by `AccentColor` / `AccentColorShaded`. The measured RGB values are deliberately
**not** used: that capture is translucent glass over the green Windows 7 wallpaper, so every pixel is
wallpaper-tinted, and Aero's caption colour was the user's colorization value composited over a blur —
not a fixed palette, and not readable off one screenshot. **AUTHORED alphas**, Light `#40 / #66 / #33`
over the accent; Dark the same structure in black-alpha.

**Active / inactive.** `WindowDrawnDecorations`' pseudo-classes are `:normal`, `:maximized`,
`:fullscreen`, `:has-shadow`, `:has-border`, `:has-titlebar`, `:has-maximize`, `:has-fullscreen`,
`:has-minimize` — **there is no active/inactive state and no public path from the decorations to
`WindowBase.IsActive`.** We do not gamble on `{Binding $parent[Window].IsActive}` resolving from inside
a replaced template; it is unverified. Instead: **`MainWindow.axaml.cs` observes its own
`IsActiveProperty` and writes `Resources["CaptionGlassBrush"]` to `CaptionGlassActiveBrush` or
`CaptionGlassInactiveBrush`**, and the template reads `{DynamicResource CaptionGlassBrush}`. That is
the mechanism `Services/AppTheme.cs` already uses successfully for five accent keys, and the decorations
are a real logical descendant of the Window (`TopLevel`'s constructor does
`((ISetLogicalParent)topLevelHost).SetParent(this); LogicalChildren.Add(topLevelHost)`), so
`DynamicResource` and `ActualThemeVariant` both reach `Window.Resources`.

**The title glow — the one hard Aero number Microsoft published, and it maps exactly.**
`https://learn.microsoft.com/en-us/windows/win32/dwm/customframe` Appendix B:
`DTTOPTS DttOpts = {sizeof(DTTOPTS)}; DttOpts.dwFlags = DTT_COMPOSITED | DTT_GLOWSIZE;
DttOpts.iGlowSize = 15;`. Avalonia 12.1 ships `Avalonia.Media.DropShadowEffect` with
`BlurRadius` (default 5), `Color` (default Black), `Opacity` (default 1) on `DropShadowEffectBase`, and
`OffsetX` / `OffsetY` (default 3.5355) of its own — decompiled from
`~/.nuget/packages/avalonia/12.1.0/lib/net10.0/Avalonia.Base.dll`. So:
`<DropShadowEffect BlurRadius="15" OffsetX="0" OffsetY="0" Color="White" />` in Light and
`Color="Black"` in Dark. A symmetric zero-offset drop shadow **is** a glow, and 15 is Microsoft's own
number, not ours.

**Roles.** The Underlay band carries `chrome:WindowDecorationProperties.ElementRole="TitleBar"`; the
three Overlay buttons carry `CloseButton` / `MinimizeButton` / `MaximizeButton`. That is sufficient and
complete: role → Win32 hit-test is `TitleBar → HTCAPTION`, `CloseButton → HTCLOSE`,
`MinimizeButton → HTMINBUTTON`, `MaximizeButton → HTMAXBUTTON`; drag and double-click-to-maximise come
from `DefWindowProc` for free (`WM_NCLBUTTONDBLCLK` appears nowhere in `Avalonia.Win32.WindowImpl`);
right-click on the band gives the system menu for free (`WM_NCRBUTTONUP` is not in
`CustomCaptionProc`'s switch); and `ShouldRedirectNonClientInput` returns true for exactly those three
button hit-tests, which is what converts `WM_NCMOUSEMOVE` / `WM_NCLBUTTONDOWN` / `WM_NCLBUTTONUP` into
`RawPointerEventArgs` and makes `:pointerover`, `:pressed` and `Click` work. The buttons keep the names
`PART_CloseButton` / `PART_MinimizeButton` / `PART_MaximizeButton`, so `AttachCaptionButtons()` wires
Close → `Close()`, Minimize → `WindowState = Minimized`, Maximize → toggle, and sets every automation
identity — all of it for free.

**`PART_FullScreenButton` is simply not authored.** Every `AttachCaptionButtons` lookup is
null-guarded, so an absent part is legal. That deletes the `IsVisible="False"` rule and its rationale
rather than porting them.

**Both hints stay.** `ExtendClientAreaToDecorationsHint="True"` and
`ExtendClientAreaTitleBarHeightHint` — the latter **48 → 30, and it must stay equal to the drawn
height**: `WindowImpl.UpdateExtendMargins` sets `rect2.top = (int)(_extendTitleBarHint * RenderScaling)`
and `HitTestNCA` derives the OS-level caption/resize band from `_extendedMargins.Top`. A mismatch
produces a drag band that does not line up with the caption you can see.

**The client-side title strip is deleted.** `MainWindow.axaml:194` becomes
`<Grid RowDefinitions="30,*">` with **row 0 empty** — a reserved spacer, because
`TopLevelHost.DecorationInset` stays zero on Win32 and our content is otherwise arranged over the whole
window. Gone with it: the `Border` at `:197-199`, `Grid#TitleStripContent` at `:211-213`, the icon +
name `StackPanel` at `:217-227`, `Border#SearchField` at `:234-279` (relocated, below),
`StackPanel` + `Border#CaptionSpacer` at `:289-304`.

**The search field moves to the 应用 page in this same commit**, because Task 1 deletes its host and
the app must stay runnable. The full Aero treatment of the field is Task 9; Task 1 does the minimum
move: `Border.tm-searchfield` and its `TextBox` are restored to `Views/AppPage.axaml` as a
`DockPanel.Dock="Top"` header above `PinNotice`, `Border.tm-header` is restored to `Launcher.axaml`,
and `OnSearchKeyDown` / `OnSearchFocusChanged` move from `MainWindow.axaml.cs` back to
`AppPage.axaml.cs` — which is where they lived before commit `6d00060`. The field takes a fixed
**320 DIP AUTHORED** width, centred; `ApplySearchFieldWidth`, `SearchFieldWidthRatio`,
`SearchFieldMinWidth`, `SearchFieldMaxWidth` and the `ClientSizeProperty` subscription at
`MainWindow.axaml.cs:106` are all deleted, as is `chrome:WindowDecorationProperties.ElementRole="User"`
(there is no caption to pass input through any more) and Round 6 item 1's
`SearchField.IsVisible = (page == ShellPage.App)` in `ShowPage` (the field is *in* the 应用 page now).
320 is AUTHORED: `ctrl-search-boxes` gives no width rule and its sizing section is an image; 320 holds
the longest prompt in both languages plus `ctrl-drop`'s published *"additional 30 percent"*
localisation allowance.

**The overflow `⋯` button and the page-close button do NOT come back.** Round 3 Task 2 moved
打开主目录/刷新/退出 into the rail footer and Round 5 item 2 made 关于 a settings card; re-adding
`OverflowButton` would re-create the exact pattern two rounds removed. Only the *search field* reverts.

**The backdrop.** `TransparencyLevelHint` **`Mica` → `AcrylicBlur`** (the literal is `AcrylicBlur`, not
`Acrylic` — `WindowTransparencyLevel` has exactly five static members: `None`, `Transparent`, `Blur`,
`AcrylicBlur`, `Mica`). **And `ApplyBackdrop`'s guard must change in the same edit or the conversion
silently fails**: `MainWindow.axaml.cs:526-533` tests `ActualTransparencyLevel ==
WindowTransparencyLevel.Mica`, so on a *fully successful* acrylic window the guard falls through, the
fallback subscribes, and the opaque `SurfaceBaseBrush` is painted over a perfectly good backdrop with
no error anywhere. The guard becomes `== WindowTransparencyLevel.AcrylicBlur`, and the method gains the
Serilog line from §2.7. `Background="Transparent"` at `MainWindow.axaml:10` stays required.

**`RestoreSystemCloseCommand()` survives untouched.** `WindowImpl.ExtendClientArea()` ends with
`DisableCloseButton(hwnd)` = `EnableMenuItem(GetSystemMenu(hwnd,false), SC_CLOSE, MF_GRAYED|MF_DISABLED)`
on every window-state change, independent of which caption vehicle is chosen.

**Three copies of the caption height, and a guard.** `MainWindow.axaml:15`, `MainWindow.axaml:194`
and `WindowHost.cs:39` all become 30. Nothing links them today.
`ShellConstantTests.CaptionHeight_IsTheSameNumberInAllThreePlaces` links them from this commit on.

#### Why

The one part of Aero that Windows 11 does not supply and this library does not contain is the caption;
the feasibility document's §1.2 named it as *"the actual missing piece, and it is a new project"*. It
is also the only part that is genuinely at risk, so it goes first: everything downstream — the tokens,
the controls, the pane — is a repaint that cannot fail in a way we cannot see.

Replacing the `Template` rather than emptying it and drawing in window content is the user's decision 1
read literally, and it buys a real property: the caption is genuinely non-client and cannot be covered
by page content or clipped by the content grid. The runtime plumbing is settled —
`TopLevelHost.UpdateDrawnDecorations` subscribes to `WindowDrawnDecorations.TemplateProperty` and calls
`ApplyDecorationsTemplate()` on every change; `Window.OnPropertyChanged` re-runs `UpdateDrawnDecorations`
when `WindowDecorationsThemeProperty` changes; `Template` is an ordinary `StyledProperty` that Fluent
itself sets with an ordinary `Setter`.

30 DIP rather than 48 is the whole visual point. `ctrl-search-boxes` puts a primary-entry-point search
*"centered at the top of the window"*, which is where it already is horizontally, and
`win-window-frames` forbids putting controls in the frame — so the strip empties, and an empty strip
has no reason to be 48 DIP tall.

#### Blast radius

**Files:** `MainWindow.axaml` (the `WindowDecorationsTheme` block, the window attributes, the whole
`Grid.Row="0"` subtree), `MainWindow.axaml.cs` (`ApplyBackdrop`, the `IsActive` observer,
`ApplySearchFieldWidth` + 3 constants + 1 subscription deleted, `ShowPage`'s search visibility line,
`TitleStripContent.DataContext`, the search key handlers moved out), `Views/AppPage.axaml` +
`.axaml.cs` (header restored), `Themes/Fluent2/Launcher.axaml` (`Border.tm-header` restored;
`TitleBarBackgroundBrush` consumer gone), `Themes/Fluent2/Tokens.axaml`
(`TitleBarBackgroundBrush` deleted, 5 caption keys added), `Services/WindowHost.cs`
(`TitleStripHeightDip` 48 → 30).

**Fit terms that move:** **T** 48 → 30, so `chromeHeight` 49 → 31. **R** and **B** do not move in this
task. `Ph` grows by the restored header (≈ 38 DIP).

**Tests:** 4 new (`CaptionHeight_…`, `RailWidth_…`, `MinWindowWidth_…`,
`NoTemplatePartSelector_…`). `EveryTokenTheLauncherWindowReferences_IsDefinedInTokens` still passes —
the caption's brushes are defined in `Tokens.axaml` in this task, before any Aero key is referenced.
All 23 `WindowPlacementTests` pass unchanged.

**Invariants that must survive:** `ExtendClientAreaTitleBarHeightHint` set and equal to the drawn
height; `RestoreSystemCloseCommand()`; the five re-fit triggers; `MinHeight` deliberately absent from
the `Window` (Round 4 item 4's argument is a function of `ExtendClientAreaToDecorationsHint` staying
on, and it stays on).

**Prose deleted in this commit:** the six blocks in §4 bucket A tagged Task 1 (132 lines).

#### Verification — no keyboard, no mouse

1. `dotnet build -c Release` clean; `dotnet test` → **145 passed** (141 + 4 new).
2. `dotnet publish -c Release`; IL2xxx/IL3xxx count unchanged from the pre-task baseline.
3. Launch. Log line: `Backdrop: requested AcrylicBlur, granted AcrylicBlur`.
   *(If it reads `granted Transparent`, acrylic was refused — see the fallback below.)*
4. Log line: chrome term reads `chrome 49x31`. **The height term reads 568 and that is correct** —
   §2.1. The `page {W}x{H}` term's `H` grows by the restored header.
5. `SendMessage(hwnd, WM_NCHITTEST, 0, MAKELPARAM(x, y))` at five points, in screen coordinates:
   - mid-band, left of the title → **2** (`HTCAPTION`)
   - centre of Minimize → **8** (`HTMINBUTTON`)
   - centre of Maximize → **9** (`HTMAXBUTTON`)
   - centre of Close → **20** (`HTCLOSE`)
   - a point 100 DIP into the page → **1** (`HTCLIENT`)
6. UI Automation `BoundingRectangle` on `AutomationId` `Minimize` / `Maximize` / `Close`
   (set by `AttachCaptionButtons`, so they exist without us naming them): all three heights
   19 DIP × scale; widths 28 / 27 / 48 × scale; all three tops equal and 1 DIP × scale below the client
   top; Close's right edge 8 DIP × scale left of the window's right edge.
7. `PrintWindow` flag 2, Light **and** Dark:
   - the band's pixels are **not** `AeroWindowBackground` (`#FCFCFC` / `#373737`) — proves the opaque
     fallback did not paint over the acrylic;
   - a vertical scan at the band's horizontal midpoint reproduces the measured **shape**: the
     brightest row is 11–13 DIP below the top, and both ends are darker;
   - rows 28 and 29 (0-based, the last two of the 30) are the light-then-dark bevel;
   - the title's ink starts at x = 30 DIP × scale.
8. Activate a different window with `SetForegroundWindow` (no input), `PrintWindow` again: the band's
   mean differs from the active capture by more than the capture's own noise floor. Restore focus with
   `SetForegroundWindow` and confirm it returns.
9. Crop the title's glyph band from step 7 and record it in the round's report as the glow's evidence.
   The glow is a visual claim verified by a recorded crop, exactly as six rounds' visual claims were.
10. Both variants of steps 5–8.

#### What "it proved impossible" looks like, and the fallback

**Impossible is one of exactly four observations, and each is visible in the steps above:**

- **The XAML does not compile.** `WindowDrawnDecorationsTemplate` or `WindowDrawnDecorationsContent`
  does not resolve, or `Template` refuses a `Setter`. Step 1 fails.
- **The template compiles but nothing renders.** Step 7's capture shows the band absent or the window's
  top 30 DIP showing page content. This would mean `ApplyDecorationsTemplate` did not adopt the
  content.
- **The caption renders but is not non-client.** Step 5 returns `1` (`HTCLIENT`) where it must return
  `2` — the window cannot be dragged and the buttons are dead.
- **Acrylic is refused.** Step 3 reads `granted Transparent`. This is a *partial* failure: the caption
  still works, the glass is a hole instead of a blur.

**The fallback, in order of how much is given up:**

1. **Acrylic refused only** → keep the caption, drop `AcrylicBlur` back to `Mica` in the hint list, and
   describe the backdrop as Mica in the release notes. Everything else proceeds. *(Mica granted implies
   AcrylicBlur granted — same `CompositionEffectsSurface` object, and Acrylic's version floor 10.0.15063
   is strictly below Mica's 10.0.22000 — so this outcome would mean neither is available, and the
   window falls back to `Transparent`, which is what `_defaultTransparencyLevel` resolves to in our
   configuration.)*
2. **Not non-client, or renders wrong** → give the decorations an **empty** template
   (`<chrome:WindowDrawnDecorationsContent />` with no slots is legal; every `AttachCaptionButtons`
   lookup is null-guarded) and draw the identical caption in the window's own `Grid.Row="0"` content
   with the same `ElementRole` attached properties and three code-behind `Click` handlers. This is the
   other verified vehicle. Everything else in this task — the geometry, the tint, the glow, the
   acrylic, the search relocation, T = 30 — is unchanged. Cost: roughly a day, no downstream impact.
3. **Neither vehicle works** → **keep the Windows 11 caption and let everything else go Aero.** Revert
   only: the `Template` setter, the five deleted `Style` rules, `T` back to 48 in its three places, and
   the icon + name + `CaptionSpacer(140)` back into `Grid.Row="0"`. **Keep** the search relocation (it
   is decision 2 and stands on its own — `win-window-frames` forbids controls in the frame regardless
   of who draws the frame), keep the acrylic, keep `TitleBarBackgroundBrush`. Tasks 2–10 are entirely
   unaffected: not one of them touches the caption. The round then ships as the feasibility document's
   Option B exactly as written — *"an Aero-styled client area inside a Windows 11 window frame"* — and
   it is described to the user in those words.

---

### Task 2 — Vendor the Aero resource layer, referenced by nothing

#### Decision

Create `src/TanMenu.Wpf/Themes/Aero/` and place, with the §6 headers:

| destination | source | notes |
|---|---|---|
| `Variant/Light.axaml` | `src/AeroAvalonia/Variant/Light.axaml` | 168 `<Color>`, verbatim |
| `Variant/Dark.axaml` | `src/AeroAvalonia/Variant/Dark.axaml` | 168 `<Color>`, identical key set, verbatim |
| `AVMAP/Brushes.axaml` | same | 110 brushes (57 solid, 46 linear, 4 radial, 3 `GradientStops`), verbatim |
| `AVMAP/Metrics.axaml` | same | **minus line 13** (`ContentControlThemeFontFamily` → Inter) and **minus line 9** (`ControlContentThemeFontSize` → 12, which would reach every stock control before we have decided that). The remaining 8 keys are what the closure needs |
| `Converters/*.cs` (5) | `AdjustCornerRadiusConverter`, `DoubleIsNaNConverter`, `ProgressBarFillBrushConverter`, `ProgressBarFillColorConverter`, `ProgressBarAnimationConverter` | namespace `TanMenu.Wpf.Themes.Aero.Converters` |
| `LICENSE-aero-theme-for-avalonia.txt` | upstream `LICENSE` | verbatim |

Plus **one file we write**, `Themes/Aero/AeroResources.axaml`, which is the wrapper that decides the
variant key names:

```xml
<ResourceDictionary.ThemeDictionaries>
  <ResourceDictionary x:Key="Light">   <!-- ours, not the library's "Default" -->
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://TanMenu.Wpf/Themes/Aero/Variant/Light.axaml" />
      <ResourceInclude Source="avares://TanMenu.Wpf/Themes/Aero/AVMAP/Brushes.axaml" />
```
…and the same for `Dark`, plus `Metrics.axaml` merged variant-independently.

`App.axaml` gains `<StyleInclude Source="avares://TanMenu.Wpf/Themes/Aero/AeroResources.axaml" />`
**between `<FluentTheme />` and `Tokens.axaml`** — after Fluent so Aero's keys win over it, before
Tokens so **our** overrides still win over Aero's. `Application.Styles` is searched last-first
(`Avalonia.Styling.Styles.TryGetResource` counts down from `Count-1`), so that ordering is the one
thing that keeps `ContentControlThemeFontFamily` on Segoe UI.

**Nothing references any of it yet.** `Tokens.axaml` is not touched. The app looks identical.

`TanMenu.Wpf.csproj` needs **no edit**: it declares no `<AvaloniaResource Include="**/*.axaml">` (only
`wwwroot\app.ico` and `wwwroot\app-tile.png`), so `.axaml` are picked up by the Avalonia SDK's default
glob, and the converter `.cs` compile into the assembly.

`Fluent2TokenTests`' two "referenced is defined" tests are widened here (§2.6), *before* anything
references an `Aero…` key, so the widening lands green and is never a red-build surprise.

#### Why

This is the load-bearing step for every unknown the reconnaissance could not settle: whether 336 lines
of colours × 2, 420 lines of brushes and 5 converters compile at all under Avalonia 12.1 and
NativeAOT. It is also the step where MIT compliance lands, before a single vendored line is used for
anything. Doing it as a no-op commit means the *only* thing that can fail is the thing we are testing.

Taking `Brushes.axaml`, `Light.axaml` and `Dark.axaml` **whole** rather than piecemeal is measured, not
lazy: the transitive closure of the take-set is **93 of 110 brushes and 146 of 168 colours**. Trimming
saves 38 KB and risks cutting a colour reached indirectly, and the 17 unused brushes are coherent groups
we want (`AeroWindowBackground` — which §3.2 gives a consumer, the two `AeroToolTip*`, the 10
`AeroMenuBar*`, the 3 `AeroTabControl*`, `AeroGroupBoxBorderBrush` — which §3.2 also gives a consumer).

#### Blast radius

New directory, one line in `App.axaml`, two test files touched. **Zero visual change.** No fit term
moves. No prose becomes stale.

#### Verification

1. `dotnet build -c Release` clean — this is the answer to "does the vendored colour layer compile".
2. `dotnet test` → **147 passed** (145 + `EveryVendoredAeroFile_CarriesTheUpstreamCopyrightHeader` +
   `NoVendoredFile_ReferencesTheInterFontFamily`).
3. `dotnet publish -c Release`; IL count unchanged.
4. `PrintWindow` Light and Dark; compare against Task 1's captures — **pixel-identical**. Any
   difference means a vendored key leaked into a live lookup, which is exactly what this task must not
   do.
5. Confirm `THIRD-PARTY-NOTICES.txt` is in the output of `scripts/package-portable.ps1`.

---

### Task 3 — The control themes that break nothing of ours

#### Decision

Vendor into `Themes/Aero/ControlStyles/`, with §6 headers and §5.5 deletions:

`Button_RepeatButton_ToggleButton.axaml` (5,728 B), `CheckBox.axaml` (7,784 B),
`ToggleSwitch.axaml` (9,610 B), `ScrollViewer.axaml` (3,659 B), `ScrollBar.axaml` (26,760 B),
`ListBox.axaml` (3,226 B), `ListBoxItem.axaml` (3,434 B),
`AVMAP/StyleTrailMix.axaml` (3,594 B → 3 live rules).

Merged into `AeroResources.axaml` as `ControlTheme`s keyed `{x:Type X}`, so they auto-apply.

**`CheckBox` is in the set and the feasibility document missed it.** It is constructed in code, one per
configured tool, at `Views/Settings/ToolsCard.axaml.cs:104`:
`new CheckBox { Content = …, IsChecked = tool.Show, Margin = new Thickness(0, 3, 0, 3), Tag = tool }`.
It was never a "ComboBox is the only live gap" situation.

**`TmAccentButton` (`Launcher.axaml:253-257`) is deleted.** Aero has no accent-filled default button;
its command button is the gradient one. Its two call sites (`AppPage.axaml` `PinNowButton`,
`BehaviorCard.axaml` `PinTaskbarBtn`) move to `TmButton`. `TextOnAccentBrush` loses its last consumer.

**Three vendored global overrides are dropped on vendoring, because they reach controls we are not
restyling:** `ToggleSwitchThemeMinWidth` (Aero sets 0; Fluent 12.1 sets 154),
`ToggleSwitchPreContentMargin`, `ToggleSwitchPostContentMargin`. Our five toggles set
`OnContent=""`/`OffContent=""` so the margins are inert, and a 0 minimum width is a change we have no
reason to want app-wide.

**`StyleTrailMix.axaml`'s two surviving global rules are scoped.** As shipped they read
`:is(TemplatedControl):not(:disabled) /template/ ContentPresenter#PART_ContentPresenter` — app-wide,
not Aero-scoped. Our own `ControlTheme`s are safe by accident (their presenters are unnamed at
`Launcher.axaml:101, 157, 215, 294, 384`), but **Fluent's `ComboBoxItem` names its presenter
`PART_ContentPresenter`** (`Controls/ComboBoxItem.xaml:25`), and `AppearanceCard.axaml.cs:146-148`
deliberately sets `IsEnabled=false` + `Foreground=#777777` on the font list's group captions. Until
Task 6 authors our own ComboBox, the `:disabled` rule would silently repaint those captions. **The two
rules are therefore taken with `:is(TemplatedControl)` narrowed to the concrete Aero-templated types**,
and the change is item 1 of that file's "Changes from upstream" list.

**Both navigation rails are safe and the reconnaissance's "BREAKS VISIBLY" finding is wrong.** Aero's
`ListBox` template binds `Background="{TemplateBinding Background}"` and
`BorderThickness="{TemplateBinding BorderThickness}"` on **both** its outer and inner `Border`
(VENDORED, `ControlStyles/ListBox.axaml`, read in full), and `NavRail`
(`MainWindow.axaml:358-359`) and `NavFooterRail` (`:501-502`) both set `Background="Transparent"` and
`BorderThickness="0"` as **local values**, which outrank a `ControlTheme` setter. The sunken two-ring
white box cannot appear. Verification step 6 checks it anyway.

**Prose rewritten here:** the five `ToggleSwitch.tm-card-toggle` call sites, because Aero's pill is
`Width=56 Height=22 CornerRadius=10` against Fluent's 40×20 (VENDORED) — 16 DIP wider and 2 taller. The
`Margin="0,0,-12,0"` compensation itself **survives**: Aero copies Fluent 11's inner grid verbatim,
`<ColumnDefinition Width="12" MaxWidth="12" />` included, identical to Fluent 12.1's
`Controls/ToggleSwitch.xaml:64`, and the margin cancels the column, not the pill.

#### Why

These eight files touch nothing our own layer depends on, so they can land as one coherent visual step
rather than eight incoherent ones. `ScrollBar` and `ScrollViewer` travel together; `RepeatButton` is in
the Button file and `ScrollBar` needs it; `ListBox` and `ListBoxItem` travel together and are the
prerequisite for Task 8's pane.

`Button` is the honest test of the whole port: it is the control with the most instances (21 in XAML +
1 in code), and `AppPage.axaml:62` (`Button Classes="tm-update"`) is the **only** `Button` in the tree
with no `Theme=`, so it is the one place an Aero `Button` template lands under a `tm-` class.

#### Blast radius

**Files:** 8 vendored + `AeroResources.axaml` + `Launcher.axaml` (`TmAccentButton` deleted, toggle
prose rewritten) + `AppPage.axaml` + `BehaviorCard.axaml` (theme swap on two buttons).

**Fit terms:** none directly. But `ScrollBar` goes from Fluent's `ScrollBarSize = 16` (Fluent 12.1
`Accents/FluentControlResources.xaml:701`, `:1549`) to Aero's `AeroScrollBarThickness = 18` expanded /
`AeroSmallScrollBarThickness = 9` collapsed (VENDORED), which affects `AppPage.axaml:97`
(`HorizontalScrollBarVisibility="Auto"`, so a horizontal bar appears under the tile grid),
`SettingsPage.axaml:50` and `ToolsCard.axaml:45` (`MaxHeight="240"`), and it changes `Ph` on the 应用
page. Re-measure `Ph` in this task's verification.

**Tests:** none change. All 147 stay green.

**Invariants:** the settings row spec's trailing-control column re-measures; the "one disabled
vocabulary per page" decision is untouched until Task 4.

#### Verification

1. `dotnet build -c Release` clean — **this is the answer to "do the vendored `{Binding}` expressions
   compile under compiled-bindings-by-default"**, for `ListBox`/`ListBoxItem`'s
   `{Binding #OuterBorder.CornerRadius, Converter=…, Mode=OneWay}`, `ScrollViewer`'s three
   `ElementName=PART_ContentPresenter` forms and `ScrollBar`'s seven `RelativeSource=TemplatedParent`
   forms.
2. `dotnet test` → 147. `dotnet publish`; IL count unchanged (these eight files contain **zero**
   `{ReflectionBinding}`).
3. Log: `page {W}x{H}` — record the new `Ph`, which moves with the scrollbar and the toggles.
4. UIA `BoundingRectangle` on the five `ToggleSwitch`es: width grew by 16 DIP × scale, height by 2.
5. UIA on `ToolsCard`'s checkboxes: present, and their rects are non-degenerate (the code-constructed
   ones are the easiest to lose).
6. `PrintWindow` Light and Dark, 应用 and 设置. On the 应用 capture, sample the four pixels immediately
   inside `NavRailPane`'s bounds: they must equal the backdrop, **not** `AeroListBoxBackground`
   (`#FFFFFF` / `#4A4A4A`) — the rails' local `Transparent`/`0` held.
7. Sample a `TmButton`'s fill: it is our own `ControlTheme`, so it must be **unchanged** from Task 2's
   capture. Sample `AppPage.axaml:62`'s update button: it is the one with no `Theme=`, so it must have
   **changed** to the Aero gradient.
8. Keyboard focus on an Aero button: drive focus with `SetFocus` on the UIA element (no keyboard),
   `PrintWindow`, and confirm a focus indication exists. If none does, add the §3.4 dotted-rectangle
   `FocusAdorner` to the vendored `Button` theme **in this commit**.

---

### Task 4 — TextBox, NumericUpDown, ButtonSpinner, and the five broken selectors

#### Decision

Vendor `ControlStyles/TextBox.axaml` (17,987 B), `NumericUpDown.axaml` (8,327 B),
`ButtonSpinner.axaml` (13,069 B), with §6 headers and:

- the six `Watermark` / `UseFloatingWatermark` / `WatermarkForeground` uses renamed to
  `PlaceholderText` / `UseFloatingPlaceholder` / `PlaceholderForeground` (`TextBox.axaml:145, :158,
  :226`; `NumericUpDown.axaml:27, :37, :120`). They are `[Obsolete(…, false)]` — warnings, not errors,
  and 12.1's `Watermark` getter is literally `return PlaceholderText;` — but this project does not ship
  a warning it can delete in six edits;
  > **CORRECTED after Task 4 shipped (`87dd54f`):** it is **twelve** uses of **two** properties, not
  > six of three. `TextBox.axaml` has eight (`Watermark` ×5 at upstream `:12, :13, :14, :145, :158`,
  > `UseFloatingWatermark` ×3 at `:13, :14, :226`) and `NumericUpDown.axaml` four (`Watermark` at
  > upstream `:27, :37` and both halves of `:120`). **`WatermarkForeground` appears in neither file**
  > (grep, zero hits), so nothing was renamed to `PlaceholderForeground`. Each vendored file's own
  > change list is the record; both were diffed against the upstream copy and contain exactly these
  > edits and nothing else. `Design.PreviewWith` uses count — they compile like any other markup;
- `NumericUpDown.axaml:12-13`'s two **unresolvable** `{DynamicResource HighlightBrush}` /
  `HighlightForegroundBrush` re-pointed to `AeroTextBoxSelectionBrush` /
  `AeroTextBoxSelectionForegroundBrush`, which `TextBox.axaml` already defines and which are in the
  colour closure. Neither original key exists in Fluent 12.1's 1,150 keys, nor in 11.0.10 — a
  pre-existing upstream bug whose effect is silent;
- `ButtonSpinner.axaml:194`'s dead `#PART_TextPresenter` rule deleted (§5.5).

**In the same commit, the five selectors of §5.2**: `Launcher.axaml:515` deleted;
`:1222, :1228, :1231, :1239` re-targeted `PART_BorderElement` → `BgElement`.

`TextBox.axaml` also redefines five named Fluent keys — `DefaultTextBoxContextFlyout`,
`TextBoxClearButtonData`, `PasswordBoxRevealButtonData`, `PasswordBoxHideButtonData`,
`TextBoxTopHeaderMargin`. **All five are kept**, including the context flyout: replacing the app-wide
text-box context menu (剪切/复制/粘贴) with Aero's is a behaviour change, and it is the *correct* one for
this round — but it is stated here rather than discovered, and it is item 1 of that file's change list.

#### Why

These three travel together (`NumericUpDown` requires `PART_TextBox` and `PART_Spinner`;
`ButtonSpinner` is that spinner) and they are the only vendored files that break rules we own. Landing
them apart from the five selector fixes would ship a commit where the hotkey box silently reverts to
stock paint in all four states — the exact silent regression this round exists to prevent.

`Launcher.axaml:515` is **deleted rather than re-targeted** and that is the interesting call. Its
17-line write-up says it exists specifically to beat Avalonia's own `^:focus /template/
Border#PART_BorderElement` accent band. Aero's TextBox has no accent focus band — it repaints
`BgElement`'s `BorderBrush` to `AeroTextBoxFocusBorderBrush`, which is the focus signal we *want*. The
rule becomes both a no-op and unnecessary, and a re-targeted version would suppress the one focus
signal Aero has.

#### Blast radius

**Files:** 3 vendored + `AeroResources.axaml` + `Launcher.axaml` (5 selectors, 2 prose blocks).

**Fit terms:** none. **Tests:** none change; `NoTemplatePartSelector_NamesAPartOutsideTheAllowList`
(added in Task 1) is the test that would have caught the four dead re-targets, and it must be green
**before and after**.

**Prose:** `Launcher.axaml:498-514` deleted (bucket A); `:1180-1201` rewritten (bucket B).

#### Verification

1. `dotnet build -c Release` — **zero `[Obsolete]` warnings**, which is the point of the six renames.
2. `dotnet test` → 147, including `NoTemplatePartSelector_…`.
3. `dotnet publish`; IL count unchanged.
4. UIA `BoundingRectangle` on `HotkeyBox` (`BehaviorCard`) at four states driven without input:
   resting; `IsEnabled=false` via the master toggle's existing programmatic path; focused via UIA
   `SetFocus`. `PrintWindow` each and sample the border's pixel row: resting and disabled must **differ
   from each other** and each must match the token the re-targeted rule sets. If the four rules had
   stayed dead, all four states would sample identically to Aero's stock paint — that is exactly what
   this step distinguishes.
5. `SearchBox` focused via UIA `SetFocus`, `PrintWindow`: exactly **one** focus signal on screen,
   Aero's border-brush change. No accent band anywhere in the field's rect.
6. Both variants.

---

### Task 5 — ProgressBar, determinate only

#### Decision

**Do not vendor `ControlStyles/ProgressBar.axaml` whole.** It is 26,806 bytes / 471 lines to serve one
progress bar we never set `IsIndeterminate` on (`grep -rn IsIndeterminate src tests` → 0 hits), and it
carries the port's only two `{ReflectionBinding}`s plus nine style rules targeting three names that are
never declared (§5.5).

Author `Themes/Aero/ControlStyles/ProgressBar.axaml` as the **determinate branch of the vendored file**
— roughly 60 of its 471 lines — carrying the §6 header with "Changes from upstream" reading: dropped
`ProgressBarAnimation`, `ProgressBarGlowPattern`, `StorageBar`, `LayoutTransformControl`, 33 of 35
bindings, 3 of 4 converters, and the 9 dead indeterminate rules; converted the 2 `{ReflectionBinding}`s
at `:255-256` to `{Binding #PART_Indicator.Bounds.Width|Height, Mode=OneWay}` (the idiom the same file
already uses four lines away). The two converters that survive are
`ProgressBarFillBrushConverter` and `ProgressBarFillColorConverter`;
`ProgressBarAnimationConverter.cs` is deleted from `Themes/Aero/Converters/`.

> **CORRECTED after Task 5 shipped. The last sentence is wrong; the code is the authority.**
> **ONE** converter survives, `ProgressBarFillBrushConverter`, and **two** `.cs` files were deleted —
> `ProgressBarAnimationConverter.cs` *and* `ProgressBarFillColorConverter.cs`. The paragraph
> contradicts itself: "3 of 4 converters" dropped leaves one, not two, and the "3 of 4" half is the
> correct one. Of the four converter references in the upstream file, the three `x:Key`'d resource
> instances (`StringFormatConverter`, `EnumToBoolConverter`, `ProgressBarFillColorConverter`) go and
> the `{x:Static …Instance}` one stays. `ProgressBarFillColorConverter`'s **only** consumer anywhere
> was `ProgressBarGlowPattern`, which this same paragraph drops, so keeping the file would have
> shipped a class with zero call sites into a tree whose rule is that dead code is deleted. Note the
> dependency runs the other way round from what the sentence implies: `ProgressBarFillColorConverter`
> calls `ProgressBarFillBrushConverter.TryGetColorFrom`, not the reverse, so deleting it leaves the
> survivor intact. `Themes/Aero/Converters/` now holds two files, `AdjustCornerRadiusConverter.cs`
> and `ProgressBarFillBrushConverter.cs`, and both have live call sites.
>
> **Two more things Task 5 falsified, both in §5.5's row for this file.** (a) §5.5 counts
> ":156, a `{Binding #GlowClipHelper.Bounds}` to a name that does not exist" as a live defect. It is
> not live: upstream `:156` is inside the XML comment that opens at `:152` and closes at `:158`, so
> it never bound anything. The nine style rules §5.5 lists are real and all nine are gone. (b) §5.5
> does not mention `ShineBorder`'s `CornerRadius="{Binding #PART_Indicator.Margin}"` at `:254`, which
> is a defect of the same class: it assigns a `Thickness` to a `CornerRadius`, and
> `TypeUtilities.TryConvert` has no path between those two types in Avalonia 12.1 (neither declares a
> conversion operator to the other and neither's `TypeConverter` accepts the other — all four
> decompiled). It compiles and can never resolve. Task 5 dropped it; the vendored file's change list
> is the record.

**`Views/Settings/UpdatesCard.axaml:57`'s `Height="4"` becomes 15.** **PUBLISHED**, `vis-layout`:
*"progress bar height 15"* (160 or 355 × 15 relative pixels). At 4 DIP Aero's two nested 1 px frames
(`ProgressBarRoot` radius 3 inside `InnerBorder` radius 2) consume the entire bar; 15 is the height the
shape was drawn for and it is Microsoft's own number.

The `AppPage.axaml:74-88` inline download bar (a hand-drawn `Border`-in-`Border` at `Height="4"` using
`SubtleFillTertiaryBrush` + `AccentDefaultBrush`) is **replaced by a real `ProgressBar`** in this
commit, because `SubtleFillTertiaryBrush` is deleted in Task 7 and a hand-drawn Fluent bar beside a real
Aero one is exactly the incoherence this round is removing.

#### Why

`ProgressBar.TemplateSettings` is settled — all six members Aero binds exist in Avalonia 12.1
(`ContainerWidth`, `Container2Width`, `ContainerAnimationStartPosition`,
`ContainerAnimationEndPosition`, `Container2AnimationStartPosition`,
`Container2AnimationEndPosition`) — so vendoring whole would *work*. It would just carry 400 lines of
machinery whose selectors provably cannot match, into a project whose stated rule is that a selector
matching nothing is a bug. Writing the determinate branch is smaller, and it is the change that takes
the round's entire NativeAOT liability to zero.

#### Blast radius

**Files:** 1 authored vendored file + `AeroResources.axaml` + `UpdatesCard.axaml` + `AppPage.axaml` +
`Themes/Aero/Converters/` (one deleted).

**Fit terms:** none, but `Ph` on the 应用 page changes when the update banner is open (the bar goes
4 → 15 DIP), which is re-fit trigger 2. Re-measure.

> **CORRECTED after Task 5 shipped: `Ph` does not change, and the re-measurement is what says so.**
> Both builds were run at `Downloading` 45 % on this machine at 150 %, everything else identical.
> Pre-Task-5 `AppPage.axaml`: `page 1708.6666666666667x602`, banner button UIA rect
> `2631,1290,2515x56`. Post-Task-5: `page 1708.6666666666667x602`, banner rect `2631,1290,2515x56` —
> **identical in both numbers.** The banner is a `DockPanel`, so its height is its tallest child's,
> and that child is the text row (56 px = 37.33 DIP outer), not the bar (23 px = 15.33 DIP). A bar of
> 4 DIP and a bar of 15 DIP both sit inside it. The 54 DIP the banner adds to `Ph` (548 closed → 602
> open, also measured) is therefore the same before and after.

**Tests:** none change.

#### Verification

1. Build, test (147), publish — **IL2xxx/IL3xxx count for `{ReflectionBinding}` is now provably zero
   for the whole tree**: `grep -rn ReflectionBinding src` returns nothing.
2. Drive the update state machine through its existing programmatic path (no mouse) to
   `Downloading` at three progress values; `PrintWindow` each; the indicator's ink width scales with
   the value and the two nested frames are both visible at 15 DIP.
3. Log line: `Window fit to content` fires on the banner opening (re-fit trigger 2) and `Ph` grows by
   the banner's new height.
4. Both variants.

> **CORRECTED after Task 5 shipped: steps 1 and 2 both name something that does not exist.**
> Step 1's literal test is not met and cannot be: `grep -rn ReflectionBinding src` returns **two**
> hits, both PROSE inside XML comments — `AVMAP/StyleTrailMix.axaml`'s header, which names the
> escape hatch it did not need, and this file's own change list, which has to say what it converted.
> The live count is what matters and it is **zero `{ReflectionBinding}` markup extensions in the
> tree**, with `IL2xxx`/`IL3xxx` still at zero in the publish log.
>
> Step 2 says "existing programmatic path". **There is none.** `VelopackAppUpdateService` reaches
> `Downloading` only from a real GitHub download, `FixedAppUpdateService` pins `Progress` at 0 and
> never raises `StateChanged`, and nothing reads an environment variable or a debug switch. Task 5
> verified it by TEMPORARILY patching two files — a `FixedAppUpdateService(Downloading)` branch in
> `App.axaml.cs` and an env-var `Progress` in `AppUpdateService.cs` — capturing 0 / 45 / 100 % in
> both variants, and then restoring both files, confirmed byte-identical by `git hash-object`. A
> later task that needs this again should expect to do the same.

---

### Task 6 — ComboBox and ComboBoxItem, authored

#### Decision

**There is no Aero ComboBox to copy.** All 9 upstream branches were queried via the GitHub trees API
for any `ComboBox*.axaml`: **zero hits on every branch**. `AVMAP/Controls.axaml` mentions them only
inside a comment block pointing at an assembly that does not exist. The author has no other Aero repo.

Author `Themes/Aero/ControlStyles/ComboBox.axaml` and `ComboBoxItem.axaml`. **The contract is
minimal**: `ComboBox` declares `[TemplatePart("PART_Popup", typeof(Popup), IsRequired = true)]` and
`[TemplatePart("PART_EditableTextBox", typeof(TextBox), IsRequired = false)]`, its `ItemsControl` base
finds `PART_ItemsPresenter`, and `ComboBoxItem` declares **no** `[TemplatePart]` at all.
`PART_EditableTextBox` is omitted — `IsEditable` appears nowhere in our tree.

**The closed state is built from the library's own `ButtonSpinner.axaml`**, which is the in-repo
precedent for exactly this shape (a text-field body with attached Aero-button chrome and a chevron),
with its corner joinery already worked out (all VENDORED):

- body `Border x:Name="BgElement"`, `Background={AeroTextBoxIdleBackground}`,
  `BorderBrush={AeroTextBoxIdleBorderBrush}`, `BorderThickness=1`,
  `CornerRadius={AeroControlCornerRadius}` (3), `Padding=3`, `FontSize={AeroFontSizeNormal}` (12);
- the drop-down button is a `ToggleButton` whose `Theme` is
  `BasedOn="{StaticResource {x:Type ToggleButton}}"` — the unmodified Aero button gradient — with
  `Padding="6,3"`;
- the chevron is `M 0 0 L 3.5 4 L 7 0` as a `PathIcon` at `Width=7 Height=4`;
- joinery by Avalonia's own `CornerRadiusFilterConverter Filter="TopRight,BottomRight"` and
  `MarginMultiplierConverter Indent=1 Left/Top/Bottom=True Right=False`.

**The popup needs nothing new**: its surface is Aero's `ListBox` and its rows are Aero's `ListBoxItem`,
both already in the tree from Task 3.

Height **23 DIP**, **PUBLISHED** (`vis-layout`: *"combo box / drop-down / text box height 23"*).
`MaxDropDownHeight="320"` on `FontCombo` must be honoured by the template's popup, or the font list
stops scrolling at the wrong size.

**A disabled `ComboBoxItem` must keep the caller's `Foreground`.** `AppearanceCard.axaml.cs:140-150`
builds group captions as `IsEnabled=false` + `FontWeight=Bold` + `Foreground=#777777` +
`Margin=0,3,0,0`. Our `ComboBoxItem` template names its presenter something **other** than
`PART_ContentPresenter`, which removes the `StyleTrailMix` collision structurally rather than by the
narrowing Task 3 applied as a stopgap.

**One thing is a judgement call and is recorded as one.** Whether a Windows 7 *non-editable* drop-down
list painted the whole control with the button gradient, or a white field plus a gradient button, is
**not stated by any Microsoft page** — the `ctrl-drop` page's own sizing figure lives inside an image
(`images/ctrl-drop-image22.png`) that cannot be read as text, and there is no msstyles and no reference
capture. **AUTHORED: white field plus gradient button**, because that is the shape the library's own
`ButtonSpinner` and `NumericUpDown` use for the same composition, and internal consistency with the
vendored vocabulary is a better argument than a memory.

`ComboBoxMinHeight` and `ComboBoxPadding` are plain Fluent keys; **they are not used as a stopgap**.
Task 3 through Task 5 leave the three combos on stock Fluent, which is visibly wrong but correct, and
this task replaces them outright rather than retuning something we are about to delete.

#### Why

It is the only live coverage gap, at exactly three sites (`AppearanceCard.axaml:35, 47, 63`), all
non-editable, all `Width="220"`. Authoring it after Tasks 3–5 means the popup's `ListBox`/`ListBoxItem`
and the field's `TextBox` vocabulary already exist, so this task writes composition, not paint.

#### Blast radius

**Files:** 2 authored + `AeroResources.axaml` + `AppearanceCard.axaml.cs` (the group-caption
`Foreground` survives as-is, but the `#777777` literal is re-derived to `AeroDisabledForeground`) +
`StyleTrailMix.axaml`'s narrowing from Task 3 can now be reverted to upstream's form, and is.

**Tests:** none change. **Fit terms:** none.

#### Verification

1. Build, test (147), publish.
2. UIA on `LanguageCombo` / `AppearanceCombo` / `FontCombo`: `BoundingRectangle` height = 23 DIP ×
   scale on all three; width 220 × scale.
3. Open each popup with UIA `ExpandCollapsePattern.Expand()` (no mouse). `PrintWindow` each.
   `FontCombo`'s popup height ≤ 320 DIP × scale.
4. In `FontCombo`'s popup, UIA-read the group-caption rows: `IsEnabled == false`, and their sampled ink
   colour equals `AeroDisabledForeground` (`#7F7F7F`) and **not** the enabled rows' colour.
5. UIA `SelectionPattern` to change a selection programmatically; confirm `SelectionChanged` fires by
   the settings write appearing in `config.json`. This proves the authored template did not break the
   control's behaviour, which is the failure mode an authored template actually has.
6. Both variants.

---

### Task 7 — The token layer and the shell's surfaces

#### Decision

Execute §3.2 in full: 9 aliases, 11 re-values, 23 deletions, 6 new keys. `Tokens.axaml` ends at 29
keys.

Then re-author every `.tm-*` rule in `Launcher.axaml` whose value moved. Concretely:

| rule | change |
|---|---|
| `Border.tm-page-pane.app` | `SurfacePageBrush` → the alias; `CornerRadius` `{RadiusPaneTopLeft}` → **`0`**; `BorderThickness` stays `1,1,0,0` — Explorer's own pane/list divider |
| `Border.tm-page-pane:not(.app)` | unchanged in form; the Round 6 item 2 decision (every non-应用 page shows the window material) **survives**, because Explorer's property pages did the same |
| `Border.tm-card` | fill and radius **deleted**; a card becomes a bare row on the group box's surface |
| `Border.tm-toast` | `StrokeSurfaceFlyoutBrush` → `AeroGroupBoxBorderBrush`; `BoxShadow` **deleted** |
| `Border.tm-infobar` | re-derived to the group-box vocabulary |
| `TextBlock.tm-card-title` | `FontSizeBody` 12 |
| `TextBlock.tm-card-sub` | `FontSizeBody` 12 at `Opacity="0.6"` (§3.2 #8) |
| `TextBlock.tm-page-title` | `FontSizeSubtitle` 16, `Foreground={HeadingBrush}` (#003399 **PUBLISHED**) |
| `TextBlock.tm-group-head` | `FontSizeGroupHead` 15, `Foreground={HeadingBrush}` |
| the 5 `FocusAdorner`s | two-ring → the §3.4 dotted `Rectangle` |
| every spacing literal | re-derived onto the new 5 / 7 / 11 ladder |

The 147-line Mica-straddle argument at `Launcher.axaml:722-868` is deleted (bucket A) and its
measurements archived (bucket C). Its thesis — *"Mica measures 238..247 Light and 30..34 Dark and
therefore straddles `SurfaceBaseBrush`, so card separation survives"* — has no subject left: there is no
Mica and there are no card fills.

#### Why

This is the commit where the app stops looking like Fluent 2. It comes after the controls, not before,
because a re-derived token layer under Fluent controls produces Aero-coloured Fluent — the same
incoherence in the other direction, with the added cost that every token would have to be verified
twice.

The spacing re-derivation is the correction to the feasibility document's "6 survive". 4/8/12/16/20 is
a Fluent ramp; `vis-layout` publishes 11 / 7 / 5 and publishes nothing at 16 or 20. Deleting `Space4`
and `Space5` rather than re-valuing them is deliberate: an unpublished rung is a rung somebody will
cite later as if it were measured.

#### Blast radius

**Files:** `Tokens.axaml` (rewritten), `Launcher.axaml` (most of its 485 markup lines and ~400 of its
prose lines), `AppPage.axaml`, `SettingsPage.axaml`, all 6 `Views/Settings/*.axaml`,
`Controls/SplashPanel.axaml`, `Controls/InfoBar.axaml`, `Dialogs/MessageBoxWindow.axaml`.

**Fit terms:** `Ph` and `Pw` both move substantially — the type ramp drops 14 → 12 and the padding 16 →
11. Re-measure and record.

**Tests:**
- `LauncherStyles_ContainNoColourLiterals` — **the highest-risk test in the round**, because it greps
  prose. Every Aero colour in a new comment must be a decimal triple.
- `EveryTokenTheLauncherReferences_IsDefinedInTokens` — the widening from Task 2 is what makes the
  alias layer resolvable.
- `NoCardFile_RestatesAConstantTheThemeOwns` — re-pointed here.
- `GroupPanel_CarriesTheVisibilityBinding` — green, because the class name is kept.

#### Verification

1. Build, test (147), publish.
2. `PrintWindow` Light and Dark, 应用 and 设置, scrolled to top and bottom.
3. Sample and record, per variant: the window surface, the 应用 pane, a card row's background (which
   must now equal the group box's surface — the card fill is gone), the page title's ink (`#003399` in
   Light), a subtitle's ink (60 % of `AeroForeground` over the page).
4. Scan `Border.tm-page-pane.app`'s top-left corner: the arc is **gone**, the first ink column is
   constant down the first 12 rows.
5. UIA `BoundingRectangle` on one settings row's four elements; the gaps read 5 / 7 / 11 × scale.
6. Log: `page {W}x{H}` and the resulting window geometry, recorded as the new baseline.

---

### Task 8 — The navigation pane

#### Decision

**R = 160 DIP. AUTHORED, and derived rather than picked.** The derivation, stated in the pane's own
comment:

- **PUBLISHED**, `vis-layout`, the only sizing statement the guide makes about trees:
  *"Tree views | width of longest item + 30% x an integral number of items (5 items minimum)"* — a
  formula, no pixels. The relative-pixels cell is blank.
- Longest label in either language: `打开主目录` (5 CJK glyphs ≈ 60 DIP at 12 DIP type) and
  `Open root folder` (≈ 88 DIP). +30 % → ≈ 114 DIP.
- Row structure: `Margin` 2 + `Padding` 8 + icon 16 + gap 6 = **32 DIP** before the label, and
  8 + 2 = 10 DIP after it. Label column = 160 − 32 − 10 = **118 DIP** ≥ 114. ✓
- **No indent.** **PUBLISHED**, `tree-view-control-window-extended-styles`:
  *"TVS_EX_NOINDENTSTATE — [Windows Vista]. Do not indent the tree view for the expando buttons."*
  The indent exists to make room for the expando; our rows have no children, so we are exactly that
  case. The guide's one published indent figure (*"dependent controls are indented 12 DLUs or 18
  relative pixels"*) is quoted only to say why it is not used.
- Row box **22 DIP** (16 icon + 2 × 3 padding), pitch **24 DIP** with `Margin="2,1"`. Both even, and
  even is a citation: **PUBLISHED**, `tvm-setitemheight`: *"If this argument is not even and the
  tree-view control does not have the TVS_NONEVENHEIGHT style, this value will be rounded down to the
  nearest even value."*

**The rows.** `TmNavRowItem` is rebased: `<ControlTheme x:Key="TmNavRowItem" TargetType="ListBoxItem"
BasedOn="{StaticResource {x:Type ListBoxItem}}">` with four setters —
`HorizontalContentAlignment="Left"`, `Padding="8,3"`, `Margin="2,1"`, and nothing else. Its whole
template, its `Border#Root`, its `Border#SelectionIndicator` and its 55-line Task Manager preamble are
deleted. It inherits Aero's four states for free: `:pointerover`, `:selected:not(:pointerover)`,
`:selected:pointerover`, `:disabled` — **one more state than we have today**, and none of them uses the
accent.

`TmNavRowButton` keeps its own full `ControlTheme` but its template is rebuilt to Aero's shape
(`Panel` → `Border#OuterBorder` → `Border#InnerBorder`, plus a `ContentPresenter`) with **only**
`:pointerover` and `:pressed`, using the same `AeroListBoxItemHover*` brushes. **The Button-vs-ListBoxItem
split survives and becomes more load-bearing, not less**: under Aero's four-state row, a command row
rendered as a `ListBoxItem` would gain a selected gradient it must never show, and *"a Button has no
`:selected` pseudoclass at all"* stays true by construction.

**Labels come back.** `ApplyRailText` (`MainWindow.axaml.cs:740-763`) goes from ten writes to ten
writes of a different shape: the five `AutomationProperties.Name` calls **stay** (they are the
accessibility contract and are cheaper than reasoning about what Avalonia derives from a label), the
five `ToolTip.SetTip` calls **go**, and five `TextBlock.Text` writes replace them. Microsoft's glossary
settles it: *"tooltip — A small pop-up window that labels the **unlabeled** control being pointed to"*.
A tooltip that repeats a visible label is by Microsoft's own definition the wrong control.

**Each row becomes `DockPanel { TextBlock.tm-icon Dock=Left Width=16; TextBlock.tm-nav-label }`** with a
6 DIP gap. The local `Width="16"` on each glyph **stays** — it is what stops Segoe's per-glyph advance
from de-aligning the icon column — but its derivation changes from *"exactly fills the row's content
width"* to *"the icon column of an icon-plus-label row"*, and the citation that replaces the Task
Manager one is **PUBLISHED**, `vis-layout`: *"A control size of 16x16 relative pixels is a good minimum
size for any input device."*

**The footer ordering does not change.** 打开主目录 · 刷新 · 设置 · separator · 退出.
退出 is last because the user asked for it; that is not re-litigated. `Border.tm-nav-separator`'s
`Margin` becomes `2,4` — the horizontal 2 re-derived from the rows' new `Margin="2,1"`, so the line
spans exactly the **156 DIP** item column (160 − 2×2). The footer measures **109 DIP**: four rows at
the 24 DIP pitch, plus the 9 DIP separator block (1 + 2×4), plus the footer's own 4 DIP bottom inset.
It was 173.

**`Grid.tm-nav-rail TextBlock.tm-icon`** (`Launcher.axaml:669-671`) lifts every rail glyph to
`TextPrimaryBrush`. Its justification (Task Manager solving to alpha 0.893 black) is void; the rule is
**kept and re-derived** to `AeroForeground` — Aero's rail glyphs and its rail labels are the same ink.

**`MinWidth` 760 → 872** in both places, and `Width` / `PlaceholderWidthDip` 1000 → 1112 (§2.1).

#### Why

Decision 3, and the one place the design has to be honest that it is authoring rather than measuring.
Every published fact that bears on it is used; nothing is invented that a source covers.

The two-widths reading has to be foreclosed explicitly, because "wide labelled pane" reads as
"expandable pane" to anyone who remembers Round 4 item 1. Re-adding a second width would bring back the
`DoubleTransition`, the `ClipToBounds`, the config read for the fit term and the sixth re-fit trigger —
four deletions Round 4 made deliberately. **One width, just not 48.**

#### Blast radius

**Files:** `MainWindow.axaml` (the pane, both rails, all five rows), `MainWindow.axaml.cs`
(`RailWidthDip`, `ApplyRailText`), `Services/WindowHost.cs` (`MinWindowWidthDip`,
`PlaceholderWidthDip`), `Themes/Fluent2/Launcher.axaml` (`TmNavRowItem`, `TmNavRowButton`,
`Border.tm-nav-separator`, `Grid.tm-nav-rail TextBlock.tm-icon`, a new `TextBlock.tm-nav-label`),
`FoldersCard.axaml` (premise line).

**Fit terms:** **R** 48 → 160, so `chromeWidth` 49 → **161**. `Wmin` 760 → **872**, holding the content
pane's floor at **711 DIP**, unchanged.

**Tests:** `RailWidth_MatchesTheRailPaneWidth` and `MinWindowWidth_IsTheSameNumberInBothPlaces` (both
added in Task 1) are the guards that make this task's four-copy edit safe. All 23
`WindowPlacementTests` stay green — and that is precisely why those two exist.

**Prose:** four blocks deleted, five rewritten (§4).

#### Verification

1. Build, test (147). The two constant-mirror tests are the ones that matter here.
2. Log: `chrome 161x31`.
3. UIA `BoundingRectangle` on `NavRailPane`: width 160 × scale. On each of the five rows: height 22 ×
   scale, pitch 24 × scale, left edge 2 × scale from the pane, and each row's label `TextBlock` present
   with a non-empty `Name`.
4. UIA on the five rows: `ToolTip` absent, `AutomationProperties.Name` present and equal to the visible
   label. Repeat after switching 语言 through its existing programmatic path — `ApplyRailText` is
   called from `_events.SettingsChanged` and both the label and the automation name must follow.
5. `PrintWindow`, sample the separator's ink row: it spans exactly 156 DIP × scale, starting 2 DIP ×
   scale from the pane's left edge.
6. Select 设置 via UIA `SelectionItemPattern.Select()` (no mouse), `PrintWindow`: the selected row shows
   Aero's two nested borders over a gradient, and **no pixel in it equals `AccentDefaultBrush`**. Hover
   cannot be driven without a mouse, so the `:selected:pointerover` state is verified by a style-sheet
   read, not by a capture, and is recorded as such.
7. Resize to the new floor with `SetWindowPos` (no mouse): window 872 × scale wide, content pane
   711 DIP × scale. Re-run the `FoldersCard` wrap check in Chinese **and** English at that width, which
   is what makes the four rewritten measurement records true again.
8. Both variants.

---

### Task 9 — The 应用 page

#### Decision

**The category panel keeps its class and loses every value.** `Border.tm-group-panel` becomes an Aero
group box: `Background="Transparent"`, `BorderBrush={AeroGroupBoxBorderBrush}`, `BorderThickness=1`,
`CornerRadius={AeroControlCornerRadius}` (3, **VENDORED**, against the deleted `RadiusPanel`'s 8 —
*"wrong by a factor of two-and-a-half against the vendored vocabulary"*), `Padding` re-derived onto the
11 / 7 ladder. The class name is kept so `GroupPanel_CarriesTheVisibilityBinding` never moves.

The vendored `ControlStyles/HeaderedContentControl.axaml` (3,986 B) lands here, exposed under its
opt-in `GroupBox` key, and the **settings** page's five groups adopt it (§1). The 应用 page's category
panels stay a hand-authored `Border` because their heading is already a separate, filterable element.

**The content-sized tile rule survives; the look does not.** `TmTileButton` sets no `Width` (Round 4
item 3) and `AppPage.axaml:211-222` sets `TextWrapping="NoWrap"` with no trimming. That is a layout
rule, not a Fluent idea, and it is what makes the unbounded fit probe answer both axes.
**`vis-layout`'s 75 × 23 command-button minimum is NOT applied to tiles.** A tile is not a command
button — it is an icon-plus-label launcher, closer to a large-icon list-view item, and a 75 DIP floor
would widen the narrowest categories for no published reason. Recorded as a decision, with the number
that was rejected.

**The search field takes its Aero form.** Height **23 DIP** (**PUBLISHED**, `vis-layout`), a 9 DIP
reduction from `Border.tm-searchfield`'s `MinHeight="32"` — the single most Aero-legible metric change
in the field. The magnifier `&#xE721;` moves from the **left** to the **right** end, and becomes an
`&#xE711;` "x" once text exists: **PUBLISHED**, `ctrl-search-boxes`: *"In Instant search mode, a clear
button appears after the user starts typing; an 'x' replaces the magnifying glass search symbol. On
hover, the 'x' gets a button look and can be clicked"*, captioned *"…at the right end of the control"*.
`Border.tm-searchfield-focusbar` and its two rules are **deleted** — there is no accent underline in
Aero, and Task 4 already established that Aero's TextBox draws its own focus.

**The prompt behaviour is documented and deliberately not fully implemented.** **PUBLISHED**,
`ctrl-text-boxes`: *"Draw the prompt text in italic gray"*, and *"The prompt text … should disappear
once users click in or tab into the text box. Exception: If the text box has default input focus, the
prompt is displayed, and it disappears once the user starts typing."* Avalonia's `PlaceholderText`
disappears on **text**, not on **focus**. Our field has default input focus on show
(`ConsumePendingFocus`), which lands us on the *exception* branch, so the common path coincides
exactly; it diverges only after a tab-away-and-back with the field empty. **We set
`FontStyle="Italic"` on the placeholder and accept the focus divergence**, stated in one sentence in
the field's comment. Building a second element bound to `IsFocused || HasText` to fix a divergence
nobody reaches is not worth a control.

**`Ctrl+E` is added** as the search accelerator. **PUBLISHED**, `ctrl-search-boxes`: *"Assign the
keyboard shortcut for the Search box to be Ctrl+E."* And **PUBLISHED**, same page: *"On input focus,
automatically select any previously entered text"* — one line in the existing `GotFocus` handler.

The update banner's `Margin="16,16,16,0"` becomes `11,7,11,0`: its top 16 was chosen because the strip
was in the window, not in the page, and now there is a header above it.

#### Why

The 应用 page is the app, and it is the last thing that still carries Windows 11 measurements after
Task 7. Doing it last means every token and every control it composes is already settled, so this task
is composition and citation rather than discovery.

#### Blast radius

**Files:** `Views/AppPage.axaml` + `.axaml.cs`, `Themes/Fluent2/Launcher.axaml`
(`Border.tm-group-panel`, `Border.tm-searchfield*`, `TmTileButton`, `Border.tm-header`),
`Views/SettingsPage.axaml` + the six cards (group boxes), one vendored file.

**Fit terms:** `Pw` and `Ph` both move — the tiles re-measure at 12 DIP type, the header settles at its
final height, the group boxes' padding changes. This is the task after which the predicted geometry in
§8 is checkable.

**Tests:** `GroupPanel_CarriesTheVisibilityBinding` green (class kept). The three surviving
`SettingsRowTests` green (markup-shape tests).

**Prose:** two blocks deleted, one rewritten (§4).

#### Verification

1. Build, test (147), publish.
2. UIA on `SearchField`: height 23 × scale, width 320 × scale, horizontally centred in the page
   (`|left gap − right gap| ≤ 1 px`).
3. Set `Filter` through the view model (no keyboard) and `PrintWindow`: the right-end glyph changes from
   the magnifier to the "x". Clear it; it changes back.
4. UIA on the category panels: `panels.Count` matches the visible-category count; filter to zero
   matches through the view model and confirm no empty 22 DIP box appears — the finding
   `GroupPanel_CarriesTheVisibilityBinding` guards.
5. `PrintWindow`, scan a panel's corner: the arc is 3 DIP, not 8.
6. Log the final `Window fit to content` line and record `chrome 161x31`, `page {Pw}x{Ph}`, and the
   resolved geometry — this is §8's checkpoint.
7. Both variants, and both languages (the pane's label column at 872 DIP is the English worst case).

---

### Task 10 — The record

#### Decision

No visual change. Five things:

1. **`Themes/Fluent2/` → `Themes/Shell/`**, with `Fluent2TokenTests`' hardcoded `ThemesDir` and the
   class renamed to `ShellTokenTests`. Left until last so the rename is a rename and nothing else.
2. **`docs/superpowers/archive/2026-08-04-windows11-measurement-record.md`** created and populated per
   §4 bucket C.
3. **The six existing design documents** in `docs/superpowers/specs/` each gain the §4 deprecation
   header. Not edited, not deleted.
4. **`README.md`** — its 15 Windows-11-marker lines including its opening sentence, plus the new
   "Third-party notices" section (§6). **`packaging/portable/RELEASE-NOTES.md`** — its 10 marker lines
   in both languages, plus the honest statement of the two anachronisms (dark mode, Fluent icon glyphs)
   and the one substitution (Acrylic for glass), in the same voice the feasibility document uses.
5. **Version.** 0.12.0 — the six Windows 11 rounds' output — has never been packaged, and its release
   notes describe a look this round replaces. §9 asks the human whether it ships first.

#### Verification

1. Build, full `dotnet test` → **147 passed**.
2. `dotnet publish -c Release`; final IL2xxx/IL3xxx count recorded against the round's opening
   baseline.
3. `scripts/package-portable.ps1` and `scripts/package-msix.ps1` produce packages containing
   `THIRD-PARTY-NOTICES.txt`.
4. `grep -rn 'Mica\|Fluent 2\|Windows 11\|Task Manager' src --include=*.axaml --include=*.cs` returns
   only occurrences that are deliberate historical citations pointing at the archive file.
5. Final `PrintWindow` set: 应用 and 设置, Light and Dark, at the floor and at the fit, in both
   languages — eight captures, archived as the round's baseline.

---

## 8. Predicted window geometry

On the machine these numbers come from (work area inferred at `(1280, 0, 2560, 1392)` DIP from the two
logged coordinates — self-consistent to the digit, but arithmetic, not measured; 150 % scale):

| term | today | after |
|---|---|---|
| `chromeWidth` = R + B | 49 | **161** |
| `chromeHeight` = T + Bt | 49 | **31** |
| `Wmin` | 760 | **872** |
| `Hmin` | 568 | **568** |
| content pane at the floor | 711 DIP | **711 DIP** |
| placeholder / declared `Width` | 1000 | **1112** |

**Fitted, on today's content:**

```
H    = Clamp(Ph' + 31, 568, 1392) = 568          ← floored, and it does not move. §2.1
Top  = 1392 - 8 - 568             = 816          ← unchanged
W    = Clamp(Pw' + 161, 872, 2560)
Left = 1280 + (2560 - W) / 2
```

`Pw'` and `Ph'` are not predictable to the digit — the type ramp drops 14 → 12 DIP, the tile padding
16 → 11, the scrollbar 16 → 18, and the tile grid re-measures. `Ph' ≈ 532` (494 today, less the type
and padding reduction, plus ≈ 38 for the restored search header), which is why `H` stays floored.
`Pw'` is bounded below by roughly 1500 and above by today's 1708.6666 for the same reasons.

**The three numbers to check against, and they are exact:** the logged chrome term reads
`chrome 161x31`; `H` reads `568`; `Top` reads `816`. **Anybody who verifies the caption height by
watching `H` will conclude nothing happened.** Verify it by the chrome term, by the `page` term, and by
the strip on a capture.

**At the floor:** `872 × 568` DIP, content pane `711 DIP` — the same content pane the app has today,
which is the entire reason `Wmin` moved.

---

## 9. Open decisions for the human

Two. Everything else in this document is decided, with its basis.

1. **Icons.** 25 glyph instances over 20 code points come from
   `Segoe Fluent Icons, Segoe MDL2 Assets` — flat monochrome line glyphs. Windows 7 **never had an icon
   font**: Aero's UI icons were full-colour, 3D-perspective raster assets inside `shell32.dll` /
   `imageres.dll`, and Microsoft's are not redistributable. So there is nothing to switch the font to.
   This design **keeps the Fluent glyphs as a deliberate anachronism**, beside dark mode, because
   commissioning 20 colour icons is not funded and reversing
   `TanMenu.Wpf.csproj:66-68` (*"No fonts are embedded … 22.7 MB … a third of the shipped payload"*) is
   a real cost. **If you want commissioned raster art instead, say so now** — it changes Task 9 and it
   changes the payload argument. *(One exception already survives any theme: `AboutCard.axaml` uses
   `wwwroot/app-tile.png` at `Height="16"`.)*
2. **0.12.0.** The six Windows 11 rounds have **never been packaged**, and their release notes describe
   a look this round replaces. Package and ship 0.12.0 first — so the work exists as a release — or go
   straight to 0.13.0 and rewrite `README.md` and `RELEASE-NOTES.md` before anything ships? Task 10
   assumes the second; the first costs one packaging run and changes nothing else.

---

## 10. What could not be verified

Stated rather than filled in. Each is a build away, and each is the first thing its own task discovers.

- **Whether any vendored file compiles under Avalonia 12.1.** No build was run, per the brief.
  Everything in §3 and §7 is static resolution against decompiled 12.1 metadata and the library's own
  text. Task 2 answers it for the colour layer, Task 3 for the templates.
- **Whether the vendored `{Binding}` expressions survive compiled-bindings-by-default.** The forms used
  (`#Name.Prop`, `$self`, `$parent[T]`, `RelativeSource TemplatedParent`, `ElementName=`, the attached
  `(TextBlock.Foreground)` form, and four method-to-command bindings like `$parent[TextBox].Copy`) are
  all documented as supported and none needs an `x:DataType` — but nobody has compiled them.
- **Whether `AcrylicBlur` is actually granted on this machine.** Not measurable read-only: the app is
  not running and `ActualTransparencyLevel` needs in-process code. The deduction is airtight
  (`IsSupported(AcrylicBlur)` and `IsSupported(Mica)` query the same `CompositionEffectsSurface`, and
  Acrylic's floor 10.0.15063 is strictly below Mica's 10.0.22000, so **Mica granted ⇒ AcrylicBlur
  granted**) but the premise — that Mica is granted *today* — rests on prior rounds' recorded
  measurements. Task 1's log line settles it permanently.
- **Whether `{Binding $parent[Window].IsActive}` resolves from inside a replaced decorations template.**
  Unknown, and §7.1 is designed so that it does not matter: the active/inactive swap goes through
  `Window.Resources` and a code-behind observer, which is a mechanism this app already uses.
- **The exact XAML attribute syntax for a multi-level `TransparencyLevelHint` fallback chain.** The
  property is `IReadOnlyList<WindowTransparencyLevel>` and a single token compiles to a one-element
  array (confirmed by decompiling our own Release build:
  `P_1.TransparencyLevelHint = new WindowTransparencyLevel[1] { WindowTransparencyLevel.Mica };`).
  Task 1 sets a single token; if a chain is ever wanted, property-element syntax or code-behind is the
  safe form.
- **Aero's caption font size from a primary source, and its glass tint formula.**
  `GetThemeSysFont(TMT_CAPTIONFONT)` is the documented source and the measured ≈ 9 px cap height is
  consistent with Segoe UI 9 pt, but the point size was not verified from a primary source. The tint is
  AUTHORED (§7.1) and says so.
- **What a Windows 7 non-editable drop-down list painted in its closed state.** No msstyles, no
  reference capture, and no Microsoft page states it; `ctrl-drop`'s own sizing figure lives inside an
  image. §7.6 records the choice as a judgement call, not a measurement.
- **How Aero's alpha-carrying gradients composite over Acrylic.** Every fill in
  `ControlStyles/ListBoxItem.axaml` carries alpha (`C0`, `66`, `BF`, `AD`, `E6`, `9C`, `D4`, `DA`) and
  was authored to sit over Explorer's opaque `#FFFFFF`. The values in this document are the authored
  ones, not what will render. Tasks 3 and 8 sample the real thing with `PrintWindow` before anything
  claims a match — the same instrument and the same discipline Round 6's Mica-straddle analysis used.
- **The whole of the Windows 7 navigation pane.** Not published, not measurable here. §7.8 is AUTHORED
  and every number in it says so.
