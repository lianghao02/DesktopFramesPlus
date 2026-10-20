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

# 檢查若目前已有執行中的 Desktop Frames，安全關閉以利重啟或重新建置
$running = Get-Process -Name "Desktop Frames" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[DesktopFramesPlus] 偵測到已有 Desktop Frames 程序正在執行 (PID: $($running.Id -join ', '))，正在安全結束以重新啟動..." -ForegroundColor Yellow
    foreach ($p in $running) {
        try {
            $null = $p.CloseMainWindow()
            if (-not $p.WaitForExit(1500)) {
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
        } catch {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 400
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
    Write-Host "[DesktopFramesPlus] 正在執行可攜版建置並同步至 dist\DesktopFramesPlus (Profiles 100% 保留)..." -ForegroundColor Cyan
    $buildScript = Join-Path $projectRoot "scripts\build_and_sync.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript
    if ($LASTEXITCODE -ne 0) { throw "DesktopFramesPlus 建置或同步失敗，結束碼：$LASTEXITCODE" }
}

if (Test-Path $exePath) {
    Write-Host "[DesktopFramesPlus] 正在以最新正式版本啟動: $exePath" -ForegroundColor Green
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.WorkingDirectory = (Split-Path -Parent $exePath)
    $psi.UseShellExecute = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Milliseconds 800
    if ($proc -and -not $proc.HasExited) {
        Write-Host "[DesktopFramesPlus] 啟動成功，進程已在背景運行 (PID: $($proc.Id))。" -ForegroundColor Green
    }
} else {
    Write-Error "無法啟動：執行檔不存在: $exePath"
    exit 1
}
