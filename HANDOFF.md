# HANDOFF

## 核心元資料 (Metadata)

- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：7ccac7e（已公開的 v2.9.4-zh-TW；本輪修正尚未提交）
- **Skill Version**：v1.0.0
- **Task Type**：FIX / HANDOFF
- **Local Path Hint**：19_DesktopFramesPlus

## 目前狀態

本機修正版已完成成品回歸、跨路徑 Runtime 驗證及部署。公開版本仍為 v2.9.4-zh-TW；本輪不升版、不推送、不重新發布。保留先前已驗收的 Data 分類面板與獨立便箋功能；使用者下次冷開機速度仍待量測。

## 本輪目標

修復 frames.json、options.json／MasterOptions.json 載入失敗後覆寫原檔，以及 InterCore 重複事件訂閱／計時器；加入啟動分段計時，先取得數據，不重構載入架構。

## 基準與已確認事實 (Baseline & Confirmed Facts)

- Filename 是工作區捷徑副本路徑，不是顯示名稱或原檔目標。仍以 Filename 忽略大小寫比對，相同路徑保留目標第一筆並清除重複，沒有改合併規則。
- 不同來源同名會建立編號副本；相對／絕對路徑仍可能視為不同參照，未新增身分欄位。
- 托盤有呼叫及 Shown／Closed 記錄，但使用者未看見，未證實確切原因。事件不等於肉眼驗收通過。

## 已完成 (Completed)

2026-10-12 工作區編號：本機資料夾改為 `19_DesktopFramesPlus`；GitHub origin 與 main 追蹤維持原設定。更名前後 HEAD、既有未提交修改與保護檔案雜湊核對通過；GitHub main 可存取且與本機 HEAD 一致。前輪工作完整保留；本輪僅提交與推送編號紀錄，前輪功能修改保留在本機。

- 本輪：損毀、空檔、null、錯誤 JSON 型別及讀取鎖定後，按完整路徑保護設定，只有成功重新載入才解除。面板啟動不得回退寫入預設配置；快捷鍵同步也略過受保護的選項檔。所有保護狀態只存在記憶體，不改 JSON 結構。
- 本輪：InterCore 初始化具冪等性，使用具名事件處理函式；清理確實停止計時器、解除 Tick 與快捷鍵訂閱，之後仍可重新初始化。
- 本輪：新增工作區、互動核心、桌布／系統匣、面板、全域監聽與便箋的毫秒級計時。沿用記錄日誌／Info／一般類別設定，不強制開啟日誌；首次 UI 閒置不等於 Windows 冷開機完成時間。
- 本輪：啟動遇到設定讀取失敗時彙整一次雙語警告；不把讀取失敗默默視為正常空設定。
- 以下為已發布版本的保留成果：
- Portal 離線時略過視窗、保留配置，不因此存檔刪除；啟動彙整一次非阻塞提示窗，完整路徑可捲動／複製，不自動消失。
- 舊 Portal 保留瀏覽／開啟／排序及面板操作；拖入、剪下、實體改名、刪除停用，方法入口亦防護。沒有遷移或搬動原檔。
- 圖示移除先保存，再清理 Shortcuts 無引用的 .lnk／.url。刪除面板保留 BackupDeletedFrame，只刪配置，不匯出桌面，副本仍保留。
- Data 停用整區 BitmapCache，保留單張快取；更新後完成版面刷新。使用者確認原先不可點擊殘影已消除。
- 新增 Portable-win-x64 發布設定、Runtime 打包檢查及中文路徑隔離驗收；確認實際載入同資料夾 coreclr.dll。
- 測試宿主支援 self-contained 成品；CI 僅有 tools/ci-drafts/test.yml，未啟用。

## 異動檔案 (Changed Files)

本輪：App、FrameDataManager、FrameManager、SettingsManager、InterCore、雙語資源、隔離驗收 Program 與 HANDOFF。README 已有先前入口校準修改，未覆寫。已發布版本的其他修改以 Git 歷史為準。

## 刻意未修改 (Do Not Do / Deliberately Omitted)

