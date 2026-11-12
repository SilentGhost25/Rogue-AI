<#
.SYNOPSIS
    End-to-End Build & Packaging for Unified Rogue AI Setup (RogueAI-Setup.exe)
.DESCRIPTION
    1. Publishes latest self-contained LabKiosk.
    2. Publishes ControlPlane release build.
    3. Compiles installer/setup.iss via Inno Setup (ISCC.exe) into dist/RogueAI-Setup.exe.
    4. Syncs the installer to server/ControlPlane/wwwroot for organizer distribution.
    5. Cleans up obsolete standalone kiosk setup binaries.
#>

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ROGUE AI: Automated Unified Setup Build & Packaging" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
$env:Path = "$env:LOCALAPPDATA\dotnet;$env:Path"
$dotnetCmd = if (Test-Path "$env:LOCALAPPDATA\dotnet\dotnet.exe") { "$env:LOCALAPPDATA\dotnet\dotnet.exe" } else { "dotnet" }

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$kioskProj = Join-Path $root "kiosk\LabKiosk\LabKiosk.csproj"
$kioskSelfContained = Join-Path $root "kiosk\LabKiosk\bin\SelfContained"
$cpProj = Join-Path $root "server\ControlPlane\ControlPlane.csproj"
$cpRelease = Join-Path $root "server\ControlPlane\bin\SelfContained"
$issPath = Join-Path $root "installer\setup.iss"
$distExe = Join-Path $root "dist\RogueAI-Setup.exe"
$legacyKioskExe = Join-Path $root "dist\RogueAI-Kiosk-Setup.exe"

# 1. Remove obsolete standalone kiosk installer if present
if (Test-Path $legacyKioskExe) {
    Remove-Item $legacyKioskExe -Force -ErrorAction SilentlyContinue
    Write-Host "[*] Removed obsolete standalone $legacyKioskExe" -ForegroundColor Gray
}
$legacyHostExe = Join-Path $root "dist\RogueAI-Host-Setup.exe"
if (Test-Path $legacyHostExe) {
    Remove-Item $legacyHostExe -Force -ErrorAction SilentlyContinue
    Write-Host "[*] Removed obsolete standalone $legacyHostExe" -ForegroundColor Gray
}
$staleApp = Join-Path $root "dist\app"
if (Test-Path $staleApp) {
    Remove-Item $staleApp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[*] Cleaned stale dist/app staging folder." -ForegroundColor Gray
}

# 1b. Stage OpenCode binary for offline bundling into RogueAI-Setup.exe
$vendorOcDir = Join-Path $root "vendor\opencode"
$vendorOcExe = Join-Path $vendorOcDir "opencode.exe"
if (-not (Test-Path $vendorOcExe)) {
    $npmOcExe = Join-Path $env:APPDATA "npm\node_modules\opencode-ai\node_modules\opencode-windows-x64\bin\opencode.exe"
    if (-not (Test-Path $npmOcExe)) {
        $provScript = Join-Path $root "scripts\provision-ai-clis.ps1"
        if (Test-Path $provScript) {
            Write-Host "[*] Provisioning OpenCode CLI before packaging..." -ForegroundColor Cyan
            & powershell -NoProfile -ExecutionPolicy Bypass -File $provScript -SkipAgy
        }
    }
    if (Test-Path $npmOcExe) {
        New-Item -ItemType Directory -Path $vendorOcDir -Force | Out-Null
        Copy-Item -Path $npmOcExe -Destination $vendorOcExe -Force
        Write-Host "[*] Staged opencode.exe into $vendorOcExe" -ForegroundColor Green
    } else {
        Write-Warning "opencode.exe not found locally; installer will fetch opencode during setup."
    }
}

# 1c. Stage Antigravity CLI binary (agy.exe) for offline bundling into RogueAI-Setup.exe
$vendorAgyDir = Join-Path $root "vendor\agy"
$vendorAgyExe = Join-Path $vendorAgyDir "agy.exe"
$agySources = @(
    (Join-Path $env:USERPROFILE ".gemini\bin\agy.exe"),
    (Join-Path $env:USERPROFILE ".gemini\antigravity-cli\bin\agy.exe"),
    (Join-Path $env:LOCALAPPDATA "agy\bin\agy.exe")
)
$foundAgy = $agySources | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $foundAgy) {
    $cmdAgy = Get-Command agy.exe -ErrorAction SilentlyContinue
    if ($cmdAgy) { $foundAgy = $cmdAgy.Source }
}
if ($foundAgy) {
    $copyAgy = (-not (Test-Path $vendorAgyExe)) -or ((Get-Item $foundAgy).Length -ne (Get-Item $vendorAgyExe).Length)
    if ($copyAgy) {
        New-Item -ItemType Directory -Path $vendorAgyDir -Force | Out-Null
        Copy-Item -Path $foundAgy -Destination $vendorAgyExe -Force
        Write-Host "[*] Staged agy.exe ($foundAgy) into $vendorAgyExe" -ForegroundColor Green
    }
} elseif (-not (Test-Path $vendorAgyExe)) {
    Write-Warning "agy.exe not found locally; participant laptops will need agy.exe pre-installed."
}

# 2. Clean previous publish outputs and publish Self-Contained LabKiosk (.NET 9 runtime bundled)
Write-Host "[1/4] Publishing self-contained LabKiosk..." -ForegroundColor Yellow
if (Test-Path $kioskSelfContained) { Remove-Item $kioskSelfContained -Recurse -Force -ErrorAction SilentlyContinue }
& $dotnetCmd publish "$kioskProj" -c Release -r win-x64 --self-contained true -o "$kioskSelfContained"
if ($LASTEXITCODE -ne 0) {
    Write-Error "LabKiosk publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 3. Clean previous publish outputs and publish Self-Contained ControlPlane (.NET 9 runtime bundled)
Write-Host "[2/4] Publishing self-contained ControlPlane..." -ForegroundColor Yellow
if (Test-Path $cpRelease) { Remove-Item $cpRelease -Recurse -Force -ErrorAction SilentlyContinue }
& $dotnetCmd publish "$cpProj" -c Release -r win-x64 --self-contained true -o "$cpRelease"
if ($LASTEXITCODE -ne 0) {
    Write-Error "ControlPlane publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
Remove-Item (Join-Path $cpRelease "Data"), (Join-Path $cpRelease "data") -Recurse -Force -ErrorAction SilentlyContinue

# 4. Compile Inno Setup installer
Write-Host "[3/4] Compiling Inno Setup unified installer (setup.iss)..." -ForegroundColor Yellow
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files\Inno Setup 6\ISCC.exe"
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

# 5. Sync to webroot
Write-Host "[4/4] Syncing RogueAI-Setup.exe to ControlPlane webroot..." -ForegroundColor Yellow
$webrootTargets = @(
    (Join-Path $root "server\ControlPlane\wwwroot\RogueAI-Setup.exe"),
    (Join-Path $root "server\ControlPlane\bin\Debug\net9.0\wwwroot\RogueAI-Setup.exe"),
    (Join-Path $root "server\ControlPlane\bin\Release\net9.0\wwwroot\RogueAI-Setup.exe")
)
foreach ($target in $webrootTargets) {
    $parent = Split-Path -Parent $target
    if (Test-Path $parent) {
        Copy-Item -Path $distExe -Destination $target -Force
        Write-Host "  -> Synced to: $target" -ForegroundColor Green
    }
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " UNIFIED SETUP BUILD & PACKAGING COMPLETE: ALL READY" -ForegroundColor Green
Write-Host " Setup Path: $distExe" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
