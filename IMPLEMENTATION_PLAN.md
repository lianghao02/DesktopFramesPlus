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


## Q 版 2D 介面改版（2026-10-20，已完成）

- [x] 集中共用 WPF 色彩、按鈕與選單樣式。
- [x] 更新柵欄、設定、建立視窗與便箋，加入向量收納箱標誌。
- [x] 補正標題、窄視窗語言操作與取消選取的格位狀態。
- [x] 完成可攜式建置、128 項回歸、10 項收納安全斷言與畫面檢視。
- [x] 正常關閉舊程式並更新正式成品；206 個 Profiles 檔案雜湊一致後重新啟動。

現行交付狀態與證據見 HANDOFF.md；上方已完成的收納功能規格仍保留。


## 靜態遊戲素材、雙向尺寸吸附與登入啟動（2026-10-20，已完成）

- [x] 實際生成並嵌入圖集，以九宮格呈現木框、布面、皮革與收納格。
- [x] 補上左右及角落縮放命中區，吸附時固定對側，依面板 ID 保存座標。
- [x] 登入啟動改為建立機碼、引用完整路徑、檢查 EXE 並讀回驗證。
- [x] 152 項回歸與 10 項收納安全斷言通過，人工檢視主要與收合畫面。
- [x] 正式成品更新、206 個 Profiles 檔案一致，登入啟動已登錄，System32 工作目錄啟動成功。
- [ ] 真實重開機／重新登入後的自動啟動確認；不自動重開使用者電腦。

驗證邊界與證據位置見 HANDOFF.md。

啟動診斷補充：沙箱 HKCU 視角與實際使用者環境不同，不把最初讀不到 Run 項目當成故障根因。實際註冊命令已核對；Windows 啟動開關與真實重開機驗證，依使用者要求留待回辦公室後測試。

## FrameManager 重構前的回歸測試網（2026-10-21，待使用者簽核）

### 目標與驗收條件
在拆分或重構 `FrameManager.cs` 之前，先建立「搬動程式碼若改變行為就會被抓到」的回歸測試網。
1. `docs/specs/FRAMEMANAGER_MAP.md` 的每個功能區塊，至少有一項自動化檢查，或一項寫明原因、可重複執行的手動驗收清單。
2. 全部自動化檢查由 `tools/run-all-tests.ps1` 一次執行，維持隔離（不碰正式 `Profiles`、桌面與登錄的正式啟動項）。
3. 在動任何重構前，先記錄基準結果（目前 152 項斷言加 10 項收納面板測試）。
4. 以「故意改壞程式」驗證測試有效：至少 10 種改壞，每種都必須使至少一項檢查失敗（做法與 02、08 相同）。
5. 完成上述四點並經使用者確認後，才進入「拆檔」的獨立計劃；本階段**不修改任何正式程式碼**。

### 不做範圍
- 不拆檔、不重構、不改方法簽名；不為了測試而修改 `Code/Desktop Frames/` 內的正式檔案。
- 不新增 xUnit：本專案需用 Visual Studio MSBuild（COM 型別程式庫），`dotnet test` 無法建置主專案，沿用既有的主控台驗收程式模式。
- 不測 `WndProc` 的原生訊息迴圈本身（已由尺寸吸附與原生視窗檢查間接涵蓋）。
- 不啟用 CI 的決定留到最後一階段，需使用者確認。

### 現況與限制
- **更正**：先前評估把 19 當成「測試很薄（3 個測試檔）」是誤判，因為測試放在 `tools/` 而非 `tests/`。實際已有約 100 項驗收斷言（`tools/acceptance-tests`）、面板與沙盒測試、10 項收納面板合成測試與繁中 100% 覆蓋檢查，涵蓋面明顯較廣。
- 驗收程式以反射存取非公開成員，並用 `.acceptance-session` 標記強制隔離；這套機制沿用。
- 下表為依既有檢查名稱判斷的覆蓋現況（未逐行比對程式碼，Phase 0 會精確核對）。

