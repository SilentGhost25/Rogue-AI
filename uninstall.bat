@echo off
setlocal
cd /d "%~dp0"

echo ==========================================================
echo       ROGUE AI -- LOCAL DEV CLEANUP & UNINSTALLER
echo ==========================================================
echo.
echo [*] Terminating all active event background processes...
taskkill /F /IM LabKiosk.exe 2>nul
taskkill /F /IM node.exe 2>nul
taskkill /F /IM ControlPlane.exe 2>nul
taskkill /F /IM python.exe 2>nul
echo [+] Processes stopped.
echo.

set /p CLEAN_WS="Do you want to wipe local test workspace (C:\BreachWorkspace)? (y/N): "
if /i "%CLEAN_WS%"=="y" (
    if exist "C:\BreachWorkspace" (
        echo [*] Deleting C:\BreachWorkspace...
        rd /s /q "C:\BreachWorkspace"
        echo [+] Workspace directory deleted.
    )
)

echo.
echo ==========================================================
echo               CLEANUP COMPLETED
echo ==========================================================
pause

