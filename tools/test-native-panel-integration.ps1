param(
    [string]$BinaryDir = (Join-Path $PSScriptRoot '..\Code\Desktop Frames\bin\Release\net8.0-windows7.0')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '缺少 .NET SDK，無法建置面板回歸。' }
if (-not (Test-Path (Join-Path $BinaryDir 'Desktop Frames.dll'))) { throw '請先完成正式 MSBuild Release 建置。' }
$BinaryDir = (Resolve-Path -LiteralPath $BinaryDir).Path
$runtimeConfig = Get-Content -LiteralPath (Join-Path $BinaryDir 'Desktop Frames.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$selfContained = [bool]($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'includedFrameworks')
$runtimeArgs = if ($selfContained) { @('-p:SelfContained=true', '-p:RuntimeIdentifier=win-x64', '-p:AppendRuntimeIdentifierToOutputPath=false') } else { @('-p:SelfContained=false', '-p:RuntimeIdentifier=', '-p:AppendRuntimeIdentifierToOutputPath=false') }
& dotnet build (Join-Path $PSScriptRoot 'panel-tests\PanelTests.csproj') -c Release --nologo -v quiet "-p:BinaryDir=$BinaryDir" @runtimeArgs
if ($LASTEXITCODE -ne 0) { throw '面板回歸測試建置失敗。' }
$testBin = Join-Path $PSScriptRoot 'panel-tests\bin\Release\net8.0-windows7.0'
$sessionDir = Join-Path $testBin ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sessionDir | Out-Null
Get-ChildItem -LiteralPath $testBin -File | Copy-Item -Destination $sessionDir
Get-ChildItem -LiteralPath $BinaryDir -Filter '*.dll' -File | Copy-Item -Destination $sessionDir
[IO.File]::WriteAllText((Join-Path $sessionDir '.acceptance-session'), '純合成面板驗收', $utf8)
Get-ChildItem -LiteralPath $BinaryDir -Directory | Where-Object { $_.Name -match '^[a-z]{2}(?:-[A-Za-z]+)?$' } | Copy-Item -Destination $sessionDir -Recurse
$testRun = Start-Process -FilePath (Join-Path $sessionDir 'PanelTests.exe') -WorkingDirectory $sessionDir -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $sessionDir 'result.txt') -RedirectStandardError (Join-Path $sessionDir 'error.txt') -PassThru
$null = $testRun.Handle
if (-not $testRun.WaitForExit(60000)) {
    $testRun.Kill(); $testRun.WaitForExit()
    throw "隔離面板驗收超過 60 秒，證據保留於 $sessionDir"
}
Get-Content (Join-Path $sessionDir 'result.txt') -Encoding UTF8
Get-Content (Join-Path $sessionDir 'error.txt') -Encoding UTF8
if ($testRun.ExitCode -ne 0) { throw "面板整合回歸失敗，退出碼 $($testRun.ExitCode)。" }
Write-Output '面板整合回歸通過；本次資料保留於忽略的 bin 測試目錄，未操作使用者桌面圖示。'
