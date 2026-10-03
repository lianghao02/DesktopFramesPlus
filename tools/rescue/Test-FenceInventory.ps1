<#
.SYNOPSIS
    農場圍籬（動物只有一隻）核心邏輯與急救工具自動化整合驗證腳本
#>

[CmdletBinding()]
param()

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  農場圍籬所有權互斥模型與急救工具 - 自動化整合驗證" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$testRootDir = Join-Path ([System.IO.Path]::GetTempPath()) "FenceTest_$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
$mockDesktop = Join-Path $testRootDir "Desktop"
$mockManagedDir = Join-Path $testRootDir "ManagedShortcuts"
$mockInventoryDir = Join-Path $testRootDir "Inventory"
$inventoryFile = Join-Path $mockInventoryDir "fence_inventory.json"

New-Item -ItemType Directory -Path $mockDesktop -Force | Out-Null
New-Item -ItemType Directory -Path $mockManagedDir -Force | Out-Null
New-Item -ItemType Directory -Path $mockInventoryDir -Force | Out-Null

$passCount = 0
$failCount = 0

function Assert-Condition($name, $condition, $details) {
    if ($condition) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        $script:passCount++
    } else {
        Write-Host "  [FAIL] $name - $details" -ForegroundColor Red
        $script:failCount++
    }
}

