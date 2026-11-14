<#
.SYNOPSIS
    Sets up local low-privilege runner accounts and positive ACL sandbox folders for Rogue AI evaluation.
.DESCRIPTION
    Must be executed on the event host machine with Administrator privileges.
    - Creates local group 'BreachRunners'.
    - Creates 4 runner accounts: BreachRunner1..BreachRunner4.
    - Creates C:\BreachSandboxes\Slot1..Slot4.
    - Strips inherited ACLs; grants SYSTEM & Administrators FullControl.
    - Grants BreachRunnerN Modify only to SlotN.
    - Adds explicit Deny ACLs for BreachRunners group on C:\BreachSecrets, project DB, and attack reference solutions.
#>

[CmdletBinding()]
param(
    [string]$SandboxRoot = "C:\BreachSandboxes",
    [string]$SecretsDir = "C:\BreachSecrets",
    [string]$ProjectRoot = ""
)

if (-not $ProjectRoot) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$ErrorActionPreference = "Stop"

# 1. Require elevation
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "This script requires elevated Administrator privileges to manage local accounts and ACLs."
    Write-Warning "Re-run PowerShell as Administrator: Start-Process powershell -Verb RunAs"
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ROGUE AI EVALUATOR SANDBOX & RUNNER SETUP" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. Ensure BreachRunners local group exists
$groupName = "BreachRunners"
$group = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
if (-not $group) {
    Write-Host "[+] Creating local group '$groupName'..." -ForegroundColor Green
    $group = New-LocalGroup -Name $groupName -Description "Low-privilege runners for Rogue AI participant code evaluation"
} else {
    Write-Host "[*] Local group '$groupName' already exists." -ForegroundColor Yellow
}

# 3. Create C:\BreachSecrets if not present
if (-not (Test-Path $SecretsDir)) {
    New-Item -ItemType Directory -Path $SecretsDir -Force | Out-Null
}

$credentials = @{}
$runnerNames = @("BreachRunner1", "BreachRunner2", "BreachRunner3", "BreachRunner4")

foreach ($name in $runnerNames) {
    $user = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
    # Generate 24-character random password
    $bytes = New-Object byte[] 18
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $password = [Convert]::ToBase64String($bytes).Replace('+', 'X').Replace('/', 'Y') + "!9"
    $securePass = ConvertTo-SecureString $password -AsPlainText -Force

    if (-not $user) {
        Write-Host "[+] Creating user '$name'..." -ForegroundColor Green
        $user = New-LocalUser -Name $name -Password $securePass -FullName "Rogue AI Evaluation Slot Runner" -Description "Locked-down sandbox evaluation account" -PasswordNeverExpires -UserMayNotChangePassword
    } else {
        Write-Host "[*] Resetting password for existing user '$name'..." -ForegroundColor Yellow
        Set-LocalUser -Name $name -Password $securePass
    }

    # Add to BreachRunners group, remove from Users if present
    Add-LocalGroupMember -Group $groupName -Member $name -ErrorAction SilentlyContinue
    Remove-LocalGroupMember -Group "Users" -Member $name -ErrorAction SilentlyContinue

    $credentials[$name] = $password
}

# Save credentials into C:\BreachSecrets\runner_credentials.json
$credFile = Join-Path $SecretsDir "runner_credentials.json"
$credentials | ConvertTo-Json -Depth 2 | Set-Content -Path $credFile -Encoding UTF8
Write-Host "[+] Runner credentials saved to $credFile" -ForegroundColor Green

# 4. Create and lock down C:\BreachSandboxes
if (-not (Test-Path $SandboxRoot)) {
    New-Item -ItemType Directory -Path $SandboxRoot -Force | Out-Null
}

# Strip inheritance on SandboxRoot
$rootAcl = Get-Acl $SandboxRoot
$rootAcl.SetAccessRuleProtection($true, $false) # (isProtected=$true, preserveInheritance=$false)
# Clear all existing rules
$rootAcl.Access | ForEach-Object { $rootAcl.RemoveAccessRule($_) } | Out-Null

