param(
    [string]$PackagePath = "",
    [string]$ReportPath = "",
    [switch]$SkipReset
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

function Find-AppCert {
    $cmd = Get-Command appcert.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $kits = "C:\Program Files (x86)\Windows Kits\10\App Certification Kit"
    $found = Get-ChildItem $kits -Filter appcert.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { return $found.FullName }
    throw "appcert.exe was not found. Install the Windows App Certification Kit from the Windows SDK."
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Windows App Certification Kit requires an elevated PowerShell window. Re-run this script as Administrator."
    }
}

$version = Get-Version4
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Resolve-RepoPath "dist\store\TanMenu_$($version)_x64.msix"
}
else {
    $PackagePath = Resolve-RepoPath $PackagePath
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Resolve-RepoPath "dist\store\TanMenu_$($version)_wack.xml"
}
else {
    $ReportPath = Resolve-RepoPath $ReportPath
}

if (!(Test-Path $PackagePath)) { throw "Package not found: $PackagePath" }

Assert-Administrator
$appcert = Find-AppCert
$reportDir = Split-Path $ReportPath
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

if (Test-Path $ReportPath) {
    Remove-Item -LiteralPath $ReportPath -Force
}

if (!$SkipReset) {
    & $appcert reset
}
$startedAt = Get-Date
& $appcert test -appxpackagepath $PackagePath -reportoutputpath $ReportPath

if (!(Test-Path $ReportPath)) { throw "WACK did not produce a report: $ReportPath" }
$reportItem = Get-Item $ReportPath
if ($reportItem.LastWriteTime -lt $startedAt.AddSeconds(-5)) {
    throw "WACK report appears stale: $ReportPath"
}

[xml]$report = Get-Content $ReportPath -Raw
Write-Host "Report: $ReportPath"
Write-Host "Overall result: $($report.REPORT.OVERALL_RESULT)"

foreach ($test in $report.GetElementsByTagName("TEST")) {
    $resultNode = $test.SelectSingleNode("RESULT")
    if (!$resultNode) { continue }

    $result = $resultNode.InnerText.Trim()
    if ([string]::IsNullOrWhiteSpace($result) -or $result -eq "PASS") { continue }

    $requirement = $test.ParentNode
    while ($requirement -and $requirement.Name -ne "REQUIREMENT") {
        $requirement = $requirement.ParentNode
    }

    $reqTitle = if ($requirement) { $requirement.GetAttribute("TITLE") } else { "" }
    $name = $test.GetAttribute("NAME")
    $scope = if ($test.GetAttribute("OPTIONAL") -eq "TRUE") { "OPTIONAL " } else { "REQUIRED " }
    Write-Host ""
    Write-Host "[$scope$result] $reqTitle / $name"

    $messages = $test.GetElementsByTagName("MESSAGE")
    foreach ($message in $messages | Select-Object -First 8) {
        $text = $message.GetAttribute("TEXT")
        if (![string]::IsNullOrWhiteSpace($text)) {
            Write-Host "  - $text"
        }
    }
    if ($messages.Count -gt 8) {
        Write-Host "  - ... $($messages.Count - 8) more message(s)"
    }
}
