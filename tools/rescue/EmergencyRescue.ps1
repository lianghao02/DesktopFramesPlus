<#
.SYNOPSIS
    農場圍籬（Desktop Frames+）獨立冪等災難急救工具
.DESCRIPTION
    當應用程式異常崩潰、被使用者刪除或需要手動脫困時，
    本腳本負責安全將所有被接管的桌面項目（託管捷徑與原地隱藏檔案）
    100% 安全還原回 Windows 桌面。
    
    安全承諾：
    1. 僅還原庫存清單（fence_inventory.json）記錄的項目，絕不全量取消隱藏桌面。
    2. 捷徑搬回桌面時遇到同名檔案絕不覆寫，自動序號遞增保護。
    3. 具備完全冪等性，重複執行安全無害。
#>

[CmdletBinding()]
param()

# 確保輸出編碼為 UTF-8
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Desktop Frames+ 農場圍籬災難急救還原工具" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 尋找庫存清單路徑（優先搜尋程式自帶目錄）
$candidates = @(
    (Join-Path $PSScriptRoot "fence_inventory.json"),
    (Join-Path $PSScriptRoot "..\..\Code\Desktop Frames\bin\Release\net8.0-windows7.0\fence_inventory.json"),
    (Join-Path $PSScriptRoot "..\..\Code\Desktop Frames\bin\Release\net8.0-windows7.0\Profiles\Default\fence_inventory.json"),
    (Join-Path $PSScriptRoot "..\..\Profiles\Default\fence_inventory.json"),
    (Join-Path $PSScriptRoot "..\..\fence_inventory.json"),
    (Join-Path $env:LOCALAPPDATA "DesktopFramesPlus\Inventory\fence_inventory.json")
)

$inventoryPath = $null
foreach ($path in $candidates) {
    if (Test-Path -LiteralPath $path) {
        $inventoryPath = (Resolve-Path -LiteralPath $path).Path
        break
    }
}

if (-not $inventoryPath) {
    Write-Host "[資訊] 在標準儲存位置均未找到庫存清單 (fence_inventory.json)。" -ForegroundColor Yellow
    Write-Host "這通常表示目前沒有任何桌面項目處於託管狀態，或者尚未接管任何檔案。" -ForegroundColor Gray
    Write-Host ""
    Write-Host "急救程序完成，未變更任何檔案。" -ForegroundColor Green
    exit 0
}

Write-Host "[1/3] 成功載入庫存清單：$inventoryPath" -ForegroundColor Green

try {
    $rawJson = Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8
    $inventory = $rawJson | ConvertFrom-Json
} catch {
    Write-Host "[錯誤] 解析庫存清單 JSON 失敗：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$items = $inventory.Items
if (-not $items -or $items.Count -eq 0) {
    Write-Host "[資訊] 庫存清單中無任何託管項目。" -ForegroundColor Yellow
    exit 0
}

Write-Host "[2/3] 清單中共記錄 $($items.Count) 個項目，準備進行還原..." -ForegroundColor Cyan
Write-Host ""

$successCount = 0
$failCount = 0
$skipCount = 0

foreach ($item in $items) {
    $origPath = $item.OriginalFullPath
    $managedPath = $item.ManagedStoragePath
    $type = $item.Type # 0 = ShortcutMove, 1 = InPlaceHidden
    $name = $item.DisplayName

    Write-Host ">>> 處理項目：$name ($origPath)" -ForegroundColor White

    if ($type -eq 0 -or $type -eq "ShortcutMove") {
        # 捷徑託管搬回
        if (Test-Path -LiteralPath $managedPath) {
            $dest = $origPath
            $dir = [System.IO.Path]::GetDirectoryName($dest)
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($dest)
            $ext = [System.IO.Path]::GetExtension($dest)
            $counter = 1

            # 同名防覆寫保護
            while (Test-Path -LiteralPath $dest) {
                $dest = Join-Path $dir "$baseName ($counter)$ext"
                $counter++
            }

            try {
                Move-Item -LiteralPath $managedPath -Destination $dest -Force -ErrorAction Stop
                Write-Host "    [成功] 捷徑已安全搬回桌面：$dest" -ForegroundColor Green
                $successCount++
            } catch {
                Write-Host "    [失敗] 搬回捷徑時發生錯誤：$($_.Exception.Message)" -ForegroundColor Red
                $failCount++
            }
        } elseif (Test-Path -LiteralPath $origPath) {
            Write-Host "    [跳過] 捷徑已在桌面，無需搬移。" -ForegroundColor Gray
            $skipCount++
        } else {
            Write-Host "    [警告] 託管捷徑檔案已不存在：$managedPath" -ForegroundColor Yellow
            $failCount++
        }
    } else {
        # 實體檔案原地解除隱藏
        if (Test-Path -LiteralPath $origPath) {
            try {
                $cur = [System.IO.File]::GetAttributes($origPath)
                if ($cur -band [System.IO.FileAttributes]::Hidden) {
                    $newAttr = $cur -band (-bnot [System.IO.FileAttributes]::Hidden)
                    if ($newAttr -eq 0) {
                        if (Test-Path -LiteralPath $origPath -PathType Container) {
                            $newAttr = [System.IO.FileAttributes]::Directory
                        } else {
                            $newAttr = [System.IO.FileAttributes]::Normal
                        }
                    }
                    [System.IO.File]::SetAttributes($origPath, $newAttr)
                    Write-Host "    [成功] 實體項目已解除隱藏屬性，圖示已重現。" -ForegroundColor Green
                    $successCount++
                } else {
                    Write-Host "    [跳過] 該項目原本即未隱藏。" -ForegroundColor Gray
                    $skipCount++
                }
            } catch {
                Write-Host "    [失敗] 修改屬性時發生錯誤：$($_.Exception.Message)" -ForegroundColor Red
                $failCount++
            }
        } else {
            Write-Host "    [警告] 實體項目已不存在於磁碟：$origPath" -ForegroundColor Yellow
            $failCount++
        }
    }
}

Write-Host ""
Write-Host "[3/3] 重新整理 Windows 桌面圖示快取..." -ForegroundColor Cyan

# 呼叫 Win32 SHChangeNotify 刷新桌面目錄與快取
try {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class ShellNotifier {
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    public static void Refresh(string userDesktop, string commonDesktop) {
        IntPtr p1 = Marshal.StringToHGlobalUni(userDesktop);
        try {
            SHChangeNotify(0x00001000, 0x0005, p1, IntPtr.Zero); // SHCNE_UPDATEDIR
        } finally { Marshal.FreeHGlobal(p1); }

        if (!string.IsNullOrEmpty(commonDesktop)) {
            IntPtr p2 = Marshal.StringToHGlobalUni(commonDesktop);
            try {
                SHChangeNotify(0x00001000, 0x0005, p2, IntPtr.Zero);
            } finally { Marshal.FreeHGlobal(p2); }
        }

        SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED, SHCNF_FLUSH
    }
}
"@
    $userDesk = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
    $commonDesk = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
    [ShellNotifier]::Refresh($userDesk, $commonDesk)
    Write-Host "桌面 Shell 重新整理信號已發送！" -ForegroundColor Green
} catch {
    Write-Host "無法調用原生 Shell 通知（非致命）：$($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  急救處理完成！" -ForegroundColor Green
Write-Host "  成功還原：$successCount 筆" -ForegroundColor Green
Write-Host "  略過（已在桌面）：$skipCount 筆" -ForegroundColor Gray
if ($failCount -gt 0) {
    Write-Host "  失敗或遺失：$failCount 筆（請檢視上方日誌）" -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan
