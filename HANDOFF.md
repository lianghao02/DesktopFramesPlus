# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Commit SHA：本輪基準 `886fa85`；已完成「解除隱藏失靈」之徹底修復與全流程整合驗證。
- Task Type：FEAT / BUGFIX / REFACTOR
- Local Path Hint：`16_DesktopFramesPlus`

## 目前狀態
可交付／已完成「設定頁面適配螢幕大小」與「多螢幕 / 高 DPI 自適應縮放」、MSBuild Release 編譯（0 錯誤）。
已解決小螢幕或 125%/150% 縮放時選項視窗底部被工作列截斷、無法點擊儲存/取消按鈕的問題，並支援雙擊標題列與最大化/還原按鈕、視窗邊框拉伸調整大小、全分頁 ScrollViewer 自動滾動。

## 本輪目標
1. **設定頁面螢幕與 DPI 自適應縮放**：
   - 根據滑鼠當前游標所在螢幕（多螢幕支援），動態取得 `WorkingArea`（扣除工作列之可用範圍）。
   - 透過 Win32 原生 DPI API 轉換為 WPF 裝置獨立像素 (DIU)，按螢幕可用寬高動態設限（寬度最大 800、上限 94% 螢幕寬，高度最大 780、上限 90% 螢幕高），安全置中顯示。
2. **視窗縮放與最大化功能**：
   - 啟用 `ResizeMode.CanResizeWithGrip` 支援邊框拉動調整大小。
   - 標題列新增「最大化／還原」按鈕（🗖 / 🗗），並支援雙擊標題列切換全螢幕工作區最大化與原尺寸還原。
3. **全分頁滾動防截斷**：
   - 左側 Tab 清單外層包裹 `ScrollViewer`，避免在超低垂直解析度下按鈕超高被裁切。
   - Tab 2（Tools）補齊 `ScrollViewer`。
   - `SaveOptions` 導入通用 `GetTabContentStackPanel` 安全解包，保證設定存取 100% 穩定無例外。

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
- Commit：待提交
- Push：待推送
- Working Tree：Modified (`Code/Desktop Frames/OptionsFormManager.cs`, `HANDOFF.md`)
- Branch：main

## 下一步
1. 提交本輪修改並推播至 GitHub：`feat: 設定頁面適配螢幕大小與多螢幕DPI縮放`。
2. 邀請使用者啟動程式（`Desktop Frames.exe`）驗證設定頁面之縮放、最大化與儲存功能。
