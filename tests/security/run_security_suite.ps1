<#
.SYNOPSIS
    Single Entry Point Security Suite for Rogue AI
.DESCRIPTION
    1. Executes ControlPlane.Tests (token gating, crash recovery, replay defense, backup retention).
    2. Starts ControlPlane if not already running on port 5080.
    3. Runs tests/security/test_security_suite.py (live REST & SignalR Hub hardening).
    4. Stops temporary ControlPlane process.
#>

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ROGUE AI: UNIFIED SECURITY VERIFICATION SUITE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
$env:Path = "$env:LOCALAPPDATA\dotnet;$env:Path"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")

# 1. Run C# Unit and Security Hardening Tests
Write-Host "`n[1/3] Running ControlPlane.Tests Security Suite..." -ForegroundColor Yellow
& dotnet test "$root\server\ControlPlane.Tests" -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "ControlPlane.Tests failed!"
    exit $LASTEXITCODE
}
Write-Host "--> Unit & Security Hardening Tests PASSED." -ForegroundColor Green

# 2. Check if ControlPlane is running; if not, launch it temporarily
Write-Host "`n[2/3] Checking ControlPlane live service with isolated test database..." -ForegroundColor Yellow
$startedServer = $false
$cpProcess = $null

$testDb = "$root\server\ControlPlane\data\test_breach.db"
Remove-Item "$testDb*" -Force -ErrorAction SilentlyContinue
$env:BREACH_TEST_DB = $testDb
$env:BREACH_ADMIN_KEY = "SecSuiteTestKey_987654321_Valid"

$portActive = $false
try {
    $tcp = Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue
    if ($tcp) { $portActive = $true }
} catch {}

$localDotnet = "$env:LOCALAPPDATA\dotnet\dotnet.exe"
$dotnetExe = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source) }

if (-not $portActive) {
    Write-Host "Starting ControlPlane with test database on http://127.0.0.1:5080..." -ForegroundColor Gray
    $cpProcess = Start-Process -FilePath $dotnetExe `
        -ArgumentList "run --project `"$root\server\ControlPlane`" -c Release --no-build -- --Breach:DatabasePath data/test_breach.db" `
        -WorkingDirectory "$root" `
        -PassThru -NoNewWindow
    $startedServer = $true
    Start-Sleep -Seconds 6
}

try {
    # 3. Run Live Security Suite (Python)
    Write-Host "`n[3/4] Running Live REST & SignalR Hardening Tests..." -ForegroundColor Yellow
    python "$root\tests\security\test_security_suite.py"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Live Security Suite failed!"
        exit $LASTEXITCODE
    }
    Write-Host "--> Live Hardening Tests PASSED." -ForegroundColor Green

    # 4. Run Adversarial Sandbox Escape & DoS Containment Suite
    Write-Host "`n[4/4] Running Adversarial Sandbox Escape & Resource Isolation Suite..." -ForegroundColor Yellow
    python "$root\tests\security\test_sandbox_escape.py"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Sandbox Escape Suite failed!"
        exit $LASTEXITCODE
    }
    Write-Host "--> Sandbox Escape & Resource Isolation PASSED." -ForegroundColor Green
}
finally {
    if ($startedServer -and $cpProcess -and -not $cpProcess.HasExited) {
        Write-Host "Stopping temporary ControlPlane test instance..." -ForegroundColor Gray
        Stop-Process -Id $cpProcess.Id -Force -ErrorAction SilentlyContinue
        Stop-Process -Name ControlPlane -Force -ErrorAction SilentlyContinue
    }
    Remove-Item "$testDb*" -Force -ErrorAction SilentlyContinue
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL SECURITY SUITE CHECKS COMPLETED & VERIFIED: 100% PASS" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
