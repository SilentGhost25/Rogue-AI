@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: Check for Administrator elevation and self-elevate if needed
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Requesting Administrator privileges to purge system shortcuts and registry keys...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c `\"%~f0`\"' -Verb RunAs"
    exit /b
)

echo ==========================================================
echo    ROGUE AI: PURGING OLD INSTALLATIONS ^& SHORTCUTS
echo ==========================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\clean-old.ps1"

echo.
pause
