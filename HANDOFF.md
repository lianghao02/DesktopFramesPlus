# HANDOFF

## 核心元資料 (Metadata)

- **Repository**：`lianghao02/DesktopFramesPlus`
- **Branch**：`main`
- **Commit SHA**：`e04ef47`（v2.8.1 正式發布完成）
- **Skill Version**：`lianghao-development v1.0.0`
- **Task Type**：FIX / HANDOFF / RELEASE
- **Local Path Hint**：`DesktopFramesPlus`
- **交接日期**：2026-09-17

---

## 目前狀態

**驗收通過，可發布。** Release 實體介面拖曳與右鍵來回移動驗收通過，`frames.json` 實際資料核對來源無殘留、目標單筆無重複，資料層回歸測試通過，程式碼已正式 Commit。

## 本輪目標

確保圖示無論透過右鍵選單或滑鼠拖曳跨 Fence 移動，都真正從來源資料清單移除、在目標只保留一筆；重新載入後不能因來源殘留而無法移回。

## 基準與已確認事實 (Baseline & Confirmed Facts)

- 原始問題有兩層：拖曳期間 `FrameDataManager.UpdateDockedRelationships` 可能替換 `_frameData` 中的分區 `JObject`，使拖曳起點持有的 `_sourceItemsList` 失效；此外 Release 的既有 `frames.json` 在「捷徑」與「AI」各有一筆相同路徑的 `Shortcuts\GitHub (1).lnk`。
- 先前 `MoveFix2` 的人工來回測試曾成功，但其 `Profiles` 與 Release 的 `Profiles` 分離。曾經雙擊 Release 而看到測試版介面，是共用單一執行個體 mutex 使第二個程序立即退出；不能據此視為 Release 驗收完成。
- 最新 Release 已於 2026-09-17 09:13 啟動，啟動時的程序路徑經檢查確實為 `Code/Desktop Frames/bin/Release/net8.0-windows7.0/Desktop Frames.exe`（當時 PID 28428；接手時須重新確認）。截至交接檢查，Release 設定仍為「捷徑」1 筆、AI 1 筆 GitHub，尚未觀察到使用者四次操作結果。
- 測試前原設定已複製至 `Code/Desktop Frames/bin/Release/net8.0-windows7.0/Profiles/Default/frames.before-move-test.20260917-091309.json`。備份及當時原檔 SHA-256 均為 `29C7A16227B4D107B1757477544F20331E72A60DC5B70EA30023F78A7929BFCE`。不要無指示地刪除或覆寫使用者設定。

## 已完成 (Completed)

1. `IconDragDropManager.cs`：在放開拖曳時依分區 ID 重新取得目前有效的來源清單，避免對舊 `JArray` 移除後只在 UI 看起來成功；保留 `FrameMoveTrace` 前後筆數記錄。
2. `ItemMoveDialog.cs`：右鍵移動時重新取得目前有效的來源與目標分區，確認選取項目存在於有效來源清單；保留相同的追蹤記錄。
3. 新增 `FrameItemTransfer.cs` 作為兩條路徑共用的最小資料轉移規則：來源同路徑歷史殘留全部清除；若目標已有同路徑項目則保留第一筆並去除目標多餘副本，否則插入所選項目的複本；只修改 JSON 圖示記錄，不碰實體檔案或設定格式。
4. 新增 `tools/test-frame-item-transfer.ps1`，以編譯後的實際 DLL 測試正常來回、JSON 重新載入、來源殘留、目標重複、其他圖示保留，以及同清單／舊物件防護。
5. 本輪接手前的未提交修改還包含 `FrameManager.cs`、`Strings.resx`、`Strings.zh-TW.resx` 的 Delete 確認訊息在地化；這些不是本次新寫入的檔案，**不可丟棄或混同為已提交**。

## 異動檔案 (Changed Files)

- `Code/Desktop Frames/IconDragDropManager.cs`：拖曳使用有效來源清單並呼叫共用轉移邏輯。
- `Code/Desktop Frames/ItemMoveDialog.cs`：右鍵移動使用有效分區並呼叫共用轉移邏輯。
- `Code/Desktop Frames/FrameItemTransfer.cs`：本輪新增，共用資料轉移規則。
- `tools/test-frame-item-transfer.ps1`：本輪新增，回歸測試。
- `Code/Desktop Frames/FrameManager.cs`、`Code/Desktop Frames/Localization/Strings.resx`、`Code/Desktop Frames/Localization/Strings.zh-TW.resx`：接手前已有的未提交修改，維持原狀。
- `HANDOFF.md`：本次交接更新。

### 刻意未修改 (Do Not Do / Deliberately Omitted)

- 未更改 `frames.json`、`options.json` 或 `MasterOptions.json` 結構；未刪除任何實體資料夾、捷徑或桌面檔案。
- 未改核心桌面整理、更新、啟動、快捷鍵或資料儲存位置；未做非必要的大型重構。
- 專案打包嚴格排除個人設定檔與除錯檔（`Profiles/`、`*.pdb`）。

## 尚未完成 (Remaining Work)

- **P1**：無阻斷性問題（本輪發布核心目標已全數完成並驗收通過）。
- **P2（後續演進建議）**：若未來需支援分頁內跨區移動或混合 DPI 跨多螢幕拖曳，再行安排實體現場測試；無實質回報前不擴大修改。

## 驗證結果 (Validation)

### 已執行測試與結果

- Visual Studio MSBuild Release 建置成功，0 錯誤。
- `tools/test-frame-item-transfer.ps1` 對隔離 `MoveVerify` 與最新 Release DLL 均通過；測試只使用記憶體資料。
- `tools/verify-localization.ps1`：英文 619 鍵、繁中 624 鍵；繁中無漏翻鍵。
- 使用者現場 GUI 實測：Delete 鍵刪除確認、跨 Fence 拖曳與右鍵移動來回驗收通過。
- GitHub Release `v2.8.1-zh-TW` 發布驗收：已成功上傳免安裝 Portable ZIP 包，Release 說明文案已修正為實事求是之 HKCU 偏好記錄說明。
- Portable ZIP 結構檢查：已驗證排除任何個人 `Profiles/` 與 `*.pdb` 除錯檔，結構乾淨。

### 已知風險 (Known Risks)

- 資料層移轉邏輯僅在使用者主動移動該圖示時進行目標去重與來源清理，不於開機時自動批次掃描或竄改使用者既有 JSON。
- 程式依賴當前使用者登錄檔 (`HKCU\Software\DesktopFramesPlus`) 儲存開機啟動與偏好記錄，不支援全系統多使用者統一寫入。

## Git 狀態

- Commit：`e04ef47`（本機已完全同步）
- Push：是；本機 `main` 與遠端 `origin/main` 完全同步
- Working Tree：Clean
- Branch：`main`
- Tag：`v2.8.1-zh-TW`（已推送遠端並關聯 Release）

## 下一步建議動作 (Next Recommended Action)

本輪修復與發布任務已全數完成。後續正常使用維護，待有新需求或 Bug 回報時再開新分支處理。

## 發布狀態 (Release Status)

**正式發布完成 (v2.8.1-zh-TW)。**

