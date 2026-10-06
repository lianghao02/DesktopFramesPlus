[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BinaryDir)
$ErrorActionPreference = 'Stop'
try {
    $binary = (Resolve-Path -LiteralPath $BinaryDir).Path
    $shell = (Get-Process -Id $PID).Path
    foreach ($script in @('test-native-panel-integration.ps1', 'test-representative-functions.ps1', 'test-frame-item-transfer.ps1')) {
        # 獨立程序避免上一輪已載入的 DLL 快取影響成品比對。
        & $shell -NoProfile -File (Join-Path $PSScriptRoot $script) -BinaryDir $binary
        if ($LASTEXITCODE -ne 0) { throw "$script 失敗：$LASTEXITCODE" }
    }
    & $shell -NoProfile -File (Join-Path $PSScriptRoot 'verify-localization.ps1')
    if ($LASTEXITCODE -ne 0) { throw '在地化驗證失敗。' }
    exit 0
} catch { Write-Error $_; exit 1 }
