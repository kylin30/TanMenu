param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "dist\velopack",
    [string]$VpkPath = "vpk",
    [string]$RepositoryUrl = "https://github.com/kylin30/TanMenu",
    [string]$GitHubToken = "",
    [switch]$DownloadPrevious
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return [System.IO.Path]::GetFullPath($path) }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\$path"))
}

function Get-ProductVersion {
    $propsPath = Resolve-RepoPath "Directory.Build.props"
    $xml = [xml](Get-Content $propsPath)
    $version = [string]$xml.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Directory.Build.props Version '$version' is not a three-part numeric version required by Velopack."
    }
    return $version
}

function Reset-BuildDirectory([string]$path, [string]$expectedParent) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    $fullParent = [System.IO.Path]::GetFullPath($expectedParent).TrimEnd('\') + '\'
    if (!$fullPath.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside '$fullParent': $fullPath"
    }
    if (Test-Path $fullPath) { Remove-Item -LiteralPath $fullPath -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
}

# Validates the shape of a NativeAOT publish. Under AOT the payload is one native TanMenu.Wpf.exe,
# the Avalonia native libraries, and the few loose assets the app reads from disk -- not the ~564
# files a self-contained ReadyToRun publish produced. If PublishAot silently fails to apply (a bad
# profile edit, an MSBuild property override), the folder still looks plausible but contains managed
# assemblies plus the CoreCLR host, and the shipped app would need a .NET runtime the user lacks.
function Assert-AotPublishOutput([string]$dir) {
    foreach ($file in @("TanMenu.Wpf.exe", "app.ico")) {
        if (!(Test-Path -LiteralPath (Join-Path $dir $file))) {
            throw "Publish output is missing '$file': $dir"
        }
    }
    # SoundService and MenuService read these by path at run time, so they must survive publish.
    foreach ($folder in @("wwwroot\sounds", "wwwroot\tool-icons")) {
        if (!(Test-Path -LiteralPath (Join-Path $dir $folder))) {
            throw "Publish output is missing the '$folder' asset folder: $dir"
        }
    }
    foreach ($managed in @("TanMenu.Wpf.dll", "coreclr.dll", "TanMenu.Wpf.runtimeconfig.json")) {
        if (Test-Path -LiteralPath (Join-Path $dir $managed)) {
            throw "Publish output contains '$managed', so this is a managed (non-AOT) publish: $dir"
        }
    }
    # Avalonia loads these by filename at run time. A missing one is not a build error -- it is a
    # blank or crashing window on the user's machine -- so warn loudly rather than failing a release
    # if a future Avalonia version renames them.
    $natives = @("libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll")
    $missing = @($natives | Where-Object { !(Test-Path -LiteralPath (Join-Path $dir $_)) })
    if ($missing.Count -gt 0) {
        Write-Warning "Avalonia native libraries not found in the publish output: $($missing -join ', '). Verify the payload before shipping."
    }
    $files = @(Get-ChildItem -LiteralPath $dir -Recurse -File)
    $megabytes = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Host "Publish payload: $($files.Count) files, $megabytes MB."
}

function Invoke-Vpk([string[]]$Arguments) {
    & $VpkPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "vpk failed with exit code $LASTEXITCODE."
    }
}

$version = Get-ProductVersion
$publishRoot = Resolve-RepoPath "src\TanMenu.Wpf\bin\publish"
$publishDir = Join-Path $publishRoot "win-x64"
$buildRoot = Resolve-RepoPath "build\portable"
$stageDir = Join-Path $buildRoot "stage"
$outputDir = Resolve-RepoPath $OutputRoot

Reset-BuildDirectory $publishDir $publishRoot
Reset-BuildDirectory $stageDir $buildRoot
Reset-BuildDirectory $outputDir (Split-Path $outputDir -Parent)

if ($DownloadPrevious) {
    Write-Host "Downloading the previous Velopack release for delta generation..."
    $downloadArgs = @(
        "download", "github",
        "--repoUrl", $RepositoryUrl,
        "--outputDir", $outputDir
    )
    if (![string]::IsNullOrWhiteSpace($GitHubToken)) {
        $downloadArgs += @("--token", $GitHubToken)
    }
    Invoke-Vpk $downloadArgs
}