| 區塊（見區塊地圖） | 既有覆蓋（依檢查名稱） | 缺口 | 對應階段 |
|---|---|---|---|
| 刪除與拆除、收納搬移 | 收納面板 10 項、「刪除面板只移除配置」 | 主視窗 Drop 路徑（服務層已測，UI 路徑未測） | 4 |
| 設定讀寫與保護 | 讀取異常不覆寫、鎖定解除後恢復、工作區切換 | `UpdateFrameProperty` 的屬性寫入細節、舊格式遷移 | 2 |
| 尺寸吸附、鎖定吸附、負座標 | 約 9 項 | 螢幕外面板拉回（`AdjustFramePositionToScreen`） | 2 |
| 分頁 | 主區與分頁多輪移出移回單一歸屬 | 分頁開關與重建 | 3 |
| 圖示轉移與引用清理 | 約 6 項 | 圖示顯示（大小、灰階、文字色）、`AddIcon`／`UpdateIcon` | 3 |
| 自動隱藏與喚醒 | 隱藏全部、顯示後配置不變 | 計時與喚醒狀態機、自動捲動 | 3 |
| 右鍵選單（♥、面板、圖示） | 主題勾選、子選單 | **選單結構與項目順序、動態項目** | 3 |
| 網址、網路路徑、特殊路徑判斷 | 無 | 純函式（`IsWebUrl`、`IsNetworkPath`、`IsSpecialWindowsPath`、`IsValidWebUrl`、`IsUncRoot`、`ExtractUrlFromUrlFile`、`GetNextDisplayOrder`） | 1 |
| 開啟項目（`LaunchItem`） | 無 | 分流邏輯 | 1、3 |
| 重新命名、篩選 UI、Delete 鍵 | 無 | `CreateFrame` 內的行為 | 3 |
| 拖放（主視窗） | 無（僅服務層） | 需合成 `DragEventArgs` | 4（可行性待驗證） |

### 已確認決策
1. 只加測試、不動正式程式；測試資料一律虛構、輸出到隔離沙盒。
2. 先做風險低、價值高的純函式與資料層，再做視窗層，最後才評估拖放。
3. 視窗層採「結構快照加行為模擬」：以真實視窗檢視選單項目與順序、模擬點擊與按鍵，快照存成純文字黃金檔，變動時必須人為確認。
4. 無法自動化的項目不假裝覆蓋，改列成手動驗收清單並寫明原因。
5. 每階段結束都以「故意改壞」確認測試確實會失敗。

### 工作清單
- [x] **Phase 0 盤點與基準**：逐區塊核對既有檢查，補完上表；記錄目前全部測試的基準結果｜`run-all-tests.ps1` 與收納面板測試全過並寫入驗證紀錄
- [x] **Phase 1 純函式特徵化測試**：反射呼叫網址、網路路徑、特殊路徑、UNC、`GetNextDisplayOrder` 等，鎖住現有輸出（含邊界與非法輸入）｜新增檢查全過；至少 3 種改壞被抓到
- [x] **Phase 2 資料層**：`UpdateFrameProperty` 屬性寫入（含未知欄位保留、布林字串）、`ResetAllCustomizations`、`MigrateLegacyJson`（以虛構舊格式 `frames.json`）、損毀項目備份、`AdjustFramePositionToScreen`｜向下相容：舊設定可讀、未知欄位不遺失
- [x] **Phase 3 視窗層**：選單結構快照（♥、面板右鍵、圖示右鍵，含收納面板與 Data 面板兩種）、鎖定切換、收合展開、重新命名、分頁開關、自動隱藏與喚醒、圖示顯示屬性｜黃金檔建立並可重現；快照差異會使測試失敗
- [x] **Phase 4 拖放可行性**：評估以反射建立 `DragEventArgs` 驅動主視窗 `Drop`；可行則補三種手勢（搬移、Ctrl 複製、Alt 捷徑）在兩種面板的測試，不可行則寫入手動驗收清單｜結論與證據寫入驗證紀錄
- [x] **Phase 5 改壞驗證**：至少 10 種（例如鎖定狀態不寫入、選單少一項、拖放預設變複製、屬性寫入丟欄位、網址判斷放寬）｜每種至少一項檢查失敗，結果寫入驗證紀錄
- [x] **Phase 6 整合與出口**：納入 `run-all-tests.ps1`；更新區塊地圖的「保護網」欄位；以 `workflow_dispatch` 試跑 `tools/ci-drafts/test.yml` 確認 GitHub 執行環境能開 WPF 視窗｜全部通過；CI 試跑結果如實記錄；使用者簽核後才開「拆檔」計劃

