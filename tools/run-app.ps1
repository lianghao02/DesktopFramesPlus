param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $projectRoot "Code\Desktop Frames\bin\Release\net8.0-windows7.0\Desktop Frames.exe"
$codeDir = Join-Path $projectRoot "Code\Desktop Frames"

# 檢查若目前已有執行中的 Desktop Frames，提示或重啟
$running = Get-Process | Where-Object { $_.ProcessName -eq "Desktop Frames" }
if ($running) {
    Write-Host "[DesktopFramesPlus] 偵測到已有 Desktop Frames 程序正在執行 (PID: $($running.Id -join ', '))。" -ForegroundColor Yellow
}

$needBuild = $Rebuild -or (-not (Test-Path $exePath))
if (-not $needBuild) {
    $exeTime = (Get-Item $exePath).LastWriteTime
    $latestSource = Get-ChildItem -Path $codeDir -Recurse -Include *.cs, *.xaml, *.csproj | 
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestSource -and $latestSource.LastWriteTime -gt $exeTime) {
        Write-Host "[DesktopFramesPlus] 偵測到原始碼比現有執行檔新 ($($latestSource.Name))，自動觸發重新建置..." -ForegroundColor Cyan
        $needBuild = $true
    }
}

if ($needBuild) {
    Write-Host "[DesktopFramesPlus] 正在建置最新版本..." -ForegroundColor Cyan
    $msBuildCandidates = @(
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe"
    )
    $msBuild = $msBuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($msBuild) {
        $proj = Join-Path $projectRoot "Code\Desktop Frames\Desktop Frames.csproj"
        & $msBuild $proj /p:Configuration=Release
    } else {
        Write-Error "找不到 MSBuild.exe，請確認已安裝 Visual Studio 2022 或 Build Tools。"
        exit 1
    }
}

if (Test-Path $exePath) {
    Write-Host "[DesktopFramesPlus] 正在以最新版本啟動: $exePath" -ForegroundColor Green
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath)
} else {
    Write-Error "無法啟動：執行檔不存在: $exePath"
    exit 1
}
