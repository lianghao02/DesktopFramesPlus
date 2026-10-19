# 專用收納柵欄（Managed Storage Fence）實作計畫

## 目標與驗收條件
1. **一框一專用資料夾**：新建柵欄時自動建立專屬實體收納夾（預設路徑：實際桌面目錄下的 `桌面\DesktopFrames_收納區\<柵欄名稱>`，亦允許自選位置），於 `frames.json` 寫入 `ItemsType = "Portal"`、`IsManagedStorage = true`、`StorageRootPath`。
2. **拖入即收納（含手勢即時視覺回饋）**：
   - 拖曳懸停（`DragOver`）於框頂顯示半透明操作膠囊：
     - 預設（無修飾鍵）：`📥 搬移至收納柵欄`（公用桌面來源自動顯示 `📄 從公用桌面複製捷徑`）
     - 按住 `Ctrl`：`📄 複製至收納柵欄`
     - 按住 `Alt`：`🔗 在收納柵欄建立捷徑`
   - 同框內拖放判定為同目錄 No-op，嚴禁自我複製或重複產生 `(1)` 副本。
   - 支援從專用收納柵欄拖出至其他收納柵欄（跨框移動）或拖回檔案總管／桌面。
3. **以實際目錄為準**：除既有 `FileSystemWatcher` 事件與溢位重掃外，在啟動、拖放完成及選單「重新整理內容」提供確定性目錄重掃（純讀取，不觸發背景搬檔）。
4. **專用柵欄刪除走資源回收筒**：專用收納夾內項目刪除強制使用資源回收筒，不提供永久刪除。
5. **拆除並將內容移至桌面（安全煞車機制）**：
   - 使用者明確確認後逐項移至個人桌面，遇到同名自動加 `(1)` 後綴，絕不覆寫。
   - 搬移結果採三態判定（已完成／未移動／兩端並存需人工確認），避免重試時重複產出副本。
   - 刪除專用收納夾前必須通過四道硬性檢查（受控標記、零錯誤與未取消、目錄完全為空含隱藏檔與暫存檔、非遞迴刪除）；任一項不符即 100% 保留資料夾與面板配置現場。
   - 拆框中途若程式被強制終止，下次啟動維持原現場，不自動繼續搬檔或刪除。

## 不做範圍與相容邊界
- 不重構 `FrameManager.cs` 的 Win32 視窗訊息循環與 Win+D 平滑喚醒邏輯。
- 程式啟動、退出、切換工作區時零檔案搬移。
- 既有 `Data` 面板與舊唯讀 `Portal` 面板配置原樣保留與顯示，不自動遷移。
- 獨立便箋（`NoteManager`）維持現狀。
- 專用收納柵欄不提供框內分頁（Tabs）與自由座標手動排序，統一依檔案屬性排序及檢視切換。

## 現況與限制
- 目前 `SettingsManager.DefaultPortalStorageRoot` 寫死若有 `D:\` 則預設為 `D:\DesktopFrames_Storage`，需調整為實際桌面下收納總夾。
- 現有 `PortalFrameManager.IsLegacyReadOnly` 寫死為 `true`，需改為依 `IsManagedStorage` 判定。
- 現有 `DeleteItem` 在特定設定下會直接呼叫 `File.Delete` / `Directory.Delete` 永久刪除，需加固為專用收納柵欄強制資源回收筒。

## 已確認決策
1. **單一專用收納模式**：新建柵欄全面收斂為專用收納柵欄，不提供使用者在 Data／Portal 之間選擇。
2. **設定檔向下相容擴充**：於 `frames.json` 新增 `IsManagedStorage` 與 `StorageRootPath` 欄位（`AGENTS.md` 規範已同步放寬為允許向前相容之選擇性屬性擴充）。
3. **安全搬移取代盲目 fallback**：移除 `MoveFile` 失敗時靜默降級為 `Copy` 的行為；搬移失敗即誠實報錯並保留現場。
4. **兩階段實作策略**：階段一先完成核心服務與 4 項防毒環境合成測試；階段二才切換新建預設入口。

## 工作清單
- [x] **Phase 1.1**：實作 `Code/Desktop Frames/Services/StorageFenceService.cs`（受控目錄檢驗、三態搬移判定、同名防覆寫編號、拆框移至桌面、四道非遞迴空夾刪除防線、強制資源回收筒刪除）｜單元邏輯自檢
- [x] **Phase 1.2**：修改 `SettingsManager.cs` 預設收納路徑改為實際桌面下的 `DesktopFrames_收納區`，並於 `FrameDataManager.CreateNewFrame` 支援寫入 `IsManagedStorage` 與 `StorageRootPath`｜設定值讀取與序列化驗證
- [x] **Phase 1.3**：修改 `PortalFrameManager.cs` 解封專用收納柵欄（右鍵選單改名、資源回收筒刪除、剪下、重新整理）、加入拖出拖放支援｜右鍵選單狀態與拖放事件串接
- [x] **Phase 1.4**：修改 `FrameManager.cs` 串接 `DragOver` 提示膠囊（搬移／複製／建捷徑）、`Drop` 委派 `StorageFenceService`、以及心形選單「拆除並將內容移至桌面…」確認對話框｜UI 顯示與呼叫整合
- [x] **Phase 1.5**：建立獨立合成文件驗證腳本 `tools/test-managed-storage-fence.ps1`，實測 4 大情境（拖入三手勢、跨框、拆框移至桌面與同名改名、部分鎖定保留現場與重試不重複）｜實測通過且退出碼 0
- [x] **Phase 1.6**：執行 `tools/run-all-tests.ps1` 確認既有面板與在地化無回歸退化｜全套測試退出碼 0
- [x] **Phase 2.1**：更新 `DrawFrameConfirmDialog.cs`、`FrameManager.cs`、`TrayManager.cs` 新建入口，全面預設為專用收納柵欄｜新建流程驗收
- [x] **Phase 2.2**：執行 MSBuild Release 建置、合成實測與全套回歸測試驗證｜建置 0 錯誤、全部測試通過

## 風險與因應
- **機關防毒攔截批次移動**：預設收納總夾設於同磁碟桌面目錄下（NTFS MFT Rename）；搬移失敗時精準記錄並停止，不靜默複製。
- **Office 隱藏 `~$` 暫存檔造成誤刪目錄**：四道檢查使用無過濾的 `EnumerateFileSystemEntries` 與非遞迴 `Directory.Delete(path, false)`。
- **使用者既有資料安全性**：測試一律在獨立暫存沙盒執行，嚴禁覆寫正式 `Profiles`。

## 驗證紀錄
- **Release MSBuild 建置**：`0 個錯誤`、`1271 個可為 Null/既有警告`。
- **專用收納柵欄合成測試 (`tools/test-managed-storage-fence.ps1`)**：
  - 預設拖曳搬移、Ctrl 複製、同框誤拖防呆、自身目錄防呆、跨框移動、拆框移至桌面、同名序號避讓防覆寫、鎖定保留現場與重試零重複全數通過（PASS）。
- **全套回歸測試 (`tools/run-all-tests.ps1`)**：
  - 103 項代表性功能斷言、多輪圖示轉移回歸、非同步讀取異常保護與 100% 繁體中文資源覆蓋率全數通過（PASS）。

## 剩餘問題
- 無阻斷性或重要問題，已達可交付停止條件。
