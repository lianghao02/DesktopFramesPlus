# HANDOFF

## 核心元資料 (Metadata)

- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：b179bc3
- **Skill Version**：v8.5
- **Task Type**：RELEASE / FEAT / SAFE-DATA / HANDOFF
- **Local Path Hint**：19_DesktopFramesPlus

## 目前狀態

已全面完成「專用收納柵欄（Managed Storage Fence）」架構升級、自適應圖示網格與右鍵建立資料夾微互動、圖示尺寸動態縮放（自訂面板 + `Ctrl + 滾輪` + 右鍵選單）、原生 Win32 `IShellLinkW` Unicode 捷徑解析器與雙擊直開檔案總管、磁碟防刷屏防護、單一執行個體 `-create` 快速退出與結構化日誌（`Desktop_Frames.log`）。
正式版本號晉升為 **v2.10.0 (v2.10.0-zh-TW)**，完成可攜式打包與全面回歸測試。

## 本輪目標

解決傳統 Data 分類面板僅建立捷徑、實體文件未真正收納，以及舊版 Portal 唯讀無法拖入或搬移的問題，並導入輕量實用的收納體驗微互動：
1. **一框一專用資料夾**：收納內容即為實體資料夾真實內容，程式不啟動時亦可透過檔案總管直接取用。
2. **拖入即收納**：實體搬移至專用收納夾，桌面實體文件真正收納變少。
3. **安全拆除**：明確點擊「拆除並將內容移至桌面…」時，將項目安全移回個人桌面，同名自動避讓，遇鎖定時完整保留收納夾與未完成檔案現場。
4. **細膩自適應網格與右鍵新增資料夾**：縮小柵欄時圖示間距與欄寬自動平滑配適；框內右鍵可直接建立新資料夾。
5. **向下相容**：既有 `Data` 分類面板、舊版外部目錄唯讀 `Portal` 面板與獨立便箋維持原樣可用。

## 基準與已確認事實 (Baseline & Confirmed Facts)