### 風險與因應
- **需要桌面工作階段**：真實視窗測試在無桌面環境會失敗；Phase 6 以 CI 試跑確認，不行就維持本機執行並明講。
- **時序不穩（flaky）**：視窗載入與計時器需等待；以明確等待條件取代固定延遲，不穩定的檢查寧可移出自動化。
- **反射脆弱**：私有方法改名會讓測試失敗；這是刻意的警訊，但需在失敗訊息說明原因。
- **快照過度敏感**：只快照結構（類型、名稱、文字、順序），不含座標與像素。
- **汙染正式資料**：沿用 `.acceptance-session` 隔離標記，並禁止寫入正式 `Profiles`、桌面與登錄的正式啟動項。

### 驗證紀錄
- **還原點**：工作開始前建立並推送標籤 `restore/2026-10-21-before-framemanager-work`（提交 fff3c44）；任何階段出錯可回到此版本。另 v2.11.0-zh-TW 的 GitHub Release 安裝包為第二層保險。
- **Phase 0 基準（提交 fff3c44）**：MSBuild Release 0 錯誤（1,277 個既有警告）；`run-all-tests.ps1` 155 項 PASS、0 失敗；`test-managed-storage-fence.ps1` 10 項 PASS。
- **Phase 0 精確核對**：既有測試直接呼叫 `CreateFrame`（9 處，建立真實視窗）、`AddItemToDataFrame`、`WakeUpFrames`、`ForceHideFrames`、`OnResizingEnded`、`UpdateOptionsAndClickEvents`、`RefreshFrameUsingFormApproach`，並以反射呼叫 `WndProc`、`RemoveDataItem`、`DeleteFrameConfiguration`、`LoadFrameDataFromJson`、`InitializeDefaultFrame`。**完全未呼叫**：♥／面板／圖示右鍵選單建構、`UpdateFrameProperty`、`ResetAllCustomizations`、`MigrateLegacyJson`、`AdjustFramePositionToScreen`、`UpdateLockState`、`LaunchItem` 與網址／路徑判斷。上方覆蓋表與此一致。
- **Phase 1 結果**：新增 `tools/acceptance-tests/FrameManagerHelperChecks.cs`（21 項，以反射呼叫私有靜態方法）：`IsWebUrl`、`IsValidWebUrl`、`IsSpecialWindowsPath`、`IsUncRoot`、`IsNetworkPath`（直接路徑與捷徑內容掃描）、`ExtractUrlFromUrlFile`、`ExtractUrlFromDropData`（瀏覽器、Firefox、HTML、純文字四種格式）、`GetNextDisplayOrder`。加入後 `run-all-tests.ps1` 176 項 PASS。
- **Phase 1 改壞驗證**：3 種改壞（`IsWebUrl` 不再認 mailto、拖入網址接受 file、排序值不加一）各自讓對應檢查失敗；`FrameManager.cs` 已以 git 還原並重新建置，全部測試恢復通過。
- **記錄的現狀（未修正）**：`IsSpecialWindowsPath` 以 `StartsWith("control")` 判斷，`controller-說明.txt` 這類一般檔名也會被當成特殊路徑。已以「現狀」檢查鎖住，是否修正另案評估。
- **Phase 2～4 結果**：新增 `tools/acceptance-tests/FrameManagerBehaviorChecks.cs`（42 項）與 5 份選單黃金檔（`tools/acceptance-tests/golden/`，內嵌資源，不一致時於驗收目錄輸出 `actual-*.txt`）。涵蓋：`UpdateFrameProperty` 的型別與相容規則、收合動畫中略過屬性、執行期隱藏與置頂、`MigrateLegacyJson`（缺欄位補預設、未知欄位保留、損壞分頁修復、舊版資料夾面板不改寫、重跑不改檔）、手動拉回螢幕內、Data／收納面板的 ♥ 選單與面板右鍵選單、圖示右鍵選單、鎖定切換、重新命名（Enter／Esc）、分頁開關、圖示大小、主視窗拖放（Data 建立參照、收納面板預設搬移、舊版面板介面層拒絕＋對照組）、重設自訂外觀、真實桌面隔離防護。
- **拖放可行性結論**：以反射建立 `DragEventArgs` 可驅動主視窗真正的 `Drop` 處理；但拖放手勢讀取的是實體鍵盤狀態（`Keyboard.Modifiers`），**Ctrl 複製與 Alt 捷徑無法在介面層自動化**，維持由 `test-managed-storage-fence.ps1` 在服務層驗證，介面層列入手動清單。
- **Phase 5 改壞驗證（累計 13 種）**：Phase 1 的 3 種全部抓到；Phase 5 的 10 種中 9 種抓到（鎖定不切換、改名不存檔、拖放預設變複製、舊版面板不擋拖放〔由既有「Portal 拒絕拖入」測試因跳出對話框而逾時失敗〕、選單少一項、展開高度預設錯、分頁主區未清空、置頂失效、手動拉回失效），1 種為**等價改動**（`IsHidden` 存成布林：`SaveFrameData` 的 `ApplyFormatConsistency` 會轉回字串，檔案內容相同，非測試漏洞）。過程中補強 2 處原本太弱的測試（手動拉回需關閉自動調整、舊版面板需檢查介面層拒絕）。每次改壞後皆以 git 還原並重新建置。
- **Phase 6 結果**：`run-all-tests.ps1` 共 218 項 PASS（基準 155＋新增 63），收納面板 10 項 PASS；`FrameManager.cs` 全程未修改。CI 試跑（GitHub Actions，`windows-latest`，執行編號 39618249790）：Release 建置成功，但驗收在**第一批既有檢查**「文件與資料夾建立參照」即失敗（尚未執行到新增測試）。該步驟以 Windows 捷徑 COM 元件建立 `.lnk`，推測在 GitHub 執行環境不可用，屬環境限制。依計劃規則**維持本機執行**，並移除該 workflow，避免留下會失敗的入口；草稿保留於 `tools/ci-drafts/test.yml`。若日後要上 CI，需先讓建立捷徑的步驟在無互動環境可用。
- **事故紀錄（已處理）**：Phase 3 初版測試建立收納面板時未指定路徑，程式改用真實桌面預設位置，於 `桌面\DesktopFrames_收納區` 建立 4 個空資料夾（本輪 22:05～22:08 建立，無任何檔案）。已確認全為本輪產生並以非遞迴方式刪除；測試改為一律明確指定隔離路徑，並新增「真實桌面預設收納位置前後不變」的防護檢查。

