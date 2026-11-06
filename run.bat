@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
title ROGUE AI - The Breach Protocol Launcher

:: Detect primary IP address dynamically
for /f "tokens=4" %%a in ('route print 0.0.0.0 2^>nul ^| findstr /r "^[ ]*0.0.0.0"') do (
    if not defined DETECTED_IP set DETECTED_IP=%%a
)
if not defined DETECTED_IP set DETECTED_IP=127.0.0.1

:: If arguments passed directly, pass through to powershell
if not "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
    goto :eof
)

echo ===================================================================
echo     ROGUE AI: THE BREACH PROTOCOL -- DYNAMIC LAN LAUNCHER
echo ===================================================================
echo   Detected Local LAN IP: http://!DETECTED_IP!:5080
echo   UDP Auto-Discovery:    Port 5082 (Active)
echo ===================================================================
echo.
echo   [1] Organizer Host Mode  (Control Plane + Engine + Site + LAN Broadcast)
echo   [2] Participant Kiosk    (Auto-detects and connects to Organizer)
echo   [3] Single-PC Demo       (Runs Host and Kiosk on this machine)
echo   [4] Share Online Tunnel  (Cloudflare public tunnel for remote play)
echo   [5] Exit
echo.
set /p CHOICE="Select an option [1-5] (Default: 1): "

if "%CHOICE%"=="" set CHOICE=1
if "%CHOICE%"=="1" goto :host
if "%CHOICE%"=="2" goto :kiosk
if "%CHOICE%"=="3" goto :both
if "%CHOICE%"=="4" goto :tunnel
if "%CHOICE%"=="5" goto :eof

:host
echo [*] Launching Organizer Command Center with dynamic LAN broadcasting...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -HostMode
goto :eof

:kiosk
echo [*] Launching Participant Kiosk with dynamic zero-config LAN discovery...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -Participant
goto :eof

:both
echo [*] Launching Host Mode + Local Participant Kiosk...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -Both
goto :eof

:tunnel
call "%~dp0share_online.bat"
goto :eof
