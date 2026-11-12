@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
title ROGUE AI - Launcher

:: Direct argument pass-through:
::   run.bat host             -> Organizer Host
::   run.bat 192.168.0.82     -> Connect to specific Host IP
::   run.bat play [host-url]  -> Participant Mode

if not "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
    if errorlevel 1 pause
    goto :eof
)

echo ===================================================================
echo   ROGUE AI: THE BREACH PROTOCOL -- LAUNCHER
echo ===================================================================
echo.
echo   [1] Participant (Play / Connect to Organizer Host) [DEFAULT]
echo   [2] Enter Host IP manually (e.g. 192.168.0.82)
echo   [3] Host Mode (Organizer Command Center)
echo   [4] Demo (Single PC - Host + Participant)
echo   [5] Exit
echo.
set /p CHOICE="Select [1-5] (Default: 1): "

if "%CHOICE%"=="" set CHOICE=1
if "%CHOICE%"=="1" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" play
    if errorlevel 1 pause
    goto :eof
)
if "%CHOICE%"=="2" (
    set /p HOST_IP="Enter Organizer Host IP: "
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" play !HOST_IP!
    if errorlevel 1 pause
    goto :eof
)
if "%CHOICE%"=="3" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" host
    if errorlevel 1 pause
    goto :eof
)
if "%CHOICE%"=="4" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" demo
    if errorlevel 1 pause
    goto :eof
)
goto :eof
