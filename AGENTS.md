# DesktopFramesPlus 專案邊界與維護規範

本專案源自 `limbo666/DesktopFramesPlus`，現已**獨立維護**（與上游分叉，不再逐筆合併），以台灣繁體中文 (zh-TW) 介面為主。上游僅作參考，見 `docs/specs/UPSTREAM_REFERENCE.md`。

## 1. 不可觸碰之核心架構與邊界
- **`FrameManager.cs` 不得在缺少回歸測試時重構或拆檔**：該模組深度依賴 Win32 視窗控制代碼（HWND）、訊息攔截（WndProc）與即時座標計算，且 `dynamic` 存取在拆檔後編譯器不會報錯。檔案已無上游合併顧慮，**允許在補齊回歸測試後**以 `partial class` 機械切分；切分順序與驗證方式見 `docs/specs/FRAMEMANAGER_MAP.md`。平時修改請先讀該文件的區塊地圖，只動相關區塊。
- **維持設定檔向下相容性**：保留現有 `options.json`、`frames.json` 與 `MasterOptions.json` 基本架構。允許對單一 Frame 新增具向前相容之選擇性屬性（如專用收納柵欄之 `IsManagedStorage`、`StorageRootPath`），舊版本或未支援模組讀取時自動忽略，嚴禁破壞性修改或移除既有必要欄位。
- **維持 Portable 可攜性機制**：設定檔集中於 `Profiles/` 或執行檔同層，不得依賴絕對路徑或註冊表儲存核心配置。

## 2. 上游同步與發行責任原則
- **分支定位**：獨立維護的繁體中文分支；上游有值得的修正時，評估後手動、選擇性移植。
- **預設停用自動更新**：`RemoteInfoManager.EnableRemoteUpdates` 預設為 `false`，避免使用者被提示升級回上游英文版本或遭受上游遠端停用影響。
- **外部連結維護**：不保留未授權或未關聯的個人捐款連結（如原作者個人 PayPal）；README 與 About 視窗之 GitHub 專案連結指引至當前維護儲存庫。

## 3. 多語系維護規範
- 英文基準：`Code/Desktop Frames/Localization/Strings.resx`。
- 繁中資源：`Code/Desktop Frames/Localization/Strings.zh-TW.resx`。
- 每次移植上游內容或新增字串後，執行 `tools/verify-localization.ps1` 確認繁體中文覆蓋率為 100%。

## 4. 建置前置需求
- 本專案引用 COM 型別程式庫（`IWshRuntimeLibrary` 等），建置時需使用 Visual Studio MSBuild.exe（例如 `MSBuild.exe "Code\Desktop Frames\Desktop Frames.csproj" /p:Configuration=Release`），而非純 .NET Core SDK 之 `dotnet build`。
