# TanMenu WPF + WindowChrome + BlazorWebView Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-host TanMenu's existing retro Blazor UI in a native WPF shell (WindowChrome + WPF BlazorWebView) as a new standalone repo, reusing the already-tested `TanMenu.Core`, achieving original-launcher parity plus recall (tray / Alt+Space hotkey / autostart / settings), built Store-ready but shipped first as a self-contained EXE.

**Architecture:** New git repo `D:\0.Work\TanMenuWpf`. Light layering: `TanMenu.Core` (framework-agnostic logic — copied verbatim from the WinUI repo, which has zero WinUI deps) + `TanMenu.Wpf` (WPF host + ported Blazor UI + native services) + `TanMenu.Core.Tests` (xUnit). The WPF `MainWindow` uses `WindowStyle=None` + `AllowsTransparency=true` + `System.Windows.Shell.WindowChrome`; a `BlazorWebView` fills it and renders the existing retro HTML/CSS UI. The launcher stays a content-sized, bottom-center, hide-on-blur popup recalled via tray/hotkey.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`, x64); WPF; `Microsoft.AspNetCore.Components.WebView.Wpf` 10.0.71 (pulls Microsoft.Web.WebView2 transitively); `H.NotifyIcon.Wpf` 2.3.2; `Microsoft.Extensions.DependencyInjection` + Serilog + `System.Text.Json`; `System.Drawing.Common` (icons); xUnit.

## Global Constraints

- New git repo at `D:\0.Work\TanMenuWpf` (separate from `D:\0.Work\TanMenu` and `D:\0.Work\TanMenuWinUI`). Solution `TanMenu.sln`. Product TanMenu.
- Solution layout: `src\TanMenu.Core` (classlib), `src\TanMenu.Wpf` (WPF exe), `tests\TanMenu.Core.Tests` (xUnit).
- TFM `net10.0-windows10.0.19041.0` for ALL three projects (keeps WinRT door open for later MSIX StartupTask/ApplicationData). RID `win-x64`. `<Platforms>x64</Platforms>`. Configurations only `Debug|x64` / `Release|x64`.
- Deployment this phase: self-contained net10 WPF exe (`<SelfContained>true</SelfContained>`); WebView2 via Evergreen runtime (no MSIX built this phase, but structure stays MSIX-ready).
- ALL writable data via `IAppDataPaths`. Unpackaged → `%LOCALAPPDATA%\TanMenu` (LocalFolder) and `%LOCALAPPDATA%\TanMenu\cache|logs`. NEVER write next to the EXE. `AppConfig.Folders` default is EMPTY (no hardcoded personal paths).
- Core is framework-agnostic: no WPF/Microsoft.UI types in `TanMenu.Core`. Icon pipeline outputs `byte[]?` PNG; base64 encoding happens in the WPF/Blazor layer ONLY.
- Tray menu items MUST be wired via `Command`, NOT `Click` (H.NotifyIcon PopupMenu mode invokes Command only — HavenDV/H.NotifyIcon issue #109; this bit the WinUI version).
- Default global hotkey: Alt+Space (`MOD_ALT | MOD_NOREPEAT`, `VK_SPACE`).
- Single instance: named `Mutex "TanMenu"` + window-title lookup + custom window message `0x8001` recall (port of the original).
- Commit message trailer: end with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

### Verified data contract (from the existing code — bind to these exact shapes)

- `TanMenu.Core` (copy source `D:\0.Work\TanMenuWinUI\src\TanMenu.Core`) exposes:
  - `MenuDataService.GetDirectoryContents(IEnumerable<string>) : Task<List<DirectoryContents>>`
  - `DirectoryContents { string Directory; string DirectoryName; List<DirectoryItem> Items; }`
  - `DirectoryItem { string Name; string FullPath; string? TargetPath; bool IsDirectory; bool IsDisabled; string? IconKey; }`
  - `IIconProvider.GetIconPngBytes(string path) : byte[]?` (raw PNG)
  - `ILaunchService.Launch(string) : bool`, `OpenFolder(string, bool=false) : bool`
  - `SoundService.Initialize(string soundsBaseDir)`, `PlayClickSoundAsync()`, `PlayHoverSoundAsync()`, `IsReady`
  - `ConfigService { AppConfig Config; bool HasValidConfig; LoadAsync(); SaveAsync(); UpdateWindowConfig(w,h,x,y); ShouldUpdateWindow(...) }`
  - `GeneralConfig { bool AutoClose=true; bool TopMost=true; bool ShowInTaskbar=false; int PositionOffset=8; int Tolerance=5; int ColButtonCount=8; }`
  - `IAppDataPaths` / `AppDataPaths(localFolder, localCacheFolder)` (use the 2-arg path explicitly for unpackaged) — `LogsFolder`, `ConfigFilePath`, etc.
  - `MenuLayout.ChunkIntoColumns(IReadOnlyList<DirectoryItem>, int) : List<List<DirectoryItem>>`
- The retro Blazor UI (copy source `D:\0.Work\TanMenu\TanMenu\TanMenu`) binds an item with these members: `Name`, `FullPath`, `IconBase64` (RAW base64, no `data:` prefix — `Button.razor` prepends `data:image/png;base64,`), `IsDisabled`. Groups bind `DirectoryName`, `Directory`, `Items`. The root measured element is `<div id="form-content">`. The WebView host root div is `<div id="app">`.
- **Bridge:** the WPF layer wraps Core's `DirectoryContents`/`DirectoryItem` into a view model carrying `IconBase64 = Convert.ToBase64String(IIconProvider.GetIconPngBytes(IconKey))` so the existing Razor components bind with minimal edits.

---

## Milestone 1: Scaffolding + Toolchain + Transparent Shell Validation

Goal: stand up the new repo + three-project solution, copy `TanMenu.Core` (+ tests) and prove 31 tests pass on net10/WPF context, then prove the highest-risk integration — a borderless transparent `WindowChrome` WPF window hosting a `BlazorWebView` that renders Blazor content with a transparent background.

---

### Task 1.1: New repo + solution + three projects

**Files:**
- Create: `D:\0.Work\TanMenuWpf\.gitignore`, `D:\0.Work\TanMenuWpf\TanMenu.sln`
- Create: `D:\0.Work\TanMenuWpf\src\TanMenu.Core\TanMenu.Core.csproj` (replaced in 1.2 by copy)
- Create: `D:\0.Work\TanMenuWpf\src\TanMenu.Wpf\TanMenu.Wpf.csproj`
- Create: `D:\0.Work\TanMenuWpf\tests\TanMenu.Core.Tests\TanMenu.Core.Tests.csproj` (replaced in 1.2 by copy)

- [ ] **Step 1: Create repo + git init**

```powershell
New-Item -ItemType Directory -Force D:\0.Work\TanMenuWpf | Out-Null
Set-Location D:\0.Work\TanMenuWpf
git init
```

- [ ] **Step 2: Copy a standard VS .gitignore** (reuse the one from the WinUI repo so bin/obj/AppPackages/AppLayout/*.msix are ignored)

```powershell
Copy-Item D:\0.Work\TanMenuWinUI\.gitignore D:\0.Work\TanMenuWpf\.gitignore
```

- [ ] **Step 3: Create the WPF app project** `src\TanMenu.Wpf\TanMenu.Wpf.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Platforms>x64</Platforms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ScopedCssEnabled>true</ScopedCssEnabled>
    <RootNamespace>TanMenu.Wpf</RootNamespace>
    <AssemblyName>TanMenu.Wpf</AssemblyName>
    <ApplicationIcon>wwwroot\app.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Wpf" Version="10.0.71" />
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.3.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
    <PackageReference Include="Serilog" Version="4.3.0" />
    <PackageReference Include="Serilog.Extensions.Logging" Version="9.0.1" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TanMenu.Core\TanMenu.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Temporary minimal Core + Tests csproj** so the solution builds before the 1.2 copy. (These get overwritten in 1.2; create stubs only if needed to add to the solution. Simpler: do 1.2 first if you prefer. If creating stubs, an empty classlib `net10.0-windows10.0.19041.0` suffices.)

