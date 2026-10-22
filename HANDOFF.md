# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：0cf2ef47613e2e80c5edcad411f4375682d7127e（v2.11.1-zh-TW 發布提交；本檔整理前基準）
- **Skill Version**：lianghao-development v1.0.0；全域憲法 v8.5
- **Task Type**：HANDOFF
- **Local Path Hint**：19_DesktopFramesPlus

---

## 目前狀態
- 2026-10-22 已發布 `v2.11.1-zh-TW`（GitHub Release 為最新版），本機與遠端一致，工作目錄乾淨。
- 本版內容：`FrameManager` 拆成 `partial class` 多檔（含 `CreateFrame` 由約 3,715 行降為約 200 行）、修正隱藏面板啟動仍顯示／新建面板讀不到設定／自訂外框顏色無效、同名面板改以面板 Id 辨識並釋放舊監看器。
- 自動化驗證：測試 251 項＋收納面板 10 項全過、繁中資源 100%；拆檔前後逐行比對只差預期處，故意改壞抽查 6/6 被測試抓到。細節見 `CHANGELOG.md`、`docs/releases/`、`IMPLEMENTATION_PLAN.md`，更早的逐日紀錄請用 `git log`。
- 自包含發布 ZIP：480 檔、88,020,640 bytes、SHA256 `BC2B6BD385BA821166A4131CEDCB0F24DDE3A10695EA78891A979938A8CD8199`。打包輸出在暫存區；本機 `dist/DesktopFramesPlus` 以關閉、同步、重開方式更新過（保留 Profiles）。

## 尚未驗證（不冒充通過）
1. **桌面手動驗收**：沒有人在真實桌面完整操作過一輪。清單在 `IMPLEMENTATION_PLAN.md`「手動驗收清單」：Ctrl 複製／Alt 捷徑拖入、自動隱藏與淡出、Ctrl 覆蓋層、Delete 移除、篩選列、圖示右鍵各項、實際拖曳移動與縮放、多螢幕與高 DPI。其中 Ctrl／Alt 拖放讀的是實體鍵盤狀態，介面層無法自動化。
2. **開機自動啟動**：2026-10-21 查到使用者機器的 `StartupApproved\Run` 被標為停用（Windows 設定／工作管理員關掉），程式已改成偵測並回報未啟用；系統設定本身未代改。是否已重新啟用、重開機後是否真的自動啟動，尚未確認。
3. **DPI 與 Windows 10**：100%／125%／150% 只是 WPF 圖片輸出倍率，不是實體 DPI 切換；Windows 10 未驗收。
4. `tools/test-frame-item-transfer.ps1` 需 PowerShell 7 載入 .NET 8 組件，本機未安裝 pwsh，未執行。
5. GitHub Windows 環境在既有的建立捷徑檢查即失敗，故不啟用 CI；草稿在 `tools/ci-drafts/`。

## 已知現況與風險
- `UnrolledHeight` 首次排版會被覆蓋（已發現、未修改）。
- 編譯仍有 nullable 與未使用成員警告，無阻斷性錯誤。
- 建立收納面板的測試必須明確指定 `portalPath` 與 `storageRootPath`，否則會寫到真實桌面的預設收納位置（曾發生並已清除）。
- 建置需 Visual Studio MSBuild（有 COM 參照），不能用純 `dotnet build`。

## 還原點（已推送的標籤）
`restore/2026-10-21-before-framemanager-work`、`restore/2026-10-22-before-split`、`restore/2026-10-22-before-stage2`。拆檔若出問題，可由這些標籤對照或還原。

## 刻意不做
- 不變更收納搬移、拆除、安全刪除、設定檔格式或使用者 Profiles。
- 不新增等級、貨幣、獎勵或強制動畫。
- 不修改使用者的 Windows 啟動項系統設定。

## 下一步建議動作
使用者在真實桌面走完手動驗收清單並回報；有問題再以還原點與測試定位，不擴大重構。無新需求時不需修改程式。

## 技術與設計來源
- Run 機碼：https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
- WM_SIZING：https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-sizing
- 視覺方向參考：https://wiki.stardewvalley.net/Inventory 、https://www.cookierun-kingdom.com/en/
- 圖片由內建 image_gen 生成，未複製遊戲素材。
