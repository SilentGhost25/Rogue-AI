@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
title ROGUE AI - USB Participant Station Installer

echo ========================================================
echo   ROGUE AI: Quick USB Participant Installer / Launcher
echo ========================================================
echo.

:: 1. If compiled RogueAI-Setup.exe installer exists, run silent installation
if exist "%~dp0dist\RogueAI-Setup.exe" (
    echo [*] Found installer: dist\RogueAI-Setup.exe
    set /p DESK="Enter Desk Number (1-50) [or Press ENTER for AUTO]: "
    if "!DESK!"=="" (
        echo [*] Installing in AUTO mode (Server dynamically assigns desk)...
        start "" /wait "%~dp0dist\RogueAI-Setup.exe" /TYPE=participant /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
    ) else (
        echo [*] Installing as DESK-!DESK!...
        start "" /wait "%~dp0dist\RogueAI-Setup.exe" /TYPE=participant /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DESKID=DESK-!DESK!
    )
    echo.
    echo [+] Installation complete! Launch "Rogue AI - Participant" from Desktop.
    pause
    goto :eof
)

if exist "%~dp0RogueAI-Setup.exe" (
    echo [*] Found installer: RogueAI-Setup.exe
    set /p DESK="Enter Desk Number (1-50) [or Press ENTER for AUTO]: "
    if "!DESK!"=="" (
        echo [*] Installing in AUTO mode...
        start "" /wait "%~dp0RogueAI-Setup.exe" /TYPE=participant /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
    ) else (
        echo [*] Installing as DESK-!DESK!...
        start "" /wait "%~dp0RogueAI-Setup.exe" /TYPE=participant /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DESKID=DESK-!DESK!
    )
    echo.
    echo [+] Installation complete! Launch "Rogue AI - Participant" from Desktop.
    pause
    goto :eof
)

:: 2. Otherwise, run direct portable participant launcher via run.bat / run.ps1
echo [*] Launching Participant Kiosk directly in Play Mode...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" play %*
if errorlevel 1 pause

