# clean-old.ps1
# Purges old Rogue AI / Breach Protocol installations, desktop shortcuts, and orphaned registry keys

[CmdletBinding()]
param(
    [switch]$Silent
)

$ErrorActionPreference = "Continue"

# Check elevation (attempt elevation if interactive, or continue gracefully)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if (-not $Silent -and [Environment]::UserInteractive) {
        try {
            Write-Host "[*] Requesting elevated Administrator privileges..." -ForegroundColor Yellow
            $p = Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -PassThru -ErrorAction Stop
            if ($p) { exit 0 }
        } catch {
            Write-Warning "Elevation could not be launched automatically. Continuing with user-level cleanup..."
        }
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  ROGUE AI: PURGING OLD INSTALLATIONS & SHORTCUTS" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Terminate running processes
Write-Host "[1/5] Terminating any active background processes..." -ForegroundColor Yellow
@("LabKiosk", "ControlPlane", "node", "python", "uvicorn", "opencode", "agy") | ForEach-Object {
    & taskkill.exe /F /T /IM "$_.exe" 2>$null | Out-Null
    Get-Process -Name $_ -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Write-Host "  [OK] Process tree cleared." -ForegroundColor Green

# 2. Collect all Desktop & Start Menu paths across all users & profiles
Write-Host "`n[2/5] Purging Desktop shortcuts and Start Menu folders..." -ForegroundColor Yellow
$targetDirs = @(
    "C:\Users\Public\Desktop",
    (Join-Path $env:USERPROFILE "Desktop"),
    "C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs")
)

# Detect OneDrive desktop paths
if ($env:OneDrive) { $targetDirs += (Join-Path $env:OneDrive "Desktop") }
if ($env:OneDriveConsumer) { $targetDirs += (Join-Path $env:OneDriveConsumer "Desktop") }
if ($env:OneDriveCommercial) { $targetDirs += (Join-Path $env:OneDriveCommercial "Desktop") }
if ($env:USERNAME) { $targetDirs += "C:\Users\$($env:USERNAME)\OneDrive\Desktop" }

# Scan all user profiles under C:\Users
if (Test-Path "C:\Users") {
    Get-ChildItem -Path "C:\Users" -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $targetDirs += (Join-Path $_.FullName "Desktop")
        $targetDirs += (Join-Path $_.FullName "OneDrive\Desktop")
        $targetDirs += (Join-Path $_.FullName "AppData\Roaming\Microsoft\Windows\Start Menu\Programs")
    }
}

# Add Shell Folders from Registry
try {
    $uDesk = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" -ErrorAction SilentlyContinue).Desktop
    if ($uDesk) { $targetDirs += [System.Environment]::ExpandEnvironmentVariables($uDesk) }
} catch { }

try {
    $cDesk = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" -ErrorAction SilentlyContinue)."Common Desktop"
    if ($cDesk) { $targetDirs += $cDesk }
} catch { }

try {
    $cProg = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" -ErrorAction SilentlyContinue)."Common Programs"
    if ($cProg) { $targetDirs += $cProg }
} catch { }

$cleanDirs = $targetDirs | Where-Object {
    if (-not $_) { return $false }
    try { Test-Path -LiteralPath $_ -ErrorAction SilentlyContinue } catch { $false }
} | Select-Object -Unique