- 不重構 FrameManager，不改 frames.json、options.json、MasterOptions.json 結構。
- 不改隱藏屬性，不新增啟閉掃描、搬移或還原；測試全為合成資料，不讀寫個人 Profiles。
- 本次依使用者要求將已發布成品同步至根目錄 dist/DesktopFramesPlus；只更新程式檔，沒有覆蓋 Profiles、Shortcuts 或其他個人設定。發布中間產物不再作為日常入口。
- ReleaseItemByPath 已移除，「收納捷徑可安全移出回桌面」不再是驗收要求；匯出與單一捷徑移到桌面另案。
- 不啟用 CI，不重新導入 Portal／Farm 新增入口。

## 尚未完成 (Remaining Work)

- **P1（阻斷）**：本輪已確認的設定覆寫與重複初始化問題均已修正，成品隔離回歸通過。
- **P2（重要）**：分頁 Delete 原生焦點、非阻塞提示窗肉眼確認未單獨驗收；使用者同意不阻擋本輪發布。
- **P3（暫緩）**：跨電腦／帳號、Windows 10、ARM64、多螢幕／DPI，以及 CI 草稿是否啟用。

## 驗證結果 (Validation)

### 已執行測試與結果

- 本輪紅燈基準：隔離的舊 dist 成品通過原有 56 個斷言，但新增的損毀 frames.json 防覆寫斷言失敗，確實重現缺陷。
- 本輪第一版：Visual Studio MSBuild Release 成功；run-all-tests 通過，包括損毀／鎖定／恢復保存、啟動預設配置防護與 InterCore 重複初始化。測試只用合成資料及 .acceptance-session 目錄。
- 初次測試被 Windows SDK 讀取權限阻擋；以允許讀取 SDK 的執行方式重跑後才列入結果。主專案未使用 dotnet build。
- 最終 self-contained Publish：0 errors；有既有 COM／Nullable 等警告，不宣稱零警告或已完成前後警告基線比對。
- 最終打包成品 run-all-tests 退出碼 0：panel-tests、103 個代表性斷言、圖示轉移與在地化均通過；英文 678／繁中 683 鍵，100% 覆蓋率。證據：tools/acceptance-tests/bin/Release/net8.0-windows7.0/中文 驗收-34e5a99ee08b4d3bb23446b030519210/result.txt。
- 使用者結束正式版後，本輪同步根目錄 dist：480 個程式檔與已測成品 SHA256 一致，129 個原有其他檔案（含設定／捷徑）雜湊不變。回復備份：Code/Desktop Frames/bin/StartupSafetyBackup-cf526f8e7b134cf7ab20f8f0e5aa29c0/DesktopFramesPlus。未啟動真實 Profiles 作為測試。
- C 槽中文／空白路徑：103 個合成斷言通過，確認載入成品自身 coreclr.dll。證據：OS 暫存 DesktopFrames 可攜驗收 0fdd358ea95a4b0abad24171474a8f78/runtime-evidence.txt。
- 部署後再次以根目錄 dist 為 DLL 來源執行隔離驗收：103 個斷言通過，載入 DLL 雜湊與正式入口相符。證據：tools/acceptance-tests/bin/Release/net8.0-windows7.0/中文 驗收-3364b10b52ff4082bc323f6213e8f59a/result.txt。
- 以下為已發布版本的歷史驗收：
- Visual Studio MSBuild Release self-contained Publish 通過；實際內含 Microsoft.NETCore.App／Microsoft.WindowsDesktop.App 8.0.31。未宣稱主專案零警告或完整警告基線比對。
- 打包成品 run-all-tests 退出碼 0：panel-tests、56 個代表性斷言、frame-item-transfer 與語系驗證通過。繁中 100%（英文 677／繁中 682 鍵，5 個歷史額外鍵）。
- 成品入口 7 項通過；C 槽中文＋空白路徑 56 個斷言通過，測試及正式 EXE 均確認載入本機 coreclr.dll。
- 首次成品測試因宿主仍相依全域 Runtime 失敗，修正後重跑通過；首次 Runtime 取樣因 Process.Modules 快取失敗，加入 Refresh 後重跑通過。沒有將失敗計為 PASS。
- 使用者實機：主區 Delete 選取／取消／確認／後續焦點正常；主區／分頁拖回立即消失；點擊等待五次正常。合成來源雜湊未變。
- 成品記錄：tools/acceptance-tests/bin/Release/net8.0-windows7.0/中文 驗收-a88dbc07b50f463cab6f46c95181351c/result.txt。跨路徑記錄：OS 暫存 DesktopFrames 可攜驗收 0eda75443c614d4d82fb7a364755ca5d/runtime-evidence.txt；均未提交。
- PowerShell 語法、差異空白及敏感資訊檢查通過。ZIP 480 個檔案，無 Profiles、測試宿主、驗收標記、PDB 或記錄檔。
- 本機正式 dist 部署：先備份，再同步已驗證成品；480 個程式檔 SHA256 與發布成品一致，129 個原有非成品檔案（含設定／捷徑）雜湊未變。根目錄 dist 的 ZIP 與公開下載 SHA256 一致。未啟動真實設定做測試。
- 可回復備份位於忽略的 Code/Desktop Frames/bin/LocalDistBackup-dba2f470f27d447bb17de17b22b82ab7。PortablePackage 暫存本次未刪除；若日後清理，只刪已確認不含個人資料的產物，不刪正式 dist 或備份。

