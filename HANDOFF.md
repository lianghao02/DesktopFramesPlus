# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：46b322b4（完整基準見中央 baseline.json，本輪未提交）
- **Skill Version**：v1.0.0
- **Task Type**：IMPROVE
- **Local Path Hint**：DesktopFramesPlus

---

## 目前狀態
本輪目錄整理完成；以下待驗證事項維持。舊交接原文保留於下方，屬歷史，不代表本輪 Git 或測試狀態。

## 本輪目標
依已授權目錄配置與舊產物清理要求，保留現有功能。

## 基準與已確認事實 (Baseline & Confirmed Facts)
中央 docs/project-layout/baseline.json、operations.json 保存本輪基準，HEAD/分支保持，前輪功能成果繼承。

## 已完成 (Completed)
2026-10-05 README 文件更新：補齊專案概念、開發原因、典型流程、已知 Bug／限制及回報方式，並依實際入口校正必要操作說明。本次沒有修改產品程式、環境或個人資料，未 Commit／Push；前輪成果與既有待辦繼承。文件檢核與逐案索引由控制中心 docs/readme-refresh/RESULTS.md 彙整，不代表本次重新驗收全部功能。

刪除 v2.8.1/v2.9.0 舊 ZIP 及 Debug 產物；維持 upstream Code/Images/tools/docs、v2.9.4、Release 成品與 Zero-Tamper 安全邊界。C# 與使用者配置未改。

## 異動檔案 (Changed Files)
上述明確項目與本交接；詳細清冊見中央 docs/project-layout/RESULTS.md。

## 刻意未修改 (Do Not Do / Deliberately Omitted)
業務演算法、現行環境、模型、有效測試素材及使用者原始資料未動；不覆寫未知修改，舊交接內容完整保留。

## 尚未完成 (Remaining Work)
- **P1 (阻斷/必須)**：無本輪整理阻斷。
- **P2 (重要/當次)**：無本輪未完成事項；使用者面板/便箋/設定與桌面資料未清理。
- **P3 (改善建議/暫緩)**：未因整理擴大重構；正式發布另依 release-gate 驗證。

## 驗證結果 (Validation)
### 已執行測試與結果
現行 Release 啟動 EXE 保持，Git 邊界核對；未重新建置或重跑原生功能驗收。COM 專案仍須使用 Visual Studio MSBuild，禁止 dotnet build。
### 尚未驗證項目
未重新驗收全部原生功能或其他電腦/Windows 10 發布環境。
### 已知風險 (Known Risks)
保留上述既有待辦與驗證邊界，不把清理宣稱為其修復。

## Git 狀態
- Commit：46b322b4；未 Commit。
- Push：否。
- Working Tree：Modified；包含繼承的未提交成果。
- Branch：main。

## 下一步建議動作 (Next Recommended Action)
本輪停止擴大修改；日後提交前核對工作範圍並另取得授權。

## 發布狀態 (Release Status)
本輪未發布，保留現行成品。

---

## 承接的前輪交接（原文保留，屬歷史）


# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Task Type：CONVERGENCE / ARCHITECTURE_SIMPLIFICATION / ZERO-TAMPER / HANDOFF
- Version：`v2.9.4-zh-TW`
- Commit SHA 基線：`69fd7c1`
- Local Path Hint：`D:\Development\GitHub\DesktopFramesPlus`

---

## 1. 專案修改了什麼（架構演進歷程）

1. **產品邊界收斂為「桌面分類面板＋獨立桌面便箋」**：
   - **停用非核心新建入口**：面板右鍵選單與工作列托盤選單移除「新增實體資料夾柵欄 (Portal)」與「新增嵌入式文字面板 (Note)」，專注於「新增分類面板」、「框選新增分類面板」與「新增桌面便箋」。
   - **框選建立直通 Data 面板**：框選建立對話框確認後，直接呼叫標準 `CreateFrame` 建立純 Data 分類面板，徹底拔除 `FarmFenceHost.Adopt` 呼叫。

2. **徹底拔除背景桌面對帳與啟閉還原（生命週期解耦）**：
   - 於 `App.xaml.cs` 徹底註銷：
     - 開機啟動：`FenceInventoryManager.ReconcileOnStartup()`、`DesktopReconciler.Start()`、`FenceInventoryManager.ResumeSuspendedItemsFromDesktop()`、`FarmFenceHost.Start()`。
     - 程式退出：`DesktopReconciler.Stop()`、`FenceInventoryManager.SuspendAllItemsToDesktop()`、`FarmFenceHost.Stop()`。
   - 主程式開關機成為純粹的「綠色看板」，**完全不碰觸、不掃描、不搬移、不還原桌面檔案**，徹底根除任何背景檔案操作與防毒攔截隱患。

