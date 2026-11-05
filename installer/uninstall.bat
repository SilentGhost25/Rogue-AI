@echo off
setlocal
cd /d "%~dp0"

echo ==========================================================
echo       ROGUE AI -- UNINSTALLER & CLEANUP UTILITY
echo ==========================================================
echo.
echo [*] Terminating any active Rogue AI processes...
taskkill /F /IM LabKiosk.exe 2>nul
taskkill /F /IM node.exe 2>nul
taskkill /F /IM ControlPlane.exe 2>nul
taskkill /F /IM python.exe 2>nul
echo [+] Background processes cleared.
echo.

if exist "%~dp0unins000.exe" (
    echo [*] Launching Windows Application Uninstaller...
    start /wait "" "%~dp0unins000.exe"
) else (
    echo [!] Inno Setup uninstaller (unins000.exe) not found in current folder.
)

echo.
set /p CLEAN_WS="Do you also want to remove C:\BreachWorkspace? (y/N): "
if /i "%CLEAN_WS%"=="y" (
    if exist "C:\BreachWorkspace" (
        echo [*] Removing C:\BreachWorkspace...
        rd /s /q "C:\BreachWorkspace"
        echo [+] Workspace directory deleted.
    )
)

echo.
echo ==========================================================
echo           ROGUE AI UNINSTALLATION COMPLETE
echo ==========================================================
pause

