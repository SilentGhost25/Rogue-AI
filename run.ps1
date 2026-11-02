param(
    [switch]$HostMode,
    [switch]$Both
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path "$ScriptDir\rogue-ai-base\rogue-ai") {
    $RogueRoot = "$ScriptDir\rogue-ai-base\rogue-ai"
} else {
    $RogueRoot = $ScriptDir
}

if (Test-Path "$env:LOCALAPPDATA\dotnet") {
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
    $env:Path = "$env:LOCALAPPDATA\dotnet;$env:Path"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  ROGUE AI — THE BREACH PROTOCOL (All-In-One Launcher)   " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Project Root: $RogueRoot" -ForegroundColor Gray

# 1. Initialize Breach Workspace
$WorkspaceDir = "C:\BreachWorkspace"
try {
    if (!(Test-Path $WorkspaceDir)) {
        New-Item -ItemType Directory -Path $WorkspaceDir -Force -ErrorAction Stop | Out-Null
    }
    if (!(Test-Path "$WorkspaceDir\server.js")) {
        Write-Host "[*] Initializing Breach Workspace at $WorkspaceDir..." -ForegroundColor Cyan
        Copy-Item -Path "$RogueRoot\site\*" -Destination $WorkspaceDir -Recurse -Force -ErrorAction Stop
    }
} catch {
    # If C:\ root is restricted, seamlessly fall back to local project workspace
    $WorkspaceDir = "$RogueRoot\workspace"
    if (!(Test-Path $WorkspaceDir)) {
        New-Item -ItemType Directory -Path $WorkspaceDir -Force | Out-Null
    }
    if (!(Test-Path "$WorkspaceDir\server.js")) {
        Write-Host "[*] Initializing Breach Workspace at $WorkspaceDir..." -ForegroundColor Cyan
        Copy-Item -Path "$RogueRoot\site\*" -Destination $WorkspaceDir -Recurse -Force
    }
    Write-Host "[!] Note: Using local fallback workspace: $WorkspaceDir" -ForegroundColor Yellow
}

# 2. Check and Start Target Website (Port 3000)
$siteProc = $null
$isSiteRunning = $false
try {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:3000" -TimeoutSec 1 -UseBasicParsing -ErrorAction SilentlyContinue
    if ($resp.StatusCode -eq 200) { $isSiteRunning = $true }
} catch { }

if ($isSiteRunning) {
    Write-Host "[+] Target Website already online on http://127.0.0.1:3000" -ForegroundColor Green
} else {
    Write-Host "[*] Starting Target Website (Node.js --watch) on port 3000..." -ForegroundColor Cyan
    $siteProc = Start-Process -FilePath "node" -ArgumentList "--watch server.js" -WorkingDirectory $WorkspaceDir -PassThru -WindowStyle Hidden
}

# 3. Check and Start Python Engine (Port 8000)
$engineProc = $null
$isEngineRunning = $false
try {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:8000/health" -TimeoutSec 1 -UseBasicParsing -ErrorAction SilentlyContinue
    if ($resp.StatusCode -eq 200) { $isEngineRunning = $true }
} catch { }

if ($isEngineRunning) {
    Write-Host "[+] Breach Engine already online on http://127.0.0.1:8000" -ForegroundColor Green
} else {
    Write-Host "[*] Starting Breach Engine (FastAPI) on port 8000..." -ForegroundColor Cyan
    $engineProc = Start-Process -FilePath "python" -ArgumentList "-m breach_engine.api" -WorkingDirectory "$RogueRoot\engine" -PassThru -WindowStyle Hidden
}

# 4. Check and Start Control Plane (Port 5080)
$serverProc = $null
$isServerRunning = $false
try {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:5080/api/state" -TimeoutSec 1 -UseBasicParsing -ErrorAction SilentlyContinue
    if ($resp.StatusCode -eq 200) { $isServerRunning = $true }
} catch { }

if ($isServerRunning) {
    Write-Host "[+] Control Plane already online on http://127.0.0.1:5080" -ForegroundColor Green
} else {
    Write-Host "[*] Starting Control Plane (ASP.NET Core) on port 5080..." -ForegroundColor Cyan
    $controlPlaneExe = "$RogueRoot\server\ControlPlane\bin\Debug\net9.0\ControlPlane.exe"
    if (!(Test-Path $controlPlaneExe)) {
        Write-Host "[*] Building Control Plane..." -ForegroundColor Cyan
        dotnet build "$RogueRoot\server\ControlPlane\ControlPlane.csproj" -c Debug | Out-Null
    }
    $serverProc = Start-Process -FilePath $controlPlaneExe -ArgumentList "--urls http://127.0.0.1:5080" -WorkingDirectory "$RogueRoot\server\ControlPlane" -PassThru -WindowStyle Hidden
}

# Wait for services to be ready
Write-Host "[*] Verifying all background services..." -ForegroundColor Cyan
$ready = $false
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Milliseconds 600
    try {
        $c = (Invoke-WebRequest "http://127.0.0.1:5080/api/state" -TimeoutSec 1 -UseBasicParsing -ErrorAction SilentlyContinue).StatusCode
        $e = (Invoke-WebRequest "http://127.0.0.1:8000/health" -TimeoutSec 1 -UseBasicParsing -ErrorAction SilentlyContinue).StatusCode
        if ($c -eq 200 -and $e -eq 200) { $ready = $true; break }
    } catch { }
}

