param(
    [switch]$Rebuild,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$projectRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $projectRoot "dist\DesktopFramesPlus\Desktop Frames.exe"
$codeDir = Join-Path $projectRoot "Code\Desktop Frames"

# 檢查若目前已有執行中的 Desktop Frames，提示或重啟
$running = Get-Process | Where-Object { $_.ProcessName -eq "Desktop Frames" }
if ($running) {
    Write-Host "[DesktopFramesPlus] 偵測到已有 Desktop Frames 程序正在執行 (PID: $($running.Id -join ', '))。" -ForegroundColor Yellow
}

$needBuild = $Rebuild -or (-not (Test-Path $exePath))
if (-not $needBuild) {
    $assemblyPath = Join-Path $projectRoot "dist\DesktopFramesPlus\Desktop Frames.dll"
    $latestSource = Get-ChildItem -LiteralPath $codeDir -Recurse -File |
        Where-Object { $_.FullName.Substring($codeDir.Length) -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in '.cs','.xaml','.csproj','.resx' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not (Test-Path -LiteralPath $assemblyPath) -or
        ($latestSource -and $latestSource.LastWriteTimeUtc -gt (Get-Item $assemblyPath).LastWriteTimeUtc)) {
        Write-Host "[DesktopFramesPlus] 偵測到原始碼比現有發布檔新 ($($latestSource.Name))，自動觸發重新建置與同步..." -ForegroundColor Cyan
        $needBuild = $true
    }
}

if ($ValidateOnly) {
    if ($needBuild) { throw 'DesktopFramesPlus 缺少有效成品或來源較新，請先建置。' }
    Write-Output $exePath
    return
}

if ($needBuild) {
    if ($running) { throw '請先正常結束 Desktop Frames 再建置；不會關閉正在使用的程式或覆寫其配置。' }
    Write-Host "[DesktopFramesPlus] 正在執行可攜版建置並同步至 dist\DesktopFramesPlus (Profiles 100% 保留)..." -ForegroundColor Cyan
    $buildScript = Join-Path $projectRoot "scripts\build_and_sync.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript
    if ($LASTEXITCODE -ne 0) { throw "DesktopFramesPlus 建置或同步失敗，結束碼：$LASTEXITCODE" }
}

if (Test-Path $exePath) {
    Write-Host "[DesktopFramesPlus] 正在以最新正式版本啟動: $exePath" -ForegroundColor Green
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath)
} else {
    Write-Error "無法啟動：執行檔不存在: $exePath"
    exit 1
}