foreach ($d in $cleanDirs) {
    try {
        # Delete shortcut files (.lnk, .url) matching Breach or Rogue
        Get-ChildItem -Path $d -Filter "*.lnk" -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Breach|Rogue|LabKiosk' } | ForEach-Object {
            & takeown.exe /f "$($_.FullName)" /d y 2>$null | Out-Null
            & icacls.exe "$($_.FullName)" /grant:r "$($env:USERNAME):(F)" 2>$null | Out-Null
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
            Write-Host "  [OK] Removed shortcut: $($_.FullName)" -ForegroundColor Green
        }
        Get-ChildItem -Path $d -Filter "*.url" -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Breach|Rogue' } | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
            Write-Host "  [OK] Removed url shortcut: $($_.FullName)" -ForegroundColor Green
        }
        # Delete folders matching Breach or Rogue (e.g. in Start Menu Programs)
        Get-ChildItem -Path $d -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Breach|Rogue' } | ForEach-Object {
            & takeown.exe /f "$($_.FullName)" /r /d y 2>$null | Out-Null
            & icacls.exe "$($_.FullName)" /reset /t /c /q 2>$null | Out-Null
            & icacls.exe "$($_.FullName)" /grant:r "Administrators:(OI)(CI)F" "$($env:USERNAME):(OI)(CI)F" /t /c /q 2>$null | Out-Null
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $_.FullName) {
                & cmd.exe /c "rd /s /q `"$($_.FullName)`"" 2>$null | Out-Null
            }
            Write-Host "  [OK] Removed directory: $($_.FullName)" -ForegroundColor Green
        }
    } catch { }
}
Write-Host "  [OK] Shortcuts cleaned." -ForegroundColor Green

# 3. Purge Windows Uninstall Registry entries
Write-Host "`n[3/5] Purging Windows Uninstall Registry entries..." -ForegroundColor Yellow
$roots = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
)
foreach ($r in $roots) {
    if (Test-Path $r) {
        try {
            Get-ChildItem -Path $r -ErrorAction SilentlyContinue | ForEach-Object {
                $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
                $name = if ($p) { $p.DisplayName } else { "" }
                $loc = if ($p) { $p.InstallLocation } else { "" }
                if ($_.PSChildName -match '6F2D1C9A|9F76E9B4|5E2338A1' -or $name -match 'Rogue AI|Breach Protocol|LabKiosk' -or $loc -match 'RogueAI|Breach') {
                    Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Host "  [OK] Deleted registry key: $($_.PSChildName) ($name)" -ForegroundColor Green
                }
            }
        } catch { }
    }
}
@("HKCU:\Software\RogueAI", "HKLM:\SOFTWARE\RogueAI") | ForEach-Object {
    if (Test-Path $_) {
        Remove-Item -Path $_ -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  [OK] Deleted app registry key: $_" -ForegroundColor Green
    }
}
Write-Host "  [OK] Registry cleaned." -ForegroundColor Green

# 4. Purge installation folders
Write-Host "`n[4/5] Purging old installation folders..." -ForegroundColor Yellow
$appDirs = @(
    "C:\Program Files\RogueAI",
    "C:\Program Files (x86)\RogueAI",
    "C:\Program Files\Rogue AI",
    "C:\Program Files (x86)\Rogue AI",
    "C:\Program Files\Breach Protocol",
    (Join-Path $env:LOCALAPPDATA "Programs\RogueAI"),
    (Join-Path $env:LOCALAPPDATA "Programs\Rogue AI"),
    (Join-Path $env:LOCALAPPDATA "Programs\Breach Protocol")
)
foreach ($ad in $appDirs) {
    if (Test-Path $ad) {
        try {
            & takeown.exe /f "$ad" /r /d y 2>$null | Out-Null
            & icacls.exe "$ad" /reset /t /c /q 2>$null | Out-Null
            & icacls.exe "$ad" /grant:r "Administrators:(OI)(CI)F" "$($env:USERNAME):(OI)(CI)F" /t /c /q 2>$null | Out-Null
            Remove-Item -Path $ad -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $ad) {
                & cmd.exe /c "rd /s /q `"$ad`"" 2>$null | Out-Null
            }
            Write-Host "  [OK] Removed: $ad" -ForegroundColor Green
        } catch { }
    }
}
Write-Host "  [OK] Folders cleaned." -ForegroundColor Green

# 5. Refresh Windows Explorer and Icon Cache
Write-Host "`n[5/5] Refreshing Windows Explorer and desktop icon cache..." -ForegroundColor Yellow
& ie4uinit.exe -show 2>$null | Out-Null
try {
    $refreshCode = @'
    using System;
    using System.Runtime.InteropServices;
    public class WinExplorerRefresh {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
'@
    Add-Type -TypeDefinition $refreshCode -ErrorAction SilentlyContinue
    [WinExplorerRefresh]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    Write-Host "  [OK] Desktop icon cache refreshed." -ForegroundColor Green
} catch {
    Write-Host "  [OK] Explorer refresh triggered." -ForegroundColor Green
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  CLEANUP COMPLETE: ALL OLD OCCURRENCES & SHORTCUTS GONE" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host ""
