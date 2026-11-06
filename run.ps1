<#
 ROGUE AI launcher. Checks the machine, then starts the kiosk (default) or organizer mode.
   .\run.ps1               participant kiosk
   .\run.ps1 -Organizer    starts the control plane and engine, opens the Command Center
#>
param([switch]$Organizer)
$ErrorActionPreference = 'Stop'
$app = $PSScriptRoot
$exe = Join-Path $app 'LabKiosk.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $app 'kiosk\LabKiosk\bin\Release\net9.0-windows\LabKiosk.exe' }
if (-not (Test-Path $exe)) { Write-Error "LabKiosk.exe not found. Build it first (build.ps1) or run from the installed folder."; exit 1 }

# 1. Workspace: C:\BreachWorkspace, else the app falls back to <app>\workspace by itself.
$ws = 'C:\BreachWorkspace'
try { New-Item -ItemType Directory -Force $ws | Out-Null; Set-Content -Path "$ws\.write-test" -Value ok; Remove-Item "$ws\.write-test" }
catch { Write-Warning "$ws is not writable; the kiosk will use a workspace next to the app." }

# 2. Node 22.13+ (bundled copy preferred)
$node = Join-Path $app 'node\node.exe'
if (-not (Test-Path $node)) { $node = (Get-Command node -ErrorAction SilentlyContinue).Source }
if (-not $node) { Write-Error 'Node.js was not found. Install Node 22.13 or newer, or use the installer that bundles it.'; exit 1 }
$v = [version]((& $node --version).TrimStart('v'))
if ($v -lt [version]'22.13') { Write-Error "Node $v is too old; 22.13 or newer is required."; exit 1 }

# 3. Config
if (-not $Organizer -and -not (Test-Path (Join-Path (Split-Path $exe) 'kiosk.json'))) { Write-Warning 'kiosk.json is missing; defaults will be used.' }

# 4. Start
if ($Organizer) { Start-Process $exe -ArgumentList '--host' } else { Start-Process $exe }