- [ ] **Step 5: Create the solution and add projects**

```powershell
Set-Location D:\0.Work\TanMenuWpf
dotnet new sln -n TanMenu
dotnet sln add src\TanMenu.Wpf\TanMenu.Wpf.csproj
# Core + Tests added in Task 1.2 after copy
```

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "chore: scaffold TanMenuWpf repo + WPF app project"
```

---

### Task 1.2: Copy TanMenu.Core + tests, verify 31 tests pass

**Files:**
- Create (copy): `D:\0.Work\TanMenuWpf\src\TanMenu.Core\**` from `D:\0.Work\TanMenuWinUI\src\TanMenu.Core\**`
- Create (copy): `D:\0.Work\TanMenuWpf\tests\TanMenu.Core.Tests\**` from `D:\0.Work\TanMenuWinUI\tests\TanMenu.Core.Tests\**`

**Interfaces:**
- Produces: the entire `TanMenu.Core` public API (see Global Constraints contract). Consumed by every later task.

- [ ] **Step 1: Copy Core + Tests source (exclude bin/obj)**

```powershell
$src="D:\0.Work\TanMenuWinUI\src\TanMenu.Core"; $dst="D:\0.Work\TanMenuWpf\src\TanMenu.Core"
robocopy $src $dst /E /XD bin obj | Out-Null
$src="D:\0.Work\TanMenuWinUI\tests\TanMenu.Core.Tests"; $dst="D:\0.Work\TanMenuWpf\tests\TanMenu.Core.Tests"
robocopy $src $dst /E /XD bin obj | Out-Null
```

- [ ] **Step 2: Fix the Tests project's ProjectReference path** if it points at the old repo. Open `tests\TanMenu.Core.Tests\TanMenu.Core.Tests.csproj` and ensure:

```xml
<ProjectReference Include="..\..\src\TanMenu.Core\TanMenu.Core.csproj" />
```

- [ ] **Step 3: Add Core + Tests to the solution**

```powershell
Set-Location D:\0.Work\TanMenuWpf
dotnet sln add src\TanMenu.Core\TanMenu.Core.csproj
dotnet sln add tests\TanMenu.Core.Tests\TanMenu.Core.Tests.csproj
```

- [ ] **Step 4: Run the tests — expect 31 passing** (Core is framework-agnostic; nothing should need changing)

```powershell
dotnet test tests\TanMenu.Core.Tests\TanMenu.Core.Tests.csproj -c Debug -p:Platform=x64
```
Expected: `已通过! - 失败: 0, 通过: 31` (count may be 31/32; all green).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): import framework-agnostic TanMenu.Core + tests (31 passing)"
```

---

### Task 1.3: Transparent WindowChrome + BlazorWebView shell (RISK VALIDATION)

**Files:**
- Create: `src\TanMenu.Wpf\App.xaml`, `App.xaml.cs`
- Create: `src\TanMenu.Wpf\MainWindow.xaml`, `MainWindow.xaml.cs`
- Create: `src\TanMenu.Wpf\wwwroot\index.html` (temporary minimal host page)
- Create: `src\TanMenu.Wpf\_Imports.razor`, `App.razor`, `Components\Probe.razor` (temporary)

**Interfaces:**
- Produces: a running borderless transparent window proving WebView2 transparency works under WindowChrme. Later tasks replace the probe with the real UI.

- [ ] **Step 1: `App.xaml`** (no StartupUri; we create the window in code with DI)

```xml
<Application x:Class="TanMenu.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources/>
</Application>
```

- [ ] **Step 2: `App.xaml.cs`** — DI container + Serilog + create MainWindow

```csharp
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TanMenu.Core.Infrastructure;
using TanMenu.Core.Services;

namespace TanMenu.Wpf;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var local = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TanMenu");
        var cache = System.IO.Path.Combine(local, "cache");
        var paths = new AppDataPaths(local, cache);   // 2-arg ctor = unpackaged, no WinRT
        paths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(System.IO.Path.Combine(paths.LogsFolder, "log-.txt"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(dispose: true));
        services.AddSingleton<IAppDataPaths>(paths);
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        Services = services.BuildServiceProvider();

        new MainWindow().Show();
    }
}
```

- [ ] **Step 3: `MainWindow.xaml`** — WindowChrome + transparent + BlazorWebView