# Grant SYSTEM & Administrators FullControl
$adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule("SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$rootAcl.AddAccessRule($adminRule)
$rootAcl.AddAccessRule($systemRule)
Set-Acl -Path $SandboxRoot -AclObject $rootAcl
Write-Host "[+] $SandboxRoot inheritance stripped and restricted to SYSTEM and Administrators." -ForegroundColor Green

# Configure each slot
for ($i = 1; $i -le 4; $i++) {
    $slotPath = Join-Path $SandboxRoot "Slot$i"
    $runnerName = "BreachRunner$i"
    if (-not (Test-Path $slotPath)) {
        New-Item -ItemType Directory -Path $slotPath -Force | Out-Null
    }

    $slotAcl = Get-Acl $slotPath
    $slotAcl.SetAccessRuleProtection($true, $false)
    $slotAcl.Access | ForEach-Object { $slotAcl.RemoveAccessRule($_) } | Out-Null
    $slotAcl.AddAccessRule($adminRule)
    $slotAcl.AddAccessRule($systemRule)
    
    # Grant only the corresponding BreachRunner Modify/Read/Write
    $runnerRule = New-Object System.Security.AccessControl.FileSystemAccessRule($runnerName, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $slotAcl.AddAccessRule($runnerRule)
    Set-Acl -Path $slotPath -AclObject $slotAcl

    Write-Host "[+] Configured $slotPath: only $runnerName has Modify permissions." -ForegroundColor Green
}

# 5. Apply explicit Deny ACLs for BreachRunners on sensitive locations
$sensitivePaths = @(
    $SecretsDir,
    (Join-Path $ProjectRoot "data"),
    (Join-Path $ProjectRoot "attacks"),
    (Join-Path $ProjectRoot "engine"),
    (Join-Path $ProjectRoot "server")
)

$denyRule = New-Object System.Security.AccessControl.FileSystemAccessRule($groupName, "ReadAndExecute,Write,Delete", "ContainerInherit,ObjectInherit", "None", "Deny")

foreach ($spath in $sensitivePaths) {
    if (Test-Path $spath) {
        $sacl = Get-Acl $spath
        $sacl.AddAccessRule($denyRule)
        Set-Acl -Path $spath -AclObject $sacl
        Write-Host "[+] Deny ACL applied to $spath for '$groupName'." -ForegroundColor Green
    }
}

# 6. Configure Windows Firewall rules for sandbox containment
Write-Host "[+] Configuring Windows Firewall rules for runner sandbox..." -ForegroundColor Green
try {
    # Remove existing rules if any
    Remove-NetFirewallRule -DisplayName "RogueAI-Block-ControlPlane-5080" -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName "RogueAI-Block-Engine-8000" -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName "RogueAI-Allow-Beacon-9191" -ErrorAction SilentlyContinue

    # Allow outbound loopback beacon sink for Attack 7 (port 9191)
    New-NetFirewallRule -DisplayName "RogueAI-Allow-Beacon-9191" -Direction Outbound -RemotePort 9191 -Protocol TCP -Action Allow -Profile Any -Description "Allow Attack 7 data beacon telemetry sink" | Out-Null
    Write-Host "[+] Outbound firewall allow rule added for port 9191 (Attack 7 beacon detection sink)." -ForegroundColor Green

    # Block outbound connections to ControlPlane (5080) and Engine (8000)
    New-NetFirewallRule -DisplayName "RogueAI-Block-ControlPlane-5080" -Direction Outbound -RemotePort 5080 -Protocol TCP -Action Block -Profile Any -Description "Block sandbox access to Rogue AI ControlPlane" | Out-Null
    New-NetFirewallRule -DisplayName "RogueAI-Block-Engine-8000" -Direction Outbound -RemotePort 8000 -Protocol TCP -Action Block -Profile Any -Description "Block sandbox access to Rogue AI Engine" | Out-Null
    Write-Host "[+] Outbound firewall block rules added for ports 5080 and 8000." -ForegroundColor Green
} catch {
    Write-Warning "Could not update firewall rules: $_"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " EVALUATOR SANDBOX SETUP COMPLETE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