### 怪現象清單（2026-10-22 處理完畢；流程：先改測試期望使其失敗，再修程式）
1. **已修正**：`IsSpecialWindowsPath` 原以 `StartsWith("control")` 判斷，以 control 開頭的一般檔名也會被當成特殊路徑而跳過「目標是否存在」檢查。改為只認 `control`、`control.exe`（可接參數）。測試先失敗後通過。（實際影響有限：這裡多半傳入完整路徑，只有裸檔名型目標會誤判。）
2. **已修正**：圖示右鍵的「開啟目標所在資料夾」原本顯示「開啟收納資料夾」，因 `MenuOpenTargetFolder` 同時用於收納面板右鍵。新增字串鍵 `MenuOpenStorageFolder`（英、繁中）給收納面板使用，`MenuOpenTargetFolder` 繁中改為「開啟目標所在資料夾」；黃金檔同步更新。
3. **不是問題**：「調整至最適大小」以剛讀檔的面板實測，高度確實寫回 `frames.json`（另一條路徑會保存）。未改程式，保留測試作為保護。
4. **已移除無作用程式碼**：`UpdateFrameProperty` 結尾依英文標題 "Customize"／"Color"／"Launch Effect" 更新子選單勾選；任何語言都沒有這些子選單（英文標題為 "Customize..."），不會執行。移除後行為不變，全部測試通過。
5. **撤銷（誤判）**：`MigrateLegacyJson` 的 `catch` 其實有寫入警告紀錄，先前閱讀時過濾掉紀錄行而誤判為空的 `catch`。
6. **維持現狀**：拖入收納面板失敗時以模態對話框提示，會讓自動化測試卡到逾時；這是讓使用者一定看到失敗原因的設計取捨，不改。

### 手動驗收清單（目前無法自動化的部分）
- 拖入時按住 Ctrl（複製）與 Alt（建立捷徑）在介面上的提示膠囊與實際結果。
- 自動隱藏、閒置淡出與滑鼠移入喚醒；自動捲動。
- Ctrl 覆蓋層與 Ctrl＋右鍵的進階選單（含啟用標籤頁）。
- 空白處點擊取消選取、Delete 鍵移除選取項目。
- 篩選列輸入與歷史紀錄。
- 圖示右鍵各項實際執行（編輯、移動、以管理員執行、複製路徑）與雙擊開啟項目。
- 實際滑鼠拖曳移動面板、拖曳縮放、多螢幕與高 DPI。

