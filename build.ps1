<#
 Builds the distributable app into dist\app (and the installer if Inno Setup is installed).
 Needs: .NET 9 SDK, internet (for Node, embedded Python and pip packages), optional Inno Setup 6 (iscc).
 Versions below are pinned defaults: check they still exist before an event build.
#>
param([string]$NodeVersion = '22.13.1', [string]$PythonVersion = '3.12.8', [switch]$SkipRuntimes)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\app'
if (-not $SkipRuntimes) {
  Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host '1/6 Vendoring browser assets'
& (Join-Path $root 'scripts\vendor-assets.ps1')

Write-Host '2/6 Publishing the kiosk'
$localNuget = Join-Path $env:USERPROFILE '.nuget\packages'
if (Test-Path $localNuget) {
  dotnet restore "$root\kiosk\LabKiosk\LabKiosk.csproj" -r win-x64 -s $localNuget --ignore-failed-sources | Out-Null
  dotnet restore "$root\server\ControlPlane\ControlPlane.csproj" -r win-x64 -s $localNuget --ignore-failed-sources | Out-Null
}
dotnet publish "$root\kiosk\LabKiosk\LabKiosk.csproj" -c Release -r win-x64 --self-contained true --no-restore -o $out
if ($LASTEXITCODE) { throw 'kiosk publish failed' }

Write-Host '3/6 Publishing the control plane'
dotnet publish "$root\server\ControlPlane\ControlPlane.csproj" -c Release -r win-x64 --self-contained true --no-restore -o "$out\server"
if ($LASTEXITCODE) { throw 'server publish failed' }

Write-Host '4/6 Copying engine, attacks and site'
robocopy "$root\engine" "$out\engine" /E /XD __pycache__ tests /XF *.pyc | Out-Null
robocopy "$root\attacks" "$out\attacks" /E /XF _build.py | Out-Null
robocopy "$root\site" "$out\site" /E /XD node_modules test /XF *.db* | Out-Null
robocopy "$root\site" "$out\site-template" /E /XD node_modules test /XF *.db* | Out-Null
Copy-Item "$root\run.ps1", "$root\run.bat" $out

if (-not $SkipRuntimes) {
  Write-Host "5/6 Bundling Node $NodeVersion"
  $nz = Join-Path $env:TEMP "node-$NodeVersion.zip"
  Invoke-WebRequest "https://nodejs.org/dist/v$NodeVersion/node-v$NodeVersion-win-x64.zip" -OutFile $nz
  Expand-Archive $nz $env:TEMP -Force
  Copy-Item "$env:TEMP\node-v$NodeVersion-win-x64" "$out\node" -Recurse

  Write-Host "6/6 Bundling Python $PythonVersion with the engine's packages"
  $pz = Join-Path $env:TEMP "python-$PythonVersion-embed.zip"
  Invoke-WebRequest "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip" -OutFile $pz
  Expand-Archive $pz "$out\python" -Force
  $pth = Get-ChildItem "$out\python\python*._pth" | Select-Object -First 1
  (Get-Content $pth) -replace '^#import site', 'import site' | Set-Content $pth
  Invoke-WebRequest 'https://bootstrap.pypa.io/get-pip.py' -OutFile "$env:TEMP\get-pip.py"
  & "$out\python\python.exe" "$env:TEMP\get-pip.py" --no-warn-script-location
  & "$out\python\python.exe" -m pip install --no-warn-script-location fastapi==0.115.0 uvicorn==0.30.6 pydantic==2.9.2 pyyaml==6.0.2
} else { Write-Host 'Skipping Node and Python bundling (-SkipRuntimes)' }

$isccCmd = Get-Command iscc -ErrorAction SilentlyContinue
$isccCandidates = @(
  $(if ($isccCmd) { $isccCmd.Source }),
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$isccPath = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($isccPath) {
  Write-Host "Compiling setup installer with Inno Setup ($isccPath)..."
  & $isccPath "$root\installer\setup.iss"
  if ($LASTEXITCODE -eq 0) {
    Write-Host 'Installer built successfully: dist\RogueAI-Setup.exe'
  } else {
    throw "Inno Setup compilation failed with code $LASTEXITCODE"
  }
} else {
  Write-Warning 'Inno Setup (iscc) not found; skipped the installer. The app folder is ready in dist\app.'
}
