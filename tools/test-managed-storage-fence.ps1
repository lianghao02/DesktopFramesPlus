<#
.SYNOPSIS
    專用收納柵欄 (Managed Storage Fence) 獨立合成測試。
    在隔離暫存沙盒中驗證收納、手勢、防呆、跨框移動、拆框移至桌面與鎖定保護。
#>
param(
    [string]$BinaryDir = (Join-Path $PSScriptRoot '..\Code\Desktop Frames\bin\Release\net8.0-windows7.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$binaryDirPath = [System.IO.Path]::GetFullPath($BinaryDir)
$appDll = Join-Path $binaryDirPath 'Desktop Frames.dll'
if (-not (Test-Path $appDll)) {
    throw "找不到建置成品：$appDll"
}

[void][System.Reflection.Assembly]::LoadFrom((Join-Path $binaryDirPath 'Newtonsoft.Json.dll'))
$appAssembly = [System.Reflection.Assembly]::LoadFrom($appDll)
$serviceType = $appAssembly.GetType('Desktop_Frames.Services.StorageFenceService', $true)
$dropActionType = $appAssembly.GetType('Desktop_Frames.Services.StorageDropAction', $true)
$ingestStatusType = $appAssembly.GetType('Desktop_Frames.Services.IngestResultStatus', $true)
$dismantleStatusType = $appAssembly.GetType('Desktop_Frames.Services.DismantleItemStatus', $true)

$sandboxDir = Join-Path $env:TEMP ("dfsbox_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $sandboxDir -Force | Out-Null

try {
    Write-Output "== 專用收納柵欄合成測試啟動 =="
    Write-Output "沙盒目錄: $sandboxDir"

    $storageRoot = Join-Path $sandboxDir "StorageRoot"
    $fenceDirA = Join-Path $storageRoot "柵欄A"
    $fenceDirB = Join-Path $storageRoot "柵欄B"
    $mockDesktop = Join-Path $sandboxDir "MockDesktop"
    $sourceDir = Join-Path $sandboxDir "SourceFolder"

    New-Item -ItemType Directory -Path $fenceDirA -Force | Out-Null
    New-Item -ItemType Directory -Path $fenceDirB -Force | Out-Null
    New-Item -ItemType Directory -Path $mockDesktop -Force | Out-Null
    New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null

    $frameA = [Newtonsoft.Json.Linq.JObject]::Parse(@"
{
    "Id": "fence_a",
    "Title": "公文收納框A",
    "ItemsType": "Portal",
    "Path": "$([regex]::Escape($fenceDirA).Replace('\', '\\'))",
    "IsManagedStorage": "true",
    "StorageRootPath": "$([regex]::Escape($storageRoot).Replace('\', '\\'))"
}
"@)

    $frameB = [Newtonsoft.Json.Linq.JObject]::Parse(@"
{
    "Id": "fence_b",
    "Title": "專案收納框B",
    "ItemsType": "Portal",
    "Path": "$([regex]::Escape($fenceDirB).Replace('\', '\\'))",
    "IsManagedStorage": "true",
    "StorageRootPath": "$([regex]::Escape($storageRoot).Replace('\', '\\'))"
}
"@)

    # 1. 測試預設搬移手勢 (Move)
    $srcFile1 = Join-Path $sourceDir "公文一號.txt"
    [System.IO.File]::WriteAllText($srcFile1, "公文內文1", [System.Text.Encoding]::UTF8)

    $res1 = $serviceType::IngestItem($frameA, $srcFile1, [Enum]::Parse($dropActionType, 'Move'))
    if ($res1.Status.ToString() -ne 'Completed') { throw "預期 Move 成功，實際: $($res1.Status), 錯誤訊息: $($res1.ErrorMessage)" }
    if (Test-Path $srcFile1) { throw "Move 後來源檔案應已消失" }
    if (-not (Test-Path (Join-Path $fenceDirA "公文一號.txt"))) { throw "Move 後目標檔案應存在於收納夾" }
    Write-Output "✔ PASS: 預設拖曳搬移成功，來源檔案不殘留。"

    # 2. 測試複製手勢 (Copy)
    $srcFile2 = Join-Path $sourceDir "參考簡報.pptx"
    [System.IO.File]::WriteAllText($srcFile2, "PPTX內文", [System.Text.Encoding]::UTF8)

    $res2 = $serviceType::IngestItem($frameA, $srcFile2, [Enum]::Parse($dropActionType, 'Copy'))
    if ($res2.Status.ToString() -ne 'Completed') { throw "預期 Copy 成功，實際: $($res2.Status)" }
    if (-not (Test-Path $srcFile2)) { throw "Copy 後來源檔案必須完整保留" }
    if (-not (Test-Path (Join-Path $fenceDirA "參考簡報.pptx"))) { throw "Copy 後目標檔案應存在於收納夾" }
    Write-Output "✔ PASS: 按住 Ctrl 複製成功，來源檔案完整保留。"

    # 3. 測試同目錄拖曳防呆 (No-op 避免產生 (1) 副本)
    $inFolderItem = Join-Path $fenceDirA "公文一號.txt"
    $res3 = $serviceType::IngestItem($frameA, $inFolderItem, [Enum]::Parse($dropActionType, 'Move'))
    if ($res3.Status.ToString() -ne 'SkippedSameFolder') { throw "預期同目錄跳過，實際: $($res3.Status)" }
    $copyCheck = Join-Path $fenceDirA "公文一號 (1).txt"
    if (Test-Path $copyCheck) { throw "同框誤拖曳嚴禁產生多餘編號副本！" }
    Write-Output "✔ PASS: 同框誤拖曳成功防呆，未產生多餘副本。"

    # 4. 測試祖先/子目錄防呆
    $res4 = $serviceType::IngestItem($frameA, $fenceDirA, [Enum]::Parse($dropActionType, 'Move'))
    if ($res4.Status.ToString() -ne 'SourceIsAncestorOrSame') { throw "預期拒絕搬移自己進自己，實際: $($res4.Status)" }
    Write-Output "✔ PASS: 目錄自身與子母目錄防呆正常阻擋。"

    # 5. 測試跨框移動 (柵欄A -> 柵欄B)
    $crossFile = Join-Path $fenceDirA "公文一號.txt"
    $res5 = $serviceType::IngestItem($frameB, $crossFile, [Enum]::Parse($dropActionType, 'Move'))
    if ($res5.Status.ToString() -ne 'Completed') { throw "預期跨框 Move 成功，實際: $($res5.Status)" }
    if (Test-Path $crossFile) { throw "跨框移動後來源柵欄 A 不應殘留" }
    if (-not (Test-Path (Join-Path $fenceDirB "公文一號.txt"))) { throw "跨框移動後目標柵欄 B 應存在" }
    Write-Output "✔ PASS: 跨收納柵欄實體搬移正常且無殘留。"

    # 6. 測試拆框移至桌面 (DismantleToDesktop) 與同名改名避讓
    # 在柵欄A中放置檔案
    $dismantleFile = Join-Path $fenceDirA "重要會議紀錄.docx"
    [System.IO.File]::WriteAllText($dismantleFile, "會議內容", [System.Text.Encoding]::UTF8)

    # 呼叫重載 DismantleToDesktop(frame, targetDesktopDir)
    $report1 = $serviceType::DismantleToDesktop($frameA, $mockDesktop)
    if (-not $report1.FolderDeleted) { throw "拆框後收納夾應已全空並安全刪除：$($report1.FolderDeletionReason)" }
    if (-not (Test-Path (Join-Path $mockDesktop "重要會議紀錄.docx"))) { throw "檔案應已移至模擬桌面" }
    if (Test-Path $fenceDirA) { throw "全空收納夾應已被刪除" }
    Write-Output "✔ PASS: 拆除柵欄並將內容移至桌面，全空收納夾已安全刪除。"

    # 7. 測試同名檔案避讓（桌面已有同名檔案時自動重新命名為 (1)）
    New-Item -ItemType Directory -Path $fenceDirA -Force | Out-Null
    $dismantleConflict = Join-Path $fenceDirA "重要會議紀錄.docx"
    [System.IO.File]::WriteAllText($dismantleConflict, "第二份會議內容", [System.Text.Encoding]::UTF8)

    $reportConflict = $serviceType::DismantleToDesktop($frameA, $mockDesktop)
    if (-not $reportConflict.FolderDeleted) { throw "同名避讓後收納夾應被刪除" }
    $renamedDest = Join-Path $mockDesktop "重要會議紀錄 (1).docx"
    if (-not (Test-Path $renamedDest)) { throw "同名項目應自動加上編號 (1)，但找不到：$renamedDest" }
    $originalDest = Join-Path $mockDesktop "重要會議紀錄.docx"
    if ([System.IO.File]::ReadAllText($originalDest, [System.Text.Encoding]::UTF8) -ne "會議內容") {
        throw "桌面原有檔案內容絕對不得被覆寫！"
    }
    Write-Output "✔ PASS: 拆框時桌面已有同名項目，自動序號避讓且絕無覆寫。"

    # 8. 測試部分失敗保留現場（檔案被鎖定或防毒攔截）
    New-Item -ItemType Directory -Path $fenceDirA -Force | Out-Null
    $freeItem = Join-Path $fenceDirA "自由檔案.txt"
    $lockedItem = Join-Path $fenceDirA "開啟中公文.docx"
    [System.IO.File]::WriteAllText($freeItem, "自由內文", [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($lockedItem, "鎖定內文", [System.Text.Encoding]::UTF8)

    # 以獨佔檔案串流鎖定檔案
    $lockStream = [System.IO.File]::Open($lockedItem, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $reportLock = $serviceType::DismantleToDesktop($frameA, $mockDesktop)
        if ($reportLock.FolderDeleted) { throw "有鎖定檔案時絕對禁止刪除收納夾！" }
        if ($reportLock.FailedCount -eq 0) { throw "鎖定檔案應回報失敗" }
        if (-not (Test-Path $lockedItem)) { throw "鎖定檔案應安全保留在收納夾內" }
        if (-not (Test-Path (Join-Path $mockDesktop "自由檔案.txt"))) { throw "無阻礙的檔案應已先移至桌面" }
        Write-Output "✔ PASS: 檔案開啟中時安全停止刪夾，未完成檔案現場完整保留。"
    } finally {
        $lockStream.Close()
        $lockStream.Dispose()
    }

    # 解除鎖定後重試拆框
    $reportRetry = $serviceType::DismantleToDesktop($frameA, $mockDesktop)
    if (-not $reportRetry.FolderDeleted) { throw "解除鎖定後重試拆框應成功刪除空夾：$($reportRetry.FolderDeletionReason)" }
    if (-not (Test-Path (Join-Path $mockDesktop "開啟中公文.docx"))) { throw "解除鎖定後該公文應已成功移至桌面" }
    # 確保自由檔案未被二次產生 (1) 副本
    if (Test-Path (Join-Path $mockDesktop "自由檔案 (1).txt")) { throw "重試拆框時已完成的檔案不應再次產生副本！" }
    Write-Output "✔ PASS: 解除鎖定後重試拆框全數完成，無重複副本，收納夾乾淨刪除。"

    # 9. 測試收納框內右鍵「新增資料夾」與同名自動序號避讓
    $newFolder1 = $serviceType::CreateNewFolder($fenceDirB, "新增資料夾")
    if (-not (Test-Path $newFolder1 -PathType Container)) { throw "應成功建立新增資料夾：$newFolder1" }
    $suggestedNext = $serviceType::GetSuggestedNewFolderName($fenceDirB, "新增資料夾")
    if ($suggestedNext -ne "新增資料夾 (1)") { throw "預期下一個預設名稱為 '新增資料夾 (1)'，實際: $suggestedNext" }
    $newFolder2 = $serviceType::CreateNewFolder($fenceDirB, "新增資料夾")
    if ([System.IO.Path]::GetFileName($newFolder2) -ne "新增資料夾 (1)") { throw "同名資料夾應自動避讓為 '新增資料夾 (1)'，實際: $newFolder2" }
    Write-Output "✔ PASS: 收納框內新增資料夾與同名序號避讓正常運作。"

    Write-Output "=========================================="
    Write-Output "全部專用收納柵欄合成測試 100% 通過！"
    Write-Output "=========================================="
}
finally {
    if (Test-Path $sandboxDir) {
        Remove-Item -Path $sandboxDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