# NOTE: deliberately no --no-restore. A clean runner has neither the win-x64 runtime pack nor the
# NativeAOT ILC packs, and publish must be allowed to restore them (commit b3a6cb2). Expect this step
# to take several minutes: ILC compiles and natively links the whole program in one pass.
Write-Host "Publishing TanMenu portable edition $version ($Configuration)..."
dotnet publish (Resolve-RepoPath "src\TanMenu.Wpf\TanMenu.Wpf.csproj") `
    -c $Configuration -p:Platform=x64 /p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Assert-AotPublishOutput $publishDir

Write-Host "Staging portable files..."
Copy-Item -Path (Join-Path $publishDir "*") -Destination $stageDir -Recurse -Force
# Defensive: publish never produces a config.json, but this guarantees a developer's own settings can
# never leak into a shipped package.
Remove-Item -LiteralPath (Join-Path $stageDir "config.json") -Force -ErrorAction SilentlyContinue
# Still required under AOT: ILC emits a NATIVE TanMenu.Wpf.pdb next to the executable.
Get-ChildItem -Path $stageDir -Recurse -File -Filter "*.pdb" | Remove-Item -Force

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    (Join-Path $stageDir "portable.flag"),
    "TanMenu portable mode marker. Do not remove this file.`r`n",
    $utf8NoBom)
Copy-Item -LiteralPath (Resolve-RepoPath "packaging\portable\README-PORTABLE.txt") `
    -Destination (Join-Path $stageDir "README-PORTABLE.txt") -Force

# MIT obligation for the vendored aero-theme-for-avalonia files under Themes\Aero: the copyright
# notice and the permission notice must travel with the copies, and a notice that exists only in the
# source tree does not travel with the binary. The binary is what users get, so the licence ships
# beside the executable. Fails the release rather than shipping a package without it.
$aeroLicense = Resolve-RepoPath "src\TanMenu.Wpf\Themes\Aero\LICENSE-aero-theme-for-avalonia.txt"
if (!(Test-Path -LiteralPath $aeroLicense)) {
    throw "Missing third-party licence '$aeroLicense'. Themes\Aero vendors MIT-licensed files and the package must carry their notice."
}
Copy-Item -LiteralPath $aeroLicense `
    -Destination (Join-Path $stageDir "THIRD-PARTY-NOTICES.txt") -Force

# --mainExe must stay TanMenu.Wpf.exe and --packId must stay TanMenu: packId names the stable root
# launcher (<root>\TanMenu.exe) that the taskbar pin and README-PORTABLE.txt point at, and mainExe is
# what the already-released 0.9.6 Velopack shim will shim to when it applies this update.
# --icon reads app.ico out of the stage, which the csproj's CopyAppIconToPublish target puts in the
# publish folder; Assert-AotPublishOutput fails early if that target ever stops running.
Write-Host "Creating Velopack update packages and portable ZIP..."
Invoke-Vpk @(
    "pack",
    "--packId", "TanMenu",
    "--packVersion", $version,
    "--packDir", $stageDir,
    "--mainExe", "TanMenu.Wpf.exe",
    "--packTitle", "TanMenu",
    "--packAuthors", "TanSoft",
    "--releaseNotes", (Resolve-RepoPath "packaging\portable\RELEASE-NOTES.md"),
    "--icon", (Join-Path $stageDir "app.ico"),
    "--runtime", "win-x64",
    "--outputDir", $outputDir
)

# TanMenu is distributed as a pure portable app. Velopack also emits an installer by default;
# exclude it from the upload manifest so users cannot accidentally install a build whose data
# policy is intentionally tied to the portable root.
Get-ChildItem -Path $outputDir -File -Filter "*-Setup.exe" | Remove-Item -Force
$assetsPath = Join-Path $outputDir "assets.win.json"
$assets = @((Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json) |
    Where-Object { $_.Type -ne "Installer" })
[System.IO.File]::WriteAllText(
    $assetsPath,
    ($assets | ConvertTo-Json -Compress),
    $utf8NoBom)

$portableZip = @(Get-ChildItem -Path $outputDir -File -Filter "*-Portable.zip")
if ($portableZip.Count -ne 1) {
    throw "Expected exactly one Velopack portable ZIP, found $($portableZip.Count)."
}

$hash = (Get-FileHash $portableZip[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$($portableZip[0].FullName).sha256"
[System.IO.File]::WriteAllText(
    $shaPath,
    "$hash  $($portableZip[0].Name)`n",
    $utf8NoBom)

Write-Host "Created $($portableZip[0].FullName)"
Write-Host "SHA256 $hash"
Get-ChildItem -Path $outputDir -File | Sort-Object Name
