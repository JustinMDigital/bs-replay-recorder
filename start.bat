@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launcher\Start-ReplayRecorder.ps1" -RequireInstalled %*
if errorlevel 1 (
    echo.
    echo Start failed. Run install.bat first, then try start.bat again.
    echo Press any key to close.
    pause >nul
)
