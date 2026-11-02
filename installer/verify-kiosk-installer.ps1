<#
.SYNOPSIS
    Automated Leak Scanner for RogueAI-Kiosk-Setup.exe
.DESCRIPTION
    Extracts the kiosk installer into an isolated directory and performs deep static
    analysis to guarantee zero solutions, attacks, engine code, server binaries,
    admin windows, or master keys are present on participant machines.
#>

param(
    [string]$InstallerPath = "dist\RogueAI-Kiosk-Setup.exe"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ROGUE AI: Kiosk Installer Leak & Security Verification" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $InstallerPath)) {
    Write-Error "Installer not found at path: $InstallerPath"
    exit 1
}

$extractDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "RogueAI_LeakScan_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

try {
    Write-Host "[1/4] Extracting installer to isolated sandbox: $extractDir" -ForegroundColor Yellow
    $proc = Start-Process -FilePath (Resolve-Path $InstallerPath) -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/DIR=`"$extractDir`"" -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        Write-Error "Installer extraction failed with exit code: $($proc.ExitCode)"
        exit 1
    }

    $allFiles = Get-ChildItem -Path $extractDir -Recurse
    Write-Host "      Extracted $($allFiles.Count) files/directories." -ForegroundColor Gray

    $forbiddenPathPatterns = @(
        "*solution*",
        "*attacks*",
        "*engine*",
        "*ControlPlane*",
        "*AdminWindow*"
    )

    $leaksFound = @()

    Write-Host "[2/4] Scanning file system paths for leaked components..." -ForegroundColor Yellow
    foreach ($file in $allFiles) {
        $rel = $file.FullName.Substring($extractDir.Length)
        foreach ($pattern in $forbiddenPathPatterns) {
            if ($rel -like $pattern -and -not ($rel -like "*unins000*")) {
                $leaksFound += "FORBIDDEN PATH: $rel matches pattern '$pattern'"
            }
        }
    }

    Write-Host "[3/4] Scanning text and config payloads for secrets..." -ForegroundColor Yellow
    $secretPatterns = @(
        "CHANGE-ME-BEFORE-EVENT",
        "Breach:AdminKey",
        "/admin/attacks",
        "/admin/scoreboard",
        "--host",
        "master_key",
        "master.key",
        "Ed25519"
    )

    $textFiles = $allFiles | Where-Object { -not $_.PSIsContainer -and ($_.Extension -match "\.(json|js|css|html|txt|xml|config)$") }
    foreach ($tf in $textFiles) {
        $content = Get-Content $tf.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -ne $content) {
            foreach ($sec in $secretPatterns) {
                if ($content.Contains($sec)) {
                    $rel = $tf.FullName.Substring($extractDir.Length)
                    $leaksFound += "FORBIDDEN SECRET in ${rel} - contains '$sec'"
                }
            }
        }
    }

    Write-Host "[4/4] Disassembling LabKiosk.dll for compiled admin entry points..." -ForegroundColor Yellow
    $dllPath = Join-Path $extractDir "LabKiosk.dll"
    if (Test-Path $dllPath) {
        $bytes = [System.IO.File]::ReadAllBytes($dllPath)
        
        # 1. Binary symbol / string scanning
        $rawChars = [System.Text.Encoding]::ASCII.GetString($bytes)
        if ($rawChars -match "AdminWindow") {
            $leaksFound += "COMPILED LEAK: Symbol 'AdminWindow' was found in LabKiosk.dll binary!"
        }
        if ($rawChars -match "OnOpenAdmin") {
            $leaksFound += "COMPILED LEAK: Symbol 'OnOpenAdmin' was found in LabKiosk.dll binary!"
        }

        # 2. Managed metadata reflection
        try {
            $asm = [System.Reflection.Assembly]::Load($bytes)
            $typeNames = @()
            try {
                $typeNames = $asm.GetTypes() | Select-Object -ExpandProperty FullName
            } catch [System.Reflection.ReflectionTypeLoadException] {
                $typeNames = $_.Exception.Types | Where-Object { $null -ne $_ } | Select-Object -ExpandProperty FullName
            }
            if ($typeNames -contains "LabKiosk.AdminWindow") {
                $leaksFound += "COMPILED LEAK: LabKiosk.AdminWindow type exists in LabKiosk.dll!"
            }
        } catch {
            Write-Host "      (Managed reflection skipped: native/target framework boundary)" -ForegroundColor Gray
        }
    } else {
        $leaksFound += "MISSING ARTIFACT: LabKiosk.dll not found in extracted output!"
    }

    Write-Host "==========================================================" -ForegroundColor Cyan
    if ($leaksFound.Count -eq 0) {
        Write-Host " SUCCESS: Zero leaks detected! Kiosk installer is 100% clean." -ForegroundColor Green
        Write-Host " - Zero reference solutions or attack packs" -ForegroundColor Green
        Write-Host " - Zero engine or Control Plane binaries" -ForegroundColor Green
        Write-Host " - Zero AdminWindow classes or admin entry points" -ForegroundColor Green
        Write-Host " - Zero hardcoded master keys in configs" -ForegroundColor Green
        Write-Host "==========================================================" -ForegroundColor Cyan
        exit 0
    } else {
        Write-Host " FAILED: Security leaks detected in Kiosk installer!" -ForegroundColor Red
        foreach ($leak in $leaksFound) {
            Write-Host " [!] $leak" -ForegroundColor Red
        }
        Write-Host "==========================================================" -ForegroundColor Cyan
        exit 1
    }
}
finally {
    if (Test-Path $extractDir) {
        Remove-Item -Path $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
