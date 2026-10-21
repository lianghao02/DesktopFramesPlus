# FrameManager 檔案地圖

用途：讓人與 Agent 不必整份閱讀也知道「改哪一類功能該看哪個檔案、要跑哪些驗證」。
基準：第一階段機械分檔完成（2026-10-22，提交 fe94610）；第二階段完成（`CreateFrame` 已拆成建構步驟，提交 5c1579c）。`Framemanager` 是 `public static partial class`，分散在下列 18 個檔案（皆在 `Code/Desktop Frames/`）。**行數只供參考，一律以方法名稱為準。**

## 分檔規則
- 每個分檔的 `using` 區塊必須與 `FrameManager.cs` **逐字相同**。檔頭含 `using static System.Net.Mime.MediaTypeNames;`（引入 `Application`、`Image`、`Text`）與 `using Color = System.Drawing.Color;`，少一行或多一行都可能讓同一段程式解析到不同型別，編譯器不一定報錯。
- 新增方法放進職責相符的分檔；共用靜態欄位、結構與 Win32 宣告一律留在 `FrameManager.cs`。
- 不新增只放一兩個方法的分檔；職責不明時先放核心檔，再評估。

## 檔案一覽
| 檔案 | 約略行數 | 方法 | 修改時要注意 |
|---|---|---|---|
| `FrameManager.cs`（核心） | 971 | 共用欄位、`POINT`／`MINMAXINFO`、`SetWindowPos`、**`CreateFrame`（約 200 行，依序呼叫下列建構步驟）** | 見下節；檔尾約 370 行為整段註解掉的舊程式碼 |
| `FrameManager.AutoHide.cs` | 343 | `InitializeAutoHideTimer`、`ResetAutoHideTimer`、`WakeUpFrames`、`ExecuteAutoHideSequence`、`RefreshScrollbarSettings`、`RefreshAllIconsTint`、`RefreshAutoRollSettings`、`ForceHideFrames`、`ClearAllTransitionStates` | 計時器與視窗顯示狀態；改完要實測隱藏與喚醒 |
| `FrameManager.Reload.cs` | 573 | `ReloadFrames`、`ReloadSpecificFrames`、`IsUncRoot`、`NavigatePortalFrame`、`RefreshPortalNavBar`、`ShowPortalToast`、`GetPortalFrames` | 舊 Portal 已唯讀退場，勿新增寫入功能 |
| `FrameManager.Screen.cs` | 143 | `GetDpiScaleFactor`、`AdjustFramePositionToScreen`、`ForceRepositionallFrames` | 多螢幕、負座標；`tools/` 內吸附與座標測試 |
| `FrameManager.Delete.cs` | 155 | `DeleteFrameWithConfirmation`、`DeleteFrameConfiguration` | 收納面板拆除須把內容移回桌面且不覆蓋；跑 `test-managed-storage-fence.ps1` |
| `FrameManager.Menus.cs` | 757 | `GetThemedContextMenuStyle`、`BuildHeartContextMenu`、`AttachIconContextMenu`、`UpdateAllHeartContextMenus`、`CreateBasicContextMenu` | 選單文字走資源檔；改選單結構會讓黃金檔失敗（見下方保護網） |
| `FrameManager.WebLinks.cs` | 393 | `CreateWebLinkShortcut`、`ExportAllIconsToDesktop`、`ExtractUrlFromDropData`、`IsValidWebUrl`、`AddUrlShortcutToFrame`、`GetNextDisplayOrder` | 拖入網址的資料格式判斷 |
| `FrameManager.Properties.cs` | 439 | `GetFrameData`、`UpdateFilterHistory`、`ResetAllCustomizations`、`UpdateFrameProperty`、`SetFrameIconSizeLive` | **會寫入 `frames.json`，須維持向下相容** |
| `FrameManager.Tabs.cs` | 351 | `ToggleFrameTabs`、`RefreshFrameContentSimple`、`RemoveItemFromTab`、`CreateTabButtonTemplate`、`TabButton_MouseEnter`、`TabButton_MouseLeave` | 分頁與 Data 面板的資料結構 |
| `FrameManager.Loading.cs` | 940 | `MigrateLegacyJson`、`LoadAndCreateFrames`、`StartDrawMode`、`CreateFrameFromDraw`、`CreateManagedStorageFence`、`AddItemToDataFrame`、`LoadFrameDataFromJson`、`CreateCorruptedFileBackup`、`InitializeDefaultFrame`、`CreateNewFrame` | 讀檔失敗時要保護原設定不被覆寫；`CreateNewFrame` 的收納面板未指定路徑時會寫到真實桌面 |
| `FrameManager.Icons.cs` | 1,500 | `UpdateAdaptiveIconGrid`、`AddIcon`（約 816 行）、`GetScrapedNetworkPath`、`ScrapeLnkForNetworkRoot`、`GetUrlCustomIcon`、`EditItem`、`BackupOrRestoreShortcut`、`RefreshFrameUsingFormApproach`、`FindWrapPanel`、`UpdateIcon`、`RefreshIconClickHandlers`、`RefreshSingleIconComplete`、`UpdateFrameDataForIcon` | 圖示大小、灰階、文字陰影；縮放後殘影問題曾在此修過 |
| `FrameManager.Launch.cs` | 756 | `UpdateOptionsAndClickEvents`、`ClickEventAdder`、`TriggerLaunchEffect`、`LaunchItem`、`IsWebUrl`、`IsSpecialWindowsPath`、`IsControlPanelCommand`、`ExtractUrlFromUrlFile`、`IsNetworkPath` | 單擊／雙擊開啟、特殊路徑與網路路徑判斷 |
| `FrameManager.Selection.cs` | 255 | `UpdateLockState`、`SetSelectedIcon`、`DeselectIcon`、`RemoveSelectedIconFromFrame`、`RemoveDataItem`、`CleanupUnreferencedShortcut` | 鎖頭外觀統一由 `GameTheme.ApplyLockIconStyle` 處理 |
| `FrameManager.Feedback.cs` | 303 | `ShowSizeFeedback`、`HideSizeFeedback`、`UpdateSizeFeedback`、`OnResizingStarted`、`OnResizingEnded`、`WndProc`、`HasDeadShortcuts`、`CheckItemsForDead`、`GetSafeProperty`、`FindVisualParent`、`FindVisualChild` | Win32 訊息與尺寸吸附；失效捷徑檢查為唯讀，不應刪除使用者檔案 |
| `FrameManager.FrameBuild.cs` | 1,093 | `FrameBuildContext`（`CreateFrame` 各步驟共用資料）、`BuildFrameBorder`、`BuildMenuIcon`（♥）、`BuildLockIcon`、`BuildTitleGrid`、`BuildTitleLabel`、`BuildFilterBar`、`BindRenameEvents`、`BindTitleMouseEvents` | 標題列與外框。會被事件處理器捕捉且之後重新指派的變數（`CommitRename`、`TitleLabel`）必須放在 `FrameBuildContext`，不可改成參數 |
| `FrameManager.FrameBuild.Menu.cs` | 889 | `BuildPanelContextMenu`、`BuildPanelMenuItems`、`BindPanelMenuOpened` | 面板右鍵選單；選單結構由黃金檔把關 |
| `FrameManager.FrameBuild.Window.cs` | 970 | `BindWindowStateEvents`、`BindHoverAndAutoRoll`、`ShowWindowWithSizeHooks`、`BuildIconArea`、`InitFrameContent` | 視窗狀態、自動捲起、圖示區與內容載入；**會寫入 `frames.json`** |
| `FrameManager.FrameBuild.DragDrop.cs` | 607 | `BindDragDrop` | 拖放搬移／複製／建立捷徑；改完必須跑拖放測試並手動驗證 Ctrl／Alt 拖入 |