3. **統一桌面分類面板行為（兩階段事務收納＋Zero-Tamper）**：
   - **個人桌面捷徑（`.lnk` / `.url`）**：
     採「兩階段事務收納」：先完整複製原捷徑至 `Shortcuts\`（保留工作目錄、啟動參數與自訂圖示），寫入 `frames.json` 驗證成功後，才清理桌面原捷徑；若過程有任一失敗絕不清理桌面原檔。
   - **桌面實體公文（`.docx` / `.xlsx` / 真資料夾）及非桌面檔案**：
     一律純建立指向原檔之快捷捷徑，**來源原檔 100% 留在原處不動**，絕不搬移、絕不刪除、絕不修改檔案屬性。
   - **公用桌面捷徑**：
     純建立快捷分身，不要求 Windows UAC 系統管理員提權，不跳擾人彈窗。
   - **圖示移除（`Remove`）**：
     僅從面板移除圖示並清理本地快捷複本，**徹底拔除 `ReleaseItemByPath`**，原檔毫髮無傷。
   - **刪除面板（`DeleteFrame`）**：
     僅刪除面板配置，**徹底拔除 `ReleaseFrameItemsToDesktop`**，不執行任何倒回桌面動作，原檔毫髮無傷。
   - **跨面板移動（`Drag & Drop`）**：
     僅轉移項目資料指標，**徹底拔除 `TransferItemOwnership`**。
   - **資料夾點擊**：
     交由 Windows 原生檔案總管（`explorer.exe`）開啟。

4. **托盤右鍵選單大瘦身（依操作頻率重整）**：
   - 僅保留常用核心操作：
     1. 核心建立：新增分類面板、框選新增分類面板…、桌面便箋（新增/顯示/隱藏）
     2. 桌面版面（Profile 切換）
     3. 視窗檢視：顯示隱藏面板、重新載入所有面板
     4. 系統：選項…、關於…、結束
   - 移除托盤次要開關（工作區自動切換、智慧桌面規則、立即整理、定位面板），開關與規則全面收回「選項」視窗。

5. **選項視窗（Options）重整與舊設定退場**：
   - **側邊欄命名標準化**：
     - Tab 1 更名為「外觀與操作」
     - Tab 2 更名為「備份與維護」
     - Tab 6 更名為「診斷」
     - Tab 5（智慧桌面）隱藏側邊欄導航按鈕，避免誤入且防範背景搬檔
   - **一般頁面**：
     - 徹底移除 Portal 鏡像面板設定卡片（D 槽收納路徑等）與公用桌面提權卡片
     - 捲軸開關移至「外觀與操作」
     - 「面板操作與吸附」與「提示聲音」拆為獨立卡片
   - **外觀與操作頁面**：
     - 置頂前置「新便箋預設樣式」卡片，明確加註「僅套用於之後新增的便箋，不變更既有便箋」
     - 自動隱藏與閒置淡出未勾選時，連動停用相關調整拉桿
     - 納入「隱藏面板捲軸」開關
   - **備份與維護頁面**：
     - 標註按鈕為即刻生效（免按下方儲存按鈕）
     - 清除所有資料時詳細列出清除範圍（面板配置、便箋內容、設定等，明確告知不刪除桌面實體檔案）
   - **快捷鍵頁面**：
     - 補充桌面便箋快捷鍵（`Ctrl+Alt+N`）說明
   - **版面優化**：
     - 設定卡片統一設定 `MaxWidth = 780` 與靠左對齊，徹底修復視窗最大化時控制項擠在左側、卡片橫向拉長失衡的問題。

---

## 2. 目前功能狀態（能力矩陣）

| 功能項目 | 桌面分類面板（Data 面板） | 獨立桌面便箋 |
| :--- | :--- | :--- |
| **本質與角色** | 桌面快捷圖示看板，**原檔 100% 安全** | 輕量桌面記事貼 |
| **個人桌面捷徑拖入** | ✅ 兩階段事務收納（桌面乾淨，參數全保） | 不適用 |
| **實體公文/資料夾拖入**| ✅ 建立快捷分身，**原檔 100% 留在桌面** | 不適用 |
| **外部檔案拖入** | ✅ 建立快捷分身，**來源原檔不動** | 不適用 |
| **拖曳重排順序** | ✅ 支援（藍色指示條，自由自訂順序） | 不適用 |
| **跨面板移動** | ✅ 支援（直接拖曳轉移，不留重複） | 不適用 |
| **刪除項目** | ✅ **100% 安全**（只刪除看板圖示，原檔不動） | ✅ 單張便箋刪除 |
| **刪除整個面板** | ✅ **100% 安全**（只關閉看板，原檔毫髮無傷） | ✅ 關閉或刪除 |
| **程式啟閉** | ✅ **零桌面接觸**（不隱藏、不對帳、不倒回） | ✅ 自動保存文字內容 |

---

## 3. 防毒合規與公務機關限制（Zero-Tamper 鐵律）

1. **集中控管防毒（Apex One / CrowdStrike）零誤判規範**：
   - 嚴禁背景批次操作檔案或使用 Win32 隱藏屬性（`FILE_ATTRIBUTE_HIDDEN`）。
   - 程式啟動與退出絕不掃描桌面檔案。
2. **編譯建置環境注意事項**：
   - 專案含 COM Reference（`IWshRuntimeLibrary`），**嚴禁使用 `dotnet build`**（會報 `MSB4803` 錯誤）。
   - **必須使用 Visual Studio 2022 的 MSBuild**：
     ```powershell
     & "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" "Code\Desktop Frames\Desktop Frames.csproj" /p:Configuration=Release
     ```
   - 編譯完成後，需將 `Code\Desktop Frames\bin\Release\net8.0-windows7.0\` 中的 `Desktop Frames.dll` 與 `Desktop Frames.exe` 覆蓋至 `dist\DesktopFramesPlus\`。
3. **在地化規範**：
   - 維持 100% 台灣標準繁體中文（公文、捷徑、資料夾、便箋、檔案總管、重新整理、設定、預設、登入、記憶體、硬碟）。
