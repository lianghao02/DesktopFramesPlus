# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：aac1e86（文件提交前程式／測試基準；文件提交自身 SHA 以 git log 為準）
- **Skill Version**：v1.0.0
- **Task Type**：FIX / HANDOFF
- **Local Path Hint**：DesktopFramesPlus
- **Version**：2.9.4／2.9.4.0，未升版

## 目前狀態
收尾修正與獨立打包自動驗收完成，待使用者實機驗收。不得將自動化通過當成實體滑鼠、焦點或跨電腦驗收完成。

## 本輪目標
維持 Data 分類面板＋獨立便箋；舊 Portal 採 B 退場，保留配置與瀏覽，補強合成資料驗收，不擴大重構。

## 基準與已確認事實 (Baseline & Confirmed Facts)
- 舊 Portal 無法存取路徑時，載入、建立及初始化例外三處原本會刪配置／存檔，現均保留。
- Filename 保存的是項目的捷徑路徑，通常為相對工作區的 Shortcuts/名稱.lnk 或 .url；部分舊記錄可能是絕對路徑。不是顯示名稱，也不是捷徑目標。
- 新加入同檔名來源會產生編號副本。不同副本路徑不會因同顯示名稱合併。
- FrameItemTransfer 仍以 Filename 字串忽略大小寫比較；未新增身分欄位或路徑正規化。相同路徑時保留目標第一筆的 DisplayName／圖示／參數，清除來源同路徑記錄與目標多餘記錄。這是現行規則，未改變。
- RemoveSelectedIconFromFrame 的鍵盤確認框及 Windows 焦點無法由直接呼叫資料移除函式取代驗收。

## 已完成 (Completed)
- 缺少 Portal 路徑：略過建立視窗，保留名稱、位置與設定，不因此 SaveFrameData；啟動彙整一次托盤通知。自訂樣式重建前也先檢查路徑並通知，不清空目前畫面。
- 舊 Portal：保留瀏覽、開啟、排序及面板移動／縮放／改名。剪下、實體改名、刪除選單停用，修改方法另有入口防護；拖入拒絕並提示。沒有遷移原檔，沒有大段刪除歷史程式。
- 單張圖示：右鍵移除與 Delete 共用記錄移除流程。先保存，再檢查已保存配置所有主區／分頁引用，僅清理工作區 Shortcuts 內無引用的 .lnk／.url。
- 刪除面板：保留 BackupDeletedFrame，只移除配置、關閉視窗；舊 ExportShortcutsOnFrameDeletion 不再觸發桌面匯出。Shortcuts 副本仍保留，並非本輪要掃描清除的垃圾。
- 補強初始化前隔離根目錄確認、合成資料測試、成品 DLL 雜湊比對、tools/run-all-tests.ps1。
- package-release 支援 OutputRoot，讓打包驗收只寫獨立 dist，不更新正式 dist 或讀取其 Profiles。
- tools/ci-drafts/test.yml 僅為草稿，不在 .github/workflows，未啟用 CI。

## 異動檔案 (Changed Files)
- FrameManager.cs、PortalFrameManager.cs、CustomizeFrameFormManager.cs、兩份語系資源。
- tools/acceptance-tests、panel-tests 與測試啟動器、run-all-tests.ps1、package-release.ps1、ci-drafts/test.yml。
- README、CHANGELOG、HANDOFF。

## 刻意未修改 (Do Not Do / Deliberately Omitted)
- 不改 frames.json、options.json、MasterOptions.json 結構；新通知狀態只在記憶體。
- 不使用隱藏屬性、不新增啟閉桌面掃描、搬移或還原。
- 不操作真實 Profiles，不套用正式 dist，不 push、不升版、不啟用 CI。
- 「已收納捷徑可安全移出回桌面」不再是現行驗收要求：ReleaseItemByPath 已移除，屬有意取捨，不是跨面板轉移失敗。
- 使用者提出的刪除前匯出選擇／單一捷徑移到桌面另案討論。沒有來源紀錄時只能稱匯出，不能保證精確還原。
- 舊原生農場測試原碼保留；總測試預設只走正式 Data 路徑，不啟動 Explorer 接管。歷史沙盒不算本輪 PASS。

## 尚未完成 (Remaining Work)
- **P1**：目前無已確認的本輪產品阻斷；最新成品回歸須通過才提交。
- 最新獨立打包總回歸已通過，程式與測試分目的提交完成。
- **P2**：下列手動驗收尚待使用者操作；不能宣布完整實機驗收完成。
- **P3**：日後可清理 Portal 停用的剪下／RenameItem／DeleteItem 實作與 FrameManager Portal Move/Copy 分支；本輪不刪除。
- CI 草稿要不要啟用，另由使用者決定。

