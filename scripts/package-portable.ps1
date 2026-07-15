param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "dist\portable"
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
    if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "Directory.Build.props Version '$version' is not a three- or four-part numeric version."
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

$version = Get-ProductVersion
$publishRoot = Resolve-RepoPath "src\TanMenu.Wpf\bin\publish"
$publishDir = Join-Path $publishRoot "win-x64"
$buildRoot = Resolve-RepoPath "build\portable"
$stageName = "TanMenu_$($version)_win-x64_portable"
$stageDir = Join-Path $buildRoot $stageName
$outputDir = Resolve-RepoPath $OutputRoot

Reset-BuildDirectory $publishDir $publishRoot
Write-Host "Publishing TanMenu portable edition ($Configuration)..."
dotnet publish (Resolve-RepoPath "src\TanMenu.Wpf\TanMenu.Wpf.csproj") `
    -c $Configuration -p:Platform=x64 /p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Reset-BuildDirectory $stageDir $buildRoot
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

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

$zipPath = Join-Path $outputDir "$stageName.zip"
$shaPath = "$zipPath.sha256"
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $shaPath -Force -ErrorAction SilentlyContinue

Write-Host "Creating portable ZIP..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    $shaPath,
    "$hash  $([System.IO.Path]::GetFileName($zipPath))`n",
    $utf8NoBom)

Write-Host "Created $zipPath"
Write-Host "SHA256 $hash"
Get-Item $zipPath, $shaPath
