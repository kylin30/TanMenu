# TanMenu portable GitHub release

The portable edition is a self-contained, no-install Windows x64 ZIP. It is separate from the Store
MSIX: Store updates are packaged locally and uploaded manually, while a version tag publishes the
portable ZIP to GitHub Releases automatically.

## Portable runtime policy

`portable.flag` at the application root enables portable mode. In that mode:

- Config, themes, caches, and logs are stored under `<app>\Data`.
- WebView2 user data is stored under `<app>\Data\WebView2`.
- The normal `Documents\TanMenu` migration and data-location registry pointer are not used.
- Windows autostart is disabled so the portable edition never creates a Run-key value.
- The data folder cannot be relocated in Settings.

Removing `portable.flag` changes the application back to normal unpackaged behavior, so the marker
must remain beside `TanMenu.Wpf.exe`. Extract the ZIP to a writable folder; running directly from the
ZIP or placing it under a read-only directory is unsupported.

The application is self-contained for .NET 10 but still requires Microsoft Edge WebView2 Evergreen
Runtime. It is normally present on Windows 11.

## Build locally

From the repository root:

```powershell
dotnet restore TanMenu.slnx
powershell -ExecutionPolicy Bypass -File scripts\package-portable.ps1
```

Outputs:

```text
dist\portable\TanMenu_<version>_win-x64_portable.zip
dist\portable\TanMenu_<version>_win-x64_portable.zip.sha256
```

The ZIP contains one versioned top-level folder so extracting it does not scatter files into the
current directory.

## Publish a GitHub Release

1. Choose a version not used by an existing release.
2. Update `Directory.Build.props`, commit, and push `main`.
3. Create and push an exactly matching immutable tag:

```powershell
git tag v0.9.2
git push origin v0.9.2
```

`.github/workflows/github-release.yml` validates the tag, restores, builds, runs the tests, builds
the ZIP, and creates a public GitHub Release using the repository-provided `GITHUB_TOKEN`. No custom
secrets or GitHub Environment are required.

If a release attempt fails before the GitHub Release is created, fix the source and use a new higher
version. Do not move a published tag to different source code.