### 尚未驗證項目

無全域 .NET 的乾淨虛擬機、第二台電腦、不同 Windows 帳號、Windows 10、ARM64、多螢幕／DPI 尚未實測。本機載入 bundled Runtime 不等於所有環境驗證。

### 手動驗收清單

僅在隔離目錄使用合成檔案：

1. 主區／分頁 Delete：取消不變；確認移除，來源雜湊不變，無引用副本清理，後續焦點正常。
2. 點擊捷徑等待開啟時移動滑鼠經過鄰圖示並放開，五次無誤拖曳，測後仍可正常拖曳。
3. 主區／分頁 A→B→A 三輪，每輪重啟，只有一份，來源立即無殘影，名稱／參數／順序一致。
4. 兩個離線 Portal 啟動只提示一次，完整路徑可捲動／複製，主程式可操作，配置保留；合成資料夾恢復後重啟面板回復。
5. Portal 圖示／詳細檢視均停用剪下、改名、刪除，拒絕拖入，原檔不變。
6. 刪除面板僅刪配置；隱藏找回／重啟後位置、尺寸、歸屬與順序一致。

### 已知風險 (Known Risks)

自動測試不取代 Windows 原生焦點或螢幕像素驗收；企業防毒相容性不作保證。內含 Runtime 隨後續版本維護更新。

## Git 狀態

- Commit：3187a31（殘影）、f53d479（提示）、7fbb090（驗收）、53e2f16（Runtime）；文件提交另見 Git log。
- Push：7ccac7e 已推送；本輪修正不提交、不推送、不發布。
- Working Tree：Modified；包含本輪程式／測試／HANDOFF，以及先前 README 入口校準修改；沒有納入個人資料或建置產物。
- Branch：main。

## 下一步建議動作 (Next Recommended Action)

正式入口統一使用根目錄 dist/DesktopFramesPlus/Desktop Frames.exe，已同步本輪修正版，保留個人設定與捷徑。下次啟動若需分析速度，開啟日誌並確認 Info／一般類別；比較 [啟動計時] 各段，再決定是否有必要優化。計時從 Application_Startup 開始，不包含之前的 Runtime 載入／Windows 開機排程，不宣稱已加速。沒有未授權更新啟動登錄項目。

## 發布狀態 (Release Status)

既有公開發布：https://github.com/lianghao02/DesktopFramesPlus/releases/tag/v2.9.4-zh-TW 。公開 ZIP 85,402,348 bytes，SHA256：748266EA27719487CDE59C5F1AC611B1E1F91081717BCB296F3E0573178E533E。本輪修正尚未發布，版號維持 2.9.4；更新後的本機 dist 不等於 GitHub 既有成品，勿誤稱兩者 DLL 雜湊一致。


## 2026-10-12 原始碼同步補充

使用者已授權本輪提交與推送所有專案，先前不提交／不推送的工作範圍限制由本輪授權取代。更新事項已依實際差異補入 CHANGELOG；提交 SHA 與推送結果以 Git log、遠端分支及控制中心同步報告為準。本輪不建立新 Release 或切換正式雲端網站；前述手機、跨帳號、跨裝置與跨電腦的待驗收項目保留。