### 剩餘問題
- 拖放是否能自動化，Phase 4 前不確定。
- 是否啟用 CI，待 Phase 6 的試跑結果與使用者決定。
- 拆檔本身（`partial class` 切分與 `CreateFrame` 抽取）另立計劃，不在本階段。

## FrameManager.cs 拆檔（2026-10-22 起；第一階段已完成，第二階段進行中）

### 目標與驗收條件
把 10,900 行、單一類別的 `FrameManager.cs` 拆成依功能分區的多個檔案，並逐步拆解 3,789 行的 `CreateFrame`，讓人與 Agent 修改時只需讀相關區塊。
1. **行為完全不變**：每一步後 MSBuild Release 0 錯誤、`run-all-tests.ps1` 220 項全過、收納面板 10 項全過、5 份選單黃金檔不變、繁中 100%。
2. **每一步可單獨退回**：一個區塊一個提交，失敗就退回該提交，不帶著問題往下做。
3. **設定檔與公開介面不變**：`Framemanager` 的類別名稱、命名空間、所有 public／internal 方法簽名不變（`tools/` 驗收程式以反射呼叫的私有方法名稱也不變）。
4. 完成後更新 `docs/specs/FRAMEMANAGER_MAP.md`（改為「檔案地圖」）與 `AGENTS.md` 的對應規則。

### 不做範圍
- 不改任何邏輯、演算法、文字與設定檔格式；不順手修 Bug（發現的問題記入清單，另案處理）。
- 不改 `NonActivatingWindow`、`FrameDataManager`、`PortalFrameManager` 等其他類別。
- `AddIcon`（816 行）列為第三階段，本計劃先不做，視第二階段結果再決定。

### 現況（以提交 960c7c1 分析）
- 類別層級：約 25 個共用靜態欄位、2 個結構（`POINT`、`MINMAXINFO`）、1 個 Win32 宣告（`SetWindowPos`），其餘約 92 個方法。
- `CreateFrame`：3,789 行（約占 35%），內有 57 個區域變數，其中 13 個橫跨 800 行以上（`win` 被引用 152 次、面板右鍵選單 80 次，另有 `cborder`、`dp`、`titletb`、`titlelabel`、`CommitRename`、`titleGrid`、`lockIcon`、`currentFrame`、`wpcont` 等），以及 83 個直接捕捉這些變數的事件處理器。
- 專案檔為 SDK 格式，新增的 `.cs` 會自動編譯，不需改專案檔。
- **名稱解析陷阱**：檔頭含 `using static System.Net.Mime.MediaTypeNames;`（會引入名為 `Application`、`Image`、`Text` 的型別）與 `using Color = System.Drawing.Color;`。每個分檔都必須**逐字複製相同的 using 區塊**，否則同一段程式在不同檔案可能解析到不同型別，編譯器不一定報錯。
- 還原點：開工前另建標籤 `restore/2026-10-22-before-split`。

### 已確認決策
1. 分兩個階段，第一階段完全不改任何方法內容，風險最低、收益可立即驗證。
2. 第一階段以**腳本**依方法名稱搬移，並比對「搬移前後的方法本文完全相同」（逐字比對），不手工剪貼。
3. 第二階段才動 `CreateFrame`：先引入一個私有的 `FrameBuildContext`（建構內容物件），把橫跨全方法的共用變數收進去，再把各區段抽成私有方法；事件處理器改為讀寫內容物件的欄位，語意與原本捕捉區域變數相同。
4. 第二階段由耦合最低的區段先做，每抽一段就建置、跑全部測試、對照手動驗收清單中相關項目。

### 第一階段：`partial class` 機械分檔（不改方法內容）
`public static class Framemanager` 改為 `public static partial class Framemanager`，依區塊地圖搬到下列檔案（皆在 `Code/Desktop Frames/`）：

