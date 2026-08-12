# TanMenu

TanMenu is a fast Windows desktop launcher built with .NET 10 and Avalonia 12. It scans a folder
tree into launcher groups, provides instant search, and stays available from the notification area
or a global shortcut. Release packages are compiled with .NET NativeAOT.

## Interface

The application follows the current Windows app model:

- FluentAvalonia 3 supplies the Fluent v2 theme and WinUI-style `FAAppWindow`, `FANavigationView`,
  `FACommandBar`, `FAInfoBar` and `FASettingsExpander` controls.
- The main window uses adaptive navigation with the Settings entry fixed at the bottom. Search and
  launcher-only commands live in the Apps page header.
- Launcher groups are distinct horizontal panels. Each panel fills application columns from top to
  bottom using a configurable item count (10 by default), while the window fits itself to the full row.
- Settings fill the main window, use a single scrollable column of settings cards, and save changes
  immediately without OK, Cancel or Apply buttons. About information is at the bottom of Settings.
- Alibaba PuHuiTi is embedded in the executable, and button hover/click sounds can be switched
  independently.
- Windows keeps ownership of title bars, borders, resize behavior, light/dark appearance, focus
  states and accessibility visuals.

The retired Windows 7 Aero library and the hand-built window/control themes are not part of the
project or its packages.

## Features

- Folder-backed launcher groups and built-in common tools
- Search with keyboard navigation (`Ctrl+E`, Enter, Escape and arrow keys)
- Notification-area recall, optional global shortcut and hide-on-focus-loss behavior
- System, light and dark appearance modes with installed-font selection
- Configurable folders and common tools
- Configurable application count per column (10 by default)
- Independent application-button hover and click sounds
- Taskbar pin guidance
- Portable data mode and opt-in in-app updates
- Windows x64 support from Windows 10 version 2004 (build 19041)

## Build and test

Requirements: Windows, .NET 10 SDK, and the Windows desktop build tools required by NativeAOT.

```powershell
dotnet restore TanMenu.slnx
dotnet build TanMenu.slnx -c Release
dotnet test tests\TanMenu.Core.Tests\TanMenu.Core.Tests.csproj -c Release
```

For a local publish:

```powershell
dotnet publish src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Release /p:PublishProfile=win-x64
```

For the release-ready portable ZIP, install the matching Velopack CLI and run:

```powershell
[xml]$props = Get-Content Directory.Build.props -Raw
dotnet tool install --tool-path build\tools vpk --version ([string]$props.Project.PropertyGroup.VelopackVersion)
powershell -ExecutionPolicy Bypass -File scripts\package-portable.ps1 -VpkPath .\build\tools\vpk.exe
```

See [docs/portable-release.md](docs/portable-release.md) for packaging and update details.

## Repository layout

- `src/TanMenu.Core` — configuration, menu discovery, launching and platform-independent services
- `src/TanMenu.Wpf` — Avalonia desktop application and Fluent UI
- `tests/TanMenu.Core.Tests` — behavior and UI-structure regression tests
- `scripts` — portable and MSIX packaging
- `packaging` — manifests, portable readme and release notes

Portable builds keep settings, caches and logs in a `Data` folder beside the application. Installed
builds use the configured application data location.
