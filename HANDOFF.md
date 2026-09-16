# HANDOFF: DesktopFramesPlus 維護交接狀態

- **Repository**: `lianghao02/DesktopFramesPlus`
- **Branch**: `main`
- **Commit SHA**: `0adb376`
- **Task Type**: FEAT / ENHANCEMENT
- **Date**: 2026-09-16

## 1. 當前已完成工作
1. **繁體中文與發行治理 (v2.7.8-zh-TW)**：
   - 建立完整 `Strings.zh-TW.resx`（609 筆鍵值，100% 覆蓋）。
   - 停用指向上游之自動更新，清理外部 PayPal 連結。
   - 改版中英雙語 README.md，發布 GitHub Release 並上架 13_Project-Hub。
2. **第一階段：框架智慧對齊與 8px 間距吸附 (FrameSnapCalculator)**：
   - **純幾何運算類別**：新增 `FrameSnapCalculator.cs`，純數學運算，零外部依賴。
   - **吸附優先序支援**：
     1. 螢幕工作區邊緣（自動排除 Windows 工作列）。
     2. 其他框架同向邊緣對齊（左對左、右對右、頂對頂、底對底）。
     3. 其他框架相鄰留白吸附（標準 8px 間距）。
     4. 框架水平/垂直中心線對齊。
   - **脫離阻尼手感 (Hysteresis)**：10px 觸發吸附、14px 脫離阻尼，徹底杜絕臨界點抖動。
   - **穿透對齊輔助線**：新增 `SnapGuideOverlay.cs`，WS_EX_TRANSPARENT 滑鼠完全穿透，拖曳中命中時繪製 1px 點狀半透明對齊線，放開即刻隱藏。
   - **架構潔淨**：完全不更動 `FrameManager.cs`，不碰圖示與實體檔案，不強制防重疊。

## 2. 驗證證據
- MSBuild Release 建置通過（0 個錯誤）。
- 產出主程式包含完整多語系與新吸附引擎。
- Git 工作目錄乾淨，已成功推送至 `origin/main`。

3. **第二階段：簡化桌面版面切換入口（Desktop Layouts / Profiles）**：
   - **系統匣 (Tray) 一鍵切換**：將「桌面版面」提升至系統匣選單首要位置，動態顯示目前啟用版面名稱（例如：`桌面版面 (Default)`），展開子選單即列出所有版面，當前版面打勾，點擊其他版面即時秒切。
   - **桌面框架本體選單（愛心功能選單）整合**：在每個桌面框架的標題列愛心選單中整合「桌面版面」子選單，使用者操作桌面時無需移至工作列系統匣即可一鍵切換不同工作情境。
   - **介面現代化與舊代碼清理**：移除 `Microsoft.VisualBasic.Interaction.InputBox` 老舊彈窗，管理與建立版面統一銜接現代化卡片風格之 `ProfileManagerForm`。
   - **多語系與術語優化**：新增 `MenuDesktopLayouts`（繁中：桌面版面、簡中：桌面布局、英文：Desktop Layouts），並修正繁中項目翻譯（剪下項目、刪除項目）。

## 2. 驗證證據
- MSBuild Release 建置通過（0 個錯誤）。
- `tools/verify-localization.ps1` 檢驗繁體中文資源檔 100% 覆蓋無遺漏。
- 系統匣與框架愛心選單皆可正常載入最新版面清單並完成動態切換。

## 3. 下一步路線
- 階段三：框選範圍後收納圖示（以非破壞性安全預覽與確認機制實現）。
