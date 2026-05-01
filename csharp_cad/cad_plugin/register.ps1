# register.ps1 - CAD Plugin Registration Tool

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
Write-Host "CAD Plugin Registration Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "DLL Path: $dllPath" -ForegroundColor White
Write-Host ""

if (-not (Test-Path $dllPath)) {
    Write-Host "[ERROR] DLL file not found: $dllPath" -ForegroundColor Red
    Write-Host "Please run: dotnet build -c Debug" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Scanning and registering all AutoCAD versions..." -ForegroundColor Yellow
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
    Write-Host "[ERROR] No AutoCAD installation found" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Found $($versions.Count) AutoCAD version(s)" -ForegroundColor Green
Write-Host ""

foreach ($v in $versions) {
    $appPath = Join-Path $v.Path 'Applications\mycad'
    Write-Host "Registering: $($v.Version) - $($v.Product)" -ForegroundColor Cyan
    
    New-Item -Path $appPath -Force | Out-Null
    New-ItemProperty -Path $appPath -Name 'DESCRIPTION' -Value 'CAD Plugin' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $appPath -Name 'LOADCTRLS' -Value 2 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $appPath -Name 'LOADER' -Value $dllPath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $appPath -Name 'MANAGED' -Value 1 -PropertyType DWord -Force | Out-Null
    
    Write-Host "  Registry path: $appPath" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "Registration Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "DLL Location: $dllPath" -ForegroundColor White
Write-Host ""
Write-Host "Use NETLOAD command in AutoCAD to load this DLL" -ForegroundColor Yellow
Write-Host "Then type HELLO command to test the plugin" -ForegroundColor Yellow
Write-Host ""

Read-Host "Press Enter to exit"