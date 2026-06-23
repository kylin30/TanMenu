# MSIX-readiness notes (packaging deferred)

This phase ships a **self-contained EXE** (see README). The code is structured to be wrapped as a
full-trust MSIX for the Microsoft Store later **without architectural changes**. When ready:

## What's already MSIX-ready
- **Writable data** goes through `TanMenu.Core.Infrastructure.IAppDataPaths`. The running app
  (`App.OnStartup`) uses **`MutableAppDataPaths`** over `DataLocation.GetDataRoot()` — the default is
  **`Documents\TanMenu`** (an HKCU pointer records a user-chosen folder; a legacy
  `%LOCALAPPDATA%\TanMenu` is migrated once on first run). A parameterless `AppDataPaths()` ctor and
  `PackageRuntime.HasPackageIdentity` exist for the packaged path but are **not yet wired into
  startup** — see step 3.
- **No hardcoded personal paths**, nothing written next to the EXE.
- TFM is `net10.0-windows10.0.19041.0` (WinRT projection available for `StartupTask`/`ApplicationData`).

## Steps to package (when desired)
1. Wrap with a single-project MSIX (or a `.wapproj` Windows Application Packaging Project) referencing `TanMenu.Wpf`.
2. Declare `runFullTrust` (rescap). Full-trust = direct `System.IO`, no `broadFileSystemAccess`.
3. Route the data root through `PackageRuntime`: when packaged, build the writable paths from
   `Windows.Storage.ApplicationData`. Do NOT just swap to the parameterless `new AppDataPaths()` —
   `App` currently uses `MutableAppDataPaths`/`DataLocation` (Documents\TanMenu + HKCU pointer + live
   data-folder relocation), and a blind swap would drop that relocation feature.
4. Replace `RegistryAutoStartService` with a `windows.startupTask`-backed `IAutoStartService`
   (manifest `<desktop:Extension Category="windows.startupTask" .../>`, `Windows.ApplicationModel.StartupTask`).
   The `IAutoStartService` interface already isolates this swap.
5. Bundle WebView2 as a framework dependency / ensure the Evergreen runtime requirement is declared.
6. Store submission: reserve the name in Partner Center, provide a **privacy policy URL**
   (folder enumeration triggers Policy 10.5.1), justify `runFullTrust`, address Policy 10.1
   ("distinct value" — the retro UX is the angle), supply Store assets.

## Known caveat
- WebView2 transparency under `WindowChrome` + `AllowsTransparency` works (validated). Re-verify
  after any WebView2 SDK bump.
