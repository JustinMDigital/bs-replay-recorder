@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\launcher\Start-ReplayRecorder.ps1" -RequireInstalled %*
if errorlevel 1 (
    echo.
    echo Start failed. Run Support\install.bat first, then try Replay Recorder.exe again.
    echo Press any key to close.
    pause >nul
)
