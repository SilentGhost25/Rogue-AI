<#
.SYNOPSIS
    End-to-End Build & Leak-Verified Packaging for Rogue AI Kiosk
.DESCRIPTION
    1. Cleans dedicated KioskRelease directories.
    2. Builds LabKiosk with -p:KioskOnly=true into bin/KioskRelease/net9.0-windows/.
    3. Compiles installer/kiosk.iss via Inno Setup.
    4. Runs verify-kiosk-installer.ps1 on the resulting installer.
#>

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ROGUE AI: Automated Kiosk Clean-Build-Package-Verify" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
$env:Path = "$env:LOCALAPPDATA\dotnet;$env:Path"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$kioskProj = Join-Path $root "kiosk\LabKiosk\LabKiosk.csproj"
$kioskReleaseDir = Join-Path $root "kiosk\LabKiosk\bin\KioskRelease"
$kioskObjDir = Join-Path $root "kiosk\LabKiosk\obj\KioskRelease"
$issPath = Join-Path $root "installer\kiosk.iss"
$distExe = Join-Path $root "dist\RogueAI-Kiosk-Setup.exe"

# 1. Clean dedicated KioskRelease output
Write-Host "[1/4] Cleaning dedicated KioskRelease directories..." -ForegroundColor Yellow
if (Test-Path $kioskReleaseDir) { Remove-Item -Path $kioskReleaseDir -Recurse -Force }
if (Test-Path $kioskObjDir) { Remove-Item -Path $kioskObjDir -Recurse -Force }

# 2. Compile LabKiosk with KIOSK_ONLY define and dedicated output
Write-Host "[2/4] Building LabKiosk in KioskOnly Release mode..." -ForegroundColor Yellow
& dotnet build "$kioskProj" -c Release -p:KioskOnly=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 3. Compile Inno Setup installer
Write-Host "[3/4] Compiling Inno Setup kiosk installer..." -ForegroundColor Yellow
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $isccPath)) {
    Write-Error "Inno Setup compiler (ISCC.exe) not found."
    exit 1
}

& "$isccPath" "$issPath"
if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 4. Run automated leak scanner
Write-Host "[4/4] Executing automated leak verification scanner..." -ForegroundColor Yellow
$verifyScript = Join-Path $root "installer\verify-kiosk-installer.ps1"
powershell -ExecutionPolicy Bypass -File "$verifyScript" -InstallerPath "$distExe"
if ($LASTEXITCODE -ne 0) {
    if (Test-Path "$distExe") {
        Write-Warning "Deleting non-compliant installer to prevent distribution: $distExe"
        Remove-Item -Path "$distExe" -Force -ErrorAction SilentlyContinue
    }
    Write-Error "Leak scan failed on built installer!"
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " KIOSK BUILD, PACKAGING & VERIFICATION COMPLETE: ALL CLEAN" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
