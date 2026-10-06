[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BinaryDir)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$binary = (Resolve-Path -LiteralPath $BinaryDir).Path
$projectVersion = [string]([xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot '../Code/Desktop Frames/Desktop Frames.csproj'))).Project.PropertyGroup.AssemblyVersion
$project = Join-Path $PSScriptRoot 'acceptance-tests/AcceptanceTests.csproj'
& dotnet build $project -c Release --nologo -v quiet "-p:BinaryDir=$binary"
if ($LASTEXITCODE -ne 0) { throw '代表性功能測試建置失敗。' }
$build = Join-Path $PSScriptRoot 'acceptance-tests/bin/Release/net8.0-windows7.0'
$session = Join-Path $build ('中文 驗收-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $session | Out-Null
Get-ChildItem -LiteralPath $build -File | Copy-Item -Destination $session
Get-ChildItem -LiteralPath $binary -File -Filter '*.dll' | Copy-Item -Destination $session -Force
Get-ChildItem -LiteralPath $binary -Directory | Where-Object { $_.Name -match '^[a-z]{2}(?:-[A-Za-z]+)?$' } | Copy-Item -Destination $session -Recurse
[IO.File]::WriteAllText((Join-Path $session '.acceptance-session'), '純合成驗收', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $session '.tested-dll-sha256'), (Get-FileHash -LiteralPath (Join-Path $binary 'Desktop Frames.dll') -Algorithm SHA256).Hash, [Text.UTF8Encoding]::new($false))
$run = Start-Process -FilePath (Join-Path $session 'AcceptanceTests.exe') -ArgumentList $projectVersion -WorkingDirectory $session -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $session 'result.txt') -RedirectStandardError (Join-Path $session 'error.txt')
$null = $run.Handle
if (-not $run.WaitForExit(60000)) {
    # 僅回收本次啟動的隔離驗收程序；不關閉使用者應用程式。
    $run.Kill()
    $run.WaitForExit()
    throw "隔離驗收超過 60 秒；證據保留於 $session"
}
Get-Content -LiteralPath (Join-Path $session 'result.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $session 'error.txt') -Encoding UTF8
if ($run.ExitCode -ne 0) { throw "代表性功能驗收失敗，退出碼 $($run.ExitCode)：$session" }
Write-Output "代表性功能驗收完成：$session"
