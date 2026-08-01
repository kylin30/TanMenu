# TanMenu Microsoft Store release

This project produces an MSIX package for Microsoft Store submission. Packaging is implemented as a
`makeappx`-based script (`scripts/package-msix.ps1` + `packaging/msix/AppxManifest.xml.in`), not a
`.wapproj`, and it runs automatically on every `v*` tag.

## 0. How the packaged build differs at run time

- The app detects package identity through `PackageRuntime.HasPackageIdentity` and then routes its
  writable data to `Windows.Storage.ApplicationData` instead of `Documents\TanMenu`. The data folder
  cannot be relocated in Settings for a packaged build.
- Autostart uses the manifest-declared `windows.startupTask` extension
  (`StartupTaskAutoStartService`), not the per-user Run key. The manifest's `TaskId` and the constant
  in that service must stay equal.
- Velopack is not used: the packaged build reports "managed by Microsoft Store" and never
  self-updates.
- `runFullTrust` is declared because it is a packaged desktop app; full trust means direct
  `System.IO`, so `broadFileSystemAccess` is not needed.
- The payload is a NativeAOT build: one native `TanMenu.Wpf.exe` plus the Avalonia native libraries
  and the loose `app.ico` / `wwwroot\sounds` / `wwwroot\tool-icons` assets. There is no bundled .NET
  runtime and no WebView2 dependency.

## 1. Reserve the app name

1. Open Partner Center.
2. Create a new **MSIX or PWA app** product.
3. Reserve the public app name.
4. Copy the package identity values from Product identity:
   - Package/Identity/Name
   - Package/Identity/Publisher
   - Package/Properties/PublisherDisplayName

Current reserved TanMenu identity:

```text
Package/Identity/Name: TanXiang.TanMenu
Package/Identity/Publisher: CN=263873C5-111A-4594-91B1-894ED1A74F54
Package/Properties/PublisherDisplayName: TanXiang
Store ID: 9N1G9796LV6G
Store URL: https://apps.microsoft.com/detail/9N1G9796LV6G
```

The package declares both `zh-Hans` and `en-US` in `AppxManifest.xml`, so Partner Center requires both Chinese (Simplified) and English (United States) Store listings to be completed before submission.

Microsoft docs:
- Reserve name: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name
- Create submission: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission
- Store listings: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info

## 2. Build the Store package

Run this from the repo root, replacing the identity values with the exact Partner Center values:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 `
  -IdentityName "<Package.Identity.Name>" `
  -Publisher "<Package.Identity.Publisher>" `
  -PublisherDisplayName "<Package.Properties.PublisherDisplayName>"
```

For the current TanMenu Partner Center product, the defaults already match Product identity, so this is enough:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1
```

Output:

```text
dist\store\TanMenu_<version>_x64.msix
dist\store\TanMenu_<version>_x64.msix.sha256
```

Every `v*` release tag also runs the same Store packaging script in GitHub Actions. Download the
`TanMenu-v<version>-Microsoft-Store` artifact from that workflow run; it contains the unsigned MSIX
and checksum and is retained for 90 days. It is intentionally not attached to the public GitHub
Release, so portable users do not mistake it for the green ZIP.

For Microsoft Store distribution, the package does not need local production signing; the Store signs it during submission. Local sideload testing still needs a trusted test certificate.

Microsoft docs:
- Upload packages: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages
- Store signing: https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide

## 3. Run certification validation

Run in an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-msix-wack.ps1
```

The report is written to:

```text
dist\store\TanMenu_<version>_wack.xml
```

The Desktop Bridge "blocked executables" test is an optional Windows 10 S compatibility check. TanMenu's core purpose is launching desktop items, so any remaining warnings should be reviewed and documented for certification notes rather than hidden.

The package now contains a single native executable instead of several hundred managed assemblies,
so the WACK "Binary analyzer" and "Supported APIs" results differ from the pre-AOT submissions.
Re-run WACK and re-document the outcome for the first AOT submission rather than reusing the notes
from an earlier release.

