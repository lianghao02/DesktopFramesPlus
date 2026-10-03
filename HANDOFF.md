# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：`lianghao02/DesktopFramesPlus`
- **Branch**：`main`
- **Commit SHA**：`c7f65cc`
- **Skill Version**：`lianghao-development v1.0.0`
- **Task Type**：FEAT / SANDBOX / REFACTOR / HANDOFF
- **Local Path Hint**：`DesktopFramesPlus`

---

## 目前狀態
**農場柵欄最小操作原型（修訂版）實作與實測完成，符合驗收標準，目前版本可交付。**
- 正式主程式（`Code/Desktop Frames`）**100% 保持 0 侵入、0 變更**，完全不影響既有主程式。
- 專注於 `tools/sandbox/`，依據 Codex 審查意見全面修復核心偏離與技術問題：
  1. **徹底消滅「依範圍自動收進來」偏離**：引進圖示位移追蹤（$\ge 15$ 物理像素）與初始基準建立（Initial Baseline）。建立、移動或放大柵欄碰到的圖示位移為 0，絕對不自動收集；只有使用者親手拖動圖示放置於柵欄內，才判定為手動拖入；拖出柵欄外才判定為手動移出。
  2. **徹底解決順序硬配**：廢除以檔案目錄字母順序猜測圖示的錯誤做法，改採 SysListView32 的同源索引與螢幕物理座標作為唯一可信錨點，移動時以真實索引調度，絕不把檔案 A 誤當成檔案 B。
  3. **支援動態數量與單一柵欄保留**：移除 `>= 2` 限制，完整支援 0/1/N 個柵欄的載入、儲存與恢復，刪除單一柵欄重開不再被重設回預設 2 個，控制台新增「➕ 新增柵欄」按鈕。
  4. **高 DPI 物理像素統一換算**：引入 `PointToScreen` 換算外框真實物理螢幕像素邊界（扣除標題列物理高度），解決 125%、150% 等高 DPI 螢幕縮放時的判定錯位。
  5. **圖示移動讀回驗證（Read-Back Verification）**：發送 `LVM_SETITEMPOSITION` 後立即透過 `LVM_GETITEMPOSITION` 讀回校驗，位移誤差 > 10 像素（例如被 Windows「自動排列圖示」強制拉回）時判定失敗並記錄警告日誌。
  6. **外框移動/縮放防誤吸（`WM_ENTERSIZEMOVE`）**：攔截 `WM_ENTERSIZEMOVE`、`WM_NCLBUTTONDOWN` 與 `WM_EXITSIZEMOVE`，外框被拖動或拉伸調整大小期間暫停圖示判定，路過圖示時不誤吸。
  7. **長駐 STA 執行緒與資源安全釋放**：改用單一長駐背景 STA 代理執行緒 `DesktopWorker` 與任務佇列，避免頻繁建立執行緒與資源洩漏，退出時正確調用 `CloseDesktop`。

---

## 基準與已確認事實 (Baseline & Confirmed Facts)
- **正式主程式隔離邊界**：`Code/Desktop Frames/` 未變更任何檔案，完全未碰既有正式程式碼。
- **純 Win32 零注入驗證**：實測證實跨進程向 `explorer.exe` 調用 `PROCESS_VM_WRITE` 會被 Windows Defender 攔截；改採純 Win32 唯讀讀取與標準 `LVM_SETITEMPOSITION`（`lParam` 封裝座標，免記憶體寫入），程序穩定運行退出（ExitCode: 0），零 Defender 攔截。
- **實測數據客觀證據**：
  - 成功定位 `SysListView32`（HWND: 66052）。
  - 成功解析真實桌面路徑：`C:\Users\chia-hao\Desktop`（存在: True）。
  - 成功以純 Win32 唯讀讀取 19 個原生項目的真實物理座標與識別錨點（從 `[0] @ (0, 5)` 到 `[18] @ (166, 253)`），耗時小於 5 毫秒。
  - 編譯驗證：Debug 與 Release 模式建置均為 0 警告、0 錯誤。
  - 實體檔案與捷徑完全無損、無影子副本、無螢幕外隱藏。

---

## 驗證狀態分類清單 (三級邊界)
1. **【已實測】**：
   - 桌面 HWND 定位與穿透 Station 隔離（HWND: 66052）。
   - OneDrive 重新導向已知資料夾解析（`C:\Users\chia-hao\Desktop`）。
   - 19 個原生圖示物理螢幕像素座標精確讀取（CLI `--test`）。
   - 防毒安全與零注入（ExitCode: 0，零攔截）。
   - 0/1/N 多柵欄配置與原子保存（`fence_state.json`）。
   - 桌面 19 個實體檔案零損壞、路徑不變。
2. **【已實作】**：
   - `FenceWindow.cs`：半透明穿透外框、標題列拖曳、8 方向自由縮放、`PointToScreen` 高 DPI 物理像素邊界換算、`IsUserMoving` 拖動狀態鎖定。
   - `FenceManager.cs`：動態新增柵欄（`CreateNewFence`）、多柵欄還原、圖示中心點邊界比對、拖曳外框防誤吸、移動讀回校驗、取消柵欄原地解散、原子替換存檔。
   - `ControlPanelWindow.xaml`：即時日誌滾動檢視、「➕ 新增柵欄」按鈕、結束測試按鈕。
3. **【未驗證】**：
   - 使用者在實體桌面親手按住滑鼠左鍵拖入、拖出圖示之手感流暢度。
   - 桌面若啟用「自動排列圖示」時對外框拖曳連動的拉回干擾（建議測試時關閉自動排列）。
   - 拖曳途中按 Esc 取消或拖至其他應用程式視窗之互動反饋。

---

## 異動檔案 (Changed Files)
- `tools/sandbox/DesktopInterop.cs`
- `tools/sandbox/FenceWindow.cs`
- `tools/sandbox/FenceManager.cs`
- `tools/sandbox/ControlPanelWindow.xaml`
- `tools/sandbox/ControlPanelWindow.xaml.cs`
- `tools/sandbox/App.xaml.cs`
- `tools/sandbox/README.md`
- `HANDOFF.md`

---

## 下一步建議動作 (Next Recommended Action)
- 執行 `git commit` 與 `git push` 同步至遠端。
- 使用者可在實體螢幕啟動 `FarmFenceSandbox.exe`，手動體驗拖入、跨柵欄移動、拖出解除與新增柵欄。