## `CreateFrame` 的建構順序（第二階段完成後）
`CreateFrame` 只負責依序呼叫建構步驟，**呼叫順序就是原本的執行順序，不可任意調換**（例如面板選單項目必須在視窗建立後加入、內容載入必須在拖放與位置事件掛上之後）：

1. 標題輸入框、`FrameBuildContext`、檢查資料夾是否存在
2. `BuildFrameBorder` → `BuildMenuIcon` → `BuildLockIcon` → `BuildTitleGrid` → `BuildPanelContextMenu`
3. 建立 `NonActivatingWindow` → `BuildPanelMenuItems` → `BindWindowStateEvents` → `BindHoverAndAutoRoll` → `ShowWindowWithSizeHooks`
4. `BuildTitleLabel` → `BindPanelMenuOpened` → `BuildFilterBar` → `BindRenameEvents` → 組合標題列與分頁列
5. 依 Id 找回面板資料（找不到即結束）→ `BindTitleMouseEvents` → `BuildIconArea` → `BindDragDrop` → 位置變更事件 → `InitFrameContent`
6. 選取與 Delete 鍵、便箋選單、第二次 `Show()`、彩蛋標題、吸附、色調、啟動目標檢查

抽出的方法開頭以區域別名取得 `frame`、`titletb` 等「建立後不再指派」的變數（例如 `dynamic frame = ctx.Frame;`），方法本文與拆解前逐字相同。部分註解有編碼損毀（亂碼），不影響執行。

## 目前的保護網
- 自動化：`tools/run-all-tests.ps1`（241 項）＋`tools/test-managed-storage-fence.ps1`（10 項）。重構相關的特徵化測試在 `tools/acceptance-tests/FrameManagerHelperChecks.cs`（純函式，以反射依方法名稱呼叫）與 `FrameManagerBehaviorChecks.cs`（資料層、視窗層、拖放、選單與視窗元件結構黃金檔）。**改私有方法名稱時要同步更新這兩個檔案。**
- 選單結構以 `tools/acceptance-tests/golden/*.txt` 比對；**刻意**修改選單時，依失敗訊息取得 `actual-*.txt`，人工確認後覆蓋黃金檔並提交。
- 無法自動化的部分列在 `IMPLEMENTATION_PLAN.md` 的「手動驗收清單」。
- 還原點標籤：`restore/2026-10-21-before-framemanager-work`、`restore/2026-10-22-before-split`、`restore/2026-10-22-before-stage2`。

## 修改 FrameManager 任一分檔的最小驗證
`MSBuild` Release 建置 → `tools/run-all-tests.ps1 -BinaryDir <成品目錄>` → `tools/test-managed-storage-fence.ps1 -BinaryDir <成品目錄>` → `tools/verify-localization.ps1`；涉及畫面行為者另需在桌面實測。
