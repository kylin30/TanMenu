# TanMenu portable GitHub release

The portable edition is a self-contained, no-install Windows x64 ZIP. It is separate from the Store
MSIX: a version tag publishes the portable ZIP and Velopack update feed to GitHub Releases and also
retains a Store submission package as an Actions artifact. Store submission remains manual.

## Portable runtime policy

`portable.flag` at the application root enables portable mode. In that mode:

- Config, themes, caches, and logs are stored under `<portable-root>\Data`.
- WebView2 user data is stored under `<portable-root>\Data\WebView2`.
- Velopack may replace the application content directory during updates, but never the stable Data folder.
- The normal `Documents\TanMenu` migration and data-location registry pointer are not used.
- Windows autostart is disabled so the portable edition never creates a Run-key value.
- Taskbar integration is opt-in. **Settings > Behavior > Pin to taskbar** creates the Start-menu
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

The application is self-contained for .NET 10 and targets Windows 10 version 2004 (build 19041) or
later on x64. It still requires Microsoft Edge WebView2 Evergreen Runtime, which is normally present
on Windows 11 but may need to be installed separately on Windows 10.

## Build locally

From the repository root:

```powershell
dotnet restore TanMenu.slnx
dotnet tool install --tool-path build\tools vpk --version 1.2.0
powershell -ExecutionPolicy Bypass -File scripts\package-portable.ps1
```

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
version. Do not move a published tag to different source code.
