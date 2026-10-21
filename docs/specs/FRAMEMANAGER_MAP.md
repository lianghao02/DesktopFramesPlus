# FrameManager.cs 區塊地圖

用途：讓人與 Agent 不必整份閱讀 10,900 行也知道「改哪一類功能該看哪一段、要跑哪些驗證」。
基準：提交 ddd365a（2026-10-21）。**行號只供定位，會隨修改位移，一律以方法名稱為準。**

## 先看結論：問題在「兩個超大方法」，不是檔案大小
| 方法 | 約略行數 | 占整檔 |
|---|---|---|
| `CreateFrame` | 3,789 | 約 35% |
| `AddIcon` | 816 | 約 7.5% |
| 其餘 86 個方法 | 平均約 70 行 | 約 57% |

單純把檔案用 `partial class` 切成多個檔案只能整理其餘 57%，`CreateFrame` 仍是一個 3,789 行的方法，裡面大量事件處理（lambda）捕捉區域變數。真正降低風險需要把裡面的區塊抽成獨立方法，這比搬檔案危險得多，必須先有回歸測試。

## 功能分區（依方法名稱）
| 區塊 | 方法（約略行號） | 修改時要注意 |
|---|---|---|
| 自動隱藏與喚醒 | `InitializeAutoHideTimer`、`ResetAutoHideTimer`、`WakeUpFrames`、`ExecuteAutoHideSequence`、`RefreshScrollbarSettings`、`RefreshAllIconsTint`、`RefreshAutoRollSettings`（L108–378） | 計時器與視窗顯示狀態；改完要實測隱藏與喚醒 |
| 重新載入與舊 Portal | `ReloadFrames`、`ReloadSpecificFrames`、`NavigatePortalFrame`、`RefreshPortalNavBar`、`ShowPortalToast`（L380–936） | 舊 Portal 已唯讀退場，勿新增寫入功能 |
| 螢幕與 DPI 定位 | `GetDpiScaleFactor`、`AdjustFramePositionToScreen`、`ForceRepositionallFrames`（L937–1055） | 多螢幕、負座標；`tools/` 內吸附與座標測試 |
| 刪除與拆除面板 | `DeleteFrameWithConfirmation`、`DeleteFrameConfiguration`（L1063–1183） | 收納面板拆除須把內容移回桌面且不覆蓋；跑 `test-managed-storage-fence.ps1` |
| 右鍵選單 | `BuildHeartContextMenu`、`AttachIconContextMenu`、`UpdateAllHeartContextMenus`、`CreateBasicContextMenu`（L1184–1723、L2104–2283） | 選單文字走資源檔；♥ 選單與面板右鍵的「更多」子選單 |
| 網址捷徑與匯出 | `CreateWebLinkShortcut`、`ExportAllIconsToDesktop`、`ExtractUrlFromDropData`、`AddUrlShortcutToFrame`（L1724–2103） | 拖入網址的資料格式判斷 |
| 面板屬性與設定 | `UpdateFrameProperty`、`ResetAllCustomizations`、`SetFrameIconSizeLive`、`UpdateFilterHistory`（L2284–2723） | **會寫入 `frames.json`，須維持向下相容** |
| 分頁 | `ToggleFrameTabs`、`RefreshFrameContentSimple`、`RemoveItemFromTab`、`CreateTabButtonTemplate`（L2724–3058） | 分頁與 Data 面板的資料結構 |
| 載入、遷移與建立 | `MigrateLegacyJson`、`LoadAndCreateFrames`、`CreateFrameFromDraw`、`CreateManagedStorageFence`、`AddItemToDataFrame`、`LoadFrameDataFromJson`、`InitializeDefaultFrame`（L3059–3931） | 讀檔失敗時要保護原設定不被覆寫 |
| **面板視窗本體** | **`CreateFrame`（L3932–7720）** | 見下節 |
| 圖示 | `UpdateAdaptiveIconGrid`、`AddIcon`（816 行）、`UpdateIcon`、`EditItem`、`RefreshSingleIconComplete`、`UpdateFrameDataForIcon`（L7721–10573 間） | 圖示大小、灰階、文字陰影；縮放後殘影問題曾在此修過 |
| 點擊與開啟 | `ClickEventAdder`、`LaunchItem`、`TriggerLaunchEffect`、`IsWebUrl`、`IsNetworkPath`（L9625–10363） | 單擊／雙擊開啟、特殊路徑與網路路徑判斷 |
| 鎖定與選取 | `UpdateLockState`、`SetSelectedIcon`、`DeselectIcon`、`RemoveSelectedIconFromFrame`（L8791、L9458–9592） | 鎖頭外觀統一由 `GameTheme.ApplyLockIconStyle` 處理 |
| 縮放回饋與視窗訊息 | `ShowSizeFeedback`、`OnResizingStarted/Ended`、`UpdateSizeFeedback`、`WndProc`（L10646–10905） | Win32 訊息；尺寸吸附的原生縮放 |
| 失效捷徑 | `HasDeadShortcuts`、`CheckItemsForDead`（L10778–10840） | 唯讀檢查，不應刪除使用者檔案 |

