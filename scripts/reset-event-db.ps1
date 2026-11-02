<#
.SYNOPSIS
    Official Event Database Reset & Secret Provisioning Script
.DESCRIPTION
    Wipes the event database, generates new cryptographic keys and desk tokens,
    writes token slips directly to C:\BreachSecrets (restricted), and establishes
    the immutable genesis state for the competition.
    To be run on the event host machine on event eve.
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Red
Write-Host " ROGUE AI: OFFICIAL COMPETITION EVENT DATABASE RESET" -ForegroundColor Red
Write-Host "==========================================================" -ForegroundColor Red

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$secretsDir = "C:\BreachSecrets"
$dbDir = Join-Path $root "server\ControlPlane\data"
$dbPath = Join-Path $dbDir "breach.db"
$workspacesDir = Join-Path $dbDir "workspaces"

# 1. Safety verification
if (-not $Force) {
    $confirm = Read-Host "WARNING: This will wipe all participant data and regenerate all desk tokens! Type 'RESET' to continue"
    if ($confirm -ne "RESET") {
        Write-Warning "Reset aborted."
        exit 0
    }
}

# 2. Terminate any running services that could lock the DB
Write-Host "`n[1/5] Ensuring ControlPlane and Kiosks are stopped..." -ForegroundColor Yellow
Stop-Process -Name ControlPlane -Force -ErrorAction SilentlyContinue
Stop-Process -Name LabKiosk -Force -ErrorAction SilentlyContinue

# 3. Wipe data directory
Write-Host "`n[2/5] Wiping database and participant workspaces..." -ForegroundColor Yellow
if (Test-Path "$dbPath*") {
    Remove-Item "$dbPath*" -Force
    Write-Host "Deleted existing breach.db and WAL files." -ForegroundColor Gray
}
if (Test-Path $workspacesDir) {
    Remove-Item $workspacesDir -Recurse -Force
    Write-Host "Cleaned workspaces directory." -ForegroundColor Gray
}
New-Item -ItemType Directory -Path $dbDir -Force | Out-Null
New-Item -ItemType Directory -Path $workspacesDir -Force | Out-Null

# 4. Generate fresh 50 desk tokens & slips into C:\BreachSecrets
Write-Host "`n[3/5] Generating fresh event signing keys and 50 desk tokens..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $secretsDir -Force | Out-Null
& icacls "$secretsDir" /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F" | Out-Null

$slipsOutput = Join-Path $secretsDir "desk_tokens_slips.csv"
python "$root\engine\keygen.py" --output-db "$dbPath" --output-slips "$slipsOutput"
if ($LASTEXITCODE -ne 0) {
    Write-Error "keygen.py failed!"
    exit 1
}
Write-Host "Fresh desk token slips written to $slipsOutput (restricted ACLs)." -ForegroundColor Green

# 5. Generate fresh Admin Master Key
Write-Host "`n[4/5] Generating fresh Admin Master Key..." -ForegroundColor Yellow
$adminKeyFile = Join-Path $secretsDir "admin_master_key.txt"
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$adminKey = [Convert]::ToBase64String($bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')
Set-Content -Path $adminKeyFile -Value $adminKey -Encoding UTF8
Write-Host "Admin Master Key generated and stored securely at $adminKeyFile." -ForegroundColor Green

# Write appsettings.Production.json (gitignored)
$prodSettings = Join-Path $root "server\ControlPlane\appsettings.Production.json"
$prodConfig = @{
    Breach = @{
        AdminKey = $adminKey
        EnforceEventSecurity = $true
        AdminLoopbackOnly = $true
    }
} | ConvertTo-Json -Depth 5
Set-Content -Path $prodSettings -Value $prodConfig -Encoding UTF8
Write-Host "Wrote gitignored server/ControlPlane/appsettings.Production.json." -ForegroundColor Green

# 6. Chain Fingerprint
Write-Host "`n[5/5] Recording genesis chain state..." -ForegroundColor Yellow
$fingerprintOutput = Join-Path $secretsDir "event_chain_fingerprint.txt"
python "$root\engine\keygen.py" --print-fingerprint | Out-File -FilePath $fingerprintOutput -Encoding UTF8
Write-Host "Official event fingerprint saved to $fingerprintOutput." -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " EVENT RESET COMPLETE. HARDWARE READY FOR COMPETITION." -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
