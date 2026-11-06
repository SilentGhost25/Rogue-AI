@echo off
rem Double-click launcher. Use "run.bat organizer" for organizer mode.
if /I "%~1"=="organizer" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -Organizer
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
)