## `CreateFrame` 內部（依檔內標示註解，供閱讀導航）
| 約略行號 | 內容 |
|---|---|
| L3932–3953 | 標題輸入框與儲存動作宣告 |
| L4296–4328 | 選單項：自動捲動、永遠置頂 |
| L4435–4616 | 動態選單項：清除失效捷徑、開啟資料夾（Portal）、貼上、外掛設定、刪除面板 |
| L4826–4892 | 自動隱藏掛鉤、Ctrl 覆蓋層、原生鍵盤掛鉤 |
| L4894–4974 | 自動捲動計時器 |
| L5100–5134 | 訊息掛鉤（限制視窗大小）與狀態修正 |
| L5212 起 | 選單開啟時的動態狀態更新 |
| L5729–6010 | 篩選 UI |
| L6013–6050 | 重新命名的集中邏輯 |
| L6637 起 | 硬體加速與安全剪裁（效能） |
| L7605–7719 | 空白區點擊與 Delete 鍵行為 |

注意：L7605、L7619 附近的註解有編碼損毀（亂碼），不影響執行，但閱讀時請勿誤判；有機會時另案修正編碼。

## 目前的保護網（2026-10-21）
- 自動化：`tools/run-all-tests.ps1`（218 項）＋`tools/test-managed-storage-fence.ps1`（10 項）。重構相關的特徵化測試在 `tools/acceptance-tests/FrameManagerHelperChecks.cs`（純函式）與 `FrameManagerBehaviorChecks.cs`（資料層、視窗層、拖放、選單黃金檔）。
- 選單結構以 `tools/acceptance-tests/golden/*.txt` 比對；**刻意**修改選單時，依失敗訊息取得 `actual-*.txt`，人工確認後覆蓋黃金檔並提交。
- 無法自動化的部分列在 `IMPLEMENTATION_PLAN.md` 的「手動驗收清單」，重構後必須人工走一遍。

## 若將來要拆分（本專案已脫離上游 fork，不再逐筆合併上游）
1. **先補回歸測試**：拖入（搬移、複製、捷徑）、鎖定、縮放與吸附、拆除面板、分頁、重新命名；沿用 `tools/` 既有隔離驗收腳本擴充。
2. **第一步只做 `partial class` 機械切分**（不改任何方法內容與簽名）：依上表分區搬出其餘 86 個方法。
3. **第二步才處理 `CreateFrame`**：依上節標示逐段抽成私有方法，區域變數改以明確參數或小型內容物件傳遞；一次一段、一個提交、建置加全部測試通過才繼續。
4. 全程不夾帶功能修改；`dynamic frame` 的存取在拆檔後編譯器不會報錯，必須以測試確認。

## 修改 `FrameManager.cs` 的最小驗證
`MSBuild` Release 建置 → `tools/run-all-tests.ps1 -BinaryDir <成品目錄>` → `tools/test-managed-storage-fence.ps1 -BinaryDir <成品目錄>` → `tools/verify-localization.ps1`；涉及畫面行為者另需在桌面實測。
