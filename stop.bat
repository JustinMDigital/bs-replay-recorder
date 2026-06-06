@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launcher\Stop-ReplayRecorder.ps1" %*
if errorlevel 1 (
    echo.
    echo Stop failed. Press any key to close.
    pause >nul
)