```xml
<Window x:Class="TanMenu.Wpf.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:blazor="clr-namespace:Microsoft.AspNetCore.Components.WebView.Wpf;assembly=Microsoft.AspNetCore.Components.WebView.Wpf"
        Title="TanMenu"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        ShowInTaskbar="False"
        SizeToContent="Manual"
        Width="600" Height="400"
        WindowStartupLocation="Manual">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="0" GlassFrameThickness="0" ResizeBorderThickness="0" CornerRadius="0"/>
    </WindowChrome.WindowChrome>
    <blazor:BlazorWebView x:Name="WebView" HostPage="wwwroot\index.html">
        <blazor:BlazorWebView.RootComponents>
            <blazor:RootComponent Selector="#app" ComponentType="{x:Type local:App}"
                                  xmlns:local="clr-namespace:TanMenu.Wpf"/>
        </blazor:BlazorWebView.RootComponents>
    </blazor:BlazorWebView>
</Window>
```

- [ ] **Step 4: `MainWindow.xaml.cs`** — wire DI services into the WebView + make WebView2 background transparent

```csharp
using System.Drawing;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace TanMenu.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WebView.Services = App.Services;
        WebView.WebView.DefaultBackgroundColor = Color.Transparent; // KEY: transparent WebView2
    }
}
```

- [ ] **Step 5: `_Imports.razor`**

```razor
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
```

- [ ] **Step 6: `App.razor`** + temporary `Components\Probe.razor`

`App.razor`:
```razor
<Probe/>
```
`Components\Probe.razor`:
```razor
<div id="form-content" style="background:#c0c0c0;border:2px solid navy;padding:24px;display:inline-block;font-family:sans-serif;">
    <h2 style="margin:0;">TanMenu shell OK</h2>
    <p>Transparent WindowChrome + BlazorWebView works.</p>
</div>
```

- [ ] **Step 7: Temporary `wwwroot\index.html`** (transparent body, root `#app`)

```html
<!DOCTYPE html>
<html lang="en"><head>
<meta charset="utf-8" /><base href="/" />
<style>html,body{margin:0;padding:0;background:transparent;}#app{display:inline-block;}</style>
</head><body>
<div id="app"></div>
<div id="blazor-error-ui" style="display:none"></div>
<script src="_framework/blazor.webview.js"></script>
</body></html>
```

- [ ] **Step 8: Build + run; VISUALLY verify** the window shows the grey probe box with a transparent surround (no white rectangle, no title bar)

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug -p:Platform=x64
dotnet run --project src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
```
Expected: borderless window; the area outside the grey box is transparent (desktop shows through). If a white/opaque rectangle appears around the content, transparency failed → escalate (try `WebView.WebView.DefaultBackgroundColor = Color.Transparent` timing in `CoreWebView2InitializationCompleted`, and confirm `AllowsTransparency=True` + `Background=Transparent`).

- [ ] **Step 9: Commit**

```powershell
git add -A
git commit -m "feat(wpf): transparent WindowChrome shell hosting BlazorWebView (risk validated)"
```

---

## Milestone 2: Port the retro Blazor UI + bridge to Core

Goal: bring the existing retro Blazor components + wwwroot into `TanMenu.Wpf`, add a WPF-layer menu view model that bridges Core's `DirectoryItem.IconKey` to the UI's `IconBase64`, and render the real folder grid from configured folders.

Port source = `D:\0.Work\TanMenu\TanMenu\TanMenu` (current working tree).

---

### Task 2.1: Copy wwwroot assets

**Files:**
- Create (copy): `src\TanMenu.Wpf\wwwroot\**` from `D:\0.Work\TanMenu\TanMenu\TanMenu\wwwroot\**` (css, fonts, lib, sounds, app.ico, index.html)

- [ ] **Step 1: Copy wwwroot (overwrite the temporary index.html)**

```powershell
robocopy "D:\0.Work\TanMenu\TanMenu\TanMenu\wwwroot" "D:\0.Work\TanMenuWpf\src\TanMenu.Wpf\wwwroot" /E | Out-Null
```

- [ ] **Step 2: Ensure assets copy to output.** In `TanMenu.Wpf.csproj` add (BlazorWebView serves wwwroot from the app dir):

```xml
<ItemGroup>
  <Content Include="wwwroot\**\*.*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
(If Razor SDK already globs wwwroot as content, verify no duplicate-content build error; if so, drop this ItemGroup.)

- [ ] **Step 3: Confirm `wwwroot\index.html` has `<div id="app">` and references `_framework/blazor.webview.js`** (the copied one does). Keep `css/app.css` (has the `background-color: transparent` body rule — required for the transparent shell).

- [ ] **Step 4: Build to confirm assets resolve**

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug -p:Platform=x64
```
Expected: build succeeds.

- [ ] **Step 5: Commit** (`git add -A; git commit -m "feat(wpf): import retro wwwroot assets"`)

---

### Task 2.2: Copy Razor components

**Files:**
- Create (copy): `src\TanMenu.Wpf\Components\**` from `D:\0.Work\TanMenu\TanMenu\TanMenu\Components\**` (Index.razor, SplashScreen.razor, Windows3.1\, Windows7\, ModernRetro\ and any .razor.css)
- Modify: `src\TanMenu.Wpf\App.razor` (point at the real Index), delete temporary `Components\Probe.razor`

- [ ] **Step 1: Copy components**

```powershell
robocopy "D:\0.Work\TanMenu\TanMenu\TanMenu\Components" "D:\0.Work\TanMenuWpf\src\TanMenu.Wpf\Components" /E | Out-Null
Remove-Item "D:\0.Work\TanMenuWpf\src\TanMenu.Wpf\Components\Probe.razor" -ErrorAction SilentlyContinue
```

- [ ] **Step 2: `App.razor` → render the real root**

```razor
@using TanMenu.Wpf.Components
<Index/>
```

- [ ] **Step 3: Build — EXPECT failures** about missing `DataService`, `ConfigService`, `WindowManagerService`, `SoundService` types/namespaces (these are the old WinForms project's types). That is the wiring gap Task 2.3/2.4 fix. Note the exact errors.

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug -p:Platform=x64
```
Expected: FAIL — unresolved `TanMenu.Services.*` etc. (Do not commit broken build; proceed to 2.3.)

---

### Task 2.3: WPF-layer menu view model + builder (Core → IconBase64 bridge)

**Files:**
- Create: `src\TanMenu.Wpf\ViewModels\MenuModels.cs`
- Create: `src\TanMenu.Wpf\Services\MenuService.cs`
- Test: `tests\TanMenu.Core.Tests` is Core-only; the bridge is host code — verify by build + runtime. (No new unit test; logic is a thin map. If desired, extract the pure map into Core later.)

