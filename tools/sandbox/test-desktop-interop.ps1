param(
    [string]$BinaryDir = (Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows7.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $BinaryDir 'FarmFenceSandbox.dll'
if (-not (Test-Path $assemblyPath)) {
    throw "找不到組件: $assemblyPath"
}

# 透過乾淨的 STA 線程執行測試，確保 SetThreadDesktop 成功切換至 Default
$scriptBlock = {
    param($dll)
    [System.Reflection.Assembly]::LoadFrom($dll) | Out-Null
    $interopType = [System.Type]::GetType('FarmFenceSandbox.DesktopInterop, FarmFenceSandbox', $true)

    Write-Host "=== 1. 測試桌面視窗控制代碼 (HWND) 取得 ===" -ForegroundColor Cyan
    $getHwndMethod = $interopType.GetMethod('GetDesktopListViewHwnd', [System.Reflection.BindingFlags]'Public, Static')
    $hwnd = $getHwndMethod.Invoke($null, @())
    Write-Host "Desktop SysListView32 HWND: $hwnd" -ForegroundColor Green
    if ($hwnd -eq [System.IntPtr]::Zero) {
        throw "無法找到桌面 SysListView32 視窗！"
    }

    Write-Host "`n=== 2. 測試真實桌面目錄解析 (支援 OneDrive 重新導向) ===" -ForegroundColor Cyan
    $getDirsMethod = $interopType.GetMethod('GetActualDesktopDirectories', [System.Reflection.BindingFlags]'Public, Static')
    $dirs = $getDirsMethod.Invoke($null, @())
    foreach ($d in $dirs) {
        Write-Host "  - 桌面目錄: $d" -ForegroundColor Yellow
    }

    Write-Host "`n=== 3. 測試桌面原生圖示名稱與座標讀取 ===" -ForegroundColor Cyan
    $getIconsMethod = $interopType.GetMethod('GetAllDesktopIcons', [System.Reflection.BindingFlags]'Public, Static')
    $icons = $getIconsMethod.Invoke($null, @())
    Write-Host "成功讀取到 $($icons.Count) 個桌面原生項目：" -ForegroundColor Green
    foreach ($icon in ($icons | Select-Object -First 10)) {
        Write-Host "  [$($icon.Index)] 名稱: '$($icon.Name)' | 螢幕座標: ($($icon.ScreenPoint.X), $($icon.ScreenPoint.Y)) | 解析路徑: $($icon.ResolvedPath)" -ForegroundColor White
    }
    if ($icons.Count -gt 10) {
        Write-Host "  ... (共 $($icons.Count) 個項目)" -ForegroundColor Gray
    }

    Write-Host "`n[PASS] DesktopInterop 底層測試全數通過！" -ForegroundColor Green
}

$thread = New-Object System.Threading.Thread([System.Threading.ParameterizedThreadStart]{
    param($arg)
    & $scriptBlock $arg
})
$thread.SetApartmentState([System.Threading.ApartmentState]::STA)
$thread.Start($assemblyPath)
$thread.Join()
