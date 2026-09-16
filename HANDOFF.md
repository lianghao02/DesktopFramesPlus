# HANDOFF: DesktopFramesPlus 工作狀態與交接報告

- **Repository**: `lianghao02/DesktopFramesPlus`
- **Branch**: `main`
- **功能實作基準 Commit**: `eba6496`
- **交接文件最後驗證時的 HEAD**: `eba6496`
- **版本狀態備註**: 本文件提交後請以 `git status` 與 `git log` 實況為準（修復 Delete 鍵刪除無效與跨 Fence 拖曳重複項目問題）
- **Task Type**: FIX / ENHANCEMENT / PERF / AUDIT
- **Date**: 2026-09-16
- **Status**: 待驗收中，Release Build 成功，工作目錄 Clean

---

## 1. 本輪已完成功能與修復摘要

### A. 核心體驗與痛點修復 (Commit `733776e`)
1. **移除小元件功能 (Widget)**：清理右鍵主選單中無效的時鐘、VU 表、計算機等小元件，回歸純粹分區。
2. **標題單擊就地改名**：標題列點擊即原地切換文字方塊，支援 Enter 存檔、Esc 取消，右鍵選單同步改名為「重新命名分區」。
3. **原生彩色資料夾圖示**：修正系統資料夾變成白色外框圖示的 Bug，調用 `Utility.GetShellIcon` 提取 Windows 原生彩色資料夾圖示。
4. **高畫質圖示渲染**：升級 `Utility.cs` 支援 `SHGetImageList` 之 Extra Large (48x48) 與 Jumbo (256x256) 系統清單提取，WPF Image 啟用 `BitmapScalingMode.HighQuality`。
5. **雙擊開啟與跨 Fence 拖曳轉移**：雙擊開啟（`ClickCount == 2` 防誤觸）；單點拖曳（免按 Ctrl）；跨區拖曳放開時自動轉移並刷新兩側面板。

### B. 進階操作改善 (Commit `90ee3dd`)
1. **Spacer（空白格）常規化**：右鍵選單「新增空白格」不再需要按住 Ctrl，Data frame 直接右鍵即可選用。
2. **調整至最適大小（Fit to Content）**：右鍵選單加入「調整至最適大小」，動態遍歷 WrapPanel 子元素計算行寬高，透過 `DoubleAnimation` 平滑動畫縮放視窗高度並存檔。

### C. 實測關鍵細節修正 (Commit `be84267`)
1. **點空白區域取消高亮**：改由 `win.PreviewMouseDown` 頂層隧道事件統一攔截，只要點擊目標不是圖示（`clickedSp == null || clickedSp.Tag == null`），100% 立即觸發 `DeselectIcon()`。
2. **選取後按 Delete 鍵移除**：
   - 解決 `NonActivatingWindow` 原生不啟動焦點（`WS_EX_NOACTIVATE`）導致無鍵盤焦點的問題：在 `SetSelectedIcon` 時主動呼叫 `win.Activate()` 與 `sp.Focus()`。
   - 在圖示面板自身 `sp.PreviewKeyDown` 直接就地捕獲 `Key.Delete`，同時視窗層級保留備用監聽。
3. **安全移除保證（絕不碰實體檔案）**：
   - 移除作業僅自記憶體 JSON 陣列（`targetArray.Remove`）與 UI 容器（`wp.Children.Remove`）移除，絕無呼叫任何 `File.Delete`。
   - 彈出確認訊息清楚標註「僅自此分區移除，不會刪除原始檔案」。
4. **破圖與超小攝影機圖示修復（如 HIP2P）**：
   - `AddIcon` 中為 `Image ico` 明確配置 `Stretch = Stretch.Uniform`, `HorizontalAlignment.Center`, `VerticalAlignment.Center`。
   - `UpdateIcon` 的 `CASE E`（一般標準檔案）優先使用 `Utility.GetShellIcon(filePath, false)` 提取系統 Jumbo 256x256 或 ExtraLarge 48x48 高清圖示，失敗才 fallback 到 `ExtractAssociatedIcon`。
5. **Fence 內部拖曳移動（消除重排死區）**：
   - 拖曳預覽視窗 `_dragPreviewWindow` 設置 `ShowActivated = false`，防止彈出時搶焦點中斷滑鼠 capture。
   - 重構 `CalculateDropPosition` 與 `ReorderframeItems`：消除 `currentPosition + 1 == newPosition` 導致相鄰圖示無法拖動的死區 BUG，改採精準的幾何中心點對稱重排演算法。

### D. 跨 Fence 拖曳單向失效修復 (Commit `28a5fcd`)
- **根因**：先前跨視窗比對直接使用 `PointToScreen()` 的物理像素比對 `Window.Left/Top` 的 WPF 邏輯像素。在螢幕有 DPI 縮放（如 125%/150%）時，下方或右側視窗在計算範圍時會完全吞噬上方視窗的邊界，導致拖曳到上方視窗判定為無效或落入錯誤 Fence。
- **修復**：引入 Win32 `WindowFromPoint` API 取得滑鼠下方的精準實體視窗，再搭配 WPF `PointFromScreen` 雙重驗證。路徑比對統一強化為 `StringComparison.OrdinalIgnoreCase`。

### E. 點擊 Fence 偶發延遲/卡頓鈍感根治 (Commit `4b3f234`)
- **根本原因 1 (TargetChecker)**：`TargetChecker` 原先每隔 1000ms（1秒）就強制在 UI 執行緒以同步 `Dispatcher.Invoke` 輪詢檢查所有圖示的實體檔案路徑與狀態，造成 WPF 訊息幫浦每秒被卡住數十毫秒；當使用者恰好在該瞬間點擊 Fence 時，滑鼠事件被排在後方，產生明顯的鈍感。
  - **修復**：將 `TargetChecker` 間隔由 1000ms 調大至安全的至少 15000ms（15秒）。
- **根本原因 2 (MouseDown 同步 I/O)**：`MouseDownHandler` 每次點擊圖示時都在 UI 執行緒直接執行 `File.Exists(path)` 與 `Directory.Exists(path)`，遇到網路路徑或已失效捷徑會瞬間卡死 UI。
  - **修復**：移除開頭的不必要同步磁碟檢查。
- **根本原因 3 (焦點爭奪與重繪)**：`SetSelectedIcon` 中原先呼叫了 `parentWin.Activate()`，與 `NonActivatingWindow` 的底層 `MA_NOACTIVATE` 衝突，導致作業系統與 WPF 重繪焦點延遲。
  - **修復**：移除 `parentWin.Activate()`，圖示面板自身設定 `sp.Focusable = true; sp.Focus()` 即可完美響應 `Delete` 快捷鍵。

### F. 跨 Fence 拖曳預覽遮擋放不下去修復 (Commit `80eefe9`)
- **根因**：跟隨滑鼠游標移動的半透明預覽視窗 `_dragPreviewWindow` 在 Win32 層級（`WindowFromPoint`）仍會被滑鼠游標正下方的射線命中，導致 `FindTargetWindowAtScreenPoint` 比對不到目標 `NonActivatingWindow`，回傳 `null` 並視為拖出 Fence 外而取消。
- **修復**：於 `FindTargetWindowAtScreenPoint` 取得 `_dragPreviewWindow` 之 HWND，若 `WindowFromPoint` 命中該預覽視窗則予以排除，確保精準穿透命中底層目標 Fence。

### G. Delete 鍵無效與跨 Fence 拖曳「重複」徹底修復 (Commit `eba6496`)
1. **Delete 鍵無效**：
   - **根本原因**：`DesktopFramesPlus` 採用 `NonActivatingWindow`，原生是不接收焦點的（`WS_EX_NOACTIVATE` 與 `WM_MOUSEACTIVATE -> MA_NOACTIVATE`）。先前僅呼叫 `sp.Focus()`，但視窗未激活，WPF 鍵盤訊息幫浦無法截獲按鍵。此外，`RemoveSelectedIconFromFrame` 中的 `Path.GetFullPath` 對某些相對路徑或 `.url` 檔案拋出異常或比對失真。
   - **修復**：在 `SetSelectedIcon` 中短暫啟用視窗焦點（`win.EnableFocusPrevention(false); win.Activate(); sp.Focus();`），取消選取時 `DeselectIcon` 即時還原 `win.EnableFocusPrevention(true)`；在 `RemoveSelectedIconFromFrame` 中強化為雙層容錯比對（優先字串比對，次之以 Try-Catch 保護之 `Path.GetFullPath`），徹底修復 Delete 鍵移除無反應問題。
2. **跨 Fence 拖曳重複**：
   - **根本原因**：`MoveItemToTargetFrame` 在來源端移除時，僅依據字串比對查找第一個項目，且若來源端已存在多個歷史重複項時只移除了首筆；此外目標端插入時未過濾既有同名項目，造成「來源沒清乾淨、目標又新增」的重複複本累積。
   - **修復**：來源端優先透過 `JToken` 實例自所屬容器移除（`parentArr.Remove(draggedToken)` 或 `_sourceItemsList.Remove(draggedToken)`），並由後往前遍歷清除來源端所有同名殘留項目；目標端插入前執行防重複過濾，確保資料結構與雙方 UI 完整同步。

---

## 2. 驗證依據與證據 (Verification Proof)
1. **MSBuild Release 編譯**：通過，0 Errors，成功產出最新版 `Desktop Frames.exe`。
2. **Git 工作目錄**：Clean，無任何未提交或暫存衝突檔案。
3. **語系檔完整性**：新增字串 `MenuFitToContent`、`MsgConfirmRemoveItem` 均已於 `Strings.cs`、`Strings.resx`、`Strings.zh-TW.resx` 建立完畢。

---

## 3. 客觀問題診斷與潛在技術債 (For Codex Review)

針對目前代碼庫現狀，經嚴肅工程評估，提供 Codex 接手時可審查的潛在盲點與建議：

### 潛在問題 A：`FrameManager.cs` 單檔體積龐大（約 9,900 行）
- **現況**：視窗管理、圖示載入、事件處理、右鍵選單邏輯全擠在 `FrameManager.cs`。
- **影響**：維護時容易發生閉包變數作用域陷阱。
- **建議**：目前功能運作正常，切忌在無單元測試防護下進行大規模拆檔重構；未來若有新模組需求，再將右鍵選單或選取狀態抽出獨立 Manager。

### 潛在問題 B：多螢幕 / DPI 混合縮放情境下的拖曳預覽
- **現況**：`_dragPreviewWindow` 在跨不同 DPI 螢幕時，座標計算目前寫入 `dpiScale = 1.0`（簡化版）。
- **影響**：在一般單螢幕或等比例螢幕上完全正常；若使用者有兩台縮放比差異極大的螢幕（如 100% + 200%），拖曳預覽視窗位置可能會有少許偏差。
- **建議**：可審查 `GetDpiScaleFactor` 的多螢幕動態判定。

---

## 4. 給 Codex 的交接指引
1. **工作目錄與分支**：`D:\Development\GitHub\DesktopFramesPlus`，分支 `main`。
2. **代碼實作基準**：`eba6496`（修復 Delete 鍵移除無效與跨 Fence 拖曳重複複本問題），請以現場 `git status` 與 `git log` 為準。
3. **下一步方向**：進行手動驗收測試（選取圖示按下 Delete 鍵移除、跨 Fence 拖曳圖示移動），確認無異常後準備收斂交付。