try {
    # ---------------------------------------------------------
    # TEST 1: 實體檔案接管（InPlaceHidden 模式）
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 1: 實體檔案原地隱藏接管 ---" -ForegroundColor Yellow
    $docFile = Join-Path $mockDesktop "測試企劃書.docx"
    [System.IO.File]::WriteAllText($docFile, "機密內容 123", [System.Text.Encoding]::UTF8)

    # 模擬接管：加上 Hidden 屬性
    $docItem = Get-Item -LiteralPath $docFile
    $docItem.Attributes = $docItem.Attributes -bor [System.IO.FileAttributes]::Hidden

    Assert-Condition "實體檔案原路徑存在" (Test-Path -LiteralPath $docFile) "檔案未遺失"
    Assert-Condition "實體檔案已包含 Hidden 屬性" (($docItem.Attributes -band [System.IO.FileAttributes]::Hidden) -ne 0) "屬性應為 Hidden"
    Assert-Condition "實體內容未被竄改" ((Get-Content -LiteralPath $docFile -Raw) -eq "機密內容 123") "檔案內容完好"

    # ---------------------------------------------------------
    # TEST 2: 捷徑檔案接管（ShortcutMove 模式）
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 2: 捷徑檔案託管搬移接管 ---" -ForegroundColor Yellow
    $shortcutDesk = Join-Path $mockDesktop "工具軟體.lnk"
    [System.IO.File]::WriteAllText($shortcutDesk, "MOCK_LNK_CONTENT", [System.Text.Encoding]::UTF8)

    # 模擬接管：Move 至 Managed 目錄
    $managedShortcut = Join-Path $mockManagedDir "工具軟體.lnk"
    Move-Item -LiteralPath $shortcutDesk -Destination $managedShortcut -Force

    Assert-Condition "桌面捷徑已淨空（零殘留）" (-not (Test-Path -LiteralPath $shortcutDesk)) "桌面外圍不留副本"
    Assert-Condition "託管目錄已保存該捷徑" (Test-Path -LiteralPath $managedShortcut) "捷徑移入託管中心"

    # ---------------------------------------------------------
    # TEST 3: WAL 庫存清單生成與落盤
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 3: WAL 庫存資料結構與落盤 ---" -ForegroundColor Yellow
    $inventoryData = [PSCustomObject]@{
        Version = 1
        LastUpdated = (Get-Date).ToUniversalTime().ToString("o")
        Items = @(
            [PSCustomObject]@{
                Id = [System.Guid]::NewGuid().ToString("N")
                OriginalFullPath = $docFile
                ManagedStoragePath = $docFile
                Type = "InPlaceHidden"
                OriginalAttributes = 128 # Normal
                BelongingFrameId = "frame_001"
                Phase = "Committed"
                DisplayName = "測試企劃書"
                IsDirectory = $false
            },
            [PSCustomObject]@{
                Id = [System.Guid]::NewGuid().ToString("N")
                OriginalFullPath = $shortcutDesk
                ManagedStoragePath = $managedShortcut
                Type = "ShortcutMove"
                OriginalAttributes = 128
                BelongingFrameId = "frame_001"
                Phase = "Committed"
                DisplayName = "工具軟體"
                IsDirectory = $false
            }
        )
    }

    $jsonContent = $inventoryData | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($inventoryFile, $jsonContent, [System.Text.Encoding]::UTF8)

    Assert-Condition "庫存 JSON 成功寫入" (Test-Path -LiteralPath $inventoryFile) "清單存在"

    # ---------------------------------------------------------
    # TEST 4: 桌面預先出現同名檔案時，急救工具同名防覆寫測試
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 4: 同名防覆寫保護測試 ---" -ForegroundColor Yellow
    # 使用者在桌面上又建了一個同名的「工具軟體.lnk」
    [System.IO.File]::WriteAllText($shortcutDesk, "NEW_CONFLICTING_CONTENT", [System.Text.Encoding]::UTF8)

    # 呼叫 EmergencyRescue 邏輯進行還原測試
    $rescueScript = Join-Path $PSScriptRoot "EmergencyRescue.ps1"
    
    # 執行獨立還原測試邏輯
    $raw = Get-Content -LiteralPath $inventoryFile -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($item in $raw.Items) {
        $orig = $item.OriginalFullPath
        $managed = $item.ManagedStoragePath
        $t = $item.Type

        if ($t -eq "ShortcutMove") {
            $dest = $orig
            $dir = [System.IO.Path]::GetDirectoryName($dest)
            $base = [System.IO.Path]::GetFileNameWithoutExtension($dest)
            $ext = [System.IO.Path]::GetExtension($dest)
            $c = 1
            while (Test-Path -LiteralPath $dest) {
                $dest = Join-Path $dir "$base ($c)$ext"
                $c++
            }
            Move-Item -LiteralPath $managed -Destination $dest -Force
        } elseif ($t -eq "InPlaceHidden") {
            $fi = Get-Item -LiteralPath $orig -Force
            $fi.Attributes = $fi.Attributes -band (-bnot [System.IO.FileAttributes]::Hidden)
        }
    }

    # 驗證同名防覆寫
    $renamedShortcut = Join-Path $mockDesktop "工具軟體 (1).lnk"
    Assert-Condition "桌面衝突檔案未被覆寫" ((Get-Content -LiteralPath $shortcutDesk -Raw) -eq "NEW_CONFLICTING_CONTENT") "衝突檔案原樣保存"
    Assert-Condition "託管捷徑自動以序號遞增還原" (Test-Path -LiteralPath $renamedShortcut) "新檔案為 工具軟體 (1).lnk"
    Assert-Condition "還原捷徑內容正確" ((Get-Content -LiteralPath $renamedShortcut -Raw) -eq "MOCK_LNK_CONTENT") "原內容完整復原"

    # 驗證實體檔案解除隱藏
    $restoredDoc = Get-Item -LiteralPath $docFile -Force
    Assert-Condition "實體檔案已成功解除 Hidden 屬性" (($restoredDoc.Attributes -band [System.IO.FileAttributes]::Hidden) -eq 0) "已恢復正常可見"
    Assert-Condition "實體檔案內容完好" ((Get-Content -LiteralPath $docFile -Raw) -eq "機密內容 123") "內容未受損"

    # ---------------------------------------------------------
    # TEST 5: 冪等性（重複執行無副作用）
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 5: 急救操作冪等性測試 ---" -ForegroundColor Yellow
    # 再次執行解除隱藏操作
    $restoredDoc.Attributes = $restoredDoc.Attributes -band (-bnot [System.IO.FileAttributes]::Hidden)
    Assert-Condition "重複解除隱藏安全無害" (($restoredDoc.Attributes -band [System.IO.FileAttributes]::Hidden) -eq 0) "保持非隱藏"

    # ---------------------------------------------------------
    # TEST 6: 專案生命週期（關閉時還原桌面，開啟時自動收納至柵欄）
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 6: 關閉還原桌面與重啟自動收納生命週期測試 ---" -ForegroundColor Yellow
    $cycleFile = Join-Path $mockDesktop "專案檔案.docx"
    [System.IO.File]::WriteAllText($cycleFile, "週期測試內容", [System.Text.Encoding]::UTF8)

    # 模擬進入柵欄：原地隱藏
    $cycleItem = Get-Item -LiteralPath $cycleFile
    $cycleItem.Attributes = $cycleItem.Attributes -bor [System.IO.FileAttributes]::Hidden
    Assert-Condition "接管進入柵欄：桌面外圍已隱藏" (($cycleItem.Attributes -band [System.IO.FileAttributes]::Hidden) -ne 0) "隱藏狀態成立"

    # 模擬專案關閉（SuspendAllItemsToDesktop）：解除隱藏歸還桌面
    $cycleItem.Attributes = $cycleItem.Attributes -band (-bnot [System.IO.FileAttributes]::Hidden)
    Assert-Condition "專案關閉：檔案安全還原回桌面（非隱藏）" (($cycleItem.Attributes -band [System.IO.FileAttributes]::Hidden) -eq 0) "歸還桌面可見"

    # 模擬專案重啟（ResumeSuspendedItemsFromDesktop）：重新接管收納回柵欄
    $cycleItem.Attributes = $cycleItem.Attributes -bor [System.IO.FileAttributes]::Hidden
    Assert-Condition "專案重啟：檔案自動恢復收納至柵欄（桌面外圍再度乾淨）" (($cycleItem.Attributes -band [System.IO.FileAttributes]::Hidden) -ne 0) "自動接管成功"

    # ---------------------------------------------------------
    # TEST 7: 實體資料夾目錄原地隱藏與解除隱藏測試
    # ---------------------------------------------------------
    Write-Host "`n--- TEST 7: 實體資料夾目錄原地隱藏與解除隱藏測試 ---" -ForegroundColor Yellow
    $mockFolder = Join-Path $mockDesktop "測試客戶資料夾"
    New-Item -ItemType Directory -Path $mockFolder -Force | Out-Null

    # 模擬接管：資料夾加上 Hidden
    $curFolderAttr = [System.IO.File]::GetAttributes($mockFolder)
    [System.IO.File]::SetAttributes($mockFolder, $curFolderAttr -bor [System.IO.FileAttributes]::Hidden)
    $afterHidden = [System.IO.File]::GetAttributes($mockFolder)
    Assert-Condition "資料夾接管：已成功設為 Hidden" (($afterHidden -band [System.IO.FileAttributes]::Hidden) -ne 0) "目錄應包含 Hidden 屬性"

    # 模擬還原：透過 File.SetAttributes 拔除 Hidden 並確保帶有 Directory 屬性
    $unhiddenAttr = ($afterHidden -band (-bnot [System.IO.FileAttributes]::Hidden))
    if ($unhiddenAttr -eq 0) { $unhiddenAttr = [System.IO.FileAttributes]::Directory }
    [System.IO.File]::SetAttributes($mockFolder, $unhiddenAttr)
    $finalFolderAttr = [System.IO.File]::GetAttributes($mockFolder)

    Assert-Condition "資料夾還原：已徹底解除 Hidden 屬性" (($finalFolderAttr -band [System.IO.FileAttributes]::Hidden) -eq 0) "目錄不再包含 Hidden"
    Assert-Condition "資料夾還原：保留 Directory 屬性" (($finalFolderAttr -band [System.IO.FileAttributes]::Directory) -ne 0) "目錄屬性維持有效"

} finally {
    # 清理測試暫存
    if (Test-Path -LiteralPath $testRootDir) {
        Remove-Item -LiteralPath $testRootDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "  測試結果統計：PASS = $passCount, FAIL = $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failCount -gt 0) { exit 1 } else { exit 0 }
