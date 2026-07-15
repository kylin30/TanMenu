# TanMenu (WPF + WindowChrome + BlazorWebView)

A retro-styled Windows desktop launcher: a borderless, content-sized popup that sits
bottom-center above the taskbar, scans configured folders for apps / `.lnk` shortcuts,
and launches them. Pin its permanent taskbar entry for one-click recall, or use the tray fallback.

This is the WPF host rewrite of the original WinForms+BlazorWebView TanMenu — it keeps the
existing retro HTML/CSS UI (rendered in a `BlazorWebView`) and hosts it in a native WPF
`WindowChrome` window for a clean borderless/transparent frame.

## Architecture

- **`src/TanMenu.Core`** — framework-agnostic, unit-tested logic: config, folder scanning,
  `.lnk` resolution, Win32 icon extraction (→ PNG bytes), launching, sounds, AppData paths.
- **`src/TanMenu.Wpf`** — WPF host (`WindowChrome` + `BlazorWebView`), the ported retro Blazor
  UI, and native services (tray, single instance, autostart, window placement).
- **`tests/TanMenu.Core.Tests`** — xUnit (52 tests).

Tech: .NET 10 (`net10.0-windows10.0.19041.0`, x64), WPF, `Microsoft.AspNetCore.Components.WebView.Wpf`,
`H.NotifyIcon.Wpf`, Serilog, System.Text.Json.

## Features

- Retro **Windows 98 / XP / 7** themed UI (transparent `WindowChrome` window, CSS-drawn frame),
  switchable at runtime; invalid shortcuts still get a default icon.
- Folder scanning + `.lnk` resolution + real Win32 icon extraction.
- Content-sized horizontal window with no scrollbars, locked bottom-center on the primary display,
  and hidden automatically when focus is lost.
- Recall: permanent taskbar pin, system tray (left-click toggle, 显示/退出 menu), single instance.
- Optional **autostart** (per-user registry Run entry; deliberately unavailable in portable mode).
- Native **settings** window grouped by function: root folder, theme, font, columns per group,
  hide-on-blur/TopMost, one-click taskbar pinning, autostart, and a relocatable data folder.

## Build & run (dev)

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
dotnet run --project src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
```

## Publish (share as a self-contained EXE)

```powershell
dotnet publish src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Release /p:PublishProfile=win-x64
# output: src\TanMenu.Wpf\bin\publish\win-x64\TanMenu.Wpf.exe
```

- **Self-contained**: the target machine does **not** need .NET installed.
- **WebView2 runtime**: required (the Evergreen runtime is preinstalled on Windows 11). For older
  Windows 10 targets, bundle the `MicrosoftEdgeWebView2RuntimeInstallerX64` bootstrapper.
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

The ZIP includes `portable.flag`. TanMenu keeps config, themes, caches, logs, and WebView2 browser
data under the stable `Data` folder at the portable root, outside Velopack's replaceable application
content. It checks GitHub Releases automatically, downloads an available update, and waits for the
user to confirm the restart in Settings. Extract it to a writable folder and delete the whole folder
to uninstall it. If you used **Settings > Behavior > Pin to taskbar**, approve the Windows prompt
when available; on Windows 10, TanMenu may instead open the shortcut location so you can right-click
it and choose **Pin to taskbar**. Unpin TanMenu and remove its Start-menu shortcut as part of
uninstalling; keep the extracted folder at a stable path while pinned.

Pushing a tag that exactly matches `Directory.Build.props` (for example `v0.9.3`) runs the GitHub
Release workflow, builds and tests the app, then publishes the portable ZIP, its SHA-256 file, and
the Velopack update feed/assets.
Microsoft Store packages continue to be built locally and uploaded to Partner Center manually.

## Data location

Normal unpackaged builds keep writable data under `Documents\TanMenu` by default (relocatable in
settings; data predating this default is migrated from `%LOCALAPPDATA%\TanMenu` on first run):
- `config.json` (root folder + options)
- `cache\` (link/icon caches), `cache\logs\` (Serilog daily logs)

The portable ZIP instead keeps the same files, plus WebView2 user data, under `<portable-root>\Data`. Store
MSIX builds use Windows-managed application data. See [`docs/store-release.md`](docs/store-release.md)
and [`docs/portable-release.md`](docs/portable-release.md) for release details.

## Single-instance note

Uses a WPF-app-specific mutex/window name (`TanMenuWpf`) so it does **not** collide with the
original WinForms TanMenu (which uses `TanMenu`). Both can run side by side during migration.
