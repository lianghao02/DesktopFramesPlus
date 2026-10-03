# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Commit SHA：本輪基準 `886fa85`；已完成「解除隱藏失靈」之徹底修復與全流程整合驗證。
- Task Type：FEAT / BUGFIX / REFACTOR
- Local Path Hint：`16_DesktopFramesPlus`

## 目前狀態
可交付／已完成核心實作、全量自動化整合驗證與 MSBuild Release 編譯，待使用者人工啟動測試驗收。
已徹底解決「檔案移回桌面後仍為隱藏狀態」與「關閉專案還原 / 開啟專案自動收納」之生命週期契約，實現「動物只有一隻」所有權互斥模型，且 100% 沿用原本的 WPF 實心面板（保留分頁 Tabs、捲動 ScrollViewer、收合 Roll-up、外觀樣式、Notes）。

## 本輪目標
1. 診斷並徹底修復「檔案移回桌面時依然維持隱藏狀態」之四大根因：
   - 資料夾屬性設定使用 `DirectoryInfo.Attributes` 易受快取或權限限制影響，改為雙重防線（.NET `File.SetAttributes` + Win32 Kernel32 原生 API `SetFileAttributes`）。
   - 屬性去除 `Hidden` 後若值為 0，必須保底帶 `FileAttributes.Normal`（檔案）或 `FileAttributes.Directory`（目錄）。
   - 右鍵「移回桌面」傳入之捷徑路徑與庫存原路徑不匹配問題：導入多維度解析（解析捷徑 TargetPath 反查桌面原路徑、標準化路徑比對、DisplayName 比對、以及 Fallback 兜底解除隱藏）。
   - 舊版 LocalAppData 庫存自動遷移與合併，防止先前已接管項目成為孤兒。
2. 完善「關閉專案還原桌面、開啟專案重新收納」之生命週期契約：
   - 關閉專案時（`OnExit`）將所有柵欄內的實體檔案強力解除隱藏、捷徑搬回桌面。
   - 開啟專案時（`Startup`）自動重新隱藏並收納回對應柵欄。
3. 桌面 Shell 即時刷新通知強化：
   - 新增 `SHCNE_UPDATEDIR` 針對使用者桌面與公用桌面路徑，並以 `SHCNE_UPDATEITEM` / `SHCNE_ATTRIBUTES` 精準刷新特定檔案，讓 Windows Explorer 桌面立即恢復正常可見圖示，不再殘留半透明外觀。
4. 獨立急救工具與自動化測試腳本同步升級：
   - `tools/rescue/EmergencyRescue.ps1` 升級為保底屬性解除與 Shell 雙路徑目錄刷新。
   - `tools/rescue/Test-FenceInventory.ps1` 擴增至 18 項斷言（包含實體目錄資料夾隱藏與解除隱藏全量驗證）。

## 已完成
1. **解除隱藏與屬性防禦強化（`FenceInventoryManager.cs`）**：
   - 實作靜態方法 `RemoveHiddenAttribute(string path, bool isDirectory)`：
     - 若為目錄：透過 `File.GetAttributes` 去除 Hidden，若為 0 保底帶 `Directory`，並調用 Win32 Kernel32 `SetFileAttributes` 強制寫入 NTFS。
     - 若為檔案：透過 `File.GetAttributes` 去除 Hidden，若為 0 保底帶 `Normal`，並調用 Win32 Kernel32 `SetFileAttributes` 強制寫入 NTFS。
   - 實作靜態方法 `AddHiddenAttribute(string path, bool isDirectory)`：以相同雙重防線確保可靠設置 Hidden。
   - `ReleaseItemToDesktop` 與 `SuspendAllItemsToDesktop` 統一採用上述方法，並主動清理 `Shortcuts/` 目錄下的面板捷徑檔案。
2. **多維度反查與 Fallback 兜底（`ReleaseItemByPath`）**：
   - 優先度 1：傳入 `.lnk` 捷徑時，透過 `FilePathUtilities.GetShortcutTargetUnicodeSafe` 解析 TargetPath 反查 `OriginalFullPath`。
   - 優先度 2：標準化完整路徑與原字串比對（包含 `ManagedStoragePath` 與 `OriginalFullPath`）。
   - 優先度 3：檔名與 `DisplayName` 模糊比對。
   - 優先度 4（Fallback 兜底防護）：若庫存無記錄但傳入捷徑指向桌面實體檔案，仍強力拔除其 Hidden 屬性並刪除面板捷徑，確保 100% 絕不殘留隱藏狀態。
