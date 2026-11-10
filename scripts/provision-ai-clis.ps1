# provision-ai-clis.ps1
# Automates provisioning of OpenCode CLI (opencode) and Antigravity CLI (agy)
# for Rogue AI event and developer testing on Windows.

[CmdletBinding()]
param(
    [switch]$SkipOpenCode,
    [switch]$SkipAgy,
    [switch]$ForceReinstall
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  ROGUE AI: AI CLI Provisioning Script (Windows)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check Node.js & real npm (skip dummy vendor\nodejs\npm.cmd shim)
Write-Host "[1/3] Checking Node.js and npm..." -ForegroundColor Cyan
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Split-Path -Parent $scriptRoot

function Resolve-RealNpm {
    $candidates = @()
    $cmd = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += "C:\Program Files\nodejs\npm.cmd"
    $candidates += "${env:ProgramFiles(x86)}\nodejs\npm.cmd"
    foreach ($c in ($candidates | Select-Object -Unique)) {
        if ($c -and (Test-Path $c)) {
            $body = Get-Content $c -Raw -ErrorAction SilentlyContinue
            if ($body -and ($body -notmatch 'Standalone node environment active')) {
                return $c
            }
        }
    }
    return $null
}

$realNpm = Resolve-RealNpm
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
if (-not $nodeCmd) {
    foreach ($nd in @((Join-Path $appRoot "nodejs\node.exe"), (Join-Path $appRoot "vendor\nodejs\node.exe"), "C:\Program Files\nodejs\node.exe")) {
        if (Test-Path $nd) { $nodeCmd = $nd; break }
    }
}

if (-not $nodeCmd) {
    Write-Warning "Node.js was not found."
    exit 1
}

# 2. Provision OpenCode CLI
if (-not $SkipOpenCode) {
    Write-Host ""
    Write-Host "[2/3] Provisioning OpenCode CLI (opencode)..." -ForegroundColor Cyan
    $appDataNpm = Join-Path $env:APPDATA "npm"
    $bundledOpenCode = Join-Path $appRoot "opencode\opencode.exe"
    $opencodeCmd = Get-Command opencode -ErrorAction SilentlyContinue
    if (-not $opencodeCmd -and (Test-Path (Join-Path $appDataNpm "opencode.cmd"))) {
        $opencodeCmd = Get-Item (Join-Path $appDataNpm "opencode.cmd")
    }
    if (-not $opencodeCmd -and (Test-Path $bundledOpenCode)) {
        $opencodeCmd = Get-Item $bundledOpenCode
    }

    if ($opencodeCmd -and -not $ForceReinstall) {
        Write-Host "  -> OpenCode is already installed: $($opencodeCmd)" -ForegroundColor Green
    }
    elseif ($realNpm) {
        Write-Host "  -> Installing opencode-ai globally via $realNpm..." -ForegroundColor Yellow
        & $realNpm install -g opencode-ai
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "npm install -g opencode-ai returned non-zero exit code: $LASTEXITCODE"
        }
    }
    else {
        Write-Warning "No real npm found to install opencode-ai. Downloading prebuilt opencode-windows-x64 tarball via Node..."
        $destBin = Join-Path $appRoot "opencode"
        New-Item -ItemType Directory -Path $destBin -Force | Out-Null
        $js = @'
const https = require("https"), fs = require("fs"), path = require("path"), zlib = require("zlib");
const get = (u) => new Promise((res, rej) => https.get(u, { headers: { "User-Agent": "rogue-ai-setup" } }, (r) => {
  if (r.statusCode >= 300 && r.statusCode < 400 && r.headers.location) return get(r.headers.location).then(res, rej);
  if (r.statusCode !== 200) return rej(new Error("HTTP " + r.statusCode));
  const chunks = []; r.on("data", c => chunks.push(c)); r.on("end", () => res(Buffer.concat(chunks)));
}).on("error", rej));
(async () => {
  const meta = JSON.parse((await get("https://registry.npmjs.org/opencode-windows-x64/latest")).toString());
  const tgz = await get(meta.dist.tarball);
  const tar = zlib.gunzipSync(tgz);
  let off = 0;
  while (off + 512 <= tar.length) {
    const hdr = tar.subarray(off, off + 512); off += 512;
    if (hdr.every(b => b === 0)) break;
    const name = hdr.subarray(0, 100).toString("utf8").replace(/\0.*$/, "");
    const size = parseInt(hdr.subarray(124, 136).toString("utf8").replace(/\0.*$/, "").trim() || "0", 8);
    if (name.endsWith("bin/opencode.exe") && size > 0) {
      fs.writeFileSync(path.join(process.argv[1], "opencode.exe"), tar.subarray(off, off + size));
      console.log("Installed opencode.exe (" + size + " bytes)");
      return;
    }
    off += Math.ceil(size / 512) * 512;
  }
  throw new Error("bin/opencode.exe not found in tarball");
})();
'@
        $tmpJs = Join-Path $env:TEMP "fetch_opencode.js"
        Set-Content -Path $tmpJs -Value $js -Encoding UTF8
        $nodeBin = if ($nodeCmd -is [System.Management.Automation.ApplicationInfo]) { $nodeCmd.Source } else { [string]$nodeCmd }
        & $nodeBin $tmpJs $destBin
        Remove-Item $tmpJs -Force -ErrorAction SilentlyContinue
    }

    # Ensure %APPDATA%\npm and {app}\opencode are in User PATH
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    foreach ($dirToAdd in @($appDataNpm, (Join-Path $appRoot "opencode"))) {
        if ((Test-Path $dirToAdd) -and ($userPath -notlike "*$dirToAdd*")) {
            Write-Host "  -> Adding $dirToAdd to User PATH..." -ForegroundColor Cyan
            $userPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $dirToAdd } else { "$userPath;$dirToAdd" }
            [Environment]::SetEnvironmentVariable("Path", $userPath, "User")
            $env:Path = "$dirToAdd;$env:Path"
        }
    }
}

