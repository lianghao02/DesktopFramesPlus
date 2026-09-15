# HANDOFF: DesktopFramesPlus 維護交接狀態

- **Repository**: `lianghao02/DesktopFramesPlus`
- **Branch**: `main`
- **Commit SHA**: `a86090c`
- **Task Type**: IMPROVE / GOVERNANCE
- **Date**: 2026-09-15

## 1. 當前已完成工作
1. **繁體中文介面完整落地**：
   - 建立 `Strings.zh-TW.resx`（609 筆鍵值，100% 覆蓋基準英文資源）。
   - 驗證 MSBuild Release 建置可正確產出 `zh-TW\Desktop Frames.resources.dll`（44,544 bytes）。
   - 移除 About 與 Options 畫面中的捐贈區塊呼叫，排版保持正常。
2. **發行治理與更新來源防護**：
   - 在 `RemoteInfoManager.cs` 增加 `EnableRemoteUpdates = false` 開關，阻斷向 upstream (`limbo666`) 發送請求，防止使用者端被提示覆蓋回英文版或遭受遠端強制關閉。
   - 移除 About 彩蛋中殘留的 PayPal 連結（改為點擊關閉視窗）。
   - 更新 About 與 README 的 GitHub 儲存庫與 Release 指向為 `lianghao02/DesktopFramesPlus`。
   - README 移除原作者個人 PayPal / Liberapay 贊助段落，明確標註「繁體中文維護分支」，並完整保留原作者致謝與 MIT License。
3. **品質工具與專案規範確立**：
   - 新增 `tools/verify-localization.ps1` 輕量多語系鍵值比對腳本。
   - 建立專案專屬 `AGENTS.md` 明確邊界（禁動 `FrameManager` 與 JSON 設定結構）。

## 2. 驗證證據
- MSBuild Release 建置成功（`Desktop Frames.csproj` 0 個錯誤，產生 `Desktop Frames.exe` 與 `zh-TW` 衛星組件）。
- 執行 `tools/verify-localization.ps1` 通過：基準英文 604 鍵，繁中 609 鍵，0 缺漏（100% 覆蓋）。

## 3. 下一步建議
1. 於 Windows 10/11 實機環境測試中文介面長文字之對話框排版。
2. 若未來有對外發布需求，可直接使用 Release 目錄產出 Zip 免安裝套件。
