# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：`lianghao02/DesktopFramesPlus`
- **Branch**：`main`
- **Commit SHA**：提交前基準 `f69ac3c7d80135353332a9bd2c4d6e2fec3c4ec4`；本輪進度隨此文件提交，實際最新 SHA 以 `git log -1` 為準。
- **Skill Version**：`lianghao-development v1.0.0`
- **Task Type**：IMPROVE / HANDOFF
- **Local Path Hint**：`DesktopFramesPlus`

## 目前狀態
進行中，正式整合待操作驗收；不可宣稱本輪完成或可發布。
2026-10-03 使用者確認沙盒拖入、跨框、拖出與整框移動正常，隨後回報邊界歸屬與排列問題，要求框內圖示「乖乖排好隊」。目前尚未實作格子排列。
最新正式程式 MSBuild Release 已重新建置通過（退出碼 0），仍待完整操作驗收。沙盒仍在執行，不擅自結束或替換該執行檔。

## 本輪目標
完成正式程式的手動農場柵欄；原檔不搬移、不複製、不刪除、不建立影子入口。沿用同一套沙盒核心。
新增使用者回饋：清楚的可容納範圍、靠邊不誤解除、手動加入後整齊排列。框外桌面重排範圍待確認。

## 基準與已確認事實 (Baseline & Confirmed Facts)
- 開始時 main 與 origin/main 相同，Working Tree Clean，正式版本 2.8.1。
- 舊原型的 IFolderView GUID 錯誤，索引與座標不是永久身分；本輪改用 Shell parsing name。
- 原生桌面「自動排列圖示」會把移動要求拉回。使用者已自行取消此 Windows 選項；程式沒有改動 Windows 設定。
- 本輪以原生 Shell 定位 API 改動圖示位置，不改動實體檔案。實際 bounds 由原生 ListView 取得，不固定假設 72×72。
- 舊 HANDOFF 所列「全數實測完成、可交付、零警告、零注入」不能作為本輪驗證依據；已以實際證據取代。

## 已完成 (Completed)
- 穩定 Shell 身分、真實範圍、實體座標與位置讀回。
- 觀察原生滑鼠／Esc 完成拖曳，交叉確認桌面接收、選取及穩定位置；不攔截原生接收。
- 透明內部穿透 Explorer；外框移動／縮放與命名、明確取消、退出保存分離。
- 單一歸屬及讀取失敗禁止覆寫、重複 Stop 防清空、失效 Shell 身分解除。
- 共享核心移入正式程式 FarmFences，原沙盒使用 Compile link；沒有建立第二套引擎。
- 正式系統匣「新增農場柵欄」，啟動／退出及工作區切換串接；新資料為工作區內 farm-fences.json。
- 新 UI 字串納入英文／繁中資源。
- 新增原生四類資產與設定保存回歸測試。

## 異動檔案 (Changed Files)
- `Code/Desktop Frames/FarmFences/`：DesktopInterop、DesktopDragMonitor、FenceWindow、FenceManager、FenceText、FarmFenceHost。
- `Code/Desktop Frames/App.xaml.cs`、`ProfileManager.cs`、`TrayManager.cs`：最小生命周期與入口串接。
- `Code/Desktop Frames/Localization/Strings.resx`、`Strings.zh-TW.resx`。
- `tools/sandbox/`：App、ControlPanelWindow.xaml.cs、csproj、interop 測試腳本；NativeFixtureTests、StateRegressionTests。
- 舊沙盒三個核心檔已移入正式模組，不是刪除功能。
- `IMPLEMENTATION_PLAN.md`、`HANDOFF.md`。

## 刻意未修改 (Do Not Do / Deliberately Omitted)
- FrameManager、IconDragDropManager、Portal、便箋核心、舊 JSON、Shortcuts、更新機制。
- 不變更 Windows 自動排列設定；不全面重排框外圖示。
- 原任務禁止提交；使用者於 2026-10-03 下班前明確授權提交並推送開發進度。不發布 Release、不新增第二套原型。

## 尚未完成 (Remaining Work)
- **P1 (阻斷/必須)**：處理使用者回報的框內排列及邊界問題；確認框外範圍；完整正式操作驗收。
- **P2 (重要/當次)**：執行 StateRegressionTests；移動失敗與退出途中保存回歸；Esc／其他程式接收、取消全部與重啟。
- **P3 (改善建議/暫緩)**：不得追加未授權功能。

