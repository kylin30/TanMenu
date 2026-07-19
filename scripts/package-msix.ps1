param(
    [string]$Configuration = "Release",
    [string]$IdentityName = "TanXiang.TanMenu",
    [string]$Publisher = "CN=263873C5-111A-4594-91B1-894ED1A74F54",
    [string]$PublisherDisplayName = "TanXiang",
    [string]$OutputRoot = "dist\store"
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $PSScriptRoot "..\$path"
}

function Get-Version4 {
    $propsPath = Resolve-RepoPath "Directory.Build.props"
    $xml = [xml](Get-Content $propsPath)
    $version = [string]$xml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }
    $parts = $version.Split(".")
    while ($parts.Count -lt 4) { $parts += "0" }
    return ($parts[0..3] -join ".")
}

function Find-MakeAppx {
    $cmd = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $kits = "C:\Program Files (x86)\Windows Kits\10\bin"
    $found = Get-ChildItem $kits -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\makeappx\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($found) { return $found.FullName }
    throw "makeappx.exe was not found. Install the Windows SDK/MSIX Packaging Tools."
}

function New-Logo([string]$sourcePath, [string]$targetPath, [int]$width, [int]$height) {
    Add-Type -AssemblyName System.Drawing

    $src = [System.Drawing.Image]::FromFile($sourcePath)
    try {
        $bmp = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $maxW = [math]::Floor($width * 0.78)
                $maxH = [math]::Floor($height * 0.78)
                $scale = [math]::Min($maxW / $src.Width, $maxH / $src.Height)
                $drawW = [math]::Max(1, [int]($src.Width * $scale))
                $drawH = [math]::Max(1, [int]($src.Height * $scale))
                $x = [int](($width - $drawW) / 2)
                $y = [int](($height - $drawH) / 2)
                $g.DrawImage($src, $x, $y, $drawW, $drawH)
            }
            finally { $g.Dispose() }
            $dir = Split-Path $targetPath
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            $bmp.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bmp.Dispose() }
    }
    finally { $src.Dispose() }
}

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = Resolve-RepoPath "src\TanMenu.Wpf\bin\publish\win-x64"
$stageDir = Resolve-RepoPath "build\msix\stage"
$outputDir = Resolve-RepoPath $OutputRoot
$version = Get-Version4

Write-Host "Publishing TanMenu.Wpf ($Configuration)..."
dotnet publish (Resolve-RepoPath "src\TanMenu.Wpf\TanMenu.Wpf.csproj") `
    -c $Configuration -p:Platform=x64 /p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

if (Test-Path $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "Staging package files..."
Copy-Item -Path (Join-Path $publishDir "*") -Destination $stageDir -Recurse -Force
Remove-Item -LiteralPath (Join-Path $stageDir "config.json") -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $stageDir -Recurse -File -Filter "*.pdb" | Remove-Item -Force

$assets = Join-Path $stageDir "Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$sourceLogo = Resolve-RepoPath "src\TanMenu.Wpf\wwwroot\app-tile.png"
if (!(Test-Path $sourceLogo)) { $sourceLogo = Resolve-RepoPath "src\TanMenu.Wpf\wwwroot\app.ico" }
New-Logo $sourceLogo (Join-Path $assets "StoreLogo.png") 50 50
New-Logo $sourceLogo (Join-Path $assets "Square44x44Logo.png") 44 44
New-Logo $sourceLogo (Join-Path $assets "Square71x71Logo.png") 71 71
New-Logo $sourceLogo (Join-Path $assets "Square150x150Logo.png") 150 150
New-Logo $sourceLogo (Join-Path $assets "Square310x310Logo.png") 310 310
New-Logo $sourceLogo (Join-Path $assets "Wide310x150Logo.png") 310 150

$manifestTemplate = Resolve-RepoPath "packaging\msix\AppxManifest.xml.in"
$manifest = Get-Content $manifestTemplate -Raw
$manifest = $manifest.Replace("__IDENTITY_NAME__", $IdentityName)
$manifest = $manifest.Replace("__PUBLISHER__", $Publisher)
$manifest = $manifest.Replace("__PUBLISHER_DISPLAY_NAME__", $PublisherDisplayName)
$manifest = $manifest.Replace("__VERSION__", $version)
[System.IO.File]::WriteAllText((Join-Path $stageDir "AppxManifest.xml"), $manifest, [System.Text.UTF8Encoding]::new($false))

$makeappx = Find-MakeAppx
$msix = Join-Path $outputDir "TanMenu_$($version)_x64.msix"
if (Test-Path $msix) { Remove-Item -LiteralPath $msix -Force }

Write-Host "Packing MSIX..."
& $makeappx pack /d $stageDir /p $msix /o
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $msix)) {
    throw "makeappx failed to create the Store package (exit code $LASTEXITCODE): $msix"
}

$hash = (Get-FileHash -LiteralPath $msix -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$msix.sha256"
[System.IO.File]::WriteAllText(
    $shaPath,
    "$hash  $(Split-Path $msix -Leaf)`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Created $msix"
Write-Host "SHA256 $hash"
Get-Item $msix, $shaPath
