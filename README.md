# TanMenu (Avalonia + NativeAOT)

A Windows desktop launcher drawn as a **Windows 7 Aero** application window: a hand-drawn 30 DIP
glass caption (app icon, title, and minimize / maximize / close at the measured 28 / 27 / 48 DIP) over
an opaque client, and a 160 DIP wide **labelled** navigation pane on the left — 应用 at the top, and a
bottom-pinned footer of 打开主目录 · 刷新 · 设置, then a hairline separator, then 退出 last. The 应用
page scans configured folders for apps / `.lnk` shortcuts and lays them out as horizontally-scrolling
categories, each in its own group box, with the search field at the top of the page; the 设置 page is
a Windows 7 property page — one scrolling column of group boxes, every setting applied immediately,
with no separate settings window and no OK/Cancel/Apply. Pin its permanent taskbar entry for one-click
recall, or use the tray fallback.

The UI is drawn with native Avalonia controls: the control visuals are **copied**, file by file, from
the MIT-licensed [aero-theme-for-avalonia](https://github.com/Splitwirez/aero-theme-for-avalonia)
(see [Third-party notices](#third-party-notices)) rather than taken as a dependency, and everything
the copies do not cover — the caption, the navigation pane, the launcher's own dialect — is authored
against the Windows 7 UX Guide, with each number labelled MEASURED, PUBLISHED, VENDORED or AUTHORED
in the file that states it. Type is Segoe UI at 9 pt (12 DIP), spacing is Aero's published
5 / 7 / 11 ladder, and the control radius is 3. Published with NativeAOT.

**Three deliberate departures, stated rather than hidden.** Aero's glass was a live blur plus a colour
tint, a bright inner reflection and a specular sweep; Avalonia's `AcrylicBlur` supplies the blur only,
so the tint and the reflection are drawn on top of it and the sweep is not reproduced — **a
substitution, not a match**. Windows 7 shipped High Contrast, an accessibility mode, not a dark
variant, so **dark mode is an anachronism** kept because the vendored palette has both variants and
users expect the choice. And Windows 7 had no icon *font* at all — its UI icons were full-colour
raster assets inside `shell32.dll`, which are not redistributable — so the app's icon glyphs (26
instances over 21 code points, counted in the markup) are still **Segoe Fluent Icons**, the second
anachronism. The one exception predates the conversion and survives it: 关于's mark is the product
tile bitmap. The three caption buttons are not in that count either — their glyphs are vector `Path`
geometry: one line for minimize, a closed square for maximize, two crossed lines for close.

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
  hotkey, the nav-pane shell (应用/设置 pages), the settings page's group boxes, launcher UI, and the
  app-level services (menu build, updates, autostart, taskbar pin, data location). Its theme layer is
  two directories: `Themes/Aero/` (the vendored MIT copies plus the wrapper that merges them) and
  `Themes/Shell/` (this app's own tokens and its launcher dialect).
- **`tests/TanMenu.Core.Tests`** — xUnit. Covers the on-disk JSON contracts (config, link cache, icon
  cache), the source-generated shell-link COM, the launcher's match/filter rules, the shell's
  token layer (no colour literals outside `Tokens.axaml`; every referenced token defined in both
  variants, whether by `Tokens.axaml` or by the vendored Aero layer it aliases onto), the shell
  constants (the caption height, the pane width and the minimum window width each agree across the
  two or three unlinked places that state them; no `/template/` selector names a part that does not
  exist), the Aero attribution (every vendored file carries the upstream MIT notice; no vendored file
  references the font that was deliberately not taken), the settings row spec (every card wraps the
  standard column Grid; no card restates a constant the theme owns; every header is exactly one title
  line and one subtitle line; no subtitle wording carries a hard line break), and the 应用 page's
  category panel (the panel `Border`, never the `StackPanel` inside it, carries the category's
  visibility binding — with it on the inner panel a filtered-out category renders as an empty bordered
  box). Those last five read the `.axaml` and language sources directly — they are source tests,
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
  the colour of Explorer's list background; **every other page paints nothing at all**, so the window's
  own surface runs unbroken from the navigation pane through the content.
- **Search-first**: the search field sits at the top of the 应用 page — Microsoft's own rule for a
  window frame is *"Don't add controls to a window frame. Put the controls within the window instead."*
  — 320 DIP wide and 23 DIP tall, with the magnifier at its right end turning into a clear "×" once
  there is text. Typing genuinely filters: non-matches are removed and empty sections disappear.
  `Ctrl+E` focuses it and selects what is there, `↑↓←→` walk the tile grid, `Enter` launches, `Esc`
  clears then hides.
- **A wide labelled navigation pane**, 160 DIP, replacing the icon-only rail: every row is a 16 DIP
  glyph docked left with its own visible label, so there are no tooltips repeating labels any more.
  The page rows are Aero list rows with Aero's own four states; the command rows are Buttons, which
  have no `:selected` state at all, so a one-shot action can never look like a destination.
- Folder scanning + `.lnk` resolution + real Win32 icon extraction. A shortcut whose target is gone
  stays visible but greyed and unlaunchable.
- **A resizable window that places itself.** It opens at 1112×688 DIP on a process's very first show
  and is then re-fitted to the 应用 page's own measured content. Wherever that lands, the window is
  horizontally centred on the **primary** screen's work area with its bottom edge **8 DIP** above the
  work-area bottom — on every launch and every re-fit — so it always comes back to one predictable
  place just above the taskbar. (The visible gap is a little under 8 DIP: the placement rectangle's
  bottom is the raw frame edge, and the DWM-visible edge sits inside it.) A window you have **dragged**
  stays where you put it until its content changes or you summon it again. `MinWidth` is 872 DIP —
  it moved from 760 with the navigation pane's 112 DIP growth, so that the content pane's own floor
  stays at the 711 DIP it has always been; there is deliberately no `MinHeight` — declaring one made
  Windows render the window 30 DIP taller than anything asked for — and the app's own 568 DIP height
  floor is applied to the placement *request* instead. Only the **maximized state** is remembered in
  config; neither the size nor the position is.
- Recall: permanent taskbar pin, system tray (left-click toggle, a 显示 / 设置 / 退出 menu), single
  instance.
- Optional **autostart** (per-user registry Run entry; deliberately unavailable in portable mode).
- A **settings page** (not a separate window) laid out as a Windows 7 property page: one scrolling
  column of six group boxes, each with its label notched into the top of its own outline, and no
  OK/Cancel/Apply because nothing waits to be applied. The six are
  appearance (language/light-dark/font/items per column), behavior (auto-hide-on-blur and always-on-top,
  both off by default; autostart; taskbar pinning; a configurable global hotkey), folders (main folder, a
  relocatable data folder, cache/backup/restore), common tools, updates, and 关于 (the product tile, the
  publisher, the current version and the copyright — there is no About window, no overlay and no rail
  row). Every control applies immediately; only the three actions that move or
  overwrite real data (change/reset the data folder, restore from backup) still confirm first.
  All **sixteen** rows across those groups obey one row spec — a 16 DIP icon, a header of exactly one
  title line and one subtitle line, the control trailing with its right edge on one shared line, and any
  extra content on a row of its own starting at the title's left edge — carried by shared style classes
  in `Themes/Shell/Launcher.axaml` and guarded by source tests
  (`tests/TanMenu.Core.Tests/Themes/SettingsRowTests.cs`). That spec is the one part of the Windows 11
  shell the Aero conversion kept on purpose: it is information architecture, not paint.
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
  not seconds, and note that the native link step needs the MSVC toolchain (`vswhere.exe` must be
  resolvable — see the CI workflows).
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
it. If you used **Settings > Behavior > Pin to taskbar**, approve the Windows prompt when available;
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

What was taken, complete as of 0.13.0: the colour resources — the two per-variant `<Color>`
dictionaries (168 keys each) and the 110-key brush dictionary built from them — the metrics, two value
converters, one control (`Controls/HeaderGapClipper.cs`, without which a group box's label draws on
top of its outline instead of in a notch cut out of it), the `StyleTrailMix` style sheet, and **twelve
control themes under their own upstream names**: `Button_RepeatButton_ToggleButton`, `CheckBox`,
`ToggleSwitch`, `ScrollViewer`, `ScrollBar`, `ListBox`, `ListBoxItem`, `TextBox`, `NumericUpDown`,
`ButtonSpinner`, `ProgressBar` and `HeaderedContentControl`. `ProgressBar` is a partial copy — the
determinate branch, about 60 of the original's 471 lines — and its own header enumerates the ten
differences. A thirteenth copy ships under a different name: `ControlStyles/ComboBoxItem.axaml` is
upstream's `ListBoxItem` re-keyed to `ComboBoxItem`, because Avalonia keys a control theme by the
control's own type and the library has no drop-down row of its own; it carries the same header and
change list as any other copy. Only `ControlStyles/ComboBox.axaml` is written from scratch rather than
copied: the library has no `ComboBox` at all, so there is no upstream file for it to be a copy of, and
its header names item by item the vocabulary it does reuse. `src/TanMenu.Wpf/Themes/Aero/` is the
authoritative list at any moment, every file in it carries its own header, and
`tests/TanMenu.Core.Tests/Themes/AeroAttributionTests.cs` fails the build if one does not.

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