**Interfaces:**
- Produces: `MenuGroupVm { string Directory; string DirectoryName; List<MenuItemVm> Items; }`, `MenuItemVm { string Name; string FullPath; string? TargetPath; bool IsDirectory; bool IsDisabled; string? IconBase64; }`, and `MenuService.GetMenuAsync(IEnumerable<string>) : Task<List<MenuGroupVm>>`.
- Consumes: Core `MenuDataService`, `IIconProvider`.

- [ ] **Step 1: `ViewModels\MenuModels.cs`** (shape the Razor UI binds to)

```csharp
namespace TanMenu.Wpf.ViewModels;

public sealed class MenuItemVm
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string? TargetPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsDisabled { get; init; }
    public string? IconBase64 { get; init; } // RAW base64 (no data: prefix)
}

public sealed class MenuGroupVm
{
    public string Directory { get; init; } = "";
    public string DirectoryName { get; init; } = "";
    public List<MenuItemVm> Items { get; init; } = new();
}
```

- [ ] **Step 2: `Services\MenuService.cs`** (calls Core, encodes icons)

```csharp
using TanMenu.Core.Services;
using TanMenu.Wpf.ViewModels;

namespace TanMenu.Wpf.Services;

public sealed class MenuService
{
    private readonly MenuDataService _data;
    private readonly IIconProvider _icons;

    public MenuService(MenuDataService data, IIconProvider icons)
    {
        _data = data;
        _icons = icons;
    }

    public async Task<List<MenuGroupVm>> GetMenuAsync(IEnumerable<string> folders)
    {
        var contents = await _data.GetDirectoryContents(folders);
        var groups = new List<MenuGroupVm>(contents.Count);
        foreach (var c in contents)
        {
            var items = new List<MenuItemVm>(c.Items.Count);
            foreach (var it in c.Items)
            {
                string? b64 = null;
                if (!it.IsDisabled && it.IconKey is { Length: > 0 })
                {
                    var bytes = _icons.GetIconPngBytes(it.IconKey);
                    if (bytes is { Length: > 0 }) b64 = Convert.ToBase64String(bytes);
                }
                items.Add(new MenuItemVm
                {
                    Name = it.Name, FullPath = it.FullPath, TargetPath = it.TargetPath,
                    IsDirectory = it.IsDirectory, IsDisabled = it.IsDisabled, IconBase64 = b64,
                });
            }
            groups.Add(new MenuGroupVm { Directory = c.Directory, DirectoryName = c.DirectoryName, Items = items });
        }
        return groups;
    }
}
```

- [ ] **Step 3: Build the Core+Wpf (still failing on Razor refs — expected). Commit the new files only after 2.4 makes the project build.**

---

### Task 2.4: Wire Index.razor + Button.razor to the new services; register DI

**Files:**
- Modify: `src\TanMenu.Wpf\Components\Index.razor`
- Modify: `src\TanMenu.Wpf\Components\Windows3.1\Button.razor` (and Windows7/ModernRetro Button.razor) — switch the injected sound service + remove per-button `Initialize()`
- Modify: `src\TanMenu.Wpf\App.xaml.cs` — register Core services + MenuService + IWindowHost (placeholder for M3)

**Interfaces:**
- Consumes: `MenuService`, `ConfigService`, `SoundService`, and a window-host abstraction.
- Produces: a rendering Index that lists groups/buttons from configured folders.

- [ ] **Step 1: Register services in `App.xaml.cs`** (extend the `ServiceCollection` block from Task 1.3)

```csharp
services.AddSingleton<ConfigService>();
services.AddSingleton<IShortcutResolver, ShortcutResolver>();
services.AddSingleton<MenuDataService>();
services.AddSingleton<IIconProvider, IconProvider>();
services.AddSingleton<ILaunchService, LaunchService>();
services.AddSingleton<SoundService>();
services.AddSingleton<TanMenu.Wpf.Services.MenuService>();
services.AddSingleton<TanMenu.Wpf.Services.IWindowHost, TanMenu.Wpf.Services.WindowHost>(); // defined in M3
```
Also, before creating the window, load config + init sounds:
```csharp
var cfg = Services.GetRequiredService<ConfigService>();
await cfg.LoadAsync();  // make OnStartup async void or block appropriately
Services.GetRequiredService<SoundService>()
        .Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "sounds"));
```

- [ ] **Step 2: Rewrite `Components\Index.razor`** to inject the new services and bind `MenuGroupVm`/`MenuItemVm`. Keep the SAME retro component tree (Window/Groupbox/Button) and the `#form-content` id.

```razor
@using TanMenu.Wpf.Services
@using TanMenu.Wpf.ViewModels
@using TanMenu.Core.Services
@inject MenuService MenuService
@inject ConfigService ConfigService
@inject IWindowHost WindowHost
@inject ILaunchService LaunchService
@inject Microsoft.Extensions.Logging.ILogger<Index> Logger

<div class="body">
  <TanMenu.Wpf.Components.Windows3_1.Window OnCloseClick="@HandleClose" Title="TanMenu">
    <div id="form-content">
      @if (_groups.Count > 0)
      {
        <div class="flex items-start gap-2">
          @foreach (var g in _groups)
          {
            <TanMenu.Wpf.Components.Windows3_1.Groupbox Title="@g.DirectoryName"
                OnTitleClickAction="@(() => LaunchService.OpenFolder(g.Directory))">
              <div class="flex items-start gap-1">
                @foreach (var col in g.Items.Chunk(_cfg.General.ColButtonCount))
                {
                  <div>
                    @foreach (var item in col)
                    {
                      <div class="mb-1 flex-shrink-0">
                        <TanMenu.Wpf.Components.Windows3_1.Button Text="@item.Name"
                            IconBase64="@item.IconBase64" Disabled="@item.IsDisabled"
                            OnClick="@(() => AppRun(item))" />
                      </div>
                    }
                  </div>
                }
              </div>
            </TanMenu.Wpf.Components.Windows3_1.Groupbox>
          }
        </div>
      }
      else { <SplashScreen/> }
    </div>
  </TanMenu.Wpf.Components.Windows3_1.Window>
</div>

@code {
  private List<MenuGroupVm> _groups = new();
  private TanMenu.Core.Models.AppConfig _cfg = new();

  protected override async Task OnInitializedAsync()
  {
    _cfg = ConfigService.Config;
    _groups = await MenuService.GetMenuAsync(_cfg.Folders);
  }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (firstRender && _groups.Count > 0)
    {
      await Task.Delay(100);
      await WindowHost.ResizeToContentAndPlaceAsync();
    }
  }

  private void AppRun(MenuItemVm item)
  {
    LaunchService.Launch(item.FullPath);
    WindowHost.Hide();
  }

  private void HandleClose() => WindowHost.Hide(); // close button hides to tray (exit is via tray menu)
}
```
(The exact namespace of the retro component `Windows3_1` matches the folder `Windows3.1` → Blazor namespace `Windows3_1`; verify against the copied component `@namespace`/folder. Adjust if the components declare a different `@namespace`.)

