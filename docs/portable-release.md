# TanMenu portable GitHub release

The portable edition is a self-contained, no-install Windows x64 ZIP. It is separate from the Store
MSIX: Store updates are packaged locally and uploaded manually, while a version tag publishes the
portable ZIP and Velopack update feed to GitHub Releases automatically.

## Portable runtime policy

`portable.flag` at the application root enables portable mode. In that mode:

- Config, themes, caches, and logs are stored under `<portable-root>\Data`.
- WebView2 user data is stored under `<portable-root>\Data\WebView2`.
- Velopack may replace the application content directory during updates, but never the stable Data folder.
- The normal `Documents\TanMenu` migration and data-location registry pointer are not used.
- Windows autostart is disabled so the portable edition never creates a Run-key value.
- The data folder cannot be relocated in Settings.

`portable.flag` inside the application content enables the portable data policy. Extract the ZIP to
a writable folder; running directly from the ZIP or placing it under a read-only directory is unsupported.

The portable build checks GitHub Releases after startup, downloads an available update in the
background, and waits for the user to confirm restart in Settings. The Store/MSIX build does not use
Velopack and remains managed by Microsoft Store.

The application is self-contained for .NET 10 but still requires Microsoft Edge WebView2 Evergreen
Runtime. It is normally present on Windows 11.

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
git tag v0.9.2
git push origin v0.9.2
```

`.github/workflows/github-release.yml` validates the tag, restores, builds, runs the tests, builds
the ZIP and update assets, and creates a public GitHub Release using the repository-provided
`GITHUB_TOKEN`. No custom secrets or GitHub Environment are required.

If a release attempt fails before the GitHub Release is created, fix the source and use a new higher
version. Do not move a published tag to different source code.
