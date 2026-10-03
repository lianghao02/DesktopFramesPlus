@echo off
chcp 65001 >nul
title Desktop Frames+ 災難急救工具

cd /d "%~dp0"

set "PWSH_EXE="
where pwsh.exe >nul 2>&1
if %errorlevel% equ 0 (
    set "PWSH_EXE=pwsh.exe"
) else (
    where powershell.exe >nul 2>&1
    if %errorlevel% equ 0 (
        set "PWSH_EXE=powershell.exe"
    )
)

if "%PWSH_EXE%"=="" (
    echo [錯誤] 找不到 PowerShell 執行檔，無法啟動急救程序。
    pause
    exit /b 1
)

"%PWSH_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0EmergencyRescue.ps1"

echo.
pause
