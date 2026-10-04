[CmdletBinding()]
param(
    [string]$Version = "v2.8.1-zh-TW"
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

# 若 dist/DesktopFramesPlus 內已有使用者的 Profiles 設定檔，先安全暫存備份，絕不刪除使用者資料
$existingProfiles = Join-Path $distDir "Profiles"
$tempBackup = $null
if (Test-Path $existingProfiles) {
    $tempBackup = Join-Path $env:TEMP "DFP_Profiles_Backup_$([Guid]::NewGuid().ToString('N'))"
    Copy-Item -Path $existingProfiles -Destination $tempBackup -Recurse -Force
    Write-Host "偵測到本機 Profiles 設定，已暫存保護: $tempBackup" -ForegroundColor Cyan
}

if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$files = Get-ChildItem -Path $releaseDir -File
foreach ($f in $files) {
    if ($f.Extension -ne ".pdb") {
        Copy-Item -Path $f.FullName -Destination $distDir
    }
}

$dirs = Get-ChildItem -Path $releaseDir -Directory
foreach ($d in $dirs) {
    if ($d.Name -ne "Profiles") {
        Copy-Item -Path $d.FullName -Destination $distDir -Recurse
    }
}

# 製作乾淨的發布 ZIP（此時 distDir 完全不含 Profiles，保證 ZIP 乾淨）
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath
Write-Host "打包成功 (純淨發布包): $zipPath" -ForegroundColor Green

# 打包完成後，將使用者的本機設定 Profiles 還原回 distDir，確保本地運行不丟失任何設定與柵欄
if ($tempBackup -and (Test-Path $tempBackup)) {
    Copy-Item -Path $tempBackup -Destination $existingProfiles -Recurse -Force
    Remove-Item -Recurse -Force $tempBackup
    Write-Host "已成功還原使用者本機 Profiles 設定至: $existingProfiles" -ForegroundColor Green
}

Get-Item $zipPath | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