Microsoft docs:
- Windows Desktop Bridge tests: https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests
- WACK command line: https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit

## 4. Complete Partner Center submission

Required submission areas:

- Pricing and availability.
- Properties, category, and privacy policy URL if the app collects or transmits personal information.
- Age ratings.
- Packages: upload the generated `.msix` package. Microsoft recommends `.msixupload` for Windows 10+ packages, but Partner Center also accepts `.msix`.
- Store listings: description, screenshots, logo assets, keywords, and release notes.
- Privacy policy and support info: use the GitHub repository as the product website and GitHub Issues as the support page.
- Submission options: explain restricted capability use. TanMenu declares `runFullTrust` because it is a packaged desktop app.

Partner Center shows one row per Store listing language. Open both **Chinese (Simplified)** and **English (United States)** and complete each language separately.

Suggested support links:

```text
Website:
https://github.com/kylin30/TanMenu

Support contact info:
https://github.com/kylin30/TanMenu/issues
```

Suggested Chinese (Simplified) listing copy:

```text
Product name:
TanMenu

Short description:
复古风格的 Windows 桌面启动器，快速打开应用、文件夹和快捷方式。

Description:
TanMenu 是一个复古 Windows 风格的桌面启动器。它可以扫描你指定的文件夹，整理应用、快捷方式、文件夹和常用系统工具，并在屏幕底部以紧凑菜单快速打开。

它采用 Windows 2000 经典风格界面，并支持系统托盘唤出、自动关闭、置顶、开机启动、可迁移数据目录、真实图标提取，以及中英文界面切换。所有配置和缓存默认保存在用户文档目录下，不会写入安装目录。

项目主页和源代码：https://github.com/kylin30/TanMenu

Keywords:
启动器, 快捷方式, 桌面工具, 应用菜单, 复古, Windows

Release notes:
首个 Microsoft Store 版本。支持中英文界面、Windows 2000 经典风格界面、托盘唤出、文件夹扫描和常用工具启动。
```

Suggested English (United States) listing copy:

```text
Product name:
TanMenu

Short description:
A retro Windows desktop launcher for opening apps, folders, and shortcuts quickly.

Description:
TanMenu is a retro-styled Windows desktop launcher. It scans a folder you choose, organizes apps, shortcuts, folders, and common system tools, then opens them from a compact menu near the bottom of the screen.

It uses a Windows 2000 inspired classic interface, and includes tray recall, auto close, always-on-top mode, optional startup, movable data storage, real icon extraction, and Chinese/English UI switching. Configuration and cache files are stored in the user's documents folder by default and are not written beside the installed app.

Project home and source code: https://github.com/kylin30/TanMenu

Keywords:
launcher, shortcuts, desktop tool, app menu, retro, Windows

Release notes:
First Microsoft Store release. Includes Chinese and English UI support, a retro Windows 2000 interface, tray recall, folder scanning, and common tool launching.
```

Recommended certification note:

```text
TanMenu is a full-trust Windows desktop launcher packaged as MSIX. It uses runFullTrust for its native desktop process and lets users launch desktop files, folders, apps, and shortcuts from their own Windows profile. The app does not install drivers, services, browser extensions, or private signing keys.
```

## 5. Versioning

Before each Store update, bump `Directory.Build.props`:

```xml
<Version>0.12.0</Version>
```

MSIX versions are emitted as four-part versions, for example `0.9.1.0`.

## 6. Update policy

Store submission remains deliberately manual: each version tag builds the MSIX as a GitHub
Actions artifact (or build it locally with `scripts\package-msix.ps1`), then optionally run WACK and
upload the package in Partner Center. No Microsoft Entra application or CI publishing credentials
are required.

Git version tags drive both packaging paths documented in `docs/portable-release.md`: they create the
public portable GitHub Release and retain the private Store MSIX artifact, but do not submit anything
to Microsoft Store automatically.
