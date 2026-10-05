# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Task Type：CONVERGENCE / ARCHITECTURE_SIMPLIFICATION / ZERO-TAMPER / HANDOFF
- Version：`v2.9.3-zh-TW`
- Commit SHA 基線：`e5837e3`
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

4. **獨立桌面便箋保留完好**：
   - 獨立便利貼視窗引擎（`Desktop_Frames.Notes`）保持完整獨立，保留微軟正黑體、色彩標籤、置頂、鎖定與自動存檔。

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