3. **舊版庫存自動遷移（`MigrateLegacyLocalAppDataInventory`）**：
   - 啟動載入庫存時，自動偵測並遷移 `%LOCALAPPDATA%\DesktopFramesPlus\Inventory\fence_inventory.json`，將先前 7 個測試項目無縫合併進自帶目錄清單，並將舊檔標記為 `.migrated`。
4. **即時 Shell 刷新通知（`RefreshDesktopShell` & `RefreshDesktopItem`）**：
   - 呼叫 Win32 `SHChangeNotify` 發送 `SHCNE_UPDATEDIR` 刷新 User Desktop 與 Common Desktop，發送 `SHCNE_UPDATEITEM` / `SHCNE_ATTRIBUTES` 刷新受影響檔案，並搭配 `SHCNE_ASSOCCHANGED` 與 `SHCNF_FLUSH`，使 Windows 桌面圖示瞬間重繪。
5. **FrameManager 同步路徑更新**：
   - 拖曳實體檔案建立捷徑後，呼叫 `UpdateItemStoragePath` 即時將生成的捷徑相對路徑寫入庫存 WAL。
6. **建置與測試全數通過**：
   - MSBuild Release 建置成功（Exit code 0，0 個錯誤）。已產出最新 `Desktop Frames.exe`。
   - `Test-FenceInventory.ps1` 涵蓋 7 大測試情境，18 項斷言全數 PASS（0 失敗）。

## 刻意未修改
- 未修改既有 `options.json`、`frames.json` 核心儲存架構，確保向下相容性與免安裝可攜性。
- 未竄改 Windows 檔案總管全域「顯示隱藏檔案」註冊表設定，尊重作業系統原生機制。

## 尚未完成
- 邀請使用者啟動程式進行端到端人工操作驗收。

## 驗證結果
### 已執行
1. **正式 MSBuild 建置**：
   - 指令：`MSBuild.exe "Code\Desktop Frames\Desktop Frames.csproj" /p:Configuration=Release /t:Rebuild`
   - 結果：建置成功，0 個錯誤（Exit code 0）。產出最新 `Desktop Frames.exe`（2026/10/3 下午 09:47:56）。
2. **自動化整合驗證腳本**：
   - 指令：`pwsh.exe tools\rescue\Test-FenceInventory.ps1`
   - 結果：18 項斷言全數 PASS，0 項 FAIL（Exit code 0）。
     - TEST 1 實體檔案原地隱藏接管：PASS
     - TEST 2 捷徑檔案託管搬移接管：PASS
     - TEST 3 WAL 庫存結構與落盤：PASS
     - TEST 4 同名防覆寫保護測試：PASS
     - TEST 5 急救操作冪等性：PASS
     - TEST 6 關閉還原桌面與重啟自動收納生命週期測試：PASS
     - TEST 7 實體資料夾目錄原地隱藏與解除隱藏測試：PASS

### 尚未驗證
- 真實桌面環境下，使用者手動在面板上右鍵點選「移回桌面」的視覺操作驗收。

### 已知風險
- 若使用者在 Windows 檔案總管中勾選了「顯示隱藏的檔案、資料夾及磁碟機」，接管期間（處於柵欄中時）實體檔案在桌面上會呈現半透明圖示；移回桌面或關閉專案後會立即恢復為 100% 正常鮮豔圖示。

## Git 狀態
- Commit：未提交
- Push：否
- Working Tree：Modified
- Branch：main

## 下一步
1. 邀請使用者啟動程式（`Desktop Frames.exe`）進行人工測試：
   - 拖曳實體檔案／資料夾至面板中（確認桌面外圍隱藏／面板內可見）。
   - 對面板內的圖示按右鍵選「移回桌面 (還原圖示)」（確認桌面圖示立刻完全恢復，非隱藏狀態）。
   - 關閉專案（確認柵欄內的檔案全部還原回桌面可見）。
   - 重新啟動專案（確認檔案自動重新收納進柵欄，桌面再度乾淨）。
