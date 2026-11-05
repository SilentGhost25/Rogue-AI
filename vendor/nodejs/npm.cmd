@echo off
if /i "%1"=="test" (
    "%~dp0node.exe" --test test\site.test.js
) else (
    echo [npm] Standalone node environment active.
    "%~dp0node.exe" %*
)

