[CmdletBinding()]
param(
    [string]$Version = "v2.8.0-zh-TW"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

$releaseDir = Join-Path $projectRoot "Code\Desktop Frames\bin\Release\net8.0-windows7.0"
$distDir = Join-Path $projectRoot "dist\DesktopFramesPlus"
$zipPath = Join-Path $projectRoot "DesktopFramesPlus-$Version.zip"

if (-not (Test-Path $releaseDir)) {
    throw "找不到 Release 建置輸出目錄: $releaseDir"
}

if (Test-Path (Join-Path $projectRoot "dist")) {
    Remove-Item -Recurse -Force (Join-Path $projectRoot "dist")
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

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath
Write-Host "打包成功: $zipPath" -ForegroundColor Green
Get-Item $zipPath | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
