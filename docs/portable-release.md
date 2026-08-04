# TanMenu portable GitHub release

The portable edition is a self-contained, no-install Windows x64 ZIP. It is separate from the Store
MSIX: a version tag publishes the portable ZIP and Velopack update feed to GitHub Releases and also
retains a Store submission package as an Actions artifact. Store submission remains manual.

## Portable runtime policy

`portable.flag` at the application root enables portable mode. In that mode:

- Config, caches, and logs are stored under `<portable-root>\Data`.
- Velopack may replace the application content directory during updates, but never the stable Data folder.
- The normal `Documents\TanMenu` migration and data-location registry pointer are not used.
- Windows autostart is disabled so the portable edition never creates a Run-key value.
- Taskbar integration is opt-in. **Options > Behavior > Pin to taskbar** (the English menu item is
  *Options*, published as the label a settings command takes; the Chinese one is 设置) creates the Start-menu
  shortcut required by Windows. TanMenu uses the user-approved Windows pin prompt where available;
  on Windows 10 systems that do not expose that API, it opens the shortcut in Explorer so the user
  can right-click **Pin to taskbar**. The next launcher reveal also checks Windows 10's traditional
  pinned-shortcut folder, so a completed manual pin is recognized without another prompt. The
  shortcut targets the stable root `TanMenu.exe`, so Velopack updates do not break it.
- The data folder cannot be relocated in Settings.

`portable.flag` inside the application content enables the portable data policy. Extract the ZIP to
a writable folder; running directly from the ZIP or placing it under a read-only directory is unsupported.
Keep the extracted folder at a stable path after pinning. To remove the portable edition completely,
unpin TanMenu, remove the `TanMenu` Start-menu shortcut, and then delete the extracted folder.

The portable build checks GitHub Releases after startup and shows an available version in the main
launcher. It does not download automatically: the user clicks the notice and confirms before the
download starts, then explicitly confirms the restart. The Store/MSIX build does not use Velopack
and remains managed by Microsoft Store.

The application is compiled ahead of time with NativeAOT and targets Windows 10 version 2004
(build 19041) or later on x64. Nothing has to be installed on the target machine: there is no .NET
runtime requirement and, since the WebView was removed, no Microsoft Edge WebView2 requirement either.

### Upgrading an existing 0.9.x install

The Velopack update keeps `<portable-root>\Data` untouched, so config, caches and logs survive, as
does an existing taskbar pin (it targets the stable root `TanMenu.exe`, whose name comes from
`--packId`, not from the executable inside the package). Two folders are left behind by the older
layout and are **not** deleted automatically, because both can contain data the user chose to keep:

- `Data\themes\` — from the removed CSS theme system.
- `Data\WebView2\` — the removed WebView's user-data folder, which can be a few hundred MB.

Neither is read or written any more. Users who want the space back can delete them by hand.

Because the payload changed from hundreds of managed files to a native executable, the first Velopack
delta after this migration is effectively a full download. That is expected, not an error.

## Build locally

From the repository root:

```powershell
dotnet restore TanMenu.slnx
# Install the vpk CLI at the SAME version as the Velopack SDK reference, so the two cannot drift:
[xml]$props = Get-Content Directory.Build.props -Raw
dotnet tool install --tool-path build\tools vpk --version ([string]$props.Project.PropertyGroup.VelopackVersion)
# -VpkPath is required: --tool-path does NOT put vpk on PATH, and the script's default is a bare
# "vpk", so omitting this fails at the packing step with "The term 'vpk' is not recognized".
powershell -ExecutionPolicy Bypass -File scripts\package-portable.ps1 -VpkPath .\build\tools\vpk.exe
```

`package-portable.ps1` runs a NativeAOT publish, so it takes several minutes and needs the MSVC
toolchain that ILC uses for the native link step (`vswhere.exe` must be resolvable; the CI workflows
add `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` when it is not already there).
Before staging, the script asserts the publish output really is native — a managed `TanMenu.Wpf.dll`
or `coreclr.dll` next to the exe means `PublishAot` did not apply — and prints the file count and
size of the payload it is about to ship.

Outputs:

```text
dist\velopack\TanMenu-win-Portable.zip
dist\velopack\TanMenu-win-Portable.zip.sha256
dist\velopack\releases.win.json
dist\velopack\*.nupkg
```

The ZIP is a Velopack portable distribution: it remains install-free while also supporting future
in-place updates.

## Publish a GitHub Release

1. Choose a version not used by an existing release.
2. Update `Directory.Build.props`, commit, and push `main`.
3. Create and push an exactly matching immutable tag:

```powershell
git tag v0.9.3
git push origin v0.9.3
```

`.github/workflows/github-release.yml` validates the tag, restores, builds, runs the tests, builds
the ZIP and update assets, and creates a public GitHub Release using the repository-provided
`GITHUB_TOKEN`. No custom secrets or GitHub Environment are required.

If a release attempt fails before the GitHub Release is created, fix the source and use a new higher
version. Do not move a published tag to different source code. Because an AOT publish only happens
at publish time, a push to `main` also runs a publish smoke job in `ci.yml` — that is the check that
keeps an ILC failure from being discovered for the first time at tag time.
