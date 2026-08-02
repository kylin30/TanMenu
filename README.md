# TanMenu (Avalonia + NativeAOT)

A Windows desktop launcher: an ordinary Windows 11 application window — Task Manager's shape, with a
Mica backdrop, an app-drawn 48 DIP title strip (app name on the left, search box centred) and a
48 DIP icon-only navigation rail on the left — 应用 at the top, and a bottom-pinned footer of
退出 · 关于 · 打开主目录 · 刷新 · 设置. The 应用 page scans configured folders for apps / `.lnk`
shortcuts and lays them out as horizontally-scrolling categories; the 设置 page holds every setting as
a Windows 11 setting card, applied immediately, with no separate settings window any more. Pin its
permanent taskbar entry for one-click recall, or use the tray fallback.

The UI is drawn with native Avalonia controls in the **Fluent 2** design language — a Mica backdrop
(an opaque Fluent 2 surface colour where the OS refuses it), a 4 px control radius, the system UI
face, the user's Windows accent colour, and light/dark that follows the OS — and published with
NativeAOT. Earlier versions hosted an HTML/CSS UI in a `BlazorWebView` inside a WPF window with
switchable CSS theme packs, then a light-dismiss acrylic flyout with a separate settings dialog; both
were replaced by the current shell.

## Architecture

- **`src/TanMenu.Core`** — framework-agnostic, unit-tested logic: config, folder scanning,
  `.lnk` resolution (source-generated COM), Win32 icon extraction (→ PNG bytes), launching, sounds,
  AppData paths, and the source-generated `System.Text.Json` contracts.
- **`src/TanMenu.Wpf`** — the Avalonia application: window host and placement, tray icon, global
  hotkey, the nav-rail shell (应用/设置 pages), the settings page's card groups, launcher UI, and the
  app-level services (menu build, updates, autostart, taskbar pin, data location).
- **`tests/TanMenu.Core.Tests`** — xUnit. Covers the on-disk JSON contracts (config, link cache, icon
  cache), the source-generated shell-link COM, the launcher's match/filter rules, and the Fluent 2
  token layer (no colour literals outside `Tokens.axaml`; every referenced token defined in both
  variants).

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

- **Fluent 2** UI drawn natively: a Mica backdrop with an opaque fallback, categories laid out as
  horizontally-scrolling groups with column-filling tiles (each tile as wide as its own label, uniform
  within a column, never ellipsised), and one look rendered **light or dark** (following the OS by
  default). Colour comes from one token file; the accent is read from Windows personalization. The
  应用 page is a pure white pane under the shared Mica of the title strip and rail; 设置 keeps the
  grey surface, because its cards are a translucent white that would vanish on a white page.
- **Search-first**: the search box sits centred in the title strip and typing genuinely filters —
  non-matches are removed and empty sections disappear. `↑↓←→` walk the tile grid, `Enter` launches,
  `Esc` clears then hides.
- Folder scanning + `.lnk` resolution + real Win32 icon extraction. A shortcut whose target is gone
  stays visible but greyed and unlaunchable.
- **A resizable window that places itself.** It opens at 1000×688 DIP on a process's very first show
  and is then re-fitted to the 应用 page's own measured content. Wherever that lands, the window is
  horizontally centred on the **primary** screen's work area with its bottom edge **8 DIP** above the
  work-area bottom — on every launch and every re-fit — so it always comes back to one predictable
  place just above the taskbar. (The visible gap is a little under 8 DIP: the placement rectangle's
  bottom is the raw frame edge, and the DWM-visible edge sits inside it.) A window you have **dragged**
  stays where you put it until its content changes or you summon it again. `MinWidth` is 760 DIP;
  there is deliberately no `MinHeight` — declaring one made Windows render the window 30 DIP taller
  than anything asked for — and the app's own 568 DIP height floor is applied to the placement
  *request* instead. Only the **maximized state** is remembered in config; neither the size nor the
  position is.
- Recall: permanent taskbar pin, system tray (left-click toggle, show/exit menu), single instance.
- Optional **autostart** (per-user registry Run entry; deliberately unavailable in portable mode).
- A **settings page** (not a separate window) grouped into Windows 11 setting cards — appearance
  (language/light-dark/font/items per column), behavior (auto-hide-on-blur and always-on-top, both off by
  default; autostart; taskbar pinning; a configurable global hotkey), folders (main folder, a
  relocatable data folder, cache/backup/restore), common tools, and updates. Every control applies
  immediately; only the three actions that move or overwrite real data (change/reset the data folder,
  restore from backup) still confirm first.
- **No bundled fonts.** The UI uses Segoe UI Variable Text → Segoe UI, with Microsoft YaHei UI for
  CJK, which is both the Fluent 2 typeface and a third of the payload saved.

## Build & run (dev)

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
dotnet run --project src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
```

`dotnet build` does **not** run the AOT compiler, so a debug run proves nothing about
publishability. Behaviour that depends on the published shape — sounds, bundled tool icons, embedded
fonts, the tray icon, shell COM — must be smoke-tested against a published binary, not `dotnet run`.

## Publish (share as a self-contained EXE)

```powershell
dotnet publish src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Release /p:PublishProfile=win-x64
# output: src\TanMenu.Wpf\bin\publish\win-x64\TanMenu.Wpf.exe
```

- **Self-contained**: the target machine does **not** need .NET installed. This is now because the
  app is compiled ahead of time to native code, not because a .NET runtime is bundled with it.
- **Output shape**: a single native `TanMenu.Wpf.exe` (plus its native `.pdb`), the Avalonia native
  libraries, and the few assets the app reads from disk at run time — `app.ico`, `wwwroot\sounds`
  and `wwwroot\tool-icons`. Fonts and the window icon are embedded in the executable. For scale: the
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

## Single-instance note

Uses the mutex name `TanMenuWpf` and a matching broadcast window message
(`TanMenuWpf.ShowFirstInstance`, via `RegisterWindowMessageW`) so it does **not** collide with the
original WinForms TanMenu (which uses `TanMenu`). Both can run side by side. The window's own visible
title (taskbar, Alt+Tab) is `TanMenu` — only the mutex/message identity string stays `TanMenuWpf`; it
is on-disk/runtime identity that must not change and is unrelated to the UI framework.
