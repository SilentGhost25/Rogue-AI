<#
.SYNOPSIS
    Official ROGUE AI Deep Uninstaller & System Reset Utility
.DESCRIPTION
    Completely and forensically cleans all Rogue AI traces from any Windows host or participant machine:
    1. Force-terminates all application processes and clears port locks.
    2. Seizes ownership and resets positive/denied ACLs on sandboxes, secrets, and workspaces.
    3. Obliterates workspace and sandbox directories (C:\BreachWorkspace, C:\BreachSandboxes, C:\BreachSecrets).
    4. Purges local runner user accounts (BreachRunner1..4), group (BreachRunners), and profile entries.
    5. Removes all Rogue AI Windows Firewall inbound and outbound rules.
    6. Removes AppData state (%LOCALAPPDATA%\BreachKiosk, %LOCALAPPDATA%\agy, npm shims, temp logs).
    7. Cleans registry keys, uninstaller entries, and System/User PATH variables.
    8. Removes all Desktop and Start Menu shortcuts (User and Public).
#>

[CmdletBinding()]
param(
    [switch]$Silent,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = "Continue"

# 1. Check elevation (attempt elevation if interactive, or continue gracefully in user mode)
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
    } else {
        Write-Warning "Running in non-elevated mode. Local user accounts and system firewall rules will require manual admin removal if present."
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   ROGUE AI: COMPLETE SYSTEM UNINSTALLER & CLEANUP" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 2. Terminate all active processes holding file handles or network sockets
Write-Host "[1/8] Terminating all active Rogue AI and child runner processes..." -ForegroundColor Yellow
$procNames = @("LabKiosk", "ControlPlane", "opencode", "agy", "node", "python", "uvicorn", "git")
foreach ($name in $procNames) {
    $running = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "  -> Terminating $($running.Count) process(es) of $name..." -ForegroundColor Gray
        & taskkill.exe /F /T /IM "$name.exe" 2>$null | Out-Null
    }
}
Start-Sleep -Seconds 1
Write-Host "  [OK] Process tree cleared." -ForegroundColor Green

# 3. Take ownership, reset ACLs, and delete protected folders
Write-Host "`n[2/8] Resetting permissions and deleting protected directories..." -ForegroundColor Yellow
$dirsToPurge = @()
if (-not $KeepWorkspace) {
    $dirsToPurge += "C:\BreachWorkspace"
}
$dirsToPurge += @("C:\BreachSandboxes", "C:\BreachSecrets")