## 驗證結果 (Validation)
### 已執行測試與結果
- 真實 Shell 枚舉 19 個原生項目：穩定身分與實際範圍。
- 四類新建測試資產（中文文字、資料夾、lnk、url）位置移動及回復 PASS；檔案雜湊與屬性未變。只清除本次建立的測試資產。
- 使用者沙盒操作確認：拖入 A、A 到 B、拖回桌面、整框帶圖示移動；後續另回報邊界與排列問題，故非全項驗收 PASS。
- 正式 Visual Studio MSBuild Release：最新原始碼重新建置退出碼 0；原有大量警告，沒有聲稱零警告。
- 共享核心沙盒 Verify 建置：0 警告、0 錯誤。
- 語系驗證：英文 650 鍵，繁中 655 鍵，覆蓋 100%；5 個額外鍵是既有差異。
- 舊圖示轉移回歸已獨立重跑：退出碼 0，來回、重新載入、殘留、去重、其他圖示保留、同清單及舊物件防護通過。
- git diff --check 未見空白錯誤（有 LF/CRLF 提醒）。

### 尚未驗證項目
正式入口及工作區共存、重新啟動恢復、取消 0/1/N、讀取失敗測試執行、Esc、拖至其他程式、Explorer 重啟、強制終止、同名公用桌面、OneDrive、Windows 10、混合 DPI、多螢幕及全部既有功能回歸。
測試程式建置成功不代表操作已測。

### 已知風險 (Known Risks)
- 桌面自動排列與對齊格線可能拒絕任意圖示座標，須讀回驗證及可理解警示。
- 目前 Drop 以圖示範圍中心命中；邊緣容納與框內排隊尚未實作。
- 不以不透明底色蓋住原生圖示來美化，避免破壞穿透。
- 原生桌面不在自動操作工具可選視窗，需要使用者實際操作確認。

## Git 狀態
- Commit：本文件與本輪程式作為開發進度提交，實際 SHA 請執行 `git log -1 --oneline`。
- Push：使用者已授權推送至 `origin/main`；最終成功結果由本輪結案回報確認。
- Working Tree：整理時 Modified；提交後以 `git status` 確認。
- Branch：`main`

## 下一步建議動作 (Next Recommended Action)
使用者最新要求：框內外都自動排列、圖示多時往下增高、解決柵欄重疊。尚未實作這三項，不可誤認已完成。
已評估：Windows 原生自動排列作用於整個 Explorer 桌面，不能直接分開管理框內外；可能改由程式分區排列，但需使用者確認，不能擅自全面重排桌面。
建議：框內格子包含圖示／標籤與留白，寬度固定、增加列時往下增高；螢幕／工作列／其他柵欄是擴張上限。移出先不自動縮小。拖動、縮放與增高不得侵入其他柵欄；移動外框不改歸屬。
容量不足時的本次加入回復／提示，以及既有重疊資料處理，仍需鎖定與實測。
再更新同一套核心、補回歸、建置並請使用者結束舊沙盒後驗收正式程式，不重開第二套原型。

## 回家接續方式
1. 在家若已 Clone：先 `git status`。僅在工作區乾淨且無本機分歧時執行 `git switch main`、`git pull --ff-only origin main`；有修改不要覆寫、reset 或強制同步。
2. 若尚未 Clone：`git clone https://github.com/lianghao02/DesktopFramesPlus.git`，進入專案。
3. 先讀專案 AGENTS.md、本 HANDOFF.md、IMPLEMENTATION_PLAN.md；以 `git log -1` 確認版本。
4. 正式程式需 .NET 8 SDK 與 Visual Studio 2022／Build Tools MSBuild（含專案原有 Windows／COM 建置元件）。用 Developer PowerShell 執行 `MSBuild "Code/Desktop Frames/Desktop Frames.csproj" -p:Configuration=Release`。
5. 執行 `pwsh -File tools/verify-localization.ps1`；圖示轉移回歸：`pwsh -File tools/test-frame-item-transfer.ps1 -BinaryDir "Code/Desktop Frames/bin/Release/net8.0-windows7.0"`。
6. 原沙盒：`dotnet build tools/sandbox/FarmFenceSandbox.csproj -c Release`；測試參數為 `--test`、`--test-move`、`--test-state`。後兩者須在可操作的真實桌面驗證；測試成功不得只看建置。
7. 原始碼、測試與文件同步；bin、obj、Log、桌面檔案、Profiles／便箋與面板的本機資料不在本次提交內。家中須自行建置，不會取得辦公電腦的最新個人桌面配置。

## 發布狀態 (Release Status)
不可發布，待本輪新增回饋處理與正式操作驗收。
