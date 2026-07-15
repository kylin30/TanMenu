# TanMenu Microsoft Store release

This project can produce an MSIX package for Microsoft Store submission.

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
```

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
- Submission options: explain restricted capability use. TanMenu declares `runFullTrust` because it is a packaged desktop app.

Partner Center shows one row per Store listing language. Open both **Chinese (Simplified)** and **English (United States)** and complete each language separately.

Suggested Chinese (Simplified) listing copy:

```text
Product name:
TanMenu

Short description:
复古风格的 Windows 桌面启动器，快速打开应用、文件夹和快捷方式。

Description:
TanMenu 是一个复古 Windows 风格的桌面启动器。它可以扫描你指定的文件夹，整理应用、快捷方式、文件夹和常用系统工具，并在屏幕底部以紧凑菜单快速打开。

它支持 Windows 98 / XP / 7 风格主题、系统托盘唤出、自动关闭、置顶、开机启动、可迁移数据目录、真实图标提取，以及中英文界面切换。所有配置和缓存默认保存在用户文档目录下，不会写入安装目录。

Keywords:
启动器, 快捷方式, 桌面工具, 应用菜单, 复古, Windows

Release notes:
首个 Microsoft Store 版本。支持中英文界面、复古主题、托盘唤出、文件夹扫描和常用工具启动。
```

Suggested English (United States) listing copy:

```text
Product name:
TanMenu

Short description:
A retro Windows desktop launcher for opening apps, folders, and shortcuts quickly.

Description:
TanMenu is a retro-styled Windows desktop launcher. It scans a folder you choose, organizes apps, shortcuts, folders, and common system tools, then opens them from a compact menu near the bottom of the screen.

It includes Windows 98 / XP / 7 inspired themes, tray recall, auto close, always-on-top mode, optional startup, movable data storage, real icon extraction, and Chinese/English UI switching. Configuration and cache files are stored in the user's documents folder by default and are not written beside the installed app.

Keywords:
launcher, shortcuts, desktop tool, app menu, retro, Windows

Release notes:
First Microsoft Store release. Includes Chinese and English UI support, retro themes, tray recall, folder scanning, and common tool launching.
```

Recommended certification note:

```text
TanMenu is a full-trust Windows desktop launcher packaged as MSIX. It uses runFullTrust for the WPF desktop process and lets users launch desktop files, folders, apps, and shortcuts from their own Windows profile. The app does not install drivers, services, browser extensions, or private signing keys.
```

## 5. Versioning

Before each Store update, bump `Directory.Build.props`:

```xml
<Version>0.9.1</Version>
```

MSIX versions are emitted as four-part versions, for example `0.9.1.0`.

## 6. Automated Store updates

`.github/workflows/store-release.yml` publishes subsequent package updates through GitHub Actions.
It intentionally separates package creation from Store submission:

1. A pushed `v*` tag starts a clean Windows build.
2. The workflow requires the tag to exactly match `Directory.Build.props` (for example,
   `Version` `0.9.2` must use tag `v0.9.2`).
3. Restore, Release build, tests, and MSIX packaging must all pass.
4. The MSIX is retained as a private GitHub Actions artifact for 14 days.
5. The `production` GitHub Environment gates the submission job. Configure required reviewers on
   that Environment so approval happens before Partner Center credentials are made available.
6. After approval, Microsoft Store Developer CLI uploads the MSIX and commits the submission for
   Store certification.

Create a GitHub Environment named `production`, restrict its deployment tags to `v*`, enable the
desired required reviewer, and add these Environment secrets:

```text
AZURE_AD_TENANT_ID
AZURE_AD_APPLICATION_CLIENT_ID
AZURE_AD_APPLICATION_SECRET
SELLER_ID
```

The Entra application must be associated with Partner Center and have the **Manager** role. Do not
commit any of these credential values to the repository. `STORE_PRODUCT_ID` is non-secret and is
declared in the workflow as `9N1G9796LV6G`.

For every update, choose a version higher than the package version already published in Partner
Center, change `Directory.Build.props`, commit the release-ready source, then push the matching tag:

```powershell
git tag v0.9.2
git push origin v0.9.2
```

Do not reuse or move a published version tag. If a build fails before submission, fix the source,
increase the version again, and create a new tag so the source-to-package mapping remains immutable.