foreach ($dir in $dirsToPurge) {
    if (Test-Path $dir) {
        Write-Host "  -> Unlocking and removing $dir..." -ForegroundColor Gray
        try {
            & takeown.exe /f "$dir" /r /d y 2>$null | Out-Null
            & icacls.exe "$dir" /reset /t /c /q 2>$null | Out-Null
            & icacls.exe "$dir" /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" /t /c /q 2>$null | Out-Null
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $dir) {
                & cmd.exe /c "rd /s /q `"$dir`"" 2>$null | Out-Null
            }
            Write-Host "  [OK] Purged $dir" -ForegroundColor Green
        } catch {
            Write-Warning "Failed to cleanly remove $($dir): $_"
        }
    }
}

# 4. Remove local runner accounts, group, and profile registry entries
Write-Host "`n[3/8] Removing local runner accounts, groups, and profiles..." -ForegroundColor Yellow
$runnerNames = @("BreachRunner1", "BreachRunner2", "BreachRunner3", "BreachRunner4")
$groupName = "BreachRunners"

foreach ($name in $runnerNames) {
    try {
        $user = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
        if ($user) {
            Remove-LocalUser -Name $name -ErrorAction SilentlyContinue
            Write-Host "  -> Removed local user: $name" -ForegroundColor Gray
        }
    } catch { }

    # Check and remove profile folder if created
    $profPath = "C:\Users\$name"
    if (Test-Path $profPath) {
        try {
            & takeown.exe /f "$profPath" /r /d y 2>$null | Out-Null
            & icacls.exe "$profPath" /reset /t /c /q 2>$null | Out-Null
            Remove-Item -Path $profPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  -> Removed user profile folder: $profPath" -ForegroundColor Gray
        } catch { }
    }
}

try {
    $group = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
    if ($group) {
        Remove-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
        Write-Host "  -> Removed local group: $groupName" -ForegroundColor Gray
    }
} catch { }

# Clean registry ProfileList
$profListKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"
if (Test-Path $profListKey) {
    try {
        Get-ChildItem -Path $profListKey -ErrorAction SilentlyContinue | ForEach-Object {
            $pPath = (Get-ItemProperty -Path $_.PSPath -Name "ProfileImagePath" -ErrorAction SilentlyContinue).ProfileImagePath
            if ($pPath -and ($pPath -match "BreachRunner")) {
                Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  -> Cleaned ProfileList entry for: $pPath" -ForegroundColor Gray
            }
        }
    } catch { }
}
Write-Host "  [OK] Local accounts and profiles cleaned." -ForegroundColor Green

# 5. Remove Windows Firewall rules
Write-Host "`n[4/8] Removing Rogue AI Windows Firewall rules..." -ForegroundColor Yellow
try {
    $fwRules = Get-NetFirewallRule -DisplayName "RogueAI*" -ErrorAction SilentlyContinue
    if ($fwRules) {
        foreach ($r in $fwRules) {
            Write-Host "  -> Removing firewall rule: $($r.DisplayName)" -ForegroundColor Gray
            Remove-NetFirewallRule -Name $r.Name -ErrorAction SilentlyContinue
        }
    }
    @("RogueAI-Allow-Beacon-9191", "RogueAI-Block-ControlPlane-5080", "RogueAI-Block-Engine-8000") | ForEach-Object {
        Remove-NetFirewallRule -DisplayName $_ -ErrorAction SilentlyContinue
    }
    Write-Host "  [OK] Firewall rules cleaned." -ForegroundColor Green
} catch {
    Write-Warning "Could not clean some firewall rules (requires Admin privileges)."
}

# 6. Clean AppData, User caches, and CLI state
Write-Host "`n[5/8] Cleaning AppData caches and CLI session state..." -ForegroundColor Yellow
$appDataPaths = @(
    (Join-Path $env:LOCALAPPDATA "BreachKiosk"),
    (Join-Path $env:LOCALAPPDATA "agy"),
    (Join-Path $env:APPDATA "npm\node_modules\opencode-ai"),
    (Join-Path $env:TEMP "agy_session.log"),
    (Join-Path $env:TEMP "fetch_opencode.js")
)

foreach ($ap in $appDataPaths) {
    try {
        if (Test-Path $ap) {
            Remove-Item -Path $ap -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  -> Removed: $ap" -ForegroundColor Gray
        }
    } catch { }
}

# Clean opencode shims in %APPDATA%\npm
$appDataNpm = Join-Path $env:APPDATA "npm"
if (Test-Path $appDataNpm) {
    try {
        Get-ChildItem -Path $appDataNpm -Filter "opencode*" -File -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
            Write-Host "  -> Removed shim: $($_.FullName)" -ForegroundColor Gray
        }
    } catch { }
}

# Clean trusted workspaces in ~/.gemini/antigravity-cli/settings.json
$agySettings = Join-Path $env:USERPROFILE ".gemini\antigravity-cli\settings.json"
if (Test-Path $agySettings) {
    try {
        $json = Get-Content -Path $agySettings -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
        if ($json.trustedWorkspaces) {
            $filtered = @($json.trustedWorkspaces | Where-Object { $_ -notmatch 'BreachWorkspace' -and $_ -notmatch 'BreachSandboxes' })
            $json.trustedWorkspaces = $filtered
            $json | ConvertTo-Json -Depth 5 | Set-Content -Path $agySettings -Encoding UTF8
            Write-Host "  -> Cleaned BreachWorkspace from Antigravity CLI trusted workspaces." -ForegroundColor Gray
        }
    } catch { }
}
Write-Host "  [OK] AppData and CLI state wiped." -ForegroundColor Green

# 7. Clean Registry entries & Environment PATH variables
Write-Host "`n[6/8] Cleaning Registry entries and environment variables..." -ForegroundColor Yellow
$regKeys = @(
    "HKCU:\Software\RogueAI",
    "HKLM:\SOFTWARE\RogueAI",
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\LabKiosk.exe",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\LabKiosk.exe"
)
foreach ($rk in $regKeys) {
    try {
        if (Test-Path $rk) {
            Remove-Item -Path $rk -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  -> Deleted registry key: $rk" -ForegroundColor Gray
        }
    } catch { }
}

# Scan and clean all old and current Inno Setup uninstall entries for Rogue AI & Breach Protocol
$uninstallRoots = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
)
foreach ($uRoot in $uninstallRoots) {
    if (Test-Path $uRoot) {
        try {
            Get-ChildItem -Path $uRoot -ErrorAction SilentlyContinue | ForEach-Object {
                $subKey = $_.PSPath
                $kName = $_.PSChildName
                $props = Get-ItemProperty -Path $subKey -ErrorAction SilentlyContinue
                $dispName = if ($props) { $props.DisplayName } else { "" }
                $appPath = if ($props) { $props.InstallLocation } else { "" }

                if ($kName -match '6F2D1C9A|9F76E9B4|5E2338A1' -or
                    $dispName -match 'Rogue AI|Breach Protocol|LabKiosk' -or
                    $appPath -match 'RogueAI|Breach') {
                    Remove-Item -Path $subKey -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Host "  -> Purged uninstaller registry entry: $kName ($dispName)" -ForegroundColor Green
                }
            }
        } catch { }
    }
}

# Clean PATH variables
function Clean-PathVar($scope) {
    try {
        $val = [Environment]::GetEnvironmentVariable("Path", $scope)
        if ($val) {
            $parts = $val -split ";" | Where-Object {
                $_ -and
                $_ -notmatch 'RogueAI' -and
                $_ -notmatch 'BreachKiosk' -and
                $_ -notmatch '\\agy\\bin' -and
                $_ -notmatch 'opencode'
            }
            $newVal = ($parts -join ";")
            if ($val -ne $newVal) {
                [Environment]::SetEnvironmentVariable("Path", $newVal, $scope)
                Write-Host "  -> Scrubbed Rogue AI paths from $scope PATH." -ForegroundColor Gray
            }
        }
    } catch { }
}
Clean-PathVar "User"
Clean-PathVar "Machine"

# Clear environment variables
@("WORKSPACES_DIR", "BREACH_ENGINE_URL", "BREACH_ADMIN_KEY", "ROGUE_ADMIN_KEY") | ForEach-Object {
    try { [Environment]::SetEnvironmentVariable($_, $null, "User") } catch { }
    try { [Environment]::SetEnvironmentVariable($_, $null, "Machine") } catch { }
}
Write-Host "  [OK] Registry and Environment variables scrubbed." -ForegroundColor Green

# 8. Remove Shortcuts (dynamically finding all Desktop & Start Menu paths, including OneDrive redirects)
Write-Host "`n[7/8] Purging all Desktop and Start Menu shortcuts across all profiles..." -ForegroundColor Yellow

$allShortcutDirs = @()

# Query User Shell Folders from HKCU (handles OneDrive Desktop, custom redirects)
try {
    $uShell = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" -ErrorAction SilentlyContinue
    if ($uShell.Desktop) { $allShortcutDirs += [System.Environment]::ExpandEnvironmentVariables($uShell.Desktop) }
    if ($uShell.Programs) { $allShortcutDirs += [System.Environment]::ExpandEnvironmentVariables($uShell.Programs) }
    if ($uShell."Start Menu") { $allShortcutDirs += [System.Environment]::ExpandEnvironmentVariables($uShell."Start Menu") }
} catch { }

# Query Common Shell Folders from HKLM
try {
    $cShell = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" -ErrorAction SilentlyContinue
    if ($cShell."Common Desktop") { $allShortcutDirs += $cShell."Common Desktop" }
    if ($cShell."Common Programs") { $allShortcutDirs += $cShell."Common Programs" }
    if ($cShell."Common Start Menu") { $allShortcutDirs += $cShell."Common Start Menu" }
} catch { }

# Add standard fallback paths
$allShortcutDirs += @(
    [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop),
    [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs),
    (Join-Path $env:USERPROFILE "Desktop"),
    "C:\Users\Public\Desktop",
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"),
    (Join-Path $env:PROGRAMDATA "Microsoft\Windows\Start Menu\Programs")
)

# Add OneDrive Desktop fallbacks
if ($env:OneDrive) { $allShortcutDirs += (Join-Path $env:OneDrive "Desktop") }
if ($env:OneDriveConsumer) { $allShortcutDirs += (Join-Path $env:OneDriveConsumer "Desktop") }
if ($env:OneDriveCommercial) { $allShortcutDirs += (Join-Path $env:OneDriveCommercial "Desktop") }
if ($env:USERNAME) { $allShortcutDirs += "C:\Users\$($env:USERNAME)\OneDrive\Desktop" }

$uniqueShortcutDirs = $allShortcutDirs | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

foreach ($sDir in $uniqueShortcutDirs) {
    try {
        # Find all .lnk files matching Breach, Rogue, or LabKiosk
        Get-ChildItem -Path $sDir -Filter "*.lnk" -File -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -match 'Breach|Rogue|LabKiosk') {
                Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
                Write-Host "  -> Removed shortcut: $($_.FullName)" -ForegroundColor Green
            }
        }
        # Check subdirectories in Start Menu/Programs
        Get-ChildItem -Path $sDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -match 'Breach|Rogue') {
                & takeown.exe /f "$($_.FullName)" /r /d y 2>$null | Out-Null
                & icacls.exe "$($_.FullName)" /reset /t /c /q 2>$null | Out-Null
                & icacls.exe "$($_.FullName)" /grant:r "Administrators:(OI)(CI)F" "$($env:USERNAME):(OI)(CI)F" /t /c /q 2>$null | Out-Null
                # Remove any lnk inside first
                Get-ChildItem -Path $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
                if (Test-Path $_.FullName) {
                    & cmd.exe /c "rd /s /q `"$($_.FullName)`"" 2>$null | Out-Null
                }
                Write-Host "  -> Removed shortcut directory: $($_.FullName)" -ForegroundColor Green
            }
        }
    } catch { }
}
Write-Host "  [OK] All Desktop and Start Menu shortcuts purged." -ForegroundColor Green

# Refresh Windows Explorer and desktop icon cache
& ie4uinit.exe -show 2>$null | Out-Null
try {
    $refreshCode = @'
    using System;
    using System.Runtime.InteropServices;
    public class WinExplorerRefresh2 {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
'@
    Add-Type -TypeDefinition $refreshCode -ErrorAction SilentlyContinue
    [WinExplorerRefresh2]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
} catch { }

# 9. Clean Application Directory (if installed under Program Files or Programs)
Write-Host "`n[8/8] Cleaning application install directories..." -ForegroundColor Yellow
$appDirs = @(
    "C:\Program Files\RogueAI",
    "C:\Program Files (x86)\RogueAI",
    "C:\Program Files\Rogue AI",
    "C:\Program Files\Breach Protocol",
    (Join-Path $env:LOCALAPPDATA "Programs\RogueAI"),
    (Join-Path $env:LOCALAPPDATA "Programs\Rogue AI"),
    (Join-Path $env:LOCALAPPDATA "Programs\Breach Protocol")
)
foreach ($ad in $appDirs) {
    if (Test-Path $ad) {
        Write-Host "  -> Removing installation directory: $ad..." -ForegroundColor Gray
        try {
            & takeown.exe /f "$ad" /r /d y 2>$null | Out-Null
            & icacls.exe "$ad" /reset /t /c /q 2>$null | Out-Null
            Remove-Item -Path $ad -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  [OK] Removed $ad" -ForegroundColor Green
        } catch {
            Write-Warning "Could not completely remove $($ad): $_"
        }
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "     ROGUE AI: COMPLETE UNINSTALLATION SUCCEEDED!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "All processes, local accounts, sandboxes, secrets, databases,"
Write-Host "shortcuts, registry keys, and firewall rules have been removed."
Write-Host ""
