# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：`lianghao02/DesktopFramesPlus`
- **Branch**：`main`
- **Commit SHA**：`56a109f`
- **Skill Version**：`lianghao-development v1.0.0`
- **Task Type**：FEAT / SANDBOX / HANDOFF
- **Local Path Hint**：`DesktopFramesPlus`

---

## 目前狀態
**沙盒原型可交付與實測通過**。
- 正式主程式（`Code/Desktop Frames`）100% 保持不動、完整保留既有功能。
- 在 `tools/sandbox/` 獨立完成「農場柵欄（原生桌面圖示 ＋ 穿透外框）」最小操作原型。
- 解決了背景終端桌面 Session 隔離、OneDrive 桌面解析、原生 SysListView32 座標讀取，並突破了 Windows Defender 攔截跨進程寫入 Explorer 的限制（改用純 Win32 唯讀讀取 ＋ 標準 `LVM_SETITEMPOSITION` 零注入架構）。
- 成功實測全部 19 個原生項目的真實路徑與物理座標解析、雙柵欄顯示與初始命中、異常退出狀態固化與檔案零破壞。

## 本輪目標
依照修訂指示收斂農場柵欄原型為手動操作：
1. 建立多個柵欄，可調整位置與大小。
2. 使用者自行拖入、跨柵欄移動或拖出圖示回到一般桌面。
3. 取消柵欄時安全解除歸屬，原圖示恢復一般桌面顯示，原檔與捷徑路徑 100% 不變，不複製、不刪除、不移至螢幕外。
4. 退出或重啟程式記住柵欄位置與歸屬。

## 基準與已確認事實 (Baseline & Confirmed Facts)
- **正式主程式隔離防線**：`Code/Desktop Frames` 未變更任何檔案，完全避免破壞正式功能。
- **Defender 攔截驗證**：實測證實若以 `WriteProcessMemory` 寫入 `explorer.exe` 會被 Windows Defender 終止；改用標準 `LVM_SETITEMPOSITION` 與純 Win32 唯讀後進程正常退出（ExitCode: 0），零攔截。
- **實測數據客觀證據**：
  - 成功定位 `SysListView32`（HWND: 66052）。
  - 成功解析 OneDrive 桌面：`C:\Users\chia-hao\Desktop`。
  - 成功精準讀出 19 個原生項目與物理座標（如 `0101.csv @ (0, 5)`、`憑證 @ (166, 129)`、`掛載NAS-xinhua.bat @ (166, 253)` 等）。
  - 啟動雙柵欄即時命中落在柵欄 A 內的兩個項目並固化至 `fence_state.json`。
  - 強制結束進程後配置完好，實體檔案 100% 完整。

## 驗證狀態分類清單 (三級邊界)
1. **【已實測】**：
   - 桌面 HWND 定位與穿透切換至 `WinSta0\Default`。
   - OneDrive 重新導向已知資料夾解析。
   - 19 個原生圖示物理座標與清單讀取（CLI `--test`）。
   - 防毒安全與零注入驗證。
   - 雙柵欄建立與初始命中。
   - 異常強制終止狀態持久化（`fence_state.json`）。
   - 桌面 19 個實體檔案零損壞與無影子副本。
2. **【已實作】**：
   - `FenceWindow.cs`：半透明外框、標題列、計數標籤、✕ 按鈕、`WM_NCHITTEST` 內部穿透到底層桌面原生圖示。
   - `FenceManager.cs`：空間邊界矩形比對判定、標題列拖曳連動內部原生圖示位移、取消柵欄原地解除。
   - `ControlPanelWindow.xaml`：即時分組日誌顯示。
3. **【未驗證】**：
   - 使用者在實體螢幕上手動拖曳手感。
   - 系統若開啟「自動排列圖示」時與連動位移的衝突處理。
   - 拖曳途中 Esc 取消或拖至非桌面視窗的邊界反饋。

## 異動檔案 (Changed Files)
- `tools/sandbox/FarmFenceSandbox.csproj`
- `tools/sandbox/App.xaml`
- `tools/sandbox/App.xaml.cs`
- `tools/sandbox/ControlPanelWindow.xaml`
- `tools/sandbox/ControlPanelWindow.xaml.cs`
- `tools/sandbox/DesktopInterop.cs`
- `tools/sandbox/FenceManager.cs`
- `tools/sandbox/FenceWindow.cs`
- `tools/sandbox/README.md`
- `HANDOFF.md`

## 下一步建議動作 (Next Recommended Action)
- 使用者可在實體螢幕執行 `FarmFenceSandbox.exe` 親手體驗拖曳手感與回饋。
