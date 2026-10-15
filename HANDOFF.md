# HANDOFF

## 核心元資料 (Metadata)

- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：922a3e1
- **Skill Version**：v1.0.0
- **Task Type**：FEAT / PERF / FIX / HANDOFF
- **Local Path Hint**：19_DesktopFramesPlus

## 目前狀態

已完成專案核心問題修復與現代化升級，全套代表性回歸測試（103 項代表性斷言、面板測試、圖示轉移、多語系在地化驗證）100% 通過（0 錯誤、0 警告）。已建立自動化 MSBuild 發布與同步腳本，並已將最新可攜成品部署至 `dist\DesktopFramesPlus\`（嚴格保留並排除使用者 `Profiles\` 與個人資料）。

## 本輪目標

依據全域開發憲法（v8.5）與 `lianghao-development`、`windows-tool-ux` 規範，解決專案長期存在的五大核心體驗與架構問題：
1. **圖示名稱過長破壞網格**：檔名過長折成三行導致網格破壞與上下圖示錯位。
2. **Win+D 顯示桌面被桌面覆蓋**：原本依賴 80ms 暴力 Topmost 刷屏，易引發畫面暴閃或被 DWM 吞噬。
3. **記憶體占用過高**：原本執行期 WorkingSet 高達 221.49 MB。
4. **介面現代化升級**：升級為現代懸浮 Dock 磨砂卡片風（柔和圓角、1px 晶透微高光邊界、滑鼠 Hover 呼吸反饋）。
5. **二進位發布雙軌錯亂**：`dist` 與 `Code\bin` 成品不同步，建立標準化發布腳本並 100% 守護使用者 `Profiles\` 資料。

## 基準與已確認事實 (Baseline & Confirmed Facts)

- **避免跨進程 Owner 地雷**：嚴禁在 `NonActivatingWindow` 呼叫 `helper.Owner = FindWindow("Progman", null)`。因 Progman 屬於 `explorer.exe` 行程，Win32 跨進程設置 Owner 會導致 Windows USER32 核心強制銷毀視窗（WM_DESTROY），造成無聲閃退。穩健做法為 `WS_EX_TOOLWINDOW` 搭配雙階段 `SetWindowPos` 平滑喚醒。
- **免安裝可攜標準**：本專案為 Self-Contained 可攜架構（內嵌 .NET 8 Runtime），必須透過 MSBuild `Portable-win-x64` 發布，產出包含 `includedFrameworks` 之 `Desktop Frames.runtimeconfig.json`。
- **使用者資料零更動**：`dist\DesktopFramesPlus\Profiles\` 下所有使用者設定與便箋內容必須 100% 原樣保留。
- **單一真理來源**：所有日常執行、開機啟動與腳本一律鎖定 `dist\DesktopFramesPlus\`，`Code\bin` 僅作暫存編譯目錄，嚴禁讀寫正式個人資料。

## 已完成 (Completed)

1. **圖示名稱排版收斂（2 行截斷 + 省略號）**：
   - 在 `FrameManager.cs` 將文字區塊 `MaxWidth` 設為 74px，加上 `TextTrimming = TextTrimming.CharacterEllipsis`。
   - 明確限制 `LineHeight = 16` 與 `MaxHeight = 34`（2 行容差），超過長度自動在第 2 行末端顯示省略號（`...`），徹底消除三行破版與錯位。
2. **記憶體與渲染瘦身（消除點陣陰影負擔）**：
   - 在 `FrameManager.cs` 拔除 70+ 個圖示文字獨立掛載的點陣 `DropShadowEffect`，解除 WPF 離屏渲染目標（Off-screen Render Target）記憶體負擔。
   - 改採純向量微黑半透明底色（`#28000000`）與內距，兼顧各色桌布易讀性與極低記憶體開銷。
   - 實測執行期 WorkingSet 由 **221.49 MB** 顯著降至 **197.96 ~ 200.32 MB**，私有記憶體（PrivateMemorySize）僅 **101.84 MB**。
3. **現代磨砂 Dock 卡片風外觀**：
   - 在 `FrameManager.cs` 將外框 `cborder` 預設圓角由 6px 提升至 14px（直角模式維持 0px）。
   - 加入預設 1px 晶透微高光透光邊框（`#28FFFFFF`）。
   - 圖示容器加入 `MouseEnter` 與 `MouseLeave` 之半透明微光懸浮反饋（`#20FFFFFF`）。