## 驗證結果 (Validation)
### 已執行測試與結果
- MSBuild 主專案獨立 Release 建置通過；保留既有警告，未做無關整理。
- 最終 Visual Studio MSBuild 使用 GenerateSatelliteAssembliesForCore=true；原 al.exe 語系編譯停滯四分鐘，僅回收已核對的本輪程序後重試成功。這是執行參數，未寫入 csproj。沒有宣稱零警告或完整警告基線比對。
- 第一輪總測試通過：正式 Data 視窗建立、三輪主區與分頁 A→B→A 每步存讀、隱藏找回、離線 Portal、去重現行規則、捷徑清理、配置刪除、便箋保存／損毀防覆寫。
- Portal 私有改名／刪除入口與合成 RaiseEvent 拖入拒絕已通過，包含 UseRecycleBin=false。
- 在地化：英文基準 676 鍵，繁中 681 鍵，覆蓋率 100%；額外 5 個舊鍵保留。
- 中間測試有 WPF DragEventArgs 建構方式錯誤，修正後重跑通過；沙箱 SDK 讀取拒絕改以核准執行，不列成功測試。
- 測試入口失敗傳回非零已由上述失敗實際驗證。
- 隔離資料／記錄位於 tools/*/bin 下的驗收 session，Git 忽略，不提交。
- 最終獨立 dist 驗收 44 個功能斷言全部通過，panel-tests、圖示轉移回歸及在地化均 PASS，總測試退出碼 0；兩個測試專案建置均 0 errors／0 warnings。
- 獨立打包成品：Code/Desktop Frames/bin/ClosurePackage/dist/DesktopFramesPlus；驗收記錄：tools/acceptance-tests/bin/Release/net8.0-windows7.0/中文 驗收-47230473ca5c4e688dfc08a45d38883b/result.txt。
- PowerShell 語法解析、git diff --check 與本輪差異敏感資訊檢查通過。
### 尚未驗證項目
- Windows 10、其他電腦、多螢幕／DPI，以及正式入口全部互動。
- 原生鍵盤 Delete 焦點、啟動時托盤通知實際顯示及滑鼠等待延遲。
### 手動驗收清單
僅使用獨立成品及合成檔案，不載入個人 Profiles。
1. **Delete**：加入合成文件的參照，單擊選取後按 Delete；取消確認應完全不變；同意後圖示消失、無引用副本清理，合成來源內容不變。主區／分頁各測一次。確認後可選取其他圖示，不殘留焦點或選取。
2. **點擊開啟等待**：加入合成 .lnk／.url，點擊啟動時立即移動滑鼠經過鄰近圖示並放開；連測五次。等待期間不能抓住其他圖示、出現延遲拖曳或非預期排序；之後正常拖曳仍可用。記錄目標、耗時及是否可重現，不預先宣稱毫秒改善。
3. **實際拖曳與右鍵移動**：主區與分頁 A→B→A 各三輪，每輪重啟；只出現一份，來源無殘留，名稱、參數及順序正確。
4. **離線 Portal 通知**：在隔離配置放兩個不存在路徑的 Portal，啟動應只通知一次、列出名稱與路徑，Data／便箋仍能操作；略過面板配置仍在。建立合成資料夾後重啟，面板恢復。Windows 可能抑制托盤通知，日誌應保留逐筆路徑。
5. **Portal 唯讀**：圖示與詳細資料兩種視圖檢查剪下／改名／刪除均停用；拖入合成檔案被拒絕。可開啟原檔，檔案總管仍可正常管理；不宣稱外部應用程式也唯讀。
6. **刪除面板**：取消不變；確認只刪配置，原檔不變，不新增桌面圖示；刪除備份保留。
7. **面板找回／重啟**：隱藏全部後顯示；關閉重啟後確認位置、尺寸、項目與順序一致。

### 已知風險 (Known Risks)
Filename 字串比對不會把相對／絕對路徑的同一檔案自動合併；無來源身分紀錄，未宣稱可辨識原桌面捷徑。

## Git 狀態
- Commit：a968625（Portal）、c74b030（圖示清理／配置刪除）、aac1e86（測試）；本文件提交另見 git log。
- Push：否。
- Working Tree：提交前及最終狀態另核對。
- Branch：main。

## 下一步建議動作 (Next Recommended Action)
使用者依手動清單驗收；確認後再決定正式 dist 套用／推送，不得誤將現有正式 dist 當成本輪新版。
主專案只用 Visual Studio MSBuild；沒有 COM 的測試專案可用 dotnet build，僅 HintPath 引用已建置 DLL。
執行 tools/run-all-tests.ps1 -BinaryDir <隔離成品目錄>；任何一組失敗非零退出。

## 發布狀態 (Release Status)
未發布，正式 dist／ZIP 未更新。自動驗收不等於完整實機驗收；待使用者確認。
