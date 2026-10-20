@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run-app.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo DesktopFramesPlus startup failed with error %ERRORLEVEL%.
    pause
)
endlocal