if ($ready) {
    Write-Host "[+] All systems online and operational!" -ForegroundColor Green
} else {
    Write-Host "[!] Warning: Services took longer than expected to report ready, proceeding..." -ForegroundColor Yellow
}

# 5. Launch Kiosk App
$kioskExe = "$RogueRoot\kiosk\LabKiosk\bin\Debug\net9.0-windows\LabKiosk.exe"
if (!(Test-Path $kioskExe)) {
    Write-Host "[*] Building LabKiosk application..." -ForegroundColor Cyan
    dotnet build "$RogueRoot\kiosk\LabKiosk\LabKiosk.csproj" -c Debug | Out-Null
}

$appArgs = ""
if ($HostMode) {
    Write-Host "[*] Launching ROGUE AI in Organizer Host Mode (--host)..." -ForegroundColor Magenta
    $appArgs = "--host"
} elseif ($Both) {
    Write-Host "[*] Launching BOTH Participant Kiosk and Organizer Command Center..." -ForegroundColor Magenta
    Start-Process -FilePath $kioskExe -ArgumentList "--host"
    $appArgs = ""
} else {
    Write-Host "[*] Launching ROGUE AI Defense Studio (Participant Kiosk)..." -ForegroundColor Green
    Write-Host "    (Hint: Click 'Admin Console' in top bar or pass -HostMode for Organizer view)" -ForegroundColor DarkGray
}

try {
    $kioskProc = Start-Process -FilePath $kioskExe -ArgumentList $appArgs -PassThru
    Write-Host "[+] ROGUE AI is active (PID: $($kioskProc.Id)). Monitoring session..." -ForegroundColor Green
    $kioskProc.WaitForExit()
    Write-Host "[*] Application closed." -ForegroundColor Cyan
} finally {
    Write-Host "[*] Cleaning up background service processes..." -ForegroundColor DarkGray
    if ($serverProc -and !$serverProc.HasExited) { Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue }
    if ($engineProc -and !$engineProc.HasExited) { Stop-Process -Id $engineProc.Id -Force -ErrorAction SilentlyContinue }
    if ($siteProc -and !$siteProc.HasExited) { Stop-Process -Id $siteProc.Id -Force -ErrorAction SilentlyContinue }
    Write-Host "[+] Shutdown complete. Standby." -ForegroundColor Green
}
