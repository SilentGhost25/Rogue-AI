param(
    [Parameter(Position = 0)]
    [string]$Command = "",
    [Parameter(Position = 1)]
    [string]$ServerUrl = "",
    [switch]$HostMode,
    [switch]$Both,
    [switch]$Participant
)

$cmd = $Command.Trim().ToLowerInvariant()
switch ($cmd) {
    { $_ -in @("host", "organizer", "-hostmode") } { $HostMode = $true }
    { $_ -in @("play", "participant", "kiosk", "-participant") } { $Participant = $true }
    { $_ -in @("demo", "both", "-both") } { $Both = $true }
}
if ($ServerUrl -and -not $ServerUrl.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
    $ServerUrl = if ($ServerUrl.Contains(":")) { "http://$ServerUrl" } else { "http://${ServerUrl}:5080" }
}

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path "$ScriptDir\rogue-ai-base\rogue-ai") {
    $RogueRoot = "$ScriptDir\rogue-ai-base\rogue-ai"
} else {
    $RogueRoot = $ScriptDir
}

if (Test-Path "$env:LOCALAPPDATA\dotnet" -ErrorAction SilentlyContinue) {
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
    $env:Path = "$env:LOCALAPPDATA\dotnet;$env:Path"
}
$dotnetCmd = if (Test-Path "$env:LOCALAPPDATA\dotnet\dotnet.exe") { "$env:LOCALAPPDATA\dotnet\dotnet.exe" } else { (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
$env:PYTHONPATH = "$RogueRoot\engine"

if (-not $env:BREACH_ADMIN_KEY) {
    $env:BREACH_ADMIN_KEY = "LocalDevAdminKey_2026_Secure123"
}

function Ensure-ControlPlaneBuilt {
    if (Test-Path "$RogueRoot\server\ControlPlane.exe") {
        return "$RogueRoot\server\ControlPlane.exe"
    }
    $cpProj = "$RogueRoot\server\ControlPlane\ControlPlane.csproj"
    $cpExe = "$RogueRoot\server\ControlPlane\bin\Debug\net9.0\ControlPlane.exe"
    if ((Test-Path $cpProj) -and $dotnetCmd) {
        $needsBuild = -not (Test-Path $cpExe)
        if (-not $needsBuild) {
            $exeTime = (Get-Item $cpExe).LastWriteTimeUtc
            $newer = Get-ChildItem "$RogueRoot\server\ControlPlane" -Recurse -Include *.cs,*.csproj,*.json -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\" -and $_.LastWriteTimeUtc -gt $exeTime } |
                     Select-Object -First 1
            if ($newer) { $needsBuild = $true }
        }
        if ($needsBuild) {
            Write-Host "[*] Building Control Plane (updating from source changes)..." -ForegroundColor Cyan
            & $dotnetCmd build $cpProj -c Debug | Out-Null
        }
    }
    return $cpExe
}

function Ensure-LabKioskBuilt {
    if (Test-Path "$RogueRoot\LabKiosk.exe") {
        return "$RogueRoot\LabKiosk.exe"
    }
    $kProj = "$RogueRoot\kiosk\LabKiosk\LabKiosk.csproj"
    $kExe = "$RogueRoot\kiosk\LabKiosk\bin\Debug\net9.0-windows\LabKiosk.exe"
    if ((Test-Path $kProj) -and $dotnetCmd) {
        $needsBuild = -not (Test-Path $kExe)
        if (-not $needsBuild) {
            $exeTime = (Get-Item $kExe).LastWriteTimeUtc
            $newer = Get-ChildItem "$RogueRoot\kiosk\LabKiosk" -Recurse -Include *.cs,*.xaml,*.csproj,*.json,*.html,*.css,*.js -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\" -and $_.LastWriteTimeUtc -gt $exeTime } |
                     Select-Object -First 1
            if ($newer) { $needsBuild = $true }
        }
        if ($needsBuild) {
            Write-Host "[*] Building LabKiosk application (updating from source changes)..." -ForegroundColor Cyan
            & $dotnetCmd build $kProj -c Debug | Out-Null
        }
    }
    if (Test-Path $kExe) { return $kExe }
    $scExe = "$RogueRoot\kiosk\LabKiosk\bin\SelfContained\LabKiosk.exe"
    if (Test-Path $scExe) { return $scExe }
    return $kExe
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  ROGUE AI -- THE BREACH PROTOCOL (Dynamic LAN Launcher)  " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Project Root: $RogueRoot" -ForegroundColor Gray

# -------------------------------------------------------------
# DYNAMIC LAN DETECTION (Robust across all Windows environments)
# -------------------------------------------------------------
$detectedIps = [System.Collections.Generic.List[string]]::new()

# Method 1: Dns Host Addresses
try {
    $hostAddrs = [System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) | 
        Where-Object { $_.AddressFamily -eq 'InterNetwork' -and $_.IPAddressToString -notlike '127.*' -and $_.IPAddressToString -notlike '169.254.*' }
    foreach ($a in $hostAddrs) {
        $ip = $a.IPAddressToString
        if (-not $detectedIps.Contains($ip)) { $detectedIps.Add($ip) }
    }
} catch { }

# Method 2: Route print default gateway interface
try {
    $routeOut = route print 0.0.0.0 2>$null | Select-String "0.0.0.0"
    foreach ($line in $routeOut) {
        $parts = -split ($line -replace '^\s+', '')
        if ($parts.Count -ge 4 -and $parts[3] -match '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$') {
            $rip = $parts[3]
            if ($rip -notlike "127.*" -and $rip -notlike "169.254.*" -and -not $detectedIps.Contains($rip)) {
                $detectedIps.Insert(0, $rip) # Put gateway route interface first
            }
        }
    }
} catch { }

# Method 3: NetIPAddress filtering out hypervisors/virtual adapters
try {
    $netIps = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { 
        $_.IPAddress -notlike "127.*" -and 
        $_.IPAddress -notlike "169.254.*" -and 
        $_.InterfaceAlias -notmatch "vEthernet|Virtual|Loopback|VMware|Host-Only" 
    }
    $wifi = $netIps | Where-Object { $_.InterfaceAlias -match "Wi-Fi|Wireless" } | Select-Object -First 1
    if ($wifi -and $detectedIps.Contains($wifi.IPAddress)) {
        $detectedIps.Remove($wifi.IPAddress)
        $detectedIps.Insert(0, $wifi.IPAddress)
    }
} catch { }

$primaryLanIp = if ($detectedIps.Count -gt 0) { $detectedIps[0] } else { "127.0.0.1" }

Write-Host "`n[+] Dynamic Network Detection:" -ForegroundColor Green
Write-Host "    Primary LAN IP:   http://$primaryLanIp:5080" -ForegroundColor White
if ($detectedIps.Count -gt 1) {
    Write-Host "    Other Interfaces: $(($detectedIps | Select-Object -Skip 1) -join ', ')" -ForegroundColor DarkGray
}
Write-Host "    Auto-Discovery:   UDP Broadcast on port 5082 (Active)" -ForegroundColor Cyan

# 1. Initialize Breach Workspace (refresh if legacy Harbour Notes files exist)
$WorkspaceDir = "C:\BreachWorkspace"
try {
    if (!(Test-Path $WorkspaceDir)) {
        New-Item -ItemType Directory -Path $WorkspaceDir -Force -ErrorAction Stop | Out-Null
    }
    $legacyHarbour = (Test-Path "$WorkspaceDir\routes\posts.js") -and !(Test-Path "$WorkspaceDir\routes\traffic.js")
    if (!(Test-Path "$WorkspaceDir\server.js") -or $legacyHarbour) {
        if ($legacyHarbour) {
            Write-Host "[*] Upgrading legacy workspace at $WorkspaceDir to AEGIS..." -ForegroundColor Yellow
            foreach ($d in @("routes", "views", "lib", "public", "test")) {
                if (Test-Path "$WorkspaceDir\$d") { Remove-Item "$WorkspaceDir\$d" -Recurse -Force -ErrorAction SilentlyContinue }
            }
        } else {
            Write-Host "[*] Initializing Breach Workspace at $WorkspaceDir..." -ForegroundColor Cyan
        }
        Copy-Item -Path "$RogueRoot\site\*" -Destination $WorkspaceDir -Recurse -Force -ErrorAction Stop
    }
} catch {
    # If C:\ root is restricted, seamlessly fall back to local project workspace
    $WorkspaceDir = "$RogueRoot\workspace"
    if (!(Test-Path $WorkspaceDir)) {
        New-Item -ItemType Directory -Path $WorkspaceDir -Force | Out-Null
    }
    $legacyFallback = (Test-Path "$WorkspaceDir\routes\posts.js") -and !(Test-Path "$WorkspaceDir\routes\traffic.js")
    if (!(Test-Path "$WorkspaceDir\server.js") -or $legacyFallback) {
        if ($legacyFallback) {
            foreach ($d in @("routes", "views", "lib", "public", "test")) {
                if (Test-Path "$WorkspaceDir\$d") { Remove-Item "$WorkspaceDir\$d" -Recurse -Force -ErrorAction SilentlyContinue }
            }
        }
        Write-Host "[*] Initializing Breach Workspace at $WorkspaceDir..." -ForegroundColor Cyan
        Copy-Item -Path "$RogueRoot\site\*" -Destination $WorkspaceDir -Recurse -Force
    }
    Write-Host "[!] Note: Using local fallback workspace: $WorkspaceDir" -ForegroundColor Yellow
}

# If participant-only mode requested without host services
if ($Participant) {
    $kioskExe = Ensure-LabKioskBuilt
    $targetUrl = $ServerUrl
    if (-not $targetUrl) {
        # 1. Prefer same-PC organizer on loopback if running
        try {
            $localResp = Invoke-WebRequest -Uri "http://127.0.0.1:5080/api/state" -TimeoutSec 1 -UseBasicParsing -ErrorAction SilentlyContinue
            if ($localResp.StatusCode -eq 200) { $targetUrl = "http://127.0.0.1:5080" }
        } catch { }
    }
    if (-not $targetUrl) {
        # 2. Try UDP port 5082 LAN discovery
        try {
            $udp = New-Object System.Net.Sockets.UdpClient(0)
            $udp.EnableBroadcast = $true
            $udp.Client.ReceiveTimeout = 1500
            $ping = [System.Text.Encoding]::UTF8.GetBytes("ROGUE_AI_DISCOVERY_PING")
            [void]$udp.Send($ping, $ping.Length, (New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Broadcast, 5082)))
            $remote = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
            $recv = $udp.Receive([ref]$remote)
            $msg = [System.Text.Encoding]::UTF8.GetString($recv)
            if ($msg -match '^ROGUE_AI_(?:HOST|BEACON):(\d+)') {
                $targetUrl = "http://$($remote.Address):$($Matches[1])"
            }
            $udp.Close()
        } catch { }
    }
    if (-not $targetUrl) {
        $targetUrl = "http://$primaryLanIp:5080"
    }
    Write-Host "[*] Launching Participant Kiosk (Connecting to $targetUrl)..." -ForegroundColor Green
    Start-Process -FilePath $kioskExe -ArgumentList "--server `"$targetUrl`""
    exit 0
}

try {
# Note: LabKiosk's SiteHost owns port 3000 for the participant workspace; HostMode only runs Engine (8000) + ControlPlane (5080).
$siteProc = $null

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
    $controlPlaneExe = Ensure-ControlPlaneBuilt
    $serverProc = Start-Process -FilePath $controlPlaneExe -ArgumentList "--urls http://0.0.0.0:5080" -WorkingDirectory "$RogueRoot\server\ControlPlane" -PassThru -WindowStyle Hidden
}

# Wait for services to be ready
Write-Host "[*] Verifying background services..." -ForegroundColor Cyan
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

# 5. Launch Kiosk App or Host Mode
if ($HostMode) {
    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " ROGUE AI: ORGANIZER COMMAND CENTER ONLINE (DYNAMIC LAN)  " -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "Local Admin URL:      http://127.0.0.1:5080/admin/" -ForegroundColor Cyan
    Write-Host "LAN Admin URL:        http://${primaryLanIp}:5080/admin/" -ForegroundColor Cyan
    Write-Host "Participant Download: http://${primaryLanIp}:5080/download" -ForegroundColor Magenta
    Write-Host "Direct Setup Link:    http://${primaryLanIp}:5080/RogueAI-Setup.exe" -ForegroundColor Magenta
    Write-Host "UDP LAN Discovery:    Broadcasting on port 5082 (Zero-Config)" -ForegroundColor Green
    Write-Host "Credentials:          admin / admin123456" -ForegroundColor Yellow
    Write-Host "==========================================================" -ForegroundColor Green

    # Open Admin Console in default browser
    Start-Process "http://127.0.0.1:5080/admin/"

    Write-Host "`n[+] Control Plane and Evaluation Engine are active." -ForegroundColor Green
    Write-Host "[*] Participant kiosks will automatically detect and connect to this host." -ForegroundColor Cyan
    if ($detectedIps.Count -gt 1) {
        Write-Host "[*] Additional network interfaces available:" -ForegroundColor DarkGray
        foreach ($ip in ($detectedIps | Select-Object -Skip 1)) {
            Write-Host "    -> http://${ip}:5080/download" -ForegroundColor DarkGray
        }
    }
    Write-Host "[*] Press ENTER in this window to stop all services..." -ForegroundColor Yellow
    try {
        [void][System.Console]::ReadLine()
    } catch {
        Start-Sleep -Seconds 86400
    }
} else {
    $kioskExe = Ensure-LabKioskBuilt

    $appArgs = "--server `"http://127.0.0.1:5080`""
    if ($Both) {
        Write-Host "[*] Launching BOTH Participant Kiosk and Organizer Command Center..." -ForegroundColor Magenta
        Start-Process -FilePath $kioskExe -ArgumentList "--host"
    } else {
        Write-Host "[*] Launching ROGUE AI Defense Studio (Participant Kiosk)..." -ForegroundColor Green
    }

    try {
        $kioskProc = Start-Process -FilePath $kioskExe -ArgumentList $appArgs -PassThru
        Write-Host "[+] ROGUE AI is active (PID: $($kioskProc.Id)). Monitoring session..." -ForegroundColor Green
        $kioskProc.WaitForExit()
        Write-Host "[*] Application closed." -ForegroundColor Cyan
    } catch { }
}
} finally {
    $remainingKiosks = Get-Process LabKiosk -ErrorAction SilentlyContinue | Where-Object { $kioskProc -and $_.Id -ne $kioskProc.Id }
    if ($remainingKiosks) {
        Write-Host "[*] Other LabKiosk instances are still active ($($remainingKiosks.Count)). Keeping background services running." -ForegroundColor Cyan
    } else {
        Write-Host "[*] Cleaning up background service processes..." -ForegroundColor DarkGray
        if ($serverProc -and !$serverProc.HasExited) { Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue }
        if ($engineProc -and !$engineProc.HasExited) { Stop-Process -Id $engineProc.Id -Force -ErrorAction SilentlyContinue }
        if ($siteProc -and !$siteProc.HasExited) { Stop-Process -Id $siteProc.Id -Force -ErrorAction SilentlyContinue }
        Write-Host "[+] Shutdown complete. Standby." -ForegroundColor Green
    }
}
