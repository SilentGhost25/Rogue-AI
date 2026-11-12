@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

:: Check for Administrator elevation and self-elevate if needed
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Requesting Administrator privileges for deep uninstallation...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c `\"%~f0`\"' -Verb RunAs"
    exit /b
)

echo ==========================================================
echo       ROGUE AI -- DEEP UNINSTALLER ^& CLEANUP UTILITY
echo ==========================================================
echo.
echo [*] Terminating active Rogue AI processes...
taskkill /F /IM LabKiosk.exe /T 2>nul
taskkill /F /IM opencode.exe /T 2>nul
taskkill /F /IM agy.exe /T 2>nul
taskkill /F /IM node.exe /T 2>nul
taskkill /F /IM ControlPlane.exe /T 2>nul
taskkill /F /IM python.exe /T 2>nul
echo [+] Active processes cleared.
echo.

if exist "%~dp0unins000.exe" (
    echo [*] Launching Windows Uninstaller...
    start /wait "" "%~dp0unins000.exe" /SILENT
    echo [+] Base application uninstalled.
)

set SCRIPT_PATH=%~dp0scripts\uninstall-complete.ps1
if not exist "!SCRIPT_PATH!" (
    set SCRIPT_PATH=%~dp0..\scripts\uninstall-complete.ps1
)

if exist "!SCRIPT_PATH!" (
    echo [*] Running deep forensic cleanup script...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!SCRIPT_PATH!"
) else (
    echo [*] Running fallback cleanup...
    if exist "%LOCALAPPDATA%\BreachKiosk" rd /s /q "%LOCALAPPDATA%\BreachKiosk" 2>nul
    if exist "%LOCALAPPDATA%\agy" rd /s /q "%LOCALAPPDATA%\agy" 2>nul
    if exist "C:\BreachSandboxes" rd /s /q "C:\BreachSandboxes" 2>nul
    if exist "C:\BreachSecrets" rd /s /q "C:\BreachSecrets" 2>nul
    if exist "C:\BreachWorkspace" rd /s /q "C:\BreachWorkspace" 2>nul
)

echo.
echo ==========================================================
echo           ROGUE AI UNINSTALLATION COMPLETE
echo ==========================================================
echo.
pause