- [ ] **Step 3: Fix `Button.razor` sound injection** (all three theme variants). Replace the old `@inject TanMenu.Services.SoundService` with the Core one and drop the per-button `Initialize()` (sounds are initialized once at startup):

```razor
@inject TanMenu.Core.Services.SoundService SoundService
```
Remove the `protected override void OnInitialized(){ if (EnableSounds) SoundService.Initialize(); }` block (Core's `Initialize` takes a path and is called at startup). Keep the `PlayClickSoundAsync()` / `PlayHoverSoundAsync()` calls unchanged.

- [ ] **Step 4: Add a temporary no-op `IWindowHost`** so the project builds before M3 implements the real one.

`src\TanMenu.Wpf\Services\IWindowHost.cs`:
```csharp
namespace TanMenu.Wpf.Services;
public interface IWindowHost
{
    Task ResizeToContentAndPlaceAsync();
    void Hide();
    void ShowAndActivate();
    void Toggle();
}
```
`src\TanMenu.Wpf\Services\WindowHost.cs` (stub; real impl in M3):
```csharp
namespace TanMenu.Wpf.Services;
public sealed class WindowHost : IWindowHost
{
    public Task ResizeToContentAndPlaceAsync() => Task.CompletedTask;
    public void Hide() { }
    public void ShowAndActivate() { }
    public void Toggle() { }
}
```

- [ ] **Step 5: Build + run; verify the real retro folder grid renders** (seed config first — see note). Buttons show icons; clicking launches (window may not yet reposition — that's M3).

```powershell
# seed a config with a real folder so groups appear
$cfgDir="$env:LOCALAPPDATA\TanMenu"; New-Item -ItemType Directory -Force $cfgDir | Out-Null
'{ "folders": ["C:\\Users\\Public\\Desktop"], "general": {}, "window": {} }' | Set-Content "$cfgDir\config.json" -Encoding UTF8
dotnet run --project src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
```
Expected: retro Win3.1 window with a groupbox of launcher buttons + icons.

- [ ] **Step 6: Commit** (`git commit -m "feat(wpf): render retro Blazor menu wired to TanMenu.Core"`)

---

## Milestone 3: Launcher behavior parity

Goal: implement the real `WindowHost` (content-size via `#form-content` measure, DPI, bottom-center placement, hide-on-blur, TopMost/ShowInTaskbar) and single-instance recall — matching the original launcher UX.

---

### Task 3.1: WindowHost — measure + DPI + bottom-center placement

**Files:**
- Modify: `src\TanMenu.Wpf\Services\WindowHost.cs` (replace stub)
- Modify: `src\TanMenu.Wpf\MainWindow.xaml.cs` (register the window + WebView with WindowHost)

**Interfaces:**
- Consumes: `ConfigService`, the `MainWindow`, and the `WebView2` for `ExecuteScriptAsync`.
- Produces: real `ResizeToContentAndPlaceAsync()` / `Hide()` / `ShowAndActivate()` / `Toggle()`.

- [ ] **Step 1: Implement `WindowHost`** — port `WindowManagerService` logic to WPF.

```csharp
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using TanMenu.Core.Models;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

public sealed class WindowHost : IWindowHost
{
    private readonly ConfigService _config;
    private Window? _window;
    private WebView2? _web;

    public WindowHost(ConfigService config) => _config = config;

    public void Attach(Window window, WebView2 web) { _window = window; _web = web; }

    public async Task ResizeToContentAndPlaceAsync()
    {
        if (_window is null || _web?.CoreWebView2 is null) return;
        const string js = "(function(){var e=document.querySelector('#form-content');" +
                          "return e?JSON.stringify({width:e.offsetWidth,height:e.offsetHeight}):'{\"width\":0,\"height\":0}';})()";
        var raw = await _web.CoreWebView2.ExecuteScriptAsync(js);
        var json = JsonSerializer.Deserialize<string>(raw) ?? "{}";
        var dim = JsonSerializer.Deserialize<DimensionInfo>(json) ?? new DimensionInfo();

        // WebView CSS px are DIPs; WPF Width/Height are DIPs too, so no manual DPI scaling needed.
        var w = Math.Max(dim.NaturalWidth, 200) + 8;
        var h = Math.Max(dim.NaturalHeight, 100) + 8;

        var wa = SystemParameters.WorkArea; // DIP work area of primary screen
        _window.Width = w; _window.Height = h;
        _window.Left = wa.Left + (wa.Width - w) / 2;
        _window.Top = Math.Max(wa.Top, wa.Bottom - h - _config.Config.General.PositionOffset);
        _window.Topmost = _config.Config.General.TopMost;
        _window.ShowInTaskbar = _config.Config.General.ShowInTaskbar;

        _config.UpdateWindowConfig((int)w, (int)h, (int)_window.Left, (int)_window.Top);
        await _config.SaveAsync();
    }

    public void Hide() => _window?.Hide();

    public void ShowAndActivate()
    {
        if (_window is null) return;
        _window.Show();
        _ = ResizeToContentAndPlaceAsync();
        _window.Activate();
        var h = new WindowInteropHelper(_window).Handle;
        SetForegroundWindow(h);
    }

    public void Toggle()
    {
        if (_window is null) return;
        if (_window.IsVisible) Hide(); else ShowAndActivate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
```
> NOTE on multi-monitor: `SystemParameters.WorkArea` is the primary screen. To place on the cursor's monitor (as the WinUI version did), use `System.Windows.Forms.Screen.FromPoint(Cursor.Position)` via a `System.Windows.Forms` reference, or `MonitorFromPoint` P/Invoke, and convert px→DIP by the monitor DPI. Implement primary-screen first; add cursor-monitor in a follow-up step if desired.

- [ ] **Step 2: Register the window/WebView with WindowHost** in `MainWindow.xaml.cs` ctor:

```csharp
var host = (WindowHost)App.Services.GetRequiredService<IWindowHost>();
host.Attach(this, WebView.WebView);
```
Change DI registration so `WindowHost` is the concrete singleton resolvable both as `IWindowHost` and `WindowHost`:
```csharp
services.AddSingleton<WindowHost>();
services.AddSingleton<IWindowHost>(sp => sp.GetRequiredService<WindowHost>());
```

- [ ] **Step 3: Build + run; verify the window auto-sizes to content and sits bottom-center above the taskbar.**

```powershell
dotnet run --project src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug
```
Expected: window hugs the content and is centered along the bottom.

- [ ] **Step 4: Commit** (`git commit -m "feat(wpf): content-size + bottom-center placement via #form-content measure"`)

---

### Task 3.2: Hide-on-blur + close-to-hide + launch/open parity

**Files:**
- Modify: `src\TanMenu.Wpf\MainWindow.xaml.cs` (Deactivated handler)

- [ ] **Step 1: Hide on deactivate when AutoClose** (in MainWindow ctor):

```csharp
Deactivated += (_, _) =>
{
    if (App.Services.GetRequiredService<ConfigService>().Config.General.AutoClose && !_suppressHide)
        Hide();
};
```
Add a `_suppressHide` bool field set true while a child dialog/folder picker is open (used in M4 settings).

- [ ] **Step 2: Build + run; verify clicking another window hides TanMenu; launching a button hides it; group-title opens the folder in Explorer.** (Launch/open already wired in 2.4 via `ILaunchService`.)

- [ ] **Step 3: Commit** (`git commit -m "feat(wpf): hide-on-blur + close-to-hide launcher behavior"`)

---

### Task 3.3: Single-instance recall (Mutex + window message)

**Files:**
- Create: `src\TanMenu.Wpf\Program.cs` (explicit Main with Mutex) — set `<StartupObject>TanMenu.Wpf.Program</StartupObject>` OR use App's `OnStartup` + a named Mutex. Simpler: do it in `App.OnStartup`.
- Modify: `src\TanMenu.Wpf\App.xaml.cs`, `MainWindow.xaml.cs`

- [ ] **Step 1: Named mutex in `App.OnStartup`** (before building services):

```csharp
private Mutex? _mutex;
// in OnStartup, first line:
_mutex = new Mutex(true, "TanMenu", out var createdNew);
if (!createdNew)
{
    var hWnd = FindWindow(null, "TanMenu"); // window Title = "TanMenu"
    if (hWnd != IntPtr.Zero) SendMessage(hWnd, WmShowFirstInstance, IntPtr.Zero, IntPtr.Zero);
    Shutdown();
    return;
}
// ... rest of startup
public const int WmShowFirstInstance = 0x8000 + 1;
[System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr FindWindow(string? c, string? n);
[System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
```

- [ ] **Step 2: Handle the message in `MainWindow`** via HwndSource hook (in ctor after `InitializeComponent` and once the handle exists — use `SourceInitialized`):

```csharp
SourceInitialized += (_, _) =>
{
    var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
    src?.AddHook(WndProc);
};

private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
{
    if (msg == App.WmShowFirstInstance)
    {
        App.Services.GetRequiredService<IWindowHost>().ShowAndActivate();
        handled = true;
    }
    return IntPtr.Zero;
}
```

- [ ] **Step 3: Build + run two instances; verify the second exits and resurfaces the first.**

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug -p:Platform=x64
$exe = "src\TanMenu.Wpf\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\TanMenu.Wpf.exe"
Start-Process $exe; Start-Sleep 3; Start-Process $exe; Start-Sleep 2
(Get-Process TanMenu.Wpf -ErrorAction SilentlyContinue | Measure-Object).Count  # expect 1
```
Expected: only one process; the window resurfaces.

- [ ] **Step 4: Commit** (`git commit -m "feat(wpf): single-instance recall via mutex + window message"`)

---

## Milestone 4: Recall capabilities (tray / hotkey / autostart / settings)

Goal: add the tray icon, Alt+Space global hotkey, optional autostart, and a Blazor settings page.

---

### Task 4.1: System tray (H.NotifyIcon.Wpf, Command-not-Click)

**Files:**
- Create: `src\TanMenu.Wpf\Services\TrayService.cs`
- Modify: `src\TanMenu.Wpf\MainWindow.xaml.cs` (create tray on load), `App.xaml.cs` (exit logic)

**Interfaces:**
- Consumes: `IWindowHost`.
- Produces: tray icon with left-click toggle + 显示/退出 menu; `ExitApp()`.

- [ ] **Step 1: `TrayService`** — build the `TaskbarIcon` in code; **menu items via `Command`** (issue #109):

```csharp
using System.Windows.Controls;
using System.Windows.Input;
using H.NotifyIcon;
using TanMenu.Wpf.Services;

namespace TanMenu.Wpf.Services;

public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _icon;
    private readonly IWindowHost _host;
    private readonly Action _exit;

    public TrayService(IWindowHost host, Action exit) { _host = host; _exit = exit; }

    public void Create(string icoPath)
    {
        _icon = new TaskbarIcon { ToolTipText = "TanMenu", NoLeftClickDelay = true };
        if (System.IO.File.Exists(icoPath))
            _icon.Icon = new System.Drawing.Icon(icoPath);
        _icon.LeftClickCommand = new RelayCommand(() => _host.Toggle());

        var show = new MenuItem { Header = "显示", Command = new RelayCommand(() => _host.ShowAndActivate()) };
        var exit = new MenuItem { Header = "退出", Command = new RelayCommand(_exit) };
        var menu = new ContextMenu();
        menu.Items.Add(show); menu.Items.Add(exit);
        _icon.ContextMenu = menu;       // WPF ContextMenu (not WinUI MenuFlyout)
        _icon.ForceCreate();
    }

    public void Dispose() { _icon?.Dispose(); _icon = null; }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _a;
    public RelayCommand(Action a) => _a = a;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _a();
}
```
> H.NotifyIcon.Wpf uses a WPF `ContextMenu` (not a WinUI MenuFlyout). Wiring via `Command` is correct and avoids the Click-not-firing class of bug. Verify both 显示 and 退出 fire.

- [ ] **Step 2: `ExitApp` in `App`** — dispose tray, unregister hotkey (M4.2), release mutex, shut down:

```csharp
public void ExitApp()
{
    Tray?.Dispose();
    Hotkey?.Dispose();
    _mutex?.ReleaseMutex();
    Shutdown();
}
```

- [ ] **Step 3: Create tray in `MainWindow` `SourceInitialized`**:

```csharp
App.Tray = new TrayService(App.Services.GetRequiredService<IWindowHost>(), () => ((App)Application.Current).ExitApp());
App.Tray.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "app.ico"));
```
(Add `public static TrayService? Tray { get; set; }` and `HotkeyService? Hotkey` to `App`.)

- [ ] **Step 4: Build + run; verify tray icon, left-click toggle, right-click 显示/退出 both work (退出 fully exits the process).**

- [ ] **Step 5: Commit** (`git commit -m "feat(wpf): system tray with Command-wired show/exit"`)

---

### Task 4.2: Global hotkey Alt+Space

**Files:**
- Create: `src\TanMenu.Wpf\Services\HotkeyService.cs`
- Modify: `src\TanMenu.Wpf\MainWindow.xaml.cs` (register on SourceInitialized, route WM_HOTKEY)

**Interfaces:**
- Consumes: window HWND, `IWindowHost`.
- Produces: `HotkeyService.Register(hwnd, Action onPressed)` / `Dispose()`; handles `WM_HOTKEY` in the existing `WndProc` hook.

- [ ] **Step 1: `HotkeyService`**

```csharp
using System.Runtime.InteropServices;

