# TanMenu — Windows 7 Aero feasibility

> **Status: DECISION DOCUMENT.** No source file was edited, no package was added, no build was run and
> nothing was committed while this was written. The tree was clean at `e6d031b` throughout. This is
> not an implementation plan; it answers one question — *can it be done* — and prices the answer.

Investigated 2026-08-04 against `https://github.com/Splitwirez/aero-theme-for-avalonia`, upstream
`main` at `a705b7c0` (2026-01-26).

---

## 0. Verdict

**No — not as stated, and not by taking that dependency.** The library named in the request cannot be
adopted: it targets Avalonia 11.0.10, it is not on NuGet in any form, it has zero tags and zero
releases, and the one file in it that describes a window — `ControlStyles/Window.axaml` — instantiates
two Avalonia types that no longer exist in 12.1.0, so it is a hard XAML-compile failure, not a
degraded render. That much is a version problem and version problems are fixable. The part that is
*not* fixable is underneath it: **the theme contains no Aero window chrome at all** — its `Window`
template is Avalonia 11's stock Fluent template with recoloured setters, the Aero frames in its README
screenshots are the author's own OS, and **authentic Aero glass is impossible on Windows 11 by
Microsoft's own documentation**, not merely hard. So the two things that make Aero recognisable — the
glass caption and the colour icon set — are supplied by nobody: not by Windows 11, not by this theme,
and not by any other Avalonia package I could find. What *is* buyable, and is genuinely well made, is
Aero's **client-area controls**: buttons, tabs, scrollbars, text boxes, checkboxes, radios, progress
bars, list boxes. The honest framing is therefore: **you can have an Aero-styled client area inside a
Windows 11 window frame, by hand-porting MIT-licensed control themes file by file — and you cannot
have Windows 7.** If that trade is acceptable the project is a real one and I would size it at roughly
the same order as one of the six rounds already delivered; if it is not, the answer is decline.

---

## 1. The three gates

### 1.1 Licence — **PASS.** This is the one dimension that is unambiguously clean.

`https://raw.githubusercontent.com/Splitwirez/aero-theme-for-avalonia/main/LICENSE` is the verbatim,
unmodified MIT text, opening `MIT License` / `Copyright (c) 2026 Splitwirez`. The GitHub API reports
`license.spdx_id = "MIT"`. There is no attribution clause beyond stock MIT, no additional terms and
no CLA. MIT permits vendoring the whole tree **or copying individual files** into our repository,
modified, provided the copyright notice travels with them — which matters a great deal for Option B
below.

Two caveats, neither a gate:

