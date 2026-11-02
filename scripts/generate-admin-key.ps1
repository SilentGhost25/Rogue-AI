<#
.SYNOPSIS
    Generates a cryptographically secure 32-character admin master key for Rogue AI Control Plane.
.DESCRIPTION
    Uses RandomNumberGenerator to produce high-entropy keys.
    Optionally updates server/ControlPlane/appsettings.json if -UpdateAppSettings is specified.
.PARAMETER UpdateAppSettings
    If set, writes the generated key directly into server/ControlPlane/appsettings.json.
#>

param(
    [switch]$UpdateAppSettings
)

$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ROGUE AI: Generated Admin Master Key" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "KEY: $key" -ForegroundColor Green
Write-Host "Length: $($key.Length) characters" -ForegroundColor Gray

if ($UpdateAppSettings) {
    $root = Resolve-Path (Join-Path $PSScriptRoot "..")
    $settingsPath = Join-Path $root "server\ControlPlane\appsettings.json"
    if (Test-Path $settingsPath) {
        $json = Get-Content -Raw -Path $settingsPath | ConvertFrom-Json
        $json.Breach.AdminKey = $key
        $json | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsPath -Encoding UTF8
        Write-Host "Updated server/ControlPlane/appsettings.json with new admin key." -ForegroundColor Green
    } else {
        Write-Warning "Could not find appsettings.json at $settingsPath"
    }
}