namespace TanMenu.Wpf.Services;

public sealed class HotkeyService : IDisposable
{
    public const int WmHotkey = 0x0312;
    private const int Id = 0x9001;
    private const uint ModAlt = 0x0001, ModNoRepeat = 0x4000, VkSpace = 0x20;
    private IntPtr _hwnd;

    public void Register(IntPtr hwnd) { _hwnd = hwnd; RegisterHotKey(hwnd, Id, ModAlt | ModNoRepeat, VkSpace); }
    public bool IsOurHotkey(IntPtr wParam) => wParam.ToInt32() == Id;
    public void Dispose() { if (_hwnd != IntPtr.Zero) { UnregisterHotKey(_hwnd, Id); _hwnd = IntPtr.Zero; } }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint fs, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
}
```

- [ ] **Step 2: Register + route in `MainWindow`** (extend `SourceInitialized` + `WndProc`):

```csharp
var hwnd = new WindowInteropHelper(this).Handle;
App.Hotkey = new HotkeyService();
App.Hotkey.Register(hwnd);
// in WndProc:
if (msg == HotkeyService.WmHotkey && App.Hotkey!.IsOurHotkey(w))
{
    App.Services.GetRequiredService<IWindowHost>().Toggle();
    handled = true;
}
```

- [ ] **Step 3: Build + run; verify Alt+Space toggles the window globally** (focus another app, press Alt+Space).

- [ ] **Step 4: Commit** (`git commit -m "feat(wpf): Alt+Space global hotkey toggle"`)

---

### Task 4.3: Autostart (registry Run, abstracted)

**Files:**
- Create: `src\TanMenu.Wpf\Services\IAutoStartService.cs`, `RegistryAutoStartService.cs`
- Test: `tests\TanMenu.Core.Tests` — N/A (registry side-effect); verify manually + via registry read.

**Interfaces:**
- Produces: `IAutoStartService { bool IsEnabled(); void SetEnabled(bool); }`.

- [ ] **Step 1: Interface + registry impl** (HKCU Run, value name `TanMenu`, data = exe path)

```csharp
using Microsoft.Win32;
namespace TanMenu.Wpf.Services;

