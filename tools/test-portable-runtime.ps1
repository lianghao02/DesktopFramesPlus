[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BinaryDir,
    [Parameter(Mandatory=$true)][string]$RunnerDir
)
$ErrorActionPreference = 'Stop'
$binary = (Resolve-Path -LiteralPath $BinaryDir).Path
$runner = (Resolve-Path -LiteralPath $RunnerDir).Path
& (Join-Path $PSScriptRoot 'package-release.ps1') -BinaryDir $binary -RequireSelfContained -CheckOnly
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw '成品檢查失敗。' }
$session = Join-Path ([IO.Path]::GetTempPath()) ('DesktopFrames 可攜驗收 ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $session | Out-Null
# 只複製合成驗收宿主與成品，不複製任何 Profiles。
foreach ($sourceDir in @($runner, $binary)) {
    Get-ChildItem -LiteralPath $sourceDir -File | Copy-Item -Destination $session -Force
    Get-ChildItem -LiteralPath $sourceDir -Directory | Where-Object { $_.Name -match '^[a-z]{2}(?:-[A-Za-z]+)?$' } | Copy-Item -Destination $session -Recurse -Force
}
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $session '.acceptance-session'), '純合成可攜驗收', $encoding)
[IO.File]::WriteAllText((Join-Path $session '.tested-dll-sha256'), (Get-FileHash -LiteralPath (Join-Path $binary 'Desktop Frames.dll')).Hash, $encoding)
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binary 'Desktop Frames.dll')).Version.ToString()
$run = Start-Process -FilePath (Join-Path $session 'AcceptanceTests.exe') -ArgumentList $version -WorkingDirectory $session -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $session 'result.txt') -RedirectStandardError (Join-Path $session 'error.txt')
$null = $run.Handle
$runtimePath = $null
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while (-not $run.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    try {
        $run.Refresh()
        $module = $run.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' } | Select-Object -First 1
        if ($module) { $runtimePath = $module.FileName; break }
    } catch { }
    Start-Sleep -Milliseconds 100
}
if (-not $run.WaitForExit(60000)) {
    $run.Kill(); $run.WaitForExit()
    throw "本輪隔離驗收逾時，證據保留於 $session"
}
Get-Content -LiteralPath (Join-Path $session 'result.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $session 'error.txt') -Encoding UTF8
if ($run.ExitCode -ne 0) { throw "可攜驗收失敗：$($run.ExitCode)，$session" }
if (-not $runtimePath -or -not [IO.Path]::GetFullPath($runtimePath).Equals((Join-Path $session 'coreclr.dll'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "未確認載入成品自身 Runtime，不能標記通過。證據：$session"
}
[IO.File]::WriteAllText((Join-Path $session 'runtime-evidence.txt'), "Runtime=$runtimePath`nProductDLL=$((Get-FileHash -LiteralPath (Join-Path $session 'Desktop Frames.dll')).Hash)", $encoding)
Write-Output "PASS：跨路徑隔離驗收使用成品內含 Runtime。證據：$session"
