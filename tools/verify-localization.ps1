<#
.SYNOPSIS
    驗證 Strings.resx 與 Strings.zh-TW.resx 的鍵值對照完整度。
#>

[CmdletBinding()]
param(
    [string]$ResxPath,
    [string]$ZhTwPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ResxPath)) {
    $ResxPath = Join-Path $scriptDir "..\Code\Desktop Frames\Localization\Strings.resx"
}
if ([string]::IsNullOrWhiteSpace($ZhTwPath)) {
    $ZhTwPath = Join-Path $scriptDir "..\Code\Desktop Frames\Localization\Strings.zh-TW.resx"
}

$ResxPath = [System.IO.Path]::GetFullPath($ResxPath)
$ZhTwPath = [System.IO.Path]::GetFullPath($ZhTwPath)

if (-not (Test-Path -LiteralPath $ResxPath)) {
    Write-Error "找不到英文基準資源檔: $ResxPath"
}
if (-not (Test-Path -LiteralPath $ZhTwPath)) {
    Write-Error "找不到繁體中文資源檔: $ZhTwPath"
}

[xml]$enXml = Get-Content -LiteralPath $ResxPath -Encoding UTF8
[xml]$zhXml = Get-Content -LiteralPath $ZhTwPath -Encoding UTF8

$enKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($data in $enXml.root.data) {
    if ($null -ne $data.name) {
        [void]$enKeys.Add($data.name)
    }
}

$zhKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($data in $zhXml.root.data) {
    if ($null -ne $data.name) {
        [void]$zhKeys.Add($data.name)
    }
}

$missingInZh = [System.Collections.Generic.List[string]]::new()
foreach ($key in $enKeys) {
    if (-not $zhKeys.Contains($key)) {
        $missingInZh.Add($key)
    }
}

$extraInZh = [System.Collections.Generic.List[string]]::new()
foreach ($key in $zhKeys) {
    if (-not $enKeys.Contains($key)) {
        $extraInZh.Add($key)
    }
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " 資源對照檢查結果" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "基準英文鍵數量 (Strings.resx): $($enKeys.Count)"
Write-Host "繁體中文鍵數量 (Strings.zh-TW.resx): $($zhKeys.Count)"

if ($missingInZh.Count -eq 0) {
    Write-Host "✔ 繁體中文資源檔無任何漏翻鍵！(100% 覆蓋)" -ForegroundColor Green
} else {
    Write-Host "✘ 繁體中文資源檔缺少以下鍵 ($($missingInZh.Count) 個):" -ForegroundColor Yellow
    foreach ($m in $missingInZh) {
        Write-Host "  - $m" -ForegroundColor Yellow
    }
}

if ($extraInZh.Count -gt 0) {
    Write-Host "ℹ 繁體中文資源檔有以下額外鍵 ($($extraInZh.Count) 個):" -ForegroundColor Gray
    foreach ($e in $extraInZh) {
        Write-Host "  + $e" -ForegroundColor Gray
    }
}

Write-Host "==========================================" -ForegroundColor Cyan
