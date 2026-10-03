# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Commit SHA：`26ae67b`；已完成「新增面板圖示保護、選單語意明確化與刪除面板徹底重構」。
- Task Type：FEAT / BUGFIX / REFACTOR
- Local Path Hint：`16_DesktopFramesPlus`

## 目前狀態
可交付／已完成「新增面板不破壞既有隱藏圖示」、「選單語意明確化」與「面板徹底刪除與右鍵支援」、MSBuild Release 編譯（0 錯誤）、整合測試 18 項全數 PASS。
已徹底解決：
1. 框選建立新面板時呼叫 `ReloadFrames()` 導致關閉現有視窗、刷新桌面圖示並迫使隱藏檔案現形的問題。
2. 新增面板選單選項模糊不清的問題（明確拆分為自訂範圍框選、標準圖示收納、資料夾鏡像、桌面便籤）。
3. 刪除面板只藏不刪的問題（標題列右鍵選單加入刪除選項，核心邏輯以 GUID 精準移除、釋放所屬動物回桌面、清理托盤隱藏追蹤、即時落盤 frames.json）。

## 本輪目標
1. **新增面板圖示隱藏狀態保護**：
   - 修正 `CreateFrameFromDraw`，直接為新面板建立視窗，移除破壞性的 `ReloadFrames()` 調用，防止既有視窗關閉重啟與桌面圖示強制刷新。
   - 在 `App.xaml.cs` 明確設定 `ShutdownMode = ShutdownMode.OnExplicitShutdown`，杜絕任何視窗關閉瞬間誤觸發退出的休眠歸還機制。
2. **選單語意明確化**：
   - 更新中英資源檔：
     - `MenuDrawFrame`: 「滑鼠框選範圍建立面板…」
     - `MenuNewFrame`: 「新建標準圖示面板（收納柵欄）」
     - `MenuNewPortalFrame`: 「新建資料夾鏡像面板（Folder Portal）…」
     - `MenuNewNoteFrame`: 「新建桌面便籤面板（便利貼備忘）」
   - 在心形選單中以 Separator 明確區分「框選繪製操作」與「不同類型面板直接建立」。
3. **刪除面板機制徹底重構**：
   - 在面板標題列與空白處右鍵選單（`CnMnFramemanager`）新增「刪除此面板」（`MenuDeleteThisFrame`）。
   - 抽出統一的 `DeleteFrameWithConfirmation` 方法，刪除時依 GUID 精準自 `FrameData` 移除，安全釋放所屬動物回桌面，同步清理 `_heartTextBlocks`、`_portalFrames` 與 `TrayManager` 隱藏清單，並立即落盤 `SaveFrameData()`。

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
6. **設定頁面螢幕自適應與視窗管理優化（`OptionsFormManager.cs`）**：
   - 多螢幕游標感知：透過 `Screen.FromPoint` 獲取當前螢幕工作區（WorkingArea）。
   - WPF 座標換算與邊界拘束：換算 DPI 裝置獨立像素，寬度設定在 580~800 之間且不超過螢幕可用寬度的 94%，高度設定在 460~780 之間且不超過螢幕可用高度的 90%，解決底部儲存/取消按鈕溢出螢幕無法操作的痛點。
   - 邊框拉伸與最大化切換：開啟 `CanResizeWithGrip`，標題列加入最大化／還原按鈕（🗖 / 🗗），支援雙擊標題列切換全螢幕工作區。
   - 移除 Grid 中多餘的 80px Row 3 空白佔位。
   - 左側 Tab 清單與 Tab 2（Tools）補齊 `ScrollViewer`，各頁籤內容在小螢幕下皆可流暢垂直滾動。
   - `SaveOptions` 導入 `GetTabContentStackPanel` 安全解包，保證相容性與穩定性。
7. **新增面板圖示隱藏狀態保護與選單語意明確化（`FrameManager.cs`, `App.xaml.cs`, `Strings.*.resx`）**：
   - 消除 `CreateFrameFromDraw` 呼叫 `ReloadFrames()` 造成關閉現有視窗與全域 SysListView32 刷新導致隱藏圖示現形的漏洞，改為直接針對新面板呼叫 `CreateFrame`。
   - 設定 `App.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown`，徹底防禦視窗關閉時誤觸發生命週期退出。
   - 將「框選建立面板」與「新建面板」的模糊字串，改為「滑鼠框選範圍建立面板…」、「新建標準圖示面板（收納柵欄）」、「新建資料夾鏡像面板（Folder Portal）…」、「新建桌面便籤面板（便利貼備忘）」，並在選單加入分隔線區隔動作與類型。
8. **刪除面板機制徹底重構（`FrameManager.cs`, `TrayManager.cs`）**：
   - 在面板右鍵選單（`CnMnFramemanager`）新增「刪除此面板」（`MenuDeleteThisFrame`）。
   - 抽出 `DeleteFrameWithConfirmation` 靜態方法，依 GUID 精準從 `FrameData` 清除，安全釋放所屬動物回桌面（`ReleaseFrameItemsToDesktop`），同步清除托盤隱藏清單（`TrayManager.RemoveHiddenFrame`）與內部快取，並立即落盤儲存 `frames.json`，徹底解決過去「只是隱藏、重啟又出現」的問題。

## 刻意未修改
- 未修改既有 `options.json`、`frames.json` 核心儲存架構，確保向下相容性與免安裝可攜性。
- 未竄改 Windows 檔案總管全域「顯示隱藏檔案」註冊表設定，尊重作業系統原生機制。

## 尚未完成
- 邀請使用者啟動程式進行端到端人工操作驗收。

## 驗證結果
### 已執行
1. **正式 MSBuild 建置**：
   - 指令：`MSBuild.exe "Code\Desktop Frames\Desktop Frames.csproj" /p:Configuration=Release`
   - 結果：建置成功，0 個錯誤（Exit code 0）。產出最新 `Desktop Frames.exe`。
2. **自動化整合驗證腳本**：
   - 指令：`pwsh.exe tools\rescue\Test-FenceInventory.ps1`
   - 結果：18 項斷言全數 PASS，0 項 FAIL（Exit code 0）。

### 尚未驗證
- 真實桌面環境下，使用者手動在小螢幕或 125%/150% 縮放螢幕開啟設定頁面進行按鈕點擊與視覺操作驗收。

### 已知風險
- 若使用者在 Windows 檔案總管中勾選了「顯示隱藏的檔案、資料夾及磁碟機」，接管期間（處於柵欄中時）實體檔案在桌面上會呈現半透明圖示；移回桌面或關閉專案後會立即恢復為 100% 正常鮮豔圖示。

## Git 狀態
- Commit：`26ae67b`
- Push：是
- Working Tree：Clean
- Branch：main

## 下一步
1. 邀請使用者啟動程式驗收：
   - 點擊「♥」選單確認「框選」與「新建」語意清晰，且以分隔線分類。
   - 框選或新建面板時，確認原有面板內接管的圖示絕不被被迫顯示。
   - 在面板標題列按右鍵點擊「刪除此面板」，確認面板徹底刪除、檔案安全還原回桌面，且重啟專案不再幽靈復現。
