# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：fb5c48a32e0f5626e29deafc089d3c35e5940870（本輪提交前基準；最新提交以 Git 記錄為準）
- **Skill Version**：v1.0.0
- **Task Type**：HANDOFF
- **Local Path Hint**：DesktopFramesPlus

---

## 目前狀態
本機 dist 已受控更新為 2.9.4.0 並啟動，代表性功能及資料保護驗證通過；完整舊版回復副本保留。前輪版本修復、目錄整理及文件成果繼承，下方舊交接屬歷史；正式 ZIP 未重新發布。

## 本輪目標
安全套用來源校準後的新版成品，完成代表性 Data 面板／便箋驗收，保留個人配置與回復副本。

## 基準與已確認事實 (Baseline & Confirmed Facts)
原始碼與現有 DLL 均為 2.8.1，但維護交接為 2.9.4，打包預設名稱仍為 2.9.0。本輪基準見中央 artifacts/remaining-fixes-20261006/baseline.json；期間 HEAD 從 46b322b4 前進至 fb5c48a，已確認外部提交只包含 README／HANDOFF，無程式碼，保留其內容繼續修復。

## 已完成 (Completed)
2026-10-06 GitHub 同步交接：使用者已授權提交與推送前輪成果；本輪只提交已核對範圍。最新 Commit SHA、遠端同步與 CI 結果統一見控制中心 `docs/github-sync/RESULTS.md`，不將提交本身的 SHA 寫入同一份提交。

2026-10-06 成品套用：使用者從常駐圖示正常結束舊版後，預覽／備份／套用 22 個程式檔，128 個其他檔案雜湊一致；新版啟動，124 個 Profiles 檔案於啟動前後不變。新增隔離 Data 面板／便箋驗收，13 個斷言通過；新程式保持執行。證據及回復邊界見中央 docs/new-build-acceptance/RESULTS.md。

2026-10-06：Version 校準為 2.9.4，Assembly／FileVersion 為 2.9.4.0；XML 編碼宣告與實體 UTF-8 一致。打包版本由 csproj 取得，CheckOnly 核對標籤與 DLL 版本；入口排除 bin／obj、依 DLL 判斷新舊，動態探索 Visual Studio MSBuild、檢查建置失敗並保護正在執行的程序。獨立建置與回歸結果見中央 docs/remaining-fixes/RESULTS.md。

2026-10-05 README 文件更新：補齊專案概念、開發原因、典型流程、已知 Bug／限制及回報方式，並依實際入口校正必要操作說明。本次沒有修改產品程式、環境或個人資料，未 Commit／Push；前輪成果與既有待辦繼承。文件檢核與逐案索引由控制中心 docs/readme-refresh/RESULTS.md 彙整，不代表本次重新驗收全部功能。

刪除 v2.8.1/v2.9.0 舊 ZIP 及 Debug 產物；維持 upstream Code/Images/tools/docs、v2.9.4、Release 成品與 Zero-Tamper 安全邊界。C# 與使用者配置未改。

## 異動檔案 (Changed Files)
此次新增 tools/acceptance-tests、tools/test-representative-functions.ps1，更新 README／HANDOFF，套用 Git 忽略的 dist 成品。承接的版本修復：Code/Desktop Frames/Desktop Frames.csproj（僅版本／編碼宣告）、tools/run-app.ps1、package-release.ps1、test-release-entry.ps1。前輪整理清冊見中央 docs/project-layout/RESULTS.md。

## 刻意未修改 (Do Not Do / Deliberately Omitted)
業務演算法、現行環境、模型、有效測試素材及使用者原始資料未動；不覆寫未知修改，舊交接內容完整保留。

## 尚未完成 (Remaining Work)
- **P1 (阻斷/必須)**：無本輪整理阻斷。
- **P2 (重要/當次)**：版本來源／入口／打包防護及本機新版套用完成；沒有重新發布現有 ZIP。
- **P3 (改善建議/暫緩)**：未因整理擴大重構；正式發布另依 release-gate 驗證。

## 驗證結果 (Validation)
### 已執行測試與結果
2026-10-06 本機成品套用、13 項隔離功能斷言、真實新版啟動與資料雜湊核對通過；正式發布包及跨電腦仍未驗證。完整結果見中央 docs/new-build-acceptance/RESULTS.md。

此次 Visual Studio MSBuild 獨立 Release 建置通過，Assembly／FileVersion 均為 2.9.4.0；圖示轉移回歸通過、繁中 674 個基準鍵全部涵蓋。入口／版本回歸與資料保護結果見中央修復報告。既有 nullable／未使用欄位警告保留，未因此重構核心模組。
### 尚未驗證項目
未重新驗收全部原生功能或其他電腦/Windows 10 發布環境。
### 已知風險 (Known Risks)
目前本機 dist 已更新並啟動 2.9.4.0；Code 下的舊 Release 與舊 ZIP 保留。13 個合成功能斷言與既有配置啟動不代表完整手動互動或跨電腦驗收。

## Git 狀態
- Commit：上述 SHA 為提交前基準；最新 SHA 見 `git log -1` 與中央同步報告。
- Push：實際推送及遠端核對結果見中央 `docs/github-sync/RESULTS.md`。
- Working Tree：最終狀態見中央同步報告；不含被忽略的環境、成品與使用者資料。
- Branch：main。

## 下一步建議動作 (Next Recommended Action)
日常使用 dist/DesktopFramesPlus/Desktop Frames.exe；Run-Latest.bat 是原始碼開發入口，Code 下的 Profiles 與個人 dist 配置分開。正式 ZIP 更新／發布另依發布門檻，不覆蓋個人 Profiles。

## 發布狀態 (Release Status)
本輪僅套用本機成品；未建立正式 Release 或更新 ZIP。

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
