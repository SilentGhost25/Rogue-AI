$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$cpExe = Join-Path $root "server\ControlPlane\bin\Release\net9.0\ControlPlane.exe"

if (-not (Test-Path $cpExe)) {
    & dotnet build "$root\server\ControlPlane\ControlPlane.csproj" -c Release
}

$portActive = $false
try {
    $tcp = Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue
    if ($tcp) { $portActive = $true }
} catch {}

$proc = $null
if (-not $portActive) {
    Write-Host "Starting ControlPlane on http://127.0.0.1:5080..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $cpExe -WorkingDirectory "$root\server\ControlPlane" -PassThru -NoNewWindow
    Start-Sleep -Seconds 3
}

try {
    Write-Host "Running 10 fake kiosks simulation..." -ForegroundColor Yellow
    python "$root\sim\fake_kiosks.py" --server http://127.0.0.1:5080 --count 10 --rounds 2
} finally {
    if ($proc -and -not $proc.HasExited) {
        Write-Host "Stopping temporary ControlPlane instance..." -ForegroundColor Gray
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

