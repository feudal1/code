# unregister.ps1 - CAD Plugin Unregistration Tool

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"" -Verb RunAs
    exit
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dllPath = Join-Path $scriptDir "bin\Debug\net48\cad_plugin.dll"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CAD Plugin Unregistration Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "DLL Path: $dllPath" -ForegroundColor White
Write-Host ""

if (-not (Test-Path $dllPath)) {
    Write-Host "[WARNING] DLL file not found: $dllPath" -ForegroundColor Yellow
    Write-Host "Continuing with registry cleanup anyway..." -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "Scanning and unregistering all AutoCAD versions..." -ForegroundColor Yellow
Write-Host ""

try {
    $baseKey = 'HKLM:\SOFTWARE\Autodesk\AutoCAD'
    $versions = @()
    
    if (Test-Path $baseKey) {
        Get-ChildItem $baseKey | ForEach-Object {
            $versionName = $_.PSChildName
            $versionPath = $_.PSPath
            
            Get-ChildItem $versionPath | ForEach-Object {
                if ($_.PSChildName -like 'ACAD-*') {
                    $product = $_.PSChildName
                    $fullPath = Join-Path $versionPath $product
                    $versions += @{Version=$versionName; Product=$product; Path=$fullPath}
                    Write-Host "  Found version: $versionName - $product" -ForegroundColor White
                }
            }
        }
    }
} catch {
    Write-Host "Error scanning registry: $_" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

if ($versions.Count -eq 0) {
    Write-Host "[WARNING] No AutoCAD installation found" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 0
}

Write-Host ""
Write-Host "Found $($versions.Count) AutoCAD version(s)" -ForegroundColor Green
Write-Host ""

$uninstalled = 0
foreach ($v in $versions) {
    $appPath = Join-Path $v.Path 'Applications\mycad'
    Write-Host "Unregistering: $($v.Version) - $($v.Product)" -ForegroundColor Cyan
    
    if (Test-Path $appPath) {
        Remove-Item -Path $appPath -Force -Recurse
        Write-Host "  Deleted: $appPath" -ForegroundColor Green
        $uninstalled++
    } else {
        Write-Host "  Registry entry not found, skipping" -ForegroundColor Yellow
    }
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "Unregistration Complete! Removed $uninstalled version(s)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Please restart AutoCAD for changes to take effect" -ForegroundColor Yellow
Write-Host ""

Read-Host "Press Enter to exit"