| 檔案 | 內容（方法） |
|---|---|
| `FrameManager.cs`（核心） | 共用欄位、結構、Win32 宣告、`CreateFrame`（第二階段再拆） |
| `FrameManager.AutoHide.cs` | 自動隱藏與喚醒、捲軸與色調、自動捲動設定、收合轉場 |
| `FrameManager.Reload.cs` | `ReloadFrames`、`ReloadSpecificFrames`、舊 Portal 導覽與提示 |
| `FrameManager.Screen.cs` | DPI、拉回螢幕內、`ForceRepositionallFrames` |
| `FrameManager.Delete.cs` | 刪除與拆除面板 |
| `FrameManager.Menus.cs` | ♥ 選單、圖示右鍵、URL 項目選單、選單樣式 |
| `FrameManager.WebLinks.cs` | 網址捷徑建立、拖入網址解析、匯出到桌面 |
| `FrameManager.Properties.cs` | `UpdateFrameProperty`、重設外觀、圖示大小、篩選紀錄 |
| `FrameManager.Tabs.cs` | 分頁開關、分頁按鈕、分頁內項目移除 |
| `FrameManager.Loading.cs` | 舊格式遷移、載入建立、框選新增、收納面板建立、預設面板 |
| `FrameManager.Icons.cs` | `UpdateAdaptiveIconGrid`、`AddIcon`、`UpdateIcon`、編輯項目、圖示刷新 |
| `FrameManager.Launch.cs` | 點擊與開啟項目、網址與特殊路徑判斷、網路路徑判斷 |
| `FrameManager.Selection.cs` | 鎖定狀態、選取與移除項目、捷徑清理 |
| `FrameManager.Feedback.cs` | 縮放回饋、`WndProc`、視覺樹輔助、`GetSafeProperty`、失效捷徑檢查 |

步驟：
- [x] 建立還原點標籤並推送｜標籤存在於遠端
- [x] 撰寫搬移腳本：依方法名稱取出完整本文（含上方註解與屬性）、寫入目標檔、自原檔移除；輸出「搬移前後本文逐字相同」的比對結果｜比對全部相同
- [x] 每個目標檔一個提交（共 13 個），每個提交都建置並跑全部測試｜每步 0 錯誤、220 項全過、黃金檔不變
- [x] 更新區塊地圖為檔案地圖，`AGENTS.md` 註明各分檔職責與「using 區塊必須一致」｜文件與實際檔案一致

第一階段完成後 `FrameManager.cs` 預計約 4,100 行（其中 `CreateFrame` 3,789 行），其餘約 6,800 行分到 13 個檔案。實際：核心 4,240 行（`CreateFrame` 3,715 行、檔尾約 370 行為註解掉的舊程式碼），13 個分檔合計 6,908 行（含各檔重複的 using 區塊）。

### 第二階段：拆解 `CreateFrame`（保留行為的重構）
1. 引入 `private sealed class FrameBuildContext`，欄位包含 `frame`、`checker` 與 13 個橫跨全方法的共用變數；原本的區域變數改為讀寫內容物件。只做這一步、不抽方法，先確認測試全過。
   - **2026-10-22 調整**：實作時改為「內容物件只放會被事件處理器捕捉、且之後會重新指派的變數」（目前只有 `CommitRename`），外加 `frame`、`targetChecker`、`titletb`。建立後不再指派的變數（`cborder`、`heart`、`lockIcon`、`titleGrid` 等）以回傳值交回 `CreateFrame` 的原區域變數，抽出的方法開頭以區域別名（`dynamic frame = ctx.Frame;`）取得，方法本文得以逐字搬移、差異最小。判斷依據以 Roslyn 資料流分析（流入、流出、被捕捉、重新指派次數）取得，非人工推估。
2. 依耦合由低到高逐段抽成私有方法（每段一個提交）：
   1. ~~外框與筆刷計算、標題列與 ♥／鎖頭建立~~（2026-10-22 完成，見驗證紀錄）
   2. 面板右鍵選單項目建立（含「更多」子選單）
   3. 鍵盤掛鉤與 Ctrl 覆蓋層
   4. 自動捲動計時器
   5. 選單開啟時的動態狀態
   6. 篩選列
   7. 重新命名邏輯
   8. 圖示區（`WrapPanel`）與空白處點擊
   9. 拖放（`DragOver`／`Drop`）
   10. Delete 鍵與選取
3. 每段完成：建置、220 項、黃金檔、並人工走一遍手動驗收清單中相關項目（例如抽拖放後實測 Ctrl／Alt 拖入）。
4. 全部完成後再做一次「故意改壞」抽查（至少 5 種，針對新抽出的方法），確認測試仍能抓到。

