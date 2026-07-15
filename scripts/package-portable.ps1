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

Write-Host "Publishing TanMenu portable edition $version ($Configuration)..."
dotnet publish (Resolve-RepoPath "src\TanMenu.Wpf\TanMenu.Wpf.csproj") `
    -c $Configuration -p:Platform=x64 /p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Write-Host "Staging portable files..."
Copy-Item -Path (Join-Path $publishDir "*") -Destination $stageDir -Recurse -Force
Remove-Item -LiteralPath (Join-Path $stageDir "config.json") -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $stageDir -Recurse -File -Filter "*.pdb" | Remove-Item -Force

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    (Join-Path $stageDir "portable.flag"),
    "TanMenu portable mode marker. Do not remove this file.`r`n",
    $utf8NoBom)
Copy-Item -LiteralPath (Resolve-RepoPath "packaging\portable\README-PORTABLE.txt") `
    -Destination (Join-Path $stageDir "README-PORTABLE.txt") -Force

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
