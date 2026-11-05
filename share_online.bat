@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ==========================================================
echo    ROGUE AI -- PUBLIC ONLINE DOWNLOAD & EVENT TUNNEL
echo ==========================================================
echo.

:: 1. Local LAN IP Information
for /f "tokens=4" %%a in ('route print 0.0.0.0 2^>nul ^| findstr /r "^[ ]*0.0.0.0"') do (
    if not defined LOCAL_IP set LOCAL_IP=%%a
)
if not defined LOCAL_IP set LOCAL_IP=127.0.0.1

echo [LOCAL NETWORK DOWNLOAD (Wi-Fi)]
echo   http://!LOCAL_IP!:5080/download
echo.

:: 2. Ensure vendor directory exists
if not exist "vendor" mkdir "vendor"

:: 3. Check for cloudflared.exe
if not exist "vendor\cloudflared.exe" (
    echo [*] Downloading portable Cloudflare Tunnel client (first-time setup)...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile('https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe', 'vendor\cloudflared.exe')"
)

if not exist "vendor\cloudflared.exe" (
    echo [!] Cloudflare client could not be downloaded.
    echo [*] You can still share directly on local Wi-Fi: http://!LOCAL_IP!:5080/download
    pause
    exit /b 1
)

echo [*] Launching secure public tunnel for port 5080...
echo [*] Look for the "https://*.trycloudflare.com" link below:
echo.
"vendor\cloudflared.exe" tunnel --url http://127.0.0.1:5080
pause