- **避免硬編碼與同磁碟防毒安全**：預設收納總目錄由 `D:\` 改為 Windows 實際個人桌面目錄下的 `桌面\DesktopFrames_收納區`，確保同磁碟 NTFS MFT Rename 瞬間完成，大幅降低防毒軟體對跨磁碟複製的攔截風險。
- **三態搬移防線**：拆框時將結果精確判定為「已完成」、「未移動」與「兩端並存需確認」，重試時絕不二次產出多餘編號副本。
- **四道硬性刪夾門檻**：僅在受控標記為真、零錯誤且未取消、使用無過濾的 `Directory.EnumerateFileSystemEntries` 確認全空（含隱藏檔與暫存檔）之下，才執行非遞迴 `Directory.Delete(path, false)`。
- **刪除走資源回收筒**：專用收納夾內項目刪除強制呼叫 Win32 `SHFileOperation(FO_DELETE, FOF_ALLOWUNDO)` 送入資源回收筒，絕不永久刪除。

## 已完成 (Completed)

1. **`AGENTS.md` 規範微調**：允許單一 Frame 擴充向前相容之選擇性屬性（`IsManagedStorage`、`StorageRootPath`）。
2. **`Code/Desktop Frames/Services/StorageFenceService.cs`（新建服務核心）**：
   - 實作受控目錄安全驗證（非受控、非系統根目錄、非磁碟根目錄）。
   - 實作 `IngestItem`：手勢判別、同框誤拖防呆、公用桌面強制複製、祖先目錄防呆、三態搬移防護。
   - 實作 `DismantleToDesktop`：同名 `(1)` 序號避讓不覆寫、逐項搬移、三態判定、四道刪夾門檻。
   - 實作 Win32 Shell 資源回收筒安全刪除。
   - 實作 `GetSuggestedNewFolderName`、`CreateNewFolder` 與 `PromptAndCreateNewFolder`（深色無邊框快速命名對話框與自動序號避讓）。
3. **`Code/Desktop Frames/SettingsManager.cs`**：
   - 預設收納倉庫路徑校正為實際個人桌面下的 `桌面\DesktopFrames_收納區`。
4. **`Code/Desktop Frames/FrameDataManager.cs`**：
   - `CreateNewFrame` 擴充 `isManagedStorage` 與 `storageRootPath` 參數並持久化。
5. **`Code/Desktop Frames/PortalFrameManager.cs`**：
   - `IsLegacyReadOnly` 動態判定：受控收納柵欄解除唯讀。
   - 右鍵選單啟用「新增資料夾…」、「剪下」、「重新命名」、「刪除（強制走資源回收筒）」與「重新整理內容」。
   - 圖示與詳細清單檢視皆支援滑鼠按住拖出項目進行 OLE 拖放操作。
   - `SortContents` 與 `RemoveIcon` 串接 `Framemanager.UpdateAdaptiveIconGrid(_wpcont)`。
6. **`Code/Desktop Frames/DrawFrameConfirmDialog.cs`**：
   - 升級為「建立專用收納柵欄」視窗。
   - 支援顯示預設收納路徑、名稱輸入即時聯動與自選實體資料夾按鈕。
   - 加上清楚操作提示說明。
7. **`Code/Desktop Frames/FrameManager.cs`**：
   - `CreateFrameFromDraw` 升級為建立受控專用收納柵欄，並移除傳送門背景浮水印圖案（`portal.png`）。
   - 新增 `CreateManagedStorageFence` 方法，並將心形選單「新增柵欄」接向此流程。
   - 心形選單刪除按鈕動態切換為「拆除並將內容移至桌面…」。
   - 柵欄背景右鍵選單（`CnMnFramemanager`）新增「新增資料夾…」，支援 Portal 與 Data 柵欄快速建立資料夾。
   - 新增 `UpdateAdaptiveIconGrid(WrapPanel wpcont)` 自適應網格演算法，綁定 `SizeChanged`、新增與移除圖示事件，並微調圖示間距與文字區塊高度。
   - 修正 `ClickEventAdder` 中 Portal 非資料夾項目雙擊開啟被提前返回的瑕疵。
   - `DragOver` 支援亮藍框與手勢操作提示；`Drop` 支援委派 `StorageFenceService.IngestItem`；`MouseMoveHandler` 支援收納圖示啟動 OLE 拖放。
8. **`Code/Desktop Frames/Localization/Strings.cs`**：
   - 新增 `Strings.MenuNewFolder`（含繁體中文自動回退 `"新增資料夾…"`）。
9. **`Code/Desktop Frames/TrayManager.cs`**：
   - 工作列托盤右鍵選單「新增柵欄」指向 `CreateManagedStorageFence`。
10. **獨立合成測試與全套回歸測試**：
    - `tools/test-managed-storage-fence.ps1` 擴充至 9 大核心防線（含 `CreateNewFolder` 同名避讓測試）全數通過（PASS）。
    - 執行 `tools/run-all-tests.ps1`，103 項功能回歸與 100% 繁中在地化全數通過（PASS）。
    - 執行 `scripts/build_and_sync.ps1` 同步更新至 `dist/DesktopFramesPlus/`（完整保留 `Profiles/`）。

## 異動檔案 (Changed Files)

- `AGENTS.md`
- `Code/Desktop Frames/DrawFrameConfirmDialog.cs`
- `Code/Desktop Frames/FrameDataManager.cs`
- `Code/Desktop Frames/FrameManager.cs`
- `Code/Desktop Frames/Localization/Strings.cs`
- `Code/Desktop Frames/PortalFrameManager.cs`
- `Code/Desktop Frames/Services/StorageFenceService.cs`（新建）
- `Code/Desktop Frames/SettingsManager.cs`
- `Code/Desktop Frames/TrayManager.cs`
- `IMPLEMENTATION_PLAN.md`（新建）
- `tools/test-managed-storage-fence.ps1`（新建）
- `HANDOFF.md`

## 刻意未修改 (Do Not Do / Deliberately Omitted)

- 既有 `Data` 面板與既有外部目錄唯讀 `Portal` 面板配置原樣保留與顯示，不自動遷移。
- 獨立便箋（`NoteWindow`）維持獨立運作。
- 不重構 `FrameManager.cs` 的 Win32 視窗訊息循環與 Win+D 喚醒排程。
- 不修改使用者現有 `dist/DesktopFramesPlus/Profiles/` 正式個人資料。

## 尚未完成 (Remaining Work)

- **P1（阻斷）**：無。
- **P2（重要）**：無。
- **P3（改善建議）**：使用者環境實際長時間操作與大批次檔案拖入之現場穩定性觀察。

## 驗證結果 (Validation)

- **MSBuild Release 編譯**：0 錯誤。
- **專用收納柵欄合成測試 (`tools/test-managed-storage-fence.ps1`)**：9 大項全數通過（PASS）。
- **全套回歸測試 (`tools/run-all-tests.ps1`)**：103 項代表性斷言全數通過（PASS）、繁體中文 100% 覆蓋。
- **正式可攜版同步 (`scripts/build_and_sync.ps1`)**：已同步至 `dist/DesktopFramesPlus/` 並啟動驗證。

## Git 狀態

- Commit：待提交（`feat: 實作專用收納柵欄、自適應圖示網格與右鍵新增資料夾`）
- Branch：main
- Working Tree：受控修改待提交

## 下一步建議動作 (Next Recommended Action)

使用者實際體驗「自適應圖示網格」與「右鍵新增資料夾…」微互動後，即可執行 Git 提交。
