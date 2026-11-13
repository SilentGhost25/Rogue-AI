@echo off
setlocal
title ROGUE AI - USB Participant Station Installer

echo ========================================================
echo   ROGUE AI: Quick USB Participant Installer
echo ========================================================
echo.
echo Installing Participant Defense Studio from USB...
echo Leave desk number blank for Automatic Collision-Free Lease.
echo.
set /p DESK="Enter Desk Number (1-50) [or Press ENTER for AUTO]: "

if "%DESK%"=="" (
    echo [*] Installing in AUTO mode (Server will dynamically assign next available desk)...
    start "" /wait "%~dp0..\dist\RogueAI-Setup.exe" /TYPE=participant /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
) else (
    echo [*] Installing as DESK-%DESK%...
    start "" /wait "%~dp0..\dist\RogueAI-Setup.exe" /TYPE=participant /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DESKID=DESK-%DESK%
)

echo.
echo ========================================================
echo  Installation complete! Launch "Rogue AI - Participant"
echo  from Desktop or Start Menu.
echo ========================================================
pause
