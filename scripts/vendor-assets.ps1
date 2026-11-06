# Downloads third-party browser assets once, so the event works without a CDN. Run on a build machine with internet.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$term = Join-Path $root 'kiosk\LabKiosk\Assets\terminal'
$lib  = Join-Path $root 'server\ControlPlane\wwwroot\lib'
New-Item -ItemType Directory -Force $term, $lib | Out-Null
$files = @(
  @{ Url = 'https://cdn.jsdelivr.net/npm/xterm@5.3.0/lib/xterm.js';                         Out = "$term\xterm.js" },
  @{ Url = 'https://cdn.jsdelivr.net/npm/xterm@5.3.0/css/xterm.css';                        Out = "$term\xterm.css" },
  @{ Url = 'https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/lib/xterm-addon-fit.js';     Out = "$term\xterm-addon-fit.js" },
  @{ Url = 'https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.7/dist/browser/signalr.min.js'; Out = "$lib\signalr.min.js" }
)
foreach ($f in $files) {
  if (-not (Test-Path $f.Out)) {
    Invoke-WebRequest -Uri $f.Url -OutFile $f.Out
  }
  "{0}  {1}" -f (Get-FileHash $f.Out -Algorithm SHA256).Hash, $f.Out
}
