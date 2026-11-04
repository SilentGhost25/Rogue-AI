# provision-ai-clis.ps1
# Automates provisioning of OpenCode CLI (opencode) and Antigravity CLI (agy)
# for Rogue AI event and developer testing on Windows.

[CmdletBinding()]
param(
    [switch]$SkipOpenCode,
    [switch]$SkipAgy,
    [switch]$ForceReinstall
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  ROGUE AI: AI CLI Provisioning Script (Windows)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check Node.js & npm
Write-Host "[1/3] Checking Node.js and npm..." -ForegroundColor Cyan
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
$npmCmd = Get-Command npm -ErrorAction SilentlyContinue

if (-not $nodeCmd -or -not $npmCmd) {
    Write-Warning "Node.js and npm were not found in PATH."
    Write-Host "Please install Node.js 22.x+ from https://nodejs.org/ before continuing." -ForegroundColor Red
    exit 1
}

$nodeVer = (node -v).Trim()
$npmVer = (npm -v).Trim()
Write-Host "  -> Node.js version: $nodeVer" -ForegroundColor Green
Write-Host "  -> npm version:     $npmVer" -ForegroundColor Green

# 2. Provision OpenCode CLI
if (-not $SkipOpenCode) {
    Write-Host ""
    Write-Host "[2/3] Provisioning OpenCode CLI (opencode)..." -ForegroundColor Cyan
    $opencodeCmd = Get-Command opencode -ErrorAction SilentlyContinue

    if ($opencodeCmd -and -not $ForceReinstall) {
        Write-Host "  -> OpenCode is already installed: $($opencodeCmd.Source)" -ForegroundColor Green
    }
    else {
        Write-Host "  -> Installing opencode-ai globally via npm..." -ForegroundColor Yellow
        npm install -g opencode-ai
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "npm install -g opencode-ai returned non-zero exit code: $LASTEXITCODE"
        }
    }

    # Ensure npm global bin is in User PATH
    $npmPrefix = (npm config get prefix).Trim()
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$npmPrefix*") {
        Write-Host "  -> Adding npm prefix ($npmPrefix) to User PATH..." -ForegroundColor Cyan
        [Environment]::SetEnvironmentVariable("Path", "$userPath;$npmPrefix", "User")
        $env:Path = "$npmPrefix;$env:Path"
    }

    $verifiedOpencode = Get-Command opencode -ErrorAction SilentlyContinue
    if ($verifiedOpencode) {
        Write-Host "  [OK] OpenCode CLI verified at: $($verifiedOpencode.Source)" -ForegroundColor Green
    }
    else {
        Write-Warning "OpenCode installed but not found in current process PATH. Please restart terminal."
    }
}

# 3. Provision Antigravity CLI (agy)
if (-not $SkipAgy) {
    Write-Host ""
    Write-Host "[3/3] Checking Antigravity CLI (agy)..." -ForegroundColor Cyan
    $agyDir = Join-Path $env:LOCALAPPDATA "agy\bin"
    if (-not (Test-Path $agyDir)) {
        New-Item -ItemType Directory -Path $agyDir -Force | Out-Null
        Write-Host "  -> Created directory: $agyDir" -ForegroundColor Gray
    }

    # Ensure $agyDir is in User PATH
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$agyDir*") {
        Write-Host "  -> Adding $agyDir to User PATH..." -ForegroundColor Cyan
        [Environment]::SetEnvironmentVariable("Path", "$userPath;$agyDir", "User")
        $env:Path = "$agyDir;$env:Path"
    }

    $agyExe = Join-Path $agyDir "agy.exe"
    $agyCmd = Join-Path $agyDir "agy.cmd"

    if (Test-Path $agyExe) {
        Write-Host "  [OK] Official agy binary found at: $agyExe" -ForegroundColor Green
    }
    elseif (Test-Path $agyCmd) {
        Write-Host "  [OK] agy wrapper script found at: $agyCmd" -ForegroundColor Green
    }
    else {
        # Check if agy exists elsewhere on the system
        $existingAgy = Get-Command agy -ErrorAction SilentlyContinue
        if ($existingAgy) {
            Write-Host "  -> Found existing agy at: $($existingAgy.Source)" -ForegroundColor Green
            try {
                Copy-Item -Path $existingAgy.Source -Destination $agyExe -Force
                Write-Host "  [OK] Linked agy binary to $agyExe" -ForegroundColor Green
            } catch {
                Write-Warning "Could not copy $($existingAgy.Source) to $agyExe: $_"
            }
        }
        else {
            Write-Host "  -> Official agy.exe not found in $agyDir." -ForegroundColor Yellow
            Write-Host "  -> Creating local dev/test fallback stub ($agyCmd)..." -ForegroundColor Gray
            $stubContent = @"
@echo off
echo ===================================================
echo [Antigravity CLI Dev/Test Stub]
echo Connected to workspace: %CD%
echo Run your edits or prompts here.
echo (In production campus image, official agy.exe replaces this stub)
echo ===================================================
cmd /k
"@
            Set-Content -Path $agyCmd -Value $stubContent -Encoding ASCII
            Write-Host "  [OK] Created local dev/test stub at: $agyCmd" -ForegroundColor Green
            Write-Host "       (For the live 13 Nov event, deploy the official Go binary to $agyExe)" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  AI CLI Provisioning Completed Successfully!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
