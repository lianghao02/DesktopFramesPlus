# HANDOFF

## 核心元資料 (Metadata)

- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：53e2f16（程式／測試基準；文件提交與發布目標以 Git log、v2.9.4-zh-TW tag 為準）
- **Skill Version**：v1.0.0
- **Task Type**：RELEASE / HANDOFF
- **Local Path Hint**：DesktopFramesPlus

## 目前狀態

可交付。使用者確認 Data 主區／分頁拖曳立即清空、點擊等待五次正常；同意提示窗實機確認不阻擋發布，並明確同意列出跨電腦未驗證限制後公開發布。成品 Windows x64，內含 Runtime，版號 2.9.4／2.9.4.0。

## 本輪目標

維持 Data 分類面板＋獨立便箋；舊 Portal 採 B 退場，保留配置與瀏覽。完成最小修正、隔離成品驗收及內含 Runtime 發布。

## 基準與已確認事實 (Baseline & Confirmed Facts)

- Filename 是工作區捷徑副本路徑，不是顯示名稱或原檔目標。仍以 Filename 忽略大小寫比對，相同路徑保留目標第一筆並清除重複，沒有改合併規則。
- 不同來源同名會建立編號副本；相對／絕對路徑仍可能視為不同參照，未新增身分欄位。
- 托盤有呼叫及 Shown／Closed 記錄，但使用者未看見，未證實確切原因。事件不等於肉眼驗收通過。

## 已完成 (Completed)

- Portal 離線時略過視窗、保留配置，不因此存檔刪除；啟動彙整一次非阻塞提示窗，完整路徑可捲動／複製，不自動消失。
- 舊 Portal 保留瀏覽／開啟／排序及面板操作；拖入、剪下、實體改名、刪除停用，方法入口亦防護。沒有遷移或搬動原檔。
- 圖示移除先保存，再清理 Shortcuts 無引用的 .lnk／.url。刪除面板保留 BackupDeletedFrame，只刪配置，不匯出桌面，副本仍保留。
- Data 停用整區 BitmapCache，保留單張快取；更新後完成版面刷新。使用者確認原先不可點擊殘影已消除。
- 新增 Portable-win-x64 發布設定、Runtime 打包檢查及中文路徑隔離驗收；確認實際載入同資料夾 coreclr.dll。
- 測試宿主支援 self-contained 成品；CI 僅有 tools/ci-drafts/test.yml，未啟用。

## 異動檔案 (Changed Files)

FrameManager、IconManager、PortalUnavailableWindow、TrayManager、雙語資源、發布設定、打包／驗收腳本及 README／CHANGELOG。詳細歷史以 Git Commit／Diff 為準。

## 刻意未修改 (Do Not Do / Deliberately Omitted)

- 不重構 FrameManager，不改 frames.json、options.json、MasterOptions.json 結構。
- 不改隱藏屬性，不新增啟閉掃描、搬移或還原；測試全為合成資料，不讀寫個人 Profiles。
- 沒有覆蓋本機既有 dist／Profiles；新版在忽略的 bin/PortablePackage/dist。
- ReleaseItemByPath 已移除，「收納捷徑可安全移出回桌面」不再是驗收要求；匯出與單一捷徑移到桌面另案。
- 不啟用 CI，不重新導入 Portal／Farm 新增入口。

## 尚未完成 (Remaining Work)

- **P1（阻斷）**：無已確認的本輪產品阻斷。
- **P2（重要）**：分頁 Delete 原生焦點、非阻塞提示窗肉眼確認未單獨驗收；使用者同意不阻擋本輪發布。
- **P3（暫緩）**：跨電腦／帳號、Windows 10、ARM64、多螢幕／DPI，以及 CI 草稿是否啟用。

## 驗證結果 (Validation)

### 已執行測試與結果

- Visual Studio MSBuild Release self-contained Publish 通過；實際內含 Microsoft.NETCore.App／Microsoft.WindowsDesktop.App 8.0.31。未宣稱主專案零警告或完整警告基線比對。
- 打包成品 run-all-tests 退出碼 0：panel-tests、56 個代表性斷言、frame-item-transfer 與語系驗證通過。繁中 100%（英文 677／繁中 682 鍵，5 個歷史額外鍵）。
- 成品入口 7 項通過；C 槽中文＋空白路徑 56 個斷言通過，測試及正式 EXE 均確認載入本機 coreclr.dll。
- 首次成品測試因宿主仍相依全域 Runtime 失敗，修正後重跑通過；首次 Runtime 取樣因 Process.Modules 快取失敗，加入 Refresh 後重跑通過。沒有將失敗計為 PASS。
- 使用者實機：主區 Delete 選取／取消／確認／後續焦點正常；主區／分頁拖回立即消失；點擊等待五次正常。合成來源雜湊未變。
- 成品記錄：tools/acceptance-tests/bin/Release/net8.0-windows7.0/中文 驗收-a88dbc07b50f463cab6f46c95181351c/result.txt。跨路徑記錄：OS 暫存 DesktopFrames 可攜驗收 0eda75443c614d4d82fb7a364755ca5d/runtime-evidence.txt；均未提交。
- PowerShell 語法、差異空白及敏感資訊檢查通過。ZIP 480 個檔案，無 Profiles、測試宿主、驗收標記、PDB 或記錄檔。

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
- Push：本次已獲公開發布授權；實際結果以 origin/main 與 Release 為準。
- Working Tree：文件提交後核對 Clean。
- Branch：main。

## 下一步建議動作 (Next Recommended Action)

完成公開 Release 後停止擴大修改。新使用者解壓縮完整 ZIP 再執行 Desktop Frames.exe；更新者先退出程式、備份設定，勿刪自己的 Profiles。

## 發布狀態 (Release Status)

可發布（使用者明確接受列明跨電腦未驗證限制）。v2.9.4-zh-TW，ZIP 85,402,348 bytes，SHA256：748266EA27719487CDE59C5F1AC611B1E1F91081717BCB296F3E0573178E533E。公開結果查 GitHub Release，不把本機產物當作已上傳。