public interface IAutoStartService { bool IsEnabled(); void SetEnabled(bool enabled); }

public sealed class RegistryAutoStartService : IAutoStartService
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "TanMenu";
    private static string ExePath => Environment.ProcessPath ?? "";

    public bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) is string s && string.Equals(s.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
    }
    public void SetEnabled(bool enabled)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true)
                      ?? Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) k!.SetValue(Name, $"\"{ExePath}\"");
        else k!.DeleteValue(Name, throwOnMissingValue: false);
    }
}
```
Register: `services.AddSingleton<IAutoStartService, RegistryAutoStartService>();`

- [ ] **Step 2: Build + smoke test**

```powershell
dotnet build src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Debug -p:Platform=x64
# after wiring the settings toggle (4.4), enable then check:
# reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v TanMenu
```

- [ ] **Step 3: Commit** (`git commit -m "feat(wpf): registry-based autostart service"`)

---

### Task 4.4: Settings Blazor page

**Files:**
- Create: `src\TanMenu.Wpf\Components\Settings.razor`
- Create: `src\TanMenu.Wpf\Services\IFolderPicker.cs`, `WpfFolderPicker.cs` (WPF `OpenFolderDialog`)
- Modify: `Index.razor` (a settings button to toggle the settings view), `App.xaml.cs` (DI for picker)

**Interfaces:**
- Consumes: `ConfigService`, `IAutoStartService`, `IFolderPicker`, `IWindowHost`.
- Produces: a settings UI editing folders + ColButtonCount + AutoClose/TopMost/ShowInTaskbar + autostart.

- [ ] **Step 1: `IFolderPicker` + WPF impl** (uses .NET 8+ WPF `OpenFolderDialog`):

```csharp
using Microsoft.Win32;
namespace TanMenu.Wpf.Services;

public interface IFolderPicker { string? PickFolder(); }

public sealed class WpfFolderPicker : IFolderPicker
{
    private readonly IWindowHost _host;
    public WpfFolderPicker(IWindowHost host) => _host = host;
    public string? PickFolder()
    {
        _host.SuppressHide(true);       // add SuppressHide(bool) to IWindowHost/WindowHost; sets _suppressHide
        try
        {
            var dlg = new OpenFolderDialog { Title = "选择文件夹" };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }
        finally { _host.SuppressHide(false); }
    }
}
```
Add `void SuppressHide(bool on);` to `IWindowHost`/`WindowHost` (sets the `_suppressHide` flag from Task 3.2) and register the picker.

- [ ] **Step 2: `Settings.razor`** — folder list (add/remove), numeric ColButtonCount, toggles, autostart:

```razor
@using TanMenu.Wpf.Services
@using TanMenu.Core.Services
@inject ConfigService ConfigService
@inject IFolderPicker FolderPicker
@inject IAutoStartService AutoStart
@inject IWindowHost WindowHost

