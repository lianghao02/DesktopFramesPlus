[CmdletBinding()]
param(
    [string]$Version = "v2.9.0-zh-TW"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

$releaseDir = Join-Path $projectRoot "Code\Desktop Frames\bin\Release\net8.0-windows7.0"
$distDir = Join-Path $projectRoot "dist\DesktopFramesPlus"
$zipPath = Join-Path $projectRoot "dist\DesktopFramesPlus-$Version.zip"

if (-not (Test-Path $releaseDir)) {
    throw "找不到 Release 建置輸出目錄: $releaseDir"
}

$distParent = Split-Path -Parent $distDir
if (-not (Test-Path $distParent)) {
    New-Item -ItemType Directory -Path $distParent -Force | Out-Null
}

# 1. 建立獨立臨時打包工作區，完全杜絕本機執行中行程鎖定與 Profiles 污染
$tempPackDir = Join-Path $env:TEMP "DFP_Pack_$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempPackDir -Force | Out-Null

try {
    # 複製所有檔案（排除 .pdb）
    Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Extension -ne ".pdb" } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $tempPackDir
    }

    # 複製所有子目錄（排除 Profiles）
    Get-ChildItem -Path $releaseDir -Directory | Where-Object { $_.Name -ne "Profiles" } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $tempPackDir -Recurse
    }

    # 2. 製作乾淨發布 ZIP
    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }
    Compress-Archive -Path "$tempPackDir\*" -DestinationPath $zipPath
    Write-Host "打包成功 (純淨發布包): $zipPath" -ForegroundColor Green

    # 3. 安全同步至本機 distDir（供本機測試，略過鎖定檔案與保護 Profiles）
    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }
    Get-ChildItem -Path $tempPackDir -File | ForEach-Object {
        try {
            Copy-Item -Path $_.FullName -Destination $distDir -Force -ErrorAction Stop
        } catch {
            Write-Host "注意: $($_.Name) 目前被執行中行程鎖定，略過就地更新" -ForegroundColor Yellow
        }
    }
    Get-ChildItem -Path $tempPackDir -Directory | ForEach-Object {
        try {
            Copy-Item -Path $_.FullName -Destination $distDir -Recurse -Force -ErrorAction Stop
        } catch {
            # 略過子目錄鎖定
        }
    }
}
finally {
    if (Test-Path $tempPackDir) {
        Remove-Item -Recurse -Force $tempPackDir -ErrorAction SilentlyContinue
    }
}

Get-Item $zipPath | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

