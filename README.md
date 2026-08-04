# TanMenu (Avalonia + NativeAOT)

A Windows desktop launcher drawn as a **Windows 7 Aero** application window: a hand-drawn 30 DIP
glass caption (app icon, title, and minimize / maximize / close at the measured 28 / 27 / 48 DIP) over
an opaque client, and under it a **classic main menu band** — 设置 · 打开主目录 · 刷新 · 退出 · 关于,
five direct commands with access keys and no submenus, then a vertical separator, then the search
field. The 应用 page scans configured folders for apps / `.lnk` shortcuts and lays them out as
horizontally-scrolling categories, each in its own group box. 设置 opens a **modal Options dialog** —
a Windows 7 property sheet, five tabs, 确定 / 取消 / 应用, where an edit takes effect only when you
commit it — and 关于 opens a real **About box**. Pin its permanent taskbar entry for one-click recall,
or use the tray fallback.

The UI is drawn with native Avalonia controls: the control visuals are **copied**, file by file, from
the MIT-licensed [aero-theme-for-avalonia](https://github.com/Splitwirez/aero-theme-for-avalonia)
(see [Third-party notices](#third-party-notices)) rather than taken as a dependency, and everything
the copies do not cover — the caption, the band's search field, the launcher's own dialect — is authored
against the Windows 7 UX Guide, with each number labelled MEASURED, PUBLISHED, VENDORED or AUTHORED
in the file that states it. Type is Segoe UI at 9 pt (12 DIP), spacing is the published relative-pixel
ladder — 3 / 5 / 7 / 9 / 11 / 16, each rung a cell of the same Microsoft table — and the control radius
is 3. Published with NativeAOT.

**Three deliberate departures, stated rather than hidden.** Aero's glass was a live blur plus a colour
tint, a bright inner reflection and a specular sweep; Avalonia's `AcrylicBlur` supplies the blur only,
so the tint and the reflection are drawn on top of it and the sweep is not reproduced — **a
substitution, not a match**. Windows 7 shipped High Contrast, an accessibility mode, not a dark
variant, so **dark mode is an anachronism** kept because the vendored palette has both variants and
users expect the choice. And Windows 7 had no icon *font* at all — its UI icons were full-colour
raster assets inside `shell32.dll`, which are not redistributable — so the icon glyphs the app still
draws are **Segoe Fluent Icons**, the second anachronism. There are **7 of them over 6 code points**
now, counted in the markup: the classic menu band takes no icons (published — a menu item does not
need one) and the Options dialog's rows lost the 16 DIP mark the Windows 11 row card gave them, which
between them removed most of the uses this sentence used to count. The one exception predates the
conversion and survives it: 关于's mark is the product tile bitmap. The three caption buttons are not
in that count either — their glyphs are vector `Path` geometry: one line for minimize, a closed square
for maximize, two crossed lines for close.

Earlier versions hosted an HTML/CSS UI in a `BlazorWebView` inside a WPF window with switchable CSS
theme packs, then a light-dismiss acrylic flyout with a separate settings dialog, then a Windows 11 /
Fluent 2 shell with a Mica backdrop and an icon rail. All three were replaced by the current one; the
Windows 11 measurement record is kept at
[`docs/superpowers/archive/2026-08-04-windows11-measurement-record.md`](docs/superpowers/archive/2026-08-04-windows11-measurement-record.md).

## Architecture

- **`src/TanMenu.Core`** — framework-agnostic, unit-tested logic: config, folder scanning,
  `.lnk` resolution (source-generated COM), Win32 icon extraction (→ PNG bytes), launching, sounds,
  AppData paths, and the source-generated `System.Text.Json` contracts.
- **`src/TanMenu.Wpf`** — the Avalonia application: window host and placement, tray icon, global
  hotkey, the shell (the menu band and the one page it hosts), the Options dialog and its five setting
  cards, the About and message boxes, launcher UI, and the app-level services (menu build, updates,
  autostart, taskbar pin, data location). Its theme layer is
  two directories: `Themes/Aero/` (the vendored MIT copies plus the wrapper that merges them) and
  `Themes/Shell/` (this app's own tokens and its launcher dialect).
- **`tests/TanMenu.Core.Tests`** — xUnit. Covers the on-disk JSON contracts (config, link cache, icon
  cache), the source-generated shell-link COM, the launcher's match/filter rules, the window-placement
  arithmetic, and the settings draft (the clone is independent of the live config and round-trips every
  retired field; a commit writes only its named list of properties and matches the tools by `Command`;
  `IsDirty` is computed, so an edit undone re-disables 应用; 开机自启's desire is written once and then
  cleared, even when the OS write throws).
  It also covers, by reading the `.axaml` and language sources directly rather than by rendering: the
  shell's token layer (no colour literals outside `Tokens.axaml`; every referenced token defined in
  both variants, whether by `Tokens.axaml` or by the vendored Aero layer it aliases onto); the shell
  constants (the caption height, the menu band's height, the command button's 75 × 23 and the minimum
  window width each agree across the two or three unlinked places that state them; no `/template/`
  selector names a part outside the allow-list); the Aero attribution (every vendored file carries the
  upstream MIT notice; no vendored file references the font that was deliberately not taken); the menu
  band (five commands in the order the design fixes, none with a submenu, no label ending in an
  ellipsis, distinct access keys, every label key present in both languages); the Options dialog
  (确定/取消/应用 in that order and right-aligned, no tab hosting a `ScrollViewer`, no tab header
  written as a literal); the About box (fixed, content-sized, one button, and 关于 gone from the
  Options dialog); the settings rows (no settings file restates a constant the theme owns, no row label
  carries a hard line break, every settings `Grid` is either the two-column template or has no columns
  at all); typography (no app markup states a font weight); and the 应用 page's category panel (the
  panel `Border`, never the `StackPanel` inside it, carries the category's visibility binding — with it
  on the inner panel a filtered-out category renders as an empty bordered box). Those are source tests
  because every failure they catch is silent at run time.

Tech: .NET 10 (`net10.0-windows10.0.19041.0`, x64), Avalonia 12.1, NativeAOT, Serilog,
source-generated `System.Text.Json`, `Microsoft.Extensions.DependencyInjection`, Velopack.

> **Why is the project still called `TanMenu.Wpf`?** Only the name is. The produced
> `TanMenu.Wpf.exe` filename is a compatibility contract: the per-user autostart Run value compares
> the stored path against `Environment.ProcessPath`, the taskbar-pin detector matches the pinned
> `.lnk`'s exact target, the MSIX manifest names it as the package `Executable`, and the released
> 0.9.6 Velopack shim resolves it when applying an update. Renaming it would silently break autostart
> and the taskbar pin for existing users and buy nothing user-visible — portable users run
> `TanMenu.exe` (the Velopack stub) and Store users see the tile.

## Features

- **Aero** UI drawn natively: an acrylic caption band carrying a three-stop glass gradient tinted by
  the user's accent, a 15-radius title glow (Microsoft's own `iGlowSize`), and an opaque client below
  it; categories laid out as horizontally-scrolling groups with column-filling tiles (each tile as wide
  as its own label, uniform within a column, never ellipsised); and one look rendered **light or dark**
  (following the OS by default). Colour comes from one token file, which aliases onto the vendored Aero
  palette; the accent is read from Windows personalization and now has exactly two jobs, the caption
  tint and the splash gradient. Each category sits in **its own group box** — a hairline outline at the
  3 DIP control radius, no fill — with an 11 DIP gutter between panels. The 应用 page is an opaque pane
  the colour of Explorer's list background, and the window's own surface runs unbroken from the menu
  band through it.
- **A classic main menu band**, 25 DIP tall, directly under the caption and inside the client area:
  设置 · 打开主目录 · 刷新 · 退出 · 关于. Every item is a **direct command** with an access key and no
  submenu — legal and rare, and published as such: *"A menu bar typically contains menu names, but it
  can also contain command items."* No icons and no ellipses (published for both). It replaced a
  160 DIP navigation pane whose five rows were, by the time it went, one page row and four commands —
  a navigation control for a shell with nothing left to navigate between.
- **Search-first**: the search field sits in the menu band, immediately after 关于 with one vertical
  separator between them — 320 DIP wide and 23 DIP tall, with the magnifier at its right end turning
  into a clear "×" once there is text. The band is *inside the client area*, which is what Microsoft's
  rule for a window frame asks for: *"Don't add controls to a window frame. Put the controls within the
  window instead."* Typing genuinely filters: non-matches are removed and empty sections disappear.
  `Ctrl+E` focuses it and selects what is there, `↑↓←→` walk the tile grid, `Enter` launches, `Esc`
  clears then hides.
- Folder scanning + `.lnk` resolution + real Win32 icon extraction. A shortcut whose target is gone
  stays visible but greyed and unlaunchable.
- **A resizable window that places itself.** It opens at 1000×688 DIP on a process's very first show
  and is then re-fitted to the 应用 page's own measured content. Wherever that lands, the window is
  horizontally centred on the **primary** screen's work area with its bottom edge **8 DIP** above the
  work-area bottom — on every launch and every re-fit — so it always comes back to one predictable
  place just above the taskbar. (The visible gap is a little under 8 DIP: the placement rectangle's
  bottom is the raw frame edge, and the DWM-visible edge sits inside it.) A window you have **dragged**
  stays where you put it until its content changes or you summon it again. `MinWidth` is 711 DIP —
  the content floor it has always had, with nothing beside the content to add to it now that the
  navigation pane is gone; there is deliberately no `MinHeight` — declaring one made
  Windows render the window 30 DIP taller than anything asked for — and the app's own 568 DIP height
  floor is applied to the placement *request* instead. Only the **maximized state** is remembered in
  config; neither the size nor the position is.
- Recall: permanent taskbar pin, system tray (left-click toggle, a 显示 / 设置 / 退出 menu), single
  instance.
- Optional **autostart** (per-user registry Run entry; deliberately unavailable in portable mode).
- A **modal Options dialog** — a Windows 7 property sheet, not a page and not a scrolling column.
  Five horizontal tabs: 外观 (language / light-dark / font / items per column), 行为 (auto-hide-on-blur
  and always-on-top, both off by default; autostart; taskbar pinning; a configurable global hotkey),
  文件夹 (main folder, a relocatable data folder, cache, backup/restore), 常用工具 and 更新. Nothing
  scrolls: the published rule is *"Don't scroll property pages"*, so the dialog is sized to its tallest
  tab instead, 711 × 479 DIP of client, which clears the published 800 × 600 floor.
- **Delayed commit, which is a change of behaviour and not only of layout.** Edits accumulate and are
  written only by 确定 or 应用; 取消 discards them and asks first if there are any. 应用 is disabled
  until something is actually pending — the dirty flag is computed by comparing against the live
  config, so changing a setting and changing it back disables it again. 开机自启 is buffered the same
  way, so a cancelled visit never touches the registry. Only the three actions that move or overwrite
  real data (change/reset the data folder, restore from backup) confirm before they run, as they always
  did — they act immediately, because they are commands rather than properties.
- **Windows 7 row grammar.** A settings row is a label in column 0 and its control in column 1 of a
  two-column grid on the published DLU ladder (5 label-to-control, 7 between related rows, 3 for a
  static line under the control it describes, 11 for the dialog margin) — no row icons, no subtitles
  and no row cards, all three of which were Windows 11 objects a property page has no counterpart for.
  Check boxes carry their own labels, and the five that were toggle switches are check boxes now, a
  control Windows 7 shipped. Only 文件夹 keeps group boxes, because it is the one tab holding four
  distinct sets rather than one; on the other four the tab's own header names the set. Guarded by
  source tests (`tests/TanMenu.Core.Tests/Themes/SettingsRowTests.cs`).
- 关于 is a real **About box** of its own — modal, sized to its content, one 确定 — showing the product
  tile, the publisher, the version and the copyright.
- **No bundled fonts.** The UI uses Segoe UI, with Microsoft YaHei UI for CJK. Segoe UI 9 pt *is* the
  Windows 7 system font, so this is more Aero-faithful than bundling anything would be — and it is a
  third of the payload saved. The vendored theme's own seven bundled TTFs were deliberately not taken:
  they carry no font licence upstream, and that family has no CJK coverage at all.

## Build & run (dev)

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
dotnet run --project src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
```

`dotnet build` does **not** run the AOT compiler, so a debug run proves nothing about
publishability. Behaviour that depends on the published shape — sounds, bundled tool icons, the
embedded window icon and product tile, the tray icon, shell COM — must be smoke-tested against a
published binary, not `dotnet run`.

## Publish (share as a self-contained EXE)

```powershell
dotnet publish src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Release /p:PublishProfile=win-x64
# output: src\TanMenu.Wpf\bin\publish\win-x64\TanMenu.Wpf.exe
```

- **Self-contained**: the target machine does **not** need .NET installed. This is now because the
  app is compiled ahead of time to native code, not because a .NET runtime is bundled with it.
- **Output shape**: a single native `TanMenu.Wpf.exe` (plus its native `.pdb`), the Avalonia native
  libraries, and the few assets the app reads from disk at run time — `app.ico`, `wwwroot\sounds`
  and `wwwroot\tool-icons`. The window icon and the product tile are embedded in the executable; **no
  font is** — the UI takes the system face. For scale: the
  previous self-contained ReadyToRun publish of this repo measured **564 files / 249 MB**; both
  packaging scripts print the file count and size of the payload they are about to ship, so use that
  number rather than an estimate.
- **Publish is slow**: ILC compiles and natively links the whole program in one pass. Expect minutes,
  not seconds.
- **`vswhere.exe` must be on `PATH`, and on a normal developer machine it is not.** ILC shells out to
  it to locate the MSVC linker and the Windows SDK libraries for the native link step, so without it
  the publish fails there — after several minutes of compiling, which is the annoying part. Visual
  Studio installs it in a fixed place that is not added to `PATH`, so prepend that directory for the
  session before publishing:

  ```powershell
  $env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH
  ```

  The two GitHub workflows do exactly this in a step named *Ensure vswhere is on PATH*, and
  `scripts\package-portable.ps1` runs a publish, so it needs the same.
- **Windows support**: Windows 10 version 2004 (build 19041) or later, x64. When Windows does not
  expose the app-initiated taskbar pin API, TanMenu opens its Start-menu shortcut for the supported
  manual **Pin to taskbar** action and detects the resulting Windows 10 `.lnk` pin on the next reveal.

## Portable ZIP

Build the no-install, self-contained portable edition:

```powershell
dotnet restore TanMenu.slnx
powershell -ExecutionPolicy Bypass -File scripts\package-portable.ps1
# output: dist\velopack\TanMenu-win-Portable.zip
```

The ZIP includes `portable.flag`. TanMenu keeps config, caches and logs under the stable `Data`
folder at the portable root, outside Velopack's replaceable application content. It checks GitHub
Releases automatically and shows an available version in the launcher; downloading and restarting
happen only after user confirmation. Extract it to a writable folder; delete that folder to uninstall
it. If you used **Options > Behavior > Pin to taskbar**, approve the Windows prompt when available;
on Windows 10, TanMenu may instead open the shortcut location so you can right-click it and choose
**Pin to taskbar**. Unpin TanMenu and remove its Start-menu shortcut as part of uninstalling; keep
the extracted folder at a stable path while pinned.

Pushing a tag that exactly matches `Directory.Build.props` (for example `v0.9.3`) runs the GitHub
Release workflow, builds and tests the app, then publishes the portable ZIP, its SHA-256 file, and
the Velopack update feed/assets. The same run also retains an unsigned Microsoft Store MSIX and its
SHA-256 as a GitHub Actions artifact; upload that package to Partner Center manually.

## Data location

Normal unpackaged builds keep writable data under `Documents\TanMenu` by default (relocatable in
settings; data predating this default is migrated from `%LOCALAPPDATA%\TanMenu` on first run):
- `config.json` (root folder + options)
- `cache\` (link/icon caches), `cache\logs\` (Serilog daily logs)

The portable ZIP keeps the same files under `<portable-root>\Data`. Store MSIX builds use
Windows-managed application data. See [`docs/store-release.md`](docs/store-release.md)
and [`docs/portable-release.md`](docs/portable-release.md) for release details.

Upgrading from 0.9.x leaves two harmless leftovers in an existing data folder, which the app no
longer reads or writes and deliberately does not delete: a `themes\` folder from the removed CSS
theme system, and (portable installs) `Data\WebView2` from the removed WebView. Delete them by hand
if you want the space back.

## Third-party notices

**[aero-theme-for-avalonia](https://github.com/Splitwirez/aero-theme-for-avalonia)** by Splitwirez,
**MIT**, at `main` = `a705b7c0`.

TanMenu does not reference that project as a package. Individual files are **copied** into
`src/TanMenu.Wpf/Themes/Aero/`, some of them edited, and each copy carries the upstream copyright
notice, a pointer to the permission notice, the revision it came from and a numbered record of what
was changed. The full MIT text is at
[`src/TanMenu.Wpf/Themes/Aero/LICENSE-aero-theme-for-avalonia.txt`](src/TanMenu.Wpf/Themes/Aero/LICENSE-aero-theme-for-avalonia.txt),
and it ships beside the executable as `THIRD-PARTY-NOTICES.txt` in both the portable ZIP and the
Store MSIX — a notice that exists only in a source tree does not travel with the binary, and the
binary is what users get.

What was taken, complete as of 0.14.0: the colour resources — the two per-variant `<Color>`
dictionaries (168 keys each) and the 110-key brush dictionary built from them — the metrics, three
value converters, one control (`Controls/HeaderGapClipper.cs`, without which a group box's label draws
on top of its outline instead of in a notch cut out of it), the `StyleTrailMix` style sheet, and
**fifteen control themes under their own upstream names**: `Button_RepeatButton_ToggleButton`,
`CheckBox`, `ToggleSwitch`, `ScrollViewer`, `ScrollBar`, `ListBox`, `ListBoxItem`, `TextBox`,
`NumericUpDown`, `ButtonSpinner`, `ProgressBar`, `HeaderedContentControl`, `Menu`, `TabControl` and
`TabItem` — the last three vendored for the menu band and the Options dialog. `ProgressBar` is a
partial copy — the determinate branch, about 60 of the original's 471 lines — and its own header
enumerates the ten differences; `TabControl` is byte-identical to upstream and says so. A sixteenth
copy ships under a different name: `ControlStyles/ComboBoxItem.axaml` is upstream's `ListBoxItem`
re-keyed to `ComboBoxItem`, because Avalonia keys a control theme by the control's own type and the
library has no drop-down row of its own; it carries the same header and change list as any other copy.
Only `ControlStyles/ComboBox.axaml` is written from scratch rather than copied: the library has no
`ComboBox` at all, so there is no upstream file for it to be a copy of, and its header names item by
item the vocabulary it does reuse. Two of the copies currently theme a control this app no longer
instantiates — `ToggleSwitch` (its five consumers became check boxes) and `ListBox`/`ListBoxItem` (the
navigation pane's rows) — and they stay: they are vendored files, the theme layer is not this app's to
prune, and `AeroResources.axaml` records each one as consumerless rather than pretending otherwise.
`src/TanMenu.Wpf/Themes/Aero/` is the authoritative list at any moment — 28 files today, of which the
licence text, the wrapper that merges the copies and the authored `ComboBox` are not copies — every
file in it carries its own header, and
`tests/TanMenu.Core.Tests/Themes/AeroAttributionTests.cs` fails the build if one of the other 25 does
not.

What was **not** taken, and why:

- **The bundled fonts.** Seven Inter TTFs, 2,536,600 bytes, which carry no font licence anywhere in
  the upstream tree. TanMenu deliberately embeds no fonts, and Inter has no CJK coverage — this app
  is Chinese-facing. The one upstream line that referenced them is deleted on vendoring.
- **`ControlStyles/Window.axaml`.** It is Avalonia 11's stock `Window` template with Aero brushes
  substituted, and it uses `VisualLayerManager.ChromeOverlayLayer` and `Avalonia.Controls.Chrome
  .TitleBar`, neither of which exists in Avalonia 12.1. TanMenu draws its own window caption anyway.

## Single-instance note

Uses the mutex name `TanMenuWpf` and a matching broadcast window message
(`TanMenuWpf.ShowFirstInstance`, via `RegisterWindowMessageW`) so it does **not** collide with the
original WinForms TanMenu (which uses `TanMenu`). Both can run side by side. The window's own visible
title (taskbar, Alt+Tab) is `TanMenu` — only the mutex/message identity string stays `TanMenuWpf`; it
is on-disk/runtime identity that must not change and is unrelated to the UI framework.