<div id="form-content" class="win31-window" style="padding:12px;min-width:360px;">
  <h3>设置</h3>
  <div><strong>文件夹</strong></div>
  <ul>
    @foreach (var f in _cfg.Folders.ToList())
    {
      <li>@f <button @onclick="@(() => Remove(f))">删除</button></li>
    }
  </ul>
  <button @onclick="Add">添加文件夹</button>
  <div>每列按钮数: <input type="number" min="1" max="30" @bind="_cfg.General.ColButtonCount" @bind:after="Save" /></div>
  <label><input type="checkbox" @bind="_cfg.General.AutoClose" @bind:after="Save" /> 失焦自动隐藏</label><br/>
  <label><input type="checkbox" @bind="_cfg.General.TopMost" @bind:after="Save" /> 置顶</label><br/>
  <label><input type="checkbox" @bind="_cfg.General.ShowInTaskbar" @bind:after="Save" /> 任务栏显示</label><br/>
  <label><input type="checkbox" @bind="_auto" @bind:after="ToggleAuto" /> 开机自启</label>
  <div><button @onclick="Close">完成</button></div>
</div>

@code {
  [Parameter] public EventCallback OnClosed { get; set; }
  private TanMenu.Core.Models.AppConfig _cfg = new();
  private bool _auto;
  protected override void OnInitialized() { _cfg = ConfigService.Config; _auto = AutoStart.IsEnabled(); }
  private async Task Add() { var p = FolderPicker.PickFolder(); if (!string.IsNullOrEmpty(p) && !_cfg.Folders.Contains(p)) { _cfg.Folders.Add(p); await Save(); } }
  private async Task Remove(string f) { _cfg.Folders.Remove(f); await Save(); }
  private async Task Save() => await ConfigService.SaveAsync();
  private async Task ToggleAuto() { AutoStart.SetEnabled(_auto); await Task.CompletedTask; }
  private async Task Close() => await OnClosed.InvokeAsync();
}
```

- [ ] **Step 3: Add a settings entry in `Index.razor`** (a gear button in the Window chrome or a small button) that swaps to `<Settings OnClosed="@ReloadMenu"/>`; `ReloadMenu` re-runs `GetMenuAsync` + `ResizeToContentAndPlaceAsync`.

- [ ] **Step 4: Build + run; verify add/remove folder (window does NOT hide while the folder dialog is open), column count, toggles, and autostart toggle (check registry).**

- [ ] **Step 5: Commit** (`git commit -m "feat(wpf): Blazor settings page (folders/columns/toggles/autostart)"`)

---

## Milestone 5: Packaging hardening (self-contained EXE, Store-ready structure)

Goal: produce a shareable self-contained net10 WPF exe with WebView2 provisioning; verify data lands under `%LOCALAPPDATA%\TanMenu`; document the MSIX-ready structure (no MSIX built this phase).

---

### Task 5.1: Self-contained publish + WebView2 + data-path verification

**Files:**
- Create: `src\TanMenu.Wpf\Properties\PublishProfiles\win-x64.pubxml`
- Create: `D:\0.Work\TanMenuWpf\README.md` (build/run/share instructions)

- [ ] **Step 1: Publish profile** (`win-x64.pubxml`)

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>x64</Platform>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishReadyToRun>true</PublishReadyToRun>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Publish + run from the publish dir; confirm no .NET install needed and config/logs land in `%LOCALAPPDATA%\TanMenu`.**

```powershell
dotnet publish src\TanMenu.Wpf\TanMenu.Wpf.csproj -c Release -p:Platform=x64 /p:PublishProfile=win-x64
# run the published exe, then:
dir "$env:LOCALAPPDATA\TanMenu"        # config.json
dir "$env:LOCALAPPDATA\TanMenu\logs"   # log-*.txt
```
Expected: app runs; `config.json` + `logs\` exist under LocalAppData; nothing written next to the exe.

- [ ] **Step 3: README** — document: requires WebView2 Evergreen runtime (preinstalled on Win11; bundle `MicrosoftEdgeWebView2RuntimeInstallerX64` if targeting older Win10), how to build/publish/share, the data location, and that MSIX packaging is deferred (structure is MSIX-ready: data via IAppDataPaths, runFullTrust-compatible).

- [ ] **Step 4: Commit** (`git commit -m "chore(wpf): self-contained publish profile + README"`)

---

### Task 5.2: MSIX-readiness notes (no build)

**Files:**
- Create: `D:\0.Work\TanMenuWpf\docs\msix-readiness.md`

- [ ] **Step 1: Document** the path to MSIX when ready: wrap with single-project MSIX or a `.wapproj`; declare `runFullTrust`; switch `IAutoStartService` to a `windows.startupTask`-backed impl (the parameterless `AppDataPaths()` ctor already handles packaged `ApplicationData` automatically when identity is present); reserve the name in Partner Center; privacy policy URL; Policy 10.1 value statement. No code changes this phase.

- [ ] **Step 2: Commit** (`git commit -m "docs(wpf): MSIX-readiness notes"`)

---

## Self-Review (run against the design before execution)

- **Coverage:** Repo/solution/projects (T1.1), Core reuse + tests (T1.2), transparent shell risk (T1.3), wwwroot+components port (T2.1/2.2), Core→IconBase64 bridge (T2.3), wired render (T2.4), placement (T3.1), hide-on-blur/launch (T3.2), single instance (T3.3), tray (T4.1), hotkey (T4.2), autostart (T4.3), settings (T4.4), packaging (T5.1), MSIX notes (T5.2). All design items mapped.
- **Type consistency:** `IWindowHost` members (`ResizeToContentAndPlaceAsync`, `Hide`, `ShowAndActivate`, `Toggle`, `SuppressHide`) are defined in T2.4/T3.1/T3.2/T4.4 and used consistently. `MenuGroupVm`/`MenuItemVm` defined T2.3, consumed T2.4. `App.Tray`/`App.Hotkey`/`App.WmShowFirstInstance`/`App.ExitApp` introduced T3.3/T4.1/T4.2 and cross-referenced.
- **Known risks flagged:** WebView2 transparency under WindowChrome (T1.3 gate); the retro components' Blazor `@namespace` (folder `Windows3.1` → `Windows3_1`) must be verified against the copied files in T2.4; multi-monitor placement is primary-screen-first with a documented cursor-monitor follow-up (T3.1).