### 風險與因應
- **名稱解析不同**：分檔 using 必須逐字相同；搬移腳本自動複製並在提交前比對。
- **事件處理器捕捉的變數**：第二階段若把會被重新指派的區域變數留在方法內、事件處理器卻改讀內容物件，會造成不同步。每抽一段都列出該段讀寫的共用變數，全部改走內容物件。
- **`dynamic` 不會在編譯時報錯**：拆錯的 `dynamic` 呼叫只在執行時失敗；以 220 項測試加手動清單把關。
- **測試盲區**：Ctrl／Alt 拖入、自動隱藏、Delete 鍵等無法自動化，第二階段相關步驟後必須人工驗收；這部分需要使用者在實際桌面協助。
- **退回**：任何一步失敗即 `git revert` 該提交；整體可回到 `restore/2026-10-22-before-split`。

### 驗證紀錄
- 2026-10-22 第一階段（Claude）：還原點 `restore/2026-10-22-before-split`（1b3e3a6）。13 個提交 c657d0b～fe94610，依序 AutoHide、Reload、Screen、Delete、Menus、WebLinks、Properties、Tabs、Loading、Icons、Launch、Selection、Feedback。
  - 每個提交前：搬移腳本逐字比對方法本文、核心檔非空白行數守恆、方法範圍不重疊；MSBuild Release 0 錯誤；`run-all-tests.ps1` 220 項 PASS（含 5 份選單黃金檔）；收納面板 10 項 PASS。
  - 完成後整體比對：還原點的 `FrameManager.cs` 與目前 14 個檔案的非空白行（排序後多重集合）相比，唯一差異是 `class` → `partial class` 與各分檔新增的 using／命名空間外框；14 個檔案的 using 區塊雜湊一致；繁中 100%。
  - 過程中的問題：Tabs 第一次執行時，搬移腳本把寫成單行的方法（`TabButton_MouseEnter(...) { }`）的結尾誤判到下一個方法，守恆檢查擋下、未提交；已還原檔案、修正單行方法判斷並加上範圍重疊檢查後重跑，後續 6 組全過。
  - 未做：真實桌面目視確認（本階段未改任何方法內容，依計劃不要求人工驗收）；未升版、未更新本機 `dist`。
- 2026-10-22 第二階段第一段（Claude）：還原點 `restore/2026-10-22-before-stage2`（4be99ac）。
  - a49694f 新增 `FrameBuildContext`，`CommitRename` 6 處參照改走內容物件（220 項全過）。
  - 46c9291 先補 5 項特徵化測試（標題文字外觀、外框筆刷現狀、改名中點 ♥／鎖頭先保存、一般點 ♥ 開選單），在抽出前的程式上 225 項全過。
  - 2f212ce `BuildFrameBorder`、b9c0371 `BuildMenuIcon`、f3de523 `BuildLockIcon`、9271e85 `BuildTitleGrid`＋`BuildTitleLabel`：每步 MSBuild 0 錯誤、225 項＋收納面板 10 項全過。
  - 整體比對：與還原點相比，全部 FrameManager 檔案的非空白行多重集合只差 `CommitRename` → `ctx.CommitRename`、`titlelabel = new Label` → 方法內 `Label titlelabel = new Label`、新方法外框／別名／回傳行與 5 行呼叫，其餘逐字相同。
  - 新發現的現狀（未修改，另案評估）：面板設定的「外框顏色」最後被 Q 版主題輪廓色蓋掉，且外框粗細為 0，自訂外框顏色在畫面上沒有作用。
  - 待使用者在實際桌面確認（自動化無法涵蓋）：見下方「第二階段第一段手動驗收」。

### 第二階段第一段手動驗收（需更新本機 `dist` 後在桌面操作）
- [ ] ♥ 滑鼠移入變亮、移出約 0.8 秒後淡回
- [ ] 按住 Ctrl 點 ♥ 出現延伸選單；一般點 ♥ 出現一般選單
- [ ] 鎖頭滑鼠移入移出淡入淡出；點鎖頭可鎖定／解鎖，重新開啟程式後狀態保留
- [ ] 雙擊面板空白邊緣（外框內距處）開啟搜尋
- [ ] 標題顏色、字級、粗體在「自訂外觀」修改後顯示正確
- [ ] 選單圖示與鎖頭圖示樣式（設定中切換 ♥／☰ 等、🛡️／🔑 等）顯示正確

### 剩餘問題
- 第二階段的人工驗收需要使用者在實際桌面操作，執行到那些步驟時會先通知。
- `AddIcon` 是否也拆，待第二階段完成後再決定。

