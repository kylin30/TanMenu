# MSIX-readiness notes (packaging deferred)

This phase ships a **self-contained EXE** (see README). The code is structured to be wrapped as a
full-trust MSIX for the Microsoft Store later **without architectural changes**. When ready:

## What's already MSIX-ready
- **Writable data** goes through `TanMenu.Core.Infrastructure.IAppDataPaths`. The unpackaged build
  uses the 2-arg `AppDataPaths(localFolder, localCacheFolder)` → `%LOCALAPPDATA%\TanMenu`. The
  parameterless `AppDataPaths()` ctor already detects package identity
  (`PackageRuntime.HasPackageIdentity`) and uses `Windows.Storage.ApplicationData` automatically
  when packaged — so a packaged build just switches which ctor `App` calls.
- **No hardcoded paths**, nothing written next to the EXE.
- TFM is `net10.0-windows10.0.19041.0` (WinRT projection available for `StartupTask`/`ApplicationData`).

## Steps to package (when desired)
1. Wrap with a single-project MSIX (or a `.wapproj` Windows Application Packaging Project) referencing `TanMenu.Wpf`.
2. Declare `runFullTrust` (rescap). Full-trust = direct `System.IO`, no `broadFileSystemAccess`.
3. Switch `App.OnStartup` to the parameterless `new AppDataPaths()` so data flows to `ApplicationData`.
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
