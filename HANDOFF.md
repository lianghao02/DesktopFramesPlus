# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Commit SHA：`e087d90`
- Task Type：FIX / ROBUSTNESS / TEST
- Local Path Hint：`DesktopFramesPlus`

## 目前狀態
可交付／已徹底查明並修復右鍵新增框架框選後崩潰的根因，完成動態框架物件安全包裝（`DynamicFrameData`）、預設屬性完整補齊、遮罩關閉時序非同步解耦與全域未處理例外防護，通過 VS 2022 MSBuild Release 建置與迴歸測試套件（100% 通過）。

## 本輪目標
1. **精準定位右鍵新增框架框選後崩潰之原因**：
   - 經由 Windows Application Event Log 與單元迴歸測試，精準定位崩潰根因為 DLR 拋出 `Microsoft.CSharp.RuntimeBinder.RuntimeBinderException: 'System.Dynamic.ExpandoObject' does not contain a definition for 'IsLocked'`。
   - `FrameDataManager.CreateNewFrame` 建立新框架時缺少 `IsLocked` 等二十多個標準屬性，且 `ExpandoObject` 存取未定義成員時 DLR 直接拋出例外導致行程閃退。
2. **核心修復方案**：
   - 在 `FrameDataManager.cs` 實作 `DynamicFrameData`（繼承自 `DynamicObject`，實作 `IDictionary<string, object>`），提供對任何未定義成員安全返回 `null`（與 `JObject` 一致）的安全機制，徹底杜絕 DLR 拋出例外。
   - 完整補齊 `ApplyFrameDefaults` 中缺失的標準屬性（包含 `IsLocked`、`AutoRoll`、`AlwaysOnTop` 等）。
   - `CreateNewFrame` 正確支援 `width` 與 `height` 參數並採納自訂標題。
3. **視窗關閉時序與全域例外防護**：
   - `DrawFrameOverlay.cs` 在 `OnMouseUp` 中透過 `Dispatcher.BeginInvoke` 非同步解耦喚起 `CreateFrameFromDraw`，確保全螢幕遮罩視窗完整銷毀與滑鼠訊息泵處置完成後再彈出模態對話框。
   - `FrameManager.CreateFrameFromDraw` 加上完整 `try...catch` 捕捉。
   - `App.xaml.cs` 增加全域例外處理常式（`DispatcherUnhandledException` 與 `AppDomain.CurrentDomain.UnhandledException`），未處理錯誤寫入 `crash.log` 並提示使用者，杜絕行程直接無聲閃退。
4. **原生面板重複開窗防護**：
   - 在 `CreateFrame` 開頭加入 `FarmFenceHost.IsPanel` 防護，已由原生核心接管之面板不重複建立 WPF 視窗。

## 驗證結果
- **VS 2022 MSBuild Release 編譯**：0 錯誤，1255 警告（正常）。
- **迴歸測試套件驗證（`tools/panel-tests`）**：
  - PASS：已有捷徑資料不轉換、不清除。
  - PASS：既有面板原位與尺寸保留、同一 Id 單一面板、禁止建立捷徑、舊 Items 格式保持空清單。
  - PASS：重啟接管、取消後回桌面歸屬、舊面板紀錄同步移除與其他面板保全。
  - PASS：框選新增框架邏輯、安全動態屬性存取與視窗建立完整驗證通過（Exit Code 0）。
- **打包發布驗證**：`tools/package-release.ps1` 執行成功，最新產物已同步輸出至 `dist/DesktopFramesPlus` 與 `dist/DesktopFramesPlus-v2.8.0-zh-TW.zip`。

## 下一步
1. 提交並推播修復成果至遠端儲存庫。
2. 回報使用者修復診斷根因與驗證結果。

