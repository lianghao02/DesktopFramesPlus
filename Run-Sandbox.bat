@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0tools\sandbox\bin\Release\net8.0-windows7.0\FarmFenceSandbox.exe" (
    start "" /d "%~dp0tools\sandbox\bin\Release\net8.0-windows7.0" "%~dp0tools\sandbox\bin\Release\net8.0-windows7.0\FarmFenceSandbox.exe"
) else (
    dotnet run --project "%~dp0tools\sandbox\FarmFenceSandbox.csproj" -c Release
)
endlocal