- The LICENSE file did not exist until commit `bf30daa5`, 2026-01-25 ("Add missing LICENSE. closes
  #8"). Everything published before that date was technically unlicensed. The one prior contributor,
  `Neurofibromin`, consented to re-licensing in an issue comment (issue #8, 2026-01-31: *"MIT is fine,
  my contribution can also be released under MIT or under any other FOSS (OSI approved) license of
  your choice."*). That consent lives in a comment thread, not in a file. Adequate in practice.
- **The 7 bundled Inter TTFs carry no font licence anywhere in the tree.** Inter is normally SIL OFL,
  which has its own notice requirement that a repo-level MIT LICENSE does not satisfy. This is moot
  for us — we would not take the fonts under any option (§3.4) — but it would matter to anyone
  vendoring the tree wholesale.

### 1.2 Avalonia 12.1 — **FAILS as shipped.** Two separate failures; one is cheap, one is not.

**The version pin, verbatim.** `https://raw.githubusercontent.com/Splitwirez/aero-theme-for-avalonia/main/build/Avalonia.props`:

```xml
<Project>
	<PropertyGroup>
		<AvaloniaVer>11.0.10</AvaloniaVer>
	</PropertyGroup>
```

That literal has not moved since commit `e46c8590`, 2024-11-01 ("Migrate to Avalonia 11.0.10 (#6)").
All **9 branches** were fetched individually and the highest `AvaloniaVer` found anywhere upstream is
`11.0.10`; the newest migration branch is `migration/avalonia--11-0-10`. The GitHub API returns an
**empty array for both `/tags` and `/releases`**. A regex over all 9 issues and PRs ever opened, titles
and bodies, for `avalonia 12` / `net 12` / `version` returns **zero matches** — nobody upstream has
even asked.

**It is not on NuGet.** nuget.org's search service (`azuresearch-usnc.nuget.org/query`) returns
`totalHits = 0` for `AeroAvalonia`, for `aero avalonia` and for `Aero.Theme.Avalonia`. The csproj
carries no `PackageId`, no `GeneratePackageOnBuild` and no packaging metadata; there is no `.nuspec`.
The omission is deliberate rather than inexperience — the same author publishes 7 other packages to
nuget.org. **This changes the nature of the task from "add a `PackageReference`" to "vendor source and
own the merges."** There is no version-range override to attempt, because there is no package
reference to override.

**Failure 1 — the compile error. Cheap.** `src/AeroAvalonia/ControlStyles/Window.axaml` contains:

```xml
<VisualLayerManager>
    <VisualLayerManager.ChromeOverlayLayer>   <!-- property removed in Avalonia 12 -->
        <TitleBar />                          <!-- type removed in Avalonia 12 -->
    </VisualLayerManager.ChromeOverlayLayer>
```

I verified the removal myself, locally and independently of both reports, against
`~/.nuget/packages/avalonia/12.1.0/lib/net10.0/Avalonia.Controls.xml`:

| probe | result |
|---|---|
| `Avalonia.Controls.Chrome.TitleBar` or `ChromeOverlayLayer` | **0 hits** |
| `WindowDecorationsTheme` | 3 hits |
| every `Avalonia.Controls.Chrome.*` type in 12.1.0 | `DrawnWindowDecorationParts`, `IWindowDrawnDecorationsTemplate`, `ResizeGripLayer`, `WindowDecorationProperties`, `WindowDrawnDecorations`, `WindowDrawnDecorationsContent` |

Not one filename or type name overlaps with 11.0.10's `Chrome/` directory, which contained exactly
`CaptionButtons.cs` and `TitleBar.cs`. Avalonia's XAML compiler resolves types and properties at build
time, so these are compile errors. Additionally, the theme project is `netstandard2.0` while Avalonia
12.1.0 ships only `lib/net10.0` and `lib/net8.0` — a hard **restore** failure before compilation is
even reached.

The scope of that failure is narrow and worth stating precisely: **of the 73 distinct element types
the theme's XAML instantiates, exactly one — `TitleBar` — is absent from 12.1.** The other 72 resolve.
Of 41 resource keys it inherits from Fluent, **39 still exist in Fluent 12.1.0**; the 2 that don't
(`HighlightBrush`, `HighlightForegroundBrush`, used at `ControlStyles/NumericUpDown.axaml:12-13`) are
absent from Fluent 11.0.10 too, so they are a pre-existing theme bug rather than a 12 regression. A
third-party fork, `ahenyao/aero-theme-for-avalonia` commit `25e3d2e7` (2026-07-14), ports the whole
library to 12.1.0 in a **three-file, four-line diff**: bump `AvaloniaVer`, retarget the TFM to
`net10.0`, delete the `ChromeOverlayLayer`/`TitleBar` block. That fork is ahead 1 / behind 0, has never
been offered upstream, and **left its own demo app on `netstandard2.0`/`net5.0`, so its author cannot
have run it.** Nobody has demonstrated this theme rendering under Avalonia 12.

**Failure 2 — the design blocker. Not cheap, and not fixed by porting one file.** In Avalonia 12 the
drawn window decorations no longer live in the `Window` control template at all. They moved to
`TopLevelHost.Decorations`, driven by `Window.WindowDecorationsTheme` (a `StyledProperty<ControlTheme>`
registered on `Window`) and `Avalonia.Controls.Chrome.WindowDrawnDecorations` — three APIs with **no
Avalonia 11 equivalent**, which is why an 11-era theme contains no reference to any of them.
Re-templating `Window` is the *only* chrome mechanism this theme has. **An Aero title bar under
Avalonia 12 must be written from scratch against an API the theme has never seen** — and that is
precisely the surface our own `MainWindow.axaml` is already built on (`MainWindow.axaml:14-15`, the
87-line `Window.WindowDecorationsTheme` block at `:105-192`, and `xmlns:chrome="using:Avalonia.Controls.Chrome"`
at `:3`). Deleting the `<TitleBar/>` block the way the `ahenyao` fork does leaves the Aero `Window`
theme with **no title bar overlay at all**.

**A third failure, silent, worth naming because it fails no test and throws nothing.** Avalonia 12's
`TopLevel` locates its visual layer manager by name — `[TemplatePart("PART_VisualLayerManager", …)]`,
`e.NameScope.Find<VisualLayerManager>("PART_VisualLayerManager")` — and `EnableVisualLayerManagerLayers()`
no-ops when that lookup returns null. Fluent 12.1's `Window.xaml` names the part; Fluent 11.0.10's does
not, so **Aero's `Window` template has a bare unnamed `<VisualLayerManager>`**. Ported naively, overlay,
popup-overlay and text-selector layers are never enabled: flyouts, tooltips, context menus and text
selection degrade with no compile error and no exception, surfacing only in manual testing.

**What it would take, concretely.** To get *the controls* onto 12.1: retarget to `net10.0`, bump the
Avalonia pin, and drop `Window.axaml` entirely (it is the only compile-fatal file and it delivers no
Aero anyway). That is a small job. To get *an Aero window* onto 12.1: hand-author a
`WindowDecorationsTheme` against Avalonia 12's `WindowDrawnDecorations` API, from scratch, with an
Acrylic approximation standing in for glass. That is a new project, not a port.

### 1.3 NativeAOT at 0 warnings — **UNSETTLED, and our bar is not what the brief assumed.**

**First, a correction to the premise, verified in this tree.** A search across every `.csproj`,
`.props` and `.targets` (excluding `bin`/`obj`) for `TreatWarningsAsErrors`, `WarningsAsErrors`,
`NoWarn`, `TrimMode` and `IlcArg` returns **nothing**. There is no `rd.xml`, no ILLink descriptor and
no trimmer descriptor anywhere. `Directory.Build.props:11` says so in its own words: *"Findings are
warnings, not build-breaking."* **The 0-warning bar is a convention this project has held to for six
rounds, not a build gate.** Our AOT knobs are exactly four: `TanMenu.Wpf.csproj:35` `PublishAot`, `:36`
`IsAotCompatible`, `:43` `TrimmerSingleWarn=false`, `:45` `AvaloniaUseCompiledBindingsByDefault=true`.
So a regression here would be loud in the log — `TrimmerSingleWarn=false` reports every IL2xxx/IL3xxx
individually — but it would not stop a build. That makes this gate a *quality* gate, which is how it
has been treated, and it means the decision is ours rather than the compiler's.

**Second, the theme's AOT profile is genuinely clean, and that deserves as much emphasis as the
blockers.** Across all 9 of its `.cs` files there are **zero** hits for `Activator`, `GetType()`,
`typeof(`, `System.Reflection`, `Assembly`, `dynamic` or `MakeGeneric`. It contains **zero**
`DataTemplate` (so the `x:DataType` question is moot), **zero** `x:CompileBindings` overrides, and its
7 converters are `public static X Instance { get; }` singletons with private constructors, referenced
from XAML by `{x:Static}` — nothing for the trimmer to lose. XAML loads through the standard compiled
path, `AvaloniaXamlLoader.Load(this)`. It uses **269 `{TemplateBinding}`**, which always compile.

**Third, the one real unknown.** It uses **65 `{Binding}`**, **0 `{CompiledBinding}`**, and **2
explicit `{ReflectionBinding}`** — both in `ControlStyles/ProgressBar.axaml`, binding `Bounds.Width` /
`Bounds.Height` by `ElementName`. `{ReflectionBinding}` cannot be compiled by definition, and
`Avalonia.Data.Binding` in 12.1 carries both `[RequiresUnreferencedCode("BindingExpression and
ReflectionBinding heavily use reflection. Consider using CompiledBindings instead.")]` and
`[RequiresDynamicCode(…)]`, so anything lowering to it emits IL2026 + IL3050 at publish. There is a
twist that cuts both ways: `~/.nuget/packages/avalonia/12.1.0/build/Avalonia.props:8` sets
`AvaloniaUseCompiledBindingsByDefault` to `true` unconditionally, so **on Avalonia 12 compiled bindings
are on by default for every referencing project, including a `ProjectReference`d theme.** Good for AOT;
bad for the odds of a clean first build, because compiled bindings are *stricter* than the reflection
bindings those 65 expressions were authored against under Avalonia 11. Several are non-trivial:
`{Binding TemplateSettings.ContainerAnimationEndPosition, RelativeSource={RelativeSource Mode=TemplatedParent}}`,
`{Binding (TextBlock.Foreground), ElementName=PART_TextPresenter}`, `{Binding !IsPointerOver, …}`,
`{Binding Orientation, RelativeSource={RelativeSource Mode=FindAncestor, AncestorType={x:Type ProgressBar}}}`.

**Nobody has seen these compile, and this document does not claim they do.** As a side note, our own
`TanMenu.Wpf.csproj:45` is now redundant on Avalonia 12 — it restates the framework default. It is
harmless and worth keeping as documentation of intent.

### 1.4 The cheapest experiment that settles §1.2's compile gate and §1.3 entirely

**Run outside `D:\0.Work\TanMenuWpf`, in a throwaway directory. Do not add a package to this repo.**

1. `git clone https://github.com/Splitwirez/aero-theme-for-avalonia`.
2. Delete `src/AeroAvalonia/ControlStyles/Window.axaml` and remove its single `ResourceInclude` line
   from `src/AeroAvalonia/AVMAP/Controls.axaml`. This excises the only compile-fatal file, and it costs
   nothing we want — that file delivers no Aero chrome (§2.1).
3. `build/Avalonia.props` → `<AvaloniaVer>12.1.0</AvaloniaVer>`; `src/AeroAvalonia/AeroAvalonia.csproj`
   → `<TargetFramework>net10.0</TargetFramework>`.
4. `dotnet build src/AeroAvalonia/AeroAvalonia.csproj -c Release`.

**Cost: a few minutes.** The resulting error and warning list *is* the port estimate — it settles
whether the remaining 16 control themes compile under compiled-bindings-by-default, whether
`ProgressBar.TemplateSettings` still exposes the five members the theme binds, and whether the XAML
compiler warns on the `[Obsolete]` `TextBox.Watermark` / `UseFloatingWatermark` / `WatermarkForeground`
properties it uses.

Only if step 4 succeeds is a second step worth it: a minimal `net10.0-windows` console/desktop host with
`PublishAot=true` and a `ProjectReference` to the theme, then `dotnet publish -c Release`, counting
IL2xxx/IL3xxx. **Cost: another few minutes.** That settles §1.3 completely and is the only way to.

A third step, if the look matters before committing: port `samples/AeroAvalonia.Demo` (which the fork
author never did) and *look at it under Avalonia 12 on this machine*. That is the only way to learn
whether the `PART_VisualLayerManager` regression and the missing title bar are tolerable in practice.
**Cost: perhaps an hour.** Nobody on earth has done this yet.

---

## 2. What Aero needs that nothing supplies

This section exists because §1's gates are the *fixable* problems. These are not.

### 2.1 Glass is impossible on Windows 11 — by documentation, not by difficulty

Microsoft, on `DwmEnableBlurBehindWindow`
(https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow,
Remarks): *"Beginning with Windows 8, calling this function doesn't result in the blur effect, due to a
style change in the way windows are rendered."* The API still exists and still returns `S_OK`. It
simply does not blur. The other half of Aero glass, `DwmExtendFrameIntoClientArea`, **does** still
function (its page carries no deprecation note) — but the frame it extends is the modern Windows 11
frame, and that call is already what sits behind Avalonia's `ExtendClientAreaToDecorationsHint`, which
this app uses today at `MainWindow.axaml:14`.

The nearest modern substitute is Acrylic. Aero glass had a specular highlight sweep, a coloured tint
bar, a bright inner reflection along the top edge and a distinct 1 px light rim; Acrylic is blur + tint
+ noise and has none of them. **Anything shipped would be an approximation and must be described to
users as one.**

### 2.2 The theme contributes zero window chrome, and the screenshots mislead

`src/AeroAvalonia/ControlStyles/Window.axaml` is 1,995 bytes and is **element-for-element Avalonia
11.0.10's own `src/Avalonia.Themes.Fluent/Controls/Window.xaml`** — same `Panel`, same
`PART_TransparencyFallback`, same `WindowDecorationMargin` panel, same `VisualLayerManager` /
`ChromeOverlayLayer` / bare `<TitleBar />`, same `PART_ContentPresenter`. Only the `Setter` *values*
differ (`AeroWindowBackground` vs `SystemRegionBrush`, `AeroFontSizeNormal` vs
`ControlContentThemeFontSize`). `AVMAP/Brushes.axaml` — 25,455 bytes, the theme's entire colour layer —
contains **no `ImageBrush`, no blur/acrylic/glass effect, and no brush whose name contains Glass, Blur,
Acrylic, Caption, Title or Frame.** `CaptionButtons.xaml` and `TitleBar.xaml` appear only inside XML
comment blocks in `AVMAP/Controls.axaml`.

The Aero frames in the README screenshots are **the author's operating system**: the demo's own window,
`samples/AeroAvalonia.Demo/Views/MainWindow.axaml`, is 475 bytes of bare `<Window>` with no
`ExtendClientAreaToDecorationsHint`, no `SystemDecorations`, no `TransparencyLevelHint`, and its
`Title="Windows Aero for Avalonia Sample"` is exactly the caption text visible in those images. The
author writes Windows visual-style tooling; his desktop is themed. **On a stock Windows 11 machine the
same app shows Windows 11 chrome — all four corners rounded, flat caption buttons — around an Aero
client area.** The third screenshot, a photo of the demo on an Android phone, shows no chrome at all
and independently confirms the package is a client-area restyle.

Aero's title text was black `#000000` Segoe UI 9 pt made legible over arbitrary blurred content by a
`DrawThemeTextEx` + `DTTOPTS` glow halo (`iGlowSize`: *"Specifies the size of a glow that will be drawn
on the background prior to any text being drawn"*). That is a GDI/uxtheme API with **no Avalonia
equivalent**; it would have to be faked with a blurred `TextBlock` copy or a drop-shadow effect.

### 2.3 Iconography is an unbuyable asset problem

**Windows 7 never had an icon font.** Aero's UI icons were full-colour, 3D-perspective,
gradient-and-gloss *raster* assets inside `shell32.dll` / `imageres.dll`, at 16/32/48/256 px, and
Microsoft's Windows 7 icons are not redistributable. So there is nothing to "switch the font to" —
Windows 11 ships no Aero-era icon face because none ever existed. The theme ships **no icon assets of
any kind**.

This app draws **25 glyph instances over 20 distinct code points** from
`<FontFamily x:Key="IconFontFamily">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>`
(`Themes/Fluent2/Tokens.axaml:245`) — both of which are flat monochrome line glyphs, the exact
opposite aesthetic. The choice is therefore: keep flat Fluent glyphs inside an Aero shell (visually
incoherent), or commission a colour icon set — which also reverses `TanMenu.Wpf.csproj:66-68`, *"No
fonts are embedded … removed the four bundled TTFs and the 22.7 MB they cost — a third of the shipped
payload."*

The one exception: `AboutCard.axaml` uses `avares://TanMenu.Wpf/wwwroot/app-tile.png` at `Height="16"`
as a row's leading mark, which is why `tm-card-icon` is `:is(Control)`-scoped. That one survives any
theme change; the other 20 code points do not.

### 2.4 What the theme *does* deliver, credited honestly

17 control themes, and where they exist they are good. `ScrollBar.axaml` alone is 26,760 bytes with a
`QuinticEaseInOut` 250 ms expand animation. Auto-applied via `x:Key="{x:Type …}"`: **Button,
RepeatButton, ToggleButton, RadioButton, CheckBox, TextBox, ToggleSwitch, NumericUpDown, ButtonSpinner,
ScrollViewer, ScrollBar, ListBox, ListBoxItem, TabControl, TabItem, Menu, ProgressBar, Window.** Opt-in
named keys: `GroupBox`, `StorageBar`, `AeroTopLevelMenuItem`. All Aero gradients are hex colours plus
vector geometry — **no bitmaps, no shaders**, nothing exotic to carry. And its font *scale* is
Aero-correct even though its typeface is not: `AeroFontSizeNormal = 12`, and 12 DIP = 9 pt at 96 dpi,
which is exactly what the UX Guide specifies.

Against the controls we actually use, the coverage is better than expected. **The only live gap is
`ComboBox`**, which we use three times (`Views/Settings/AppearanceCard.axaml:35, 47, 63`) plus
`ComboBoxItem` constructed in code (`AppearanceCard.axaml.cs:140`); `ComboBox` and `ComboBoxItem` are
commented out in `AVMAP/Controls.axaml`. Because `AeroTheme.axaml:6` embeds
`<FluentTheme DensityStyle="Compact" />`, anything unstyled renders as **Fluent inside an otherwise-Aero
window** rather than unstyled — which is a mercy for correctness and a problem for coherence.

Not present, and would be authored from scratch if wanted: an Explorer-style navigation pane, a
Windows 7 "command bar", `DataGrid` (the author, issue #4: *"I've never even used DataGrid before… the
final boss of Avalonia controls"*).

### 2.5 The published Windows 7 metrics, which are free and usable

From https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-layout, for 9 pt Segoe UI at 96 dpi,
in relative pixels: **command button 75 × 23; check box and radio 17; combo box / drop-down / text box
height 23; static text 13; progress bar height 15; slider 24. Dialog margins 11 all sides; label to
control 5; between related controls 7; between unrelated controls 11; between buttons 7; between
paragraphs 11; minimum interactive control 16 × 16.** From
https://learn.microsoft.com/en-us/windows/win32/uxguide/vis-fonts: **Segoe UI 9 pt is the system font;
title bar text black `#000000`; main instructions 12 pt blue `#003399`; body 9 pt black; disabled 9 pt
`#323232`; links `#0066CC`, hover `#3399FF`; group headers 11 pt `#003399`.** The `#003399` main-
instruction blue is one of the most Aero-identifying details in the system and it is a colour, not an
asset — free.

Both pages carry Microsoft's archival banner: *"This design guide was created for Windows 7 and has not
been updated for newer versions of Windows."* The guidance is frozen, which is fine — so is Aero.

Note the collision this creates with what is in the tree today: Aero's command button is 23 px tall
with 11 px margins and 7 px gutters, while `MainWindow.axaml:15` pins
`ExtendClientAreaTitleBarHeightHint="48"` (a Windows 11 caption height, stated as such in-file at
`:51-52`), and `AeroTheme.axaml` additionally forces `DensityStyle="Compact"`, which re-proportions
Avalonia's stock controls *underneath* our own token layer. **The two proportioning systems fight.**

### 2.6 The author's own assessment

Issue #4, 2024-06-08: *"truth be told, this theme is far from complete, and thus I can't really
encourage its use yet."* Issue #7, 2026-06-02: *"A NuGet package is already on my to-do list, though I
hadn't planned to do so until the theme is more complete. Then again… progress remains embarrassingly
glacial."* 39 commits since 2019; the two most recent are a licence file and a brush fix. The author is
demonstrably fluent in Avalonia 12 elsewhere — `AvaloniaControls.Ribbon.Flowery 2026.7.29.2` declares a
dependency on `Avalonia [12.1.0, )`, exactly our version — so an upstream bump is *plausible to
request*. It is not plannable: the same NuGet promise was made in 2024-06, 2024-12 and 2026-06 and has
not landed. **Assume no upstream help on any timeline of ours.**

---

## 3. What it would cost in our tree

### 3.1 The measured surface

Line counts are my own, `wc -l` at `e6d031b`. (The blast-radius report gives one more on each file; a
counting-method difference that changes nothing.)

| file | lines | markup lines, comments stripped |
|---|---|---|
| `src/TanMenu.Wpf/Themes/Fluent2/Launcher.axaml` | **1,274** | 485 |
| `src/TanMenu.Wpf/Themes/Fluent2/Tokens.axaml` | **310** | 80 |
| `src/TanMenu.Wpf/MainWindow.axaml` | **597** | 154 |
| all 15 UI `.axaml` files | **3,355** | **1,320** |

**Roughly 60 % of this app's XAML is comment prose, and essentially all of it is a Windows 11
measurement record** — Task Manager pixel samples, Mica RGB straddles, Segoe optical-size reasoning,
decompiled Fluent templates, UI Automation bounding rectangles. Under this project's own rule that a
comment which no longer describes the code is a bug, **~2,000 lines of in-XAML prose become false on
the day the reference changes** and must be deleted or rewritten. This is the single line count most
likely to be underestimated, because it reads as "just comments" until you apply the rule.

Six rounds of work, `git diff --stat ef79481..HEAD`: **58 files, 9,100 insertions** across `src` +
`tests`; **71 files, 16,768 insertions** tree-wide.

### 3.2 The specific things that break, and how they break

- **`Tokens.axaml`: of 46 keys, 6 survive unchanged** — the `Space1..Space5` spacing ramp and
  `SurfacePadding`. About 14 are re-authorable concepts with entirely new values. The remaining ~26
  encode Windows-11-only ideas: alpha-over-Mica surface layers, "subtle fill", the two-ring focus
  rectangle, the personalization accent ramp, three Segoe optical sizes, two measured 8 DIP radii. The
  file's own header states the palette is alpha over the surface *specifically so the acrylic backdrop
  shows through* — the colour model is designed around a Windows 11 material.
- **Four keys reach every stock Avalonia control in the app and would re-proportion any replacement
  theme unless deleted**: `Tokens.axaml:270` `ControlCornerRadius` 4, `:271` `OverlayCornerRadius` 8,
  `:272` `ContentControlThemeFontFamily`, `:305` `TitleBarBackgroundBrush` = Transparent. They win only
  because `App.axaml` lists Tokens *after* `<FluentTheme />` and `Application.Styles` is searched
  last-first. `TitleBarBackgroundBrush=Transparent` exists solely to clear the opaque band Avalonia's
  drawn decorations paint behind our title strip; if an Aero theme draws its own caption, **it may
  blank that caption's background instead.** I could not determine which — it depends on a template
  that does not exist yet.
- **`Launcher.axaml`: 6 `ControlTheme`s and 68 `Style` selectors over 31 distinct `.tm-*` classes.**
  `TmAccentButton` is meaningless (Aero has no accent-filled default button). `TmNavRowItem` — the
  3 × 16 DIP pill at `CornerRadius 1.5`, Round 6 item 3's entire deliverable, 55 lines of measurement
  prose — is meaningless (Windows 7 had no navigation rail). The 147-line content-pane argument
  (`:722-868`) is meaningless: its whole thesis is *"Mica measures 238..247 Light and 30..34 Dark and
  therefore straddles `SurfaceBaseBrush`, so card separation survives"*. There is no Mica in Aero.
- **~14 style rules target Avalonia Fluent's own template part names and would silently match
  nothing** under a theme that ships different templates: five in the `Window.WindowDecorationsTheme`
  block (`MainWindow.axaml:144, 147, 182, 185, 188` — `PART_TitleTextPanel`, `PART_FullScreenButton`,
  `PART_MinimizeButton`/`MaximizeButton`/`CloseButton`), plus the search field's and the hotkey box's
  `/template/ Border#PART_BorderElement` rules in `Launcher.axaml`. **An Avalonia selector that matches
  nothing does not error.** This is a whole class of silent visual regression.
- **The chrome contract inverts, and the fit arithmetic goes wrong without throwing.**
  `MainWindow.axaml:14` pulls the 48 DIP caption *into* the client area; `Services/WindowHost.cs:39,52`
  then adds it back as `ChromeHeightDip = TitleStripHeightDip + 1`. If an Aero theme restores an
  OS-drawn caption, that 48 is double-counted: the window comes out 48 DIP too tall with a bottom
  anchor computed from a wrong height, and **nothing throws.** The `MinWindowWidthDip = 760` and
  `MinWindowHeightDip = 568` workarounds are both derived from `AdjustWindowRectEx` on *this* window's
  style, so a chrome change invalidates the Round 4 item 4 analysis, not merely the numbers. Worse:
  `WindowPlacementTests.cs` carries 27 test attributes that take the chrome terms as *parameters* — so
  **they all keep passing while being about the wrong chrome.** That is the dangerous failure mode.
- **`ToggleSwitch.tm-card-toggle` carries `Margin="0,0,-12,0"`, a negative margin compensating for a
  hardcoded 12 px column inside Fluent's own ToggleSwitch template** (decompiled from
  Avalonia.Themes.Fluent 12.1.0, per its comment). The comment warns that a future Avalonia changing
  that column shifts the toggle rows. **A different theme's ToggleSwitch template is exactly that
  change.** Five call sites.
- **Light/dark is a four-layer commitment and Windows 7 Aero had no dark mode** (it had High Contrast,
  an accessibility mode, not a design variant). The four layers: a `config.json` contract already on
  users' disks (`AppAppearance.cs:15-19`, where `System` is both the default and what a key-less older
  config deserializes to), the `ThemeVariant` mapping, two 21-key `ThemeDictionaries`, and a
  user-visible 外观 row (`AppearanceCard.axaml:40-50`, labels 跟随系统 / 浅色 / 深色). And
  `Fluent2TokenTests.EveryThemedTokenIsDefinedInBothVariants` **hard-requires both dictionaries**, so
  the dark half cannot be trimmed incrementally — it is all or nothing. Note the theme itself ships a
  Dark variant, so nothing is lost *mechanically*; but that variant is an invention, and shipping it
  undercuts the authenticity the exercise is for.
- **The accent must be re-specified, not ported.** `Services/AppTheme.cs:50-51` reads
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent\AccentPalette` as an eight-entry
  palette and picks shades per variant using WinUI's own Dark1/Dark2 and Light2/Light3 rule
  (`:100-112`), republishing into `Application.Resources` and re-tinting live on `ColorValuesChanged`.
  Aero's user colour was `DwmGetColorizationColor` — a **single frame colour**, not an eight-shade ramp
  an app picks client-area fills from. The accent has six consumers today, all flat Fluent fills:
  `TmAccentButton`, the rail pill, the search focus underline, the hotkey box focus border, the update
  progress bar, and the splash gradient.

### 3.3 The tests

| file | attributes | verdict under Aero |
|---|---|---|
| `Themes/Fluent2TokenTests.cs` | 4 | `LauncherStyles_ContainNoColourLiterals` **still holds and is genuinely valuable** — it is theme-agnostic. Two "every token referenced is defined" tests still hold mechanically but need pointing at whatever replaces the consumers. `EveryThemedTokenIsDefinedInBothVariants` **becomes an obstacle**: it hard-requires a Dark dictionary. |
| `Themes/SettingsRowTests.cs` | 4 | Two hold (the card column grid, the two-line header — they are markup-shape tests, not paint tests). `NoCardFile_RestatesAConstantTheThemeOwns` becomes meaningless. `NoHeaderSubtitleString_ContainsAHardLineBreak` holds. |
| `Themes/AppPageGroupTests.cs` | 1 | **Fails outright** the moment `Border Classes="tm-group-panel"` is renamed or removed — it asserts `panels.Count == 1`. Its underlying finding is theme-independent and must be preserved. |
| `Services/AppThemeTests.cs` | 11 | 5 `ResolveAccent` facts become meaningless if the accent stops coming from the Win11 palette; `ToVariant_MapsEverySetting` fails or becomes meaningless if 跟随系统/Dark goes. |
| `Services/WindowPlacementTests.cs` | 27 | **All still pass — and that is the problem.** See §3.2. |
| everything else | — | Unaffected; data layer. |

I counted **141 `[Fact]`/`[Theory]` attributes** across `tests/` with `grep`. The brief says 188
executed tests and the blast report counted 199 including `[InlineData]`. Three different measures of
three different things; **I did not run `dotnet test`** and make no claim about the executed count.

### 3.4 The font and payload collision

The theme ships **7 Inter TTFs totalling 2,536,600 bytes (2.42 MiB)** and wires them globally at
`AVMAP/Metrics.axaml`: `<FontFamily x:Key="ContentControlThemeFontFamily">avares://AeroAvalonia/Fonts#Inter</FontFamily>`.
That is **the exact key** `Tokens.axaml:272` already overrides — deliberately, so CJK renders through
Microsoft YaHei UI. **Inter has no CJK coverage**, and this is a Chinese-facing app
(`InvariantGlobalization` is deliberately off at `TanMenu.Wpf.csproj:39-41` so Chinese collates
correctly). And Windows 7's system font is **Segoe UI**, not Inter — so adopting the theme's default
would make the app *less* Aero-faithful than it is today. Resolvable by one resource override plus not
shipping the fonts, but it means the package's default is wrong for the stated goal, and it directly
contradicts `TanMenu.Wpf.csproj:66-68`.

### 3.5 Which of the six rounds' deliverables survive — the honest count

**Of eleven named deliverables: 3 survive intact, 4 survive as concepts with every value discarded,
4 are deleted outright.**

- **Survive intact (3).** Items-per-column 每列个数 (a data-layer setting, `MenuLayout.ChunkIntoColumns`,
  17 tests, no paint). The fit-to-content and centred-low placement *arithmetic* (`WindowPlacement`
  takes chrome as parameters). 关于-as-a-settings-card (placement and content, not paint).
- **Survive as concepts, values void (4).** Content-sized tiles — the sizing logic survives, the look
  (flat fill + hairline + 4 px radius + quiet press) is the opposite of an Aero button. The unified
  settings-row spec — 16 DIP icon / one title + one subtitle / trailing control / optional expansion row
  is information architecture, the most survivable thing in `Launcher.axaml`; every measurement in it is
  Windows 11's. Per-category panels on 应用. The rail's *structure*.
- **Deleted outright (4).** Search in the title bar (it only works because we own the caption). The
  Mica / pane-surface system (147 lines of Mica measurement). The rail's Task-Manager-measured selected
  style. The Windows 11 card look.

Also: `README.md` (15 Windows-11-marker lines, **including its opening sentence**),
`packaging/portable/RELEASE-NOTES.md` (10 such lines, both languages), and **7,786 lines of specs,
plans and reports with 284 marker hits** all become factually stale. Under "code wins" each needs a
deprecation header, a rewrite, or archival. Note that 0.12.0 — the six rounds' output — **has never
been packaged**; its release notes describe a look that would be replaced before shipping.

### 3.6 The thing most likely to be underestimated

Not the repaint. It is that **six rounds of review culture are encoded as measurements against a live
Windows 11 machine**: Task Manager captured with `PrintWindow`, UI Automation bounding rectangles, the
`AccentPalette` registry, decompiled Fluent templates. Every one is a load-bearing citation in a
comment. Reproducing that standard of evidence for Aero requires a Windows 7 reference to measure
against, and **there is no sign of one anywhere in this tree** — no Aero capture, no reference image,
no measurement script targeting it. The published UX Guide numbers (§2.5) are a partial substitute for
metrics; they say nothing about gradients, glows, bevels or the caption.

---

## 4. Options

### Option A — Full adoption: vendor the theme, delete our Fluent 2 layer

**What it delivers.** Aero control visuals across the app in one move, for 17 controls, including the
excellent ScrollBar. `Tokens.axaml` and `Launcher.axaml` largely disappear; we stop owning a visual
layer and start owning an upstream merge instead.

**What it costs.** Vendor ~40 `.axaml` + 8 `.cs` + a `.targets` into our tree (there is no package —
§1.2), apply the untested `ahenyao` port, strip `Window.axaml` and the 2.42 MiB of Inter fonts, author
Aero `ComboBox` + `ComboBoxItem` from scratch, and hand-author a `Window.WindowDecorationsTheme` against
Avalonia 12's `WindowDrawnDecorations` API because the theme has no vocabulary for it. Delete 1,584
lines of `Tokens.axaml` + `Launcher.axaml`, re-author 11 more files, rewrite ~2,000 lines of measurement
prose and ~8,000 lines of docs, retire or rewrite 4 test files.

**What it risks.** Everything in §1.2 and §1.3 is unvalidated — **nobody has ever seen this theme render
under Avalonia 12.** We would own an unmaintained-in-practice hobby theme whose author says it is not
ready, permanently, with no upstream to merge from. The `PART_VisualLayerManager` regression, the ~14
silently-dead selectors and the double-counted 48 DIP chrome term are all failure modes that produce no
error.

**Buildable and shippable at every step? No.** The moment `Tokens.axaml`/`Launcher.axaml` are deleted
the app is mid-surgery; `Fluent2TokenTests` and `AppPageGroupTests` fail; and there is a long stretch —
authoring an Aero caption against a 12-only API — with nothing shippable at the end of it. Six rounds
were delivered green-at-every-commit. **This option breaks that.**

### Option B — Aero skin over the current shell, controls hand-ported file by file *(recommended)*

**What it is.** Keep the shell exactly as it is — the rail, the pages, fit-to-content, the settings
rows, the extended client area, the immediate-apply settings model. Re-author only the *visual* layer to
Aero values, and take control visuals from the theme **by copying individual MIT-licensed files** rather
than by depending on the project. MIT explicitly permits this, and it drops the only compile-fatal file
(`Window.axaml`) on the floor where it belongs.

**How much of the visual layer that actually is, measured.** The layer we own is `Tokens.axaml` (310
lines, 80 markup) + `Launcher.axaml` (1,274 lines, 485 markup) = **565 lines of markup and ~1,000 lines
of prose**. That covers every `.tm-*` class and our 6 `ControlTheme`s — but it does **not** cover the
stock controls, which come from `<FluentTheme />` retuned by four keys at `Tokens.axaml:270-272,305`.
To make those Aero you would port, per file, the ones we actually use: **Button /
Button_RepeatButton_ToggleButton, TextBox, CheckBox, ToggleSwitch, NumericUpDown (+ ButtonSpinner),
ScrollBar, ScrollViewer, ListBox, ListBoxItem, ProgressBar — 11 files, all of which exist upstream** —
and write **ComboBox + ComboBoxItem yourself**, the one real gap. Each ported file is
self-contained axaml plus the `Variant/Light.axaml` + `Variant/Dark.axaml` colour resources and
`AVMAP/StyleTrailMix.axaml` animations they reference; none of them touches `TitleBar`,
`ChromeOverlayLayer` or any 12-removed API — **the compile-fatal surface is confined entirely to the one
file we are not taking.**

**What it delivers.** An Aero-styled client area — buttons, scrollbars, text boxes, tabs, checkboxes,
list boxes, progress bars — with Segoe UI 9 pt typography and the documented `#003399` / `#0066CC`
palette (both free, both published, §2.5), inside our existing shell. The Windows 11 title bar stays,
because there is no alternative that is not a from-scratch project (§2.1, §2.2).

**What it costs.** Re-derive 40 of `Tokens.axaml`'s 46 keys against Aero values; rewrite most of
`Launcher.axaml`'s 485 markup lines; delete or rewrite ~2,000 lines of Windows 11 measurement prose;
port 11 control-theme files and author 2. Ship no fonts. Keep the fit arithmetic and the chrome
constants **exactly as they are**, because the chrome does not change — which is the single biggest cost
this option avoids (§3.2).

**What it risks.** Coherence, not correctness: an Aero client area inside a Windows 11 frame with flat
Fluent icons is a real aesthetic compromise and should be presented to the user as one, not as
"Windows 7". Also, the Aero controls' 23 px / 11 px / 7 px rhythm and our 48 DIP title strip and 48 DIP
rail will visibly disagree until the rail and strip are re-proportioned.

**Buildable and shippable at every step? Yes.** Every unit of work is one file: port one control theme,
build, run, look, commit. That is exactly how the six rounds were delivered, and it is the strongest
argument for this option over Option A.

### Option C — Decline, or defer with named conditions

**What it delivers.** 0.12.0 ships as built. The six rounds' record stays true. Zero risk.

**What would have to change for the request to become possible as literally stated:**

1. **Upstream publishes an Avalonia 12 NuGet package.** Author is on Avalonia 12 elsewhere
   (`AvaloniaControls.Ribbon.Flowery 2026.7.29.2` → `Avalonia [12.1.0, )`) and issue #7 is open and
   warm — but the same promise dates to 2024-06. Not plannable.
2. **Somebody authors an Aero `WindowDecorationsTheme` against Avalonia 12's `WindowDrawnDecorations`
   API.** Nobody has. This is the actual missing piece, and it is a new project.
3. **The goal changes from "Windows 7 Aero" to "Aero-flavoured".** This is the cheapest condition by
   far and it is entirely the user's call — see §5.

**The one alternative I found, and why it is not this.** `Classic.Avalonia.Theme 12.0.1-beta1` (MIT,
published 2026-07-04, `net8.0`/`net10.0`, depends on `Avalonia [12.0.1, )` — a range that accepts our
12.1.0, with companion `.DataGrid` / `.Dock` / `.ColorPicker` packages) clears **every bar this library
fails**: it is on NuGet, it is on Avalonia 12, and it is MIT. But it is **Windows 9x / Classic** — grey
3D bevels, not Aero glass and blue gradients — and it is a prerelease. It does not satisfy the stated
goal. I found no other Aero/Win7 theme for Avalonia; `awesome-avalonia` surfaces this same repo as the
Aero option.

**Buildable and shippable at every step? Trivially yes.**

---

## 5. What you must decide

Short, and only the forks where the answer changes what gets built.

1. **Aero, or Aero-flavoured?** These are different projects. *Faithful Windows 7* needs a hand-authored
   caption against Avalonia 12's chrome API, an Acrylic fake standing in for impossible glass, a
   commissioned colour icon set, and a full re-proportioning to 23/11/7 px. *Aero-flavoured* — gradient
   buttons, the Aero scrollbar, Segoe UI 9 pt, `#003399` — is achievable and is roughly what Option B
   delivers. **Everything below assumes the answer to this one.**
2. **The title bar.** Aero's answer does not exist on Windows 11 (§2.1: *"calling this function doesn't
   result in the blur effect"*) and is not in this theme (§2.2). Keep our Windows 11 caption, or fund a
   from-scratch one? Keeping it is the difference between Option B and Option A.
3. **Mica.** `MainWindow.axaml:9` `TransparencyLevelHint="Mica"` is a Windows 11 material with no Aero
   counterpart. **Aero's answer is: delete it.** There is no port. What replaces the window surface —
   an Acrylic approximation, or a flat Aero `#F0F0F0`-family client?
4. **Dark mode.** Windows 7 had none. Ours is a `config.json` contract already on users' disks plus a
   visible 跟随系统 / 浅色 / 深色 row, and a test hard-requires both dictionaries. **Aero's authentic
   answer is: remove the feature** — a config-contract change and a visible regression. The theme *does*
   ship a Dark variant, so keeping it costs nothing mechanically; it is simply an invention. Keep or
   remove?
5. **Icons.** 25 glyphs over 20 code points from Segoe Fluent Icons. **Aero's answer is: there is no
   font — commission raster art.** Keeping the Fluent glyphs is the pragmatic choice and it is visibly
   incoherent. Which?
6. **The navigation rail.** A 48 DIP icon-only rail is a Windows 11 / Task Manager idiom; Windows 7
   shells used a wide labelled pane. If it goes, Round 6 item 3, the footer ordering, `TmNavRowItem`,
   `TmNavRowButton` and the `RailWidthDip` fit term go with it.
7. **The settings page.** Keep immediate-apply cards, or revert to an Aero-era tabbed property sheet
   with OK/Cancel/Apply? Keeping immediate-apply inside an Aero look is defensible but is a deliberate
   anachronism, and it decides whether Round 5's row spec and 2 of its 4 guarding tests survive.
8. **0.12.0.** Package and ship the Windows 11 work first, or rewrite `README.md` and
   `packaging/portable/RELEASE-NOTES.md` before it ever ships? The six rounds have never been packaged.

---

## 6. Where the four reports disagreed, and how I ruled

- **`Separator` — ruled: not a live dependency.** The compatibility report lists `Separator` as a
  TanMenu usage in the theme's uncovered set. I checked: the **only** `<Separator` string in our
  `.axaml` is inside a comment at `MainWindow.axaml:459` recording that *"both `<Separator/>` elements
  were dropped"*. Zero live uses. This is the same error the library report caught and corrected for
  `MenuFlyout` (all three hits are comments recording its removal; our tray menu uses
  `NativeMenu`/`NativeMenuItem`, which is OS-drawn and unstyleable by any Avalonia theme).
  **`ComboBox` is the only live coverage gap** — verified at `AppearanceCard.axaml:35, 47, 63` plus
  `ComboBoxItem` at `AppearanceCard.axaml.cs:140`.
- **Size of the Avalonia 12 port — ruled: both are right, at different levels.** The library report
  frames it as *"~4 meaningful lines"* (the `ahenyao` diff); the compatibility report calls it a design
  blocker not fixable by porting one file. These are not in conflict: **the compile gate is four lines,
  the chrome gate is unbuyable at any line count.** Option B exists precisely because the compile-fatal
  surface is confined to a file we do not want.
- **NuGet availability — ruled by the exhaustive search.** The Aero-design report called "no NuGet
  package" *likely but not fully verified*; the library report queried nuget.org's search service
  directly and got `totalHits = 0` on three separate query forms, plus confirmed no `PackageId` and no
  `.nuspec` in the tree. **Settled: it is not there.**
- **The 0-warning bar — ruled: it is a convention, not a gate.** I verified this myself rather than
  taking it on report: a search across every `.csproj`/`.props`/`.targets` for `TreatWarningsAsErrors`,
  `WarningsAsErrors`, `NoWarn`, `TrimMode` and `IlcArg` returns nothing, and `Directory.Build.props:11`
  states *"Findings are warnings, not build-breaking."*
- **The `Avalonia 12` chrome deletions — corroborated three ways, twice by the reports and once by me
  directly** against `~/.nuget/packages/avalonia/12.1.0/lib/net10.0/Avalonia.Controls.xml`. This is the
  single most load-bearing external claim in the document and it is as verified as it can be without a
  build.

---

## 7. What could not be verified

Stated rather than filled in.

- **Whether the theme compiles at all against Avalonia 12.1.** No build was run, per the brief. The
  `ahenyao` fork is the only 12.1.0 artifact in existence and it left its own demo on
  `netstandard2.0`/`net5.0`, so its author cannot have run it. §1.4 is the experiment that settles this.
- **Whether the 65 `{Binding}` expressions survive compiled-bindings-by-default**, and whether
  `ProgressBar.TemplateSettings` in 12.1 still exposes the five members the theme binds. Unknown, and
  only a build answers it.
- **The actual NativeAOT publish warning count.** Unknown. Nothing in the tree would fail a build over
  it regardless.
- **Whether Avalonia's XAML compiler warns on `[Obsolete]` property use** (`TextBox.Watermark`,
  `UseFloatingWatermark`, `WatermarkForeground` are all `[Obsolete(…, false)]` in 12.1). Affects the
  warning count only.
- **Windows 7's Aero title-bar height (`SM_CYCAPTION`) and scrollbar width (`SM_CXVSCROLL`).** The UX
  Guide's Layout page states neither and I found no Microsoft page that does. The theme uses
  `AeroScrollBarThickness = 18`; I cannot say whether that is exact.
- **Whether `HKCU\…\Explorer\Accent\AccentPalette` exists on Windows 7.** Not checked, not guessed.
  Irrelevant in practice — the app runs on Windows 11 — but it means the *authentic* Aero accent model
  (`DwmGetColorizationColor`, a single frame colour) is a re-specification, not a port.
- **The licence of the 7 bundled Inter TTFs.** No font licence file exists anywhere in the theme tree.
  Moot under every option here, since none of them ships those fonts.
- **What `TitleBarBackgroundBrush = Transparent` (`Tokens.axaml:305`) does underneath an Aero caption**
  — it may blank that caption's background. Depends on a template that does not exist yet.
- **The executed test count.** I counted 141 `[Fact]`/`[Theory]` attributes; the brief says 188; the
  blast report counted 199 including `[InlineData]`. Three different measures. **I did not run
  `dotnet test`.**