# 3. Provision Antigravity CLI (agy)
if (-not $SkipAgy) {
    Write-Host ""
    Write-Host "[3/3] Checking Antigravity CLI (agy)..." -ForegroundColor Cyan
    $agyDir = Join-Path $env:LOCALAPPDATA "agy\bin"
    if (-not (Test-Path $agyDir)) {
        New-Item -ItemType Directory -Path $agyDir -Force | Out-Null
        Write-Host "  -> Created directory: $agyDir" -ForegroundColor Gray
    }

    # Ensure $agyDir is in User PATH
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$agyDir*") {
        Write-Host "  -> Adding $agyDir to User PATH..." -ForegroundColor Cyan
        [Environment]::SetEnvironmentVariable("Path", "$userPath;$agyDir", "User")
        $env:Path = "$agyDir;$env:Path"
    }

    $agyExe = Join-Path $agyDir "agy.exe"
    $agyCmd = Join-Path $agyDir "agy.cmd"

    if (Test-Path $agyExe) {
        Write-Host "  [OK] Official agy binary found at: $agyExe" -ForegroundColor Green
    }
    elseif (Test-Path $agyCmd) {
        Write-Host "  [OK] agy wrapper script found at: $agyCmd" -ForegroundColor Green
    }
    else {
        # Check if agy exists elsewhere on the system
        $existingAgy = Get-Command agy -ErrorAction SilentlyContinue
        if ($existingAgy) {
            Write-Host "  -> Found existing agy at: $($existingAgy.Source)" -ForegroundColor Green
            try {
                Copy-Item -Path $existingAgy.Source -Destination $agyExe -Force
                Write-Host "  [OK] Linked agy binary to $agyExe" -ForegroundColor Green
            } catch {
                Write-Warning "Could not copy $($existingAgy.Source) to ${agyExe}: $_"
            }
        }
        else {
            Write-Host "  -> Official agy.exe not found in $agyDir." -ForegroundColor Yellow
            Write-Host "  -> Creating local dev/test fallback stub ($agyCmd)..." -ForegroundColor Gray
            $stubContent = @"
@echo off
echo ===================================================
echo [Antigravity CLI Dev/Test Stub]
echo Connected to workspace: %CD%
echo Run your edits or prompts here.
echo (In production campus image, official agy.exe replaces this stub)
echo ===================================================
cmd /k
"@
            Set-Content -Path $agyCmd -Value $stubContent -Encoding ASCII
            Write-Host "  [OK] Created local dev/test stub at: $agyCmd" -ForegroundColor Green
            Write-Host "       (For the live 13 Nov event, deploy the official Go binary to $agyExe)" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  AI CLI Provisioning Completed Successfully!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
