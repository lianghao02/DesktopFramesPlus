param(
    [Parameter(Mandatory=$true)][string]$BinaryDir,
    [string]$PowerShellHost = 'powershell.exe'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('DesktopFrames 入口 中文 ' + [guid]::NewGuid().ToString('N'))
$scripts = Join-Path $fixture 'tools'
$source = Join-Path $fixture 'Code\Desktop Frames'
$release = Join-Path $source 'bin\Release\net8.0-windows7.0'
New-Item -ItemType Directory -Path $scripts,$release -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-app.ps1') -Destination $scripts
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package-release.ps1') -Destination $scripts
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Code\Desktop Frames\Desktop Frames.csproj') -Destination $source
Copy-Item -LiteralPath (Join-Path $BinaryDir 'Desktop Frames.dll') -Destination $release
$epoch = [datetime]::UtcNow.AddDays(-2)
(Get-Item -LiteralPath (Join-Path $source 'Desktop Frames.csproj')).LastWriteTimeUtc = $epoch
(Get-Item -LiteralPath (Join-Path $release 'Desktop Frames.dll')).LastWriteTimeUtc = $epoch.AddSeconds(10)
$exe = Join-Path $release 'Desktop Frames.exe'
$profiles = Join-Path $release 'Profiles'
New-Item -ItemType Directory -Path $profiles | Out-Null
$protected = Join-Path $profiles '保留.json'
[IO.File]::WriteAllText($protected, '{"test":"保留使用者設定"}', [Text.UTF8Encoding]::new($false))
$originalHash = (Get-FileHash -LiteralPath $protected).Hash

function Assert-Check {
    param([string]$Name, [string]$Script, [bool]$Success, [string[]]$Arguments)
    $output = & $PowerShellHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts $Script) @Arguments 2>&1
    if (($LASTEXITCODE -eq 0) -ne $Success) { throw "測試失敗：$Name`n$($output -join [Environment]::NewLine)" }
    if ((Get-FileHash -LiteralPath $protected).Hash -ne $originalHash -or (Test-Path -LiteralPath (Join-Path $fixture 'dist'))) {
        throw '唯讀驗證更動配置或建立發布包'
    }
    Write-Output "通過：$Name"
}

# 假 EXE 僅供 ValidateOnly，絕不執行，也不啟動建置。
Assert-Check '缺成品會要求建置' 'run-app.ps1' $false @('-ValidateOnly')
[IO.File]::WriteAllText($exe, '假成品，不可執行')
(Get-Item -LiteralPath $exe).LastWriteTimeUtc = $epoch.AddSeconds(-10)
Assert-Check '以新 DLL 判斷成品，忽略舊 apphost 時間' 'run-app.ps1' $true @('-ValidateOnly')
$obj = Join-Path $source 'obj\Generated.g.cs'
New-Item -ItemType Directory -Path (Split-Path -Parent $obj) -Force | Out-Null
[IO.File]::WriteAllText($obj, '生成檔案')
Assert-Check 'obj 生成檔案不誤觸重建' 'run-app.ps1' $true @('-ValidateOnly')
Assert-Check '版本由專案取得且不建立 ZIP' 'package-release.ps1' $true @('-CheckOnly')
Assert-Check '錯誤發布標籤被拒絕' 'package-release.ps1' $false @('-CheckOnly','-Version','v0.0.0-zh-TW')
$projectFile = Join-Path $source 'Desktop Frames.csproj'
$xml = [xml][IO.File]::ReadAllText($projectFile)
$xml.Project.PropertyGroup.Version = '0.0.0'
[IO.File]::WriteAllText($projectFile, $xml.OuterXml, [Text.UTF8Encoding]::new($false))
Assert-Check '標籤正確但組件版號不同仍被拒絕' 'package-release.ps1' $false @('-CheckOnly')
Assert-Check '真正來源更新仍要求建置' 'run-app.ps1' $false @('-ValidateOnly')
Write-Output "共 7 項通過；測試資料保留於：$fixture"
