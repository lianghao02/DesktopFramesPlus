# FrameManager 檔案地圖

用途：讓人與 Agent 不必整份閱讀也知道「改哪一類功能該看哪個檔案、要跑哪些驗證」。
基準：第一階段機械分檔完成（2026-10-22，提交 fe94610）；第二階段進行中（`CreateFrame` 已抽出第一段，提交 9271e85）。`Framemanager` 是 `public static partial class`，分散在下列 15 個檔案（皆在 `Code/Desktop Frames/`）。**行數只供參考，一律以方法名稱為準。**

## 分檔規則
- 每個分檔的 `using` 區塊必須與 `FrameManager.cs` **逐字相同**。檔頭含 `using static System.Net.Mime.MediaTypeNames;`（引入 `Application`、`Image`、`Text`）與 `using Color = System.Drawing.Color;`，少一行或多一行都可能讓同一段程式解析到不同型別，編譯器不一定報錯。
- 新增方法放進職責相符的分檔；共用靜態欄位、結構與 Win32 宣告一律留在 `FrameManager.cs`。
- 不新增只放一兩個方法的分檔；職責不明時先放核心檔，再評估。

## 檔案一覽
| 檔案 | 約略行數 | 方法 | 修改時要注意 |
|---|---|---|---|
| `FrameManager.cs`（核心） | 3,870 | 共用欄位、`POINT`／`MINMAXINFO`、`SetWindowPos`、**`CreateFrame`（約 3,350 行）** | 見下節；檔尾約 370 行為整段註解掉的舊程式碼 |
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
| `FrameManager.FrameBuild.cs` | 457 | `FrameBuildContext`（`CreateFrame` 各步驟共用資料）、`BuildFrameBorder`、`BuildMenuIcon`（♥）、`BuildLockIcon`、`BuildTitleGrid`、`BuildTitleLabel` | 自 `CreateFrame` 逐字抽出；會被事件處理器捕捉且之後重新指派的變數（目前只有 `CommitRename`）必須放在 `FrameBuildContext`，不可改成參數 |

## `CreateFrame`（核心檔，第二階段拆解對象）
外框、♥、鎖頭、標題列底板與標題文字已抽到 `FrameManager.FrameBuild.cs`。其餘仍在方法內，有 57 個區域變數（13 個橫跨 800 行以上，例如 `win`、`cborder`、`dp`、`titletb`、`titlelabel`、`CommitRename`、`lockIcon`、`wpcont`）與 83 個直接捕捉這些變數的事件處理器。依程式順序大致為：

1. 標題輸入框與 `FrameBuildContext` 建立
2. 面板右鍵選單項目（自動捲動、永遠置頂、清除失效捷徑、開啟資料夾、貼上、外掛設定、刪除面板、「更多」子選單）
3. 自動隱藏掛鉤、Ctrl 覆蓋層（`ToggleOverlays`）、原生鍵盤掛鉤
4. 自動捲動計時器
5. 訊息掛鉤（限制視窗大小）與狀態修正
6. 選單開啟時的動態狀態更新
7. 篩選 UI
8. 重新命名的集中邏輯
9. 硬體加速與安全剪裁
10. 空白區點擊與 Delete 鍵行為

在方法內以上述註解或區域變數名稱搜尋定位。部分註解有編碼損毀（亂碼），不影響執行，閱讀時請勿誤判。

## 目前的保護網
- 自動化：`tools/run-all-tests.ps1`（225 項）＋`tools/test-managed-storage-fence.ps1`（10 項）。重構相關的特徵化測試在 `tools/acceptance-tests/FrameManagerHelperChecks.cs`（純函式，以反射依方法名稱呼叫）與 `FrameManagerBehaviorChecks.cs`（資料層、視窗層、拖放、選單黃金檔）。**改私有方法名稱時要同步更新這兩個檔案。**
- 選單結構以 `tools/acceptance-tests/golden/*.txt` 比對；**刻意**修改選單時，依失敗訊息取得 `actual-*.txt`，人工確認後覆蓋黃金檔並提交。
- 無法自動化的部分列在 `IMPLEMENTATION_PLAN.md` 的「手動驗收清單」。
- 還原點標籤：`restore/2026-10-21-before-framemanager-work`、`restore/2026-10-22-before-split`。

## 修改 FrameManager 任一分檔的最小驗證
`MSBuild` Release 建置 → `tools/run-all-tests.ps1 -BinaryDir <成品目錄>` → `tools/test-managed-storage-fence.ps1 -BinaryDir <成品目錄>` → `tools/verify-localization.ps1`；涉及畫面行為者另需在桌面實測。
