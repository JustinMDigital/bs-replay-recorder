@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\installer\Install-ReplayRecorder.ps1" %*
if errorlevel 1 (
  echo.
  echo Install failed.
  pause
  exit /b 1
)
