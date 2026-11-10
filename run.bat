@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
title ROGUE AI - Launcher

:: Quick usage:
::   run.bat host              -> Organizer Host (Control Plane + Engine + Admin UI)
::   run.bat play [host-url]   -> Participant View (auto-discovers Host or connects to host-url)
::   run.bat demo              -> Single-PC Demo (Host + Participant together)

if not "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
    goto :eof
)

echo ===================================================================
echo   ROGUE AI: THE BREACH PROTOCOL
echo ===================================================================
echo   Commands:  run.bat host  ^|  run.bat play [ip]  ^|  run.bat demo
echo ===================================================================
echo.
echo   [1] Host   (Organizer Command Center)
echo   [2] Play   (Participant View - auto-detects Host)
echo   [3] Demo   (Host + Participant on this PC)
echo   [4] Exit
echo.
set /p CHOICE="Select [1-4] (Default: 1): "

if "%CHOICE%"=="" set CHOICE=1
if "%CHOICE%"=="1" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" host
if "%CHOICE%"=="2" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" play
if "%CHOICE%"=="3" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" demo
if errorlevel 1 pause
goto :eof