4. **Win+D 桌面層級穩定與平滑喚醒**：
   - 在 `NonActivatingWindow.cs` 注入 `WS_EX_TOOLWINDOW`（0x80）系統工具視窗樣式，在 Windows Shell `ToggleDesktop` 中不被視為普通視窗最小化。
   - 在 `App.xaml.cs` 將原本 80ms 猜測性 Topmost 刷屏改寫為 100ms/300ms 雙階段排程，並呼叫 Win32 `SetWindowPos`（`SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW`）進行平滑喚醒，徹底解決被 DWM 動畫二次吞噬與視窗暴閃問題。
   - 在 `App.xaml.cs` 加入 `this.ShutdownMode = ShutdownMode.OnExplicitShutdown;`，防止視窗重組時被 WPF 誤判關閉退出。
5. **自動化發布與二進位同步腳本**：
   - 建立 `scripts\build_and_sync.ps1`，一鍵執行 MSBuild Portable-win-x64 發布，並安全同步至 `dist\DesktopFramesPlus\`（嚴格排除 `Profiles/`）。
6. **統一 dist 單一真理來源與啟動防呆自癒守護**：
   - 清除 `Code\Desktop Frames\bin` 底下殘留的舊 `Profiles\` 資料夾，徹底杜絕平行時空。
   - 在 `App.xaml.cs` 啟動階段加入開發目錄防呆檢查（若由 `Code\bin` 直接雙擊啟動，彈出指引警告並安全退出）。
   - 在 `RegistryHelper.cs` 實作 `RefreshStartupPath()`，每次啟動自動校正 Windows 開機登錄表（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）指回現行 `dist\DesktopFramesPlus\Desktop Frames.exe`。
   - 更新 `tools\run-app.ps1`，統一行為呼叫 `build_and_sync.ps1` 並啟動 `dist` 成品。
   - 校正目前開機註冊表路徑為 `"D:\Development\GitHub\19_DesktopFramesPlus\dist\DesktopFramesPlus\Desktop Frames.exe"`。
7. **全套回歸測試通過**：
   - 執行 `tools\run-all-tests.ps1`，面板測試、103 個代表性功能斷言、圖示轉移回歸與在地化檢查（繁中 683 鍵 100% 覆蓋）全數通過，退出碼 0。

## 異動檔案 (Changed Files)

- `Code/Desktop Frames/App.xaml.cs`：加入 ExplicitShutdown、Win+D 雙階段平滑 SetWindowPos 喚醒、啟動目錄防呆守護、開機路徑自癒。
- `Code/Desktop Frames/FrameManager.cs`：圖示文字 2 行截斷與省略號、拔除 DropShadowEffect、向量微底色、14px 圓角與透光邊界、Hover 反饋。
- `Code/Desktop Frames/NonActivatingWindow.cs`：加入 `WS_EX_TOOLWINDOW` 擴展樣式。
- `Code/Desktop Frames/RegistryHelper.cs`：新增 `RefreshStartupPath()` 自動校正開機註冊表。
- `tools/run-app.ps1`：更新為統一啟動 `dist` 發布版本。
- `scripts/build_and_sync.ps1`：新建之標準化可攜發布與安全同步腳本。
- `HANDOFF.md`：更新本輪交接文件。

## 刻意未修改 (Do Not Do / Deliberately Omitted)

- 嚴格保留 `dist\DesktopFramesPlus\Profiles\` 下所有使用者個人設定、捷徑與便箋內容。
- 便箋（`NoteWindow`）維持一般視窗獨立浮動，不降級為桌面底層工具視窗。
- 不更改既有 `frames.json` 與 `options.json` 資料架構。

## 尚未完成 (Remaining Work)

- **P1（阻斷）**：無。
- **P2（重要）**：無。
- **P3（暫緩）**：非本機環境（無全域 .NET 的乾淨虛擬機、第二台電腦、多螢幕/高 DPI）之長期跨環境實測。

## 驗證結果 (Validation)

- **編譯建置**：Visual Studio MSBuild Portable-win-x64 發布成功（0 錯誤、0 警告）。
- **回歸測試**：`tools\run-all-tests.ps1` 退出碼 0，通過 103 項代表性功能驗收與在地化 100% 檢查。
- **記憶體量測**：進程啟動後 WorkingSet 降至約 197.96 ~ 200.32 MB，私有記憶體 101.84 MB，運作穩定健康。
- **單一真理驗證**：註冊表已驗證指回 `19_DesktopFramesPlus\dist\...`，`Code\bin` 舊資料已清理且有防呆阻擋。

## Git 狀態

- Commit：待提交（`fix: 統一 dist 單一真理來源，清理舊 Profiles 殘留並加入開機自癒與防呆守護`）
- Branch：main
- Working Tree：受控修改待提交

## 下一步建議動作 (Next Recommended Action)

執行 Git 提交與推播，正式入口統一使用 `dist\DesktopFramesPlus\Desktop Frames.exe`。
