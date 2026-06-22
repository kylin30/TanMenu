# TanMenu (WPF + WindowChrome + BlazorWebView)

A retro-styled Windows desktop launcher: a borderless, content-sized popup that sits
bottom-center above the taskbar, scans configured folders for apps / `.lnk` shortcuts,
and launches them. Recall it any time from the system-tray icon.

This is the WPF host rewrite of the original WinForms+BlazorWebView TanMenu — it keeps the
existing retro HTML/CSS UI (rendered in a `BlazorWebView`) and hosts it in a native WPF
`WindowChrome` window for a clean borderless/transparent frame.

## Architecture

- **`src/TanMenu.Core`** — framework-agnostic, unit-tested logic: config, folder scanning,
  `.lnk` resolution, Win32 icon extraction (→ PNG bytes), launching, sounds, AppData paths.
- **`src/TanMenu.Wpf`** — WPF host (`WindowChrome` + `BlazorWebView`), the ported retro Blazor
  UI, and native services (tray, single instance, autostart, window placement).
- **`tests/TanMenu.Core.Tests`** — xUnit (32 tests).

Tech: .NET 10 (`net10.0-windows10.0.19041.0`, x64), WPF, `Microsoft.AspNetCore.Components.WebView.Wpf`,
`H.NotifyIcon.Wpf`, Serilog, System.Text.Json.

## Features

- Retro **Windows 98 / XP / 7** themed UI (transparent `WindowChrome` window, CSS-drawn frame),
  switchable at runtime; invalid shortcuts still get a default icon.
- Folder scanning + `.lnk` resolution + real Win32 icon extraction.
- Content-sized window, bottom-center above the taskbar, hide-on-blur.
- Recall: system tray (left-click toggle, 显示/退出 menu), single instance.
- Optional **autostart** (per-user registry Run entry).
- Native **settings** window grouped by function: root folder, theme, font, columns per group,
  AutoClose/TopMost/ShowInTaskbar, autostart, and a relocatable data folder.

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

## Data location

All writable data lives under `Documents\TanMenu` by default (relocatable in settings; data
pre-dating this default is migrated from the old `%LOCALAPPDATA%\TanMenu` on first run):
- `config.json` (root folder + options)
- `cache\` (link/icon caches), `cache\logs\` (Serilog daily logs)

Nothing is written next to the EXE (install-dir-safe; ready for MSIX). See
[`docs/msix-readiness.md`](docs/msix-readiness.md) for the path to a Store MSIX build (deferred).

## Single-instance note

Uses a WPF-app-specific mutex/window name (`TanMenuWpf`) so it does **not** collide with the
original WinForms TanMenu (which uses `TanMenu`). Both can run side by side during migration.
