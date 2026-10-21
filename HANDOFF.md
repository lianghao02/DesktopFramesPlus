# HANDOFF

## 核心元資料 (Metadata)
- **Repository**：lianghao02/DesktopFramesPlus
- **Branch**：main
- **Commit SHA**：e317dc98ff477f6e2700f43bda95804eb108d8ee（本輪未提交）
- **Skill Version**：lianghao-development v1.0.0；全域憲法 v8.5
- **Task Type**：IMPROVE / FIX / HANDOFF
- **Local Path Hint**：19_DesktopFramesPlus

---

## 目前狀態
已全面完成「2D 遊戲、Q 版風格」與「高對比字體、框線、表格/道具槽、底色」之視覺重構，並徹底根治「圖示縮小時會殘留」之渲染漏洞：
1. 根治圖示縮小殘留（Artifact Prevention）：
   - 羊皮紙底板（FramePaper）改為 100% 實心不透明純色（Solid 1.0），徹底消滅 WPF Layered Window（AllowsTransparency）在縮放時半透明圖層無法清空歷史緩衝區的階梯殘影累積問題。
   - 全控制項管線嚴格邊界剪裁（Strict Clipping）：外層面板（cborder）、內部導覽（dp）、捲動容器（wpcontscr）與圖示排列區（wpcont）全面啟用 ClipToBounds = true，杜絕視窗或圖示縮小時內容溢出穿透木框底邊。
   - 外框 OnRender 移除背景半透明稀釋，維持實心木質邊界；尺寸變更時強制觸發 InvalidateVisual。
2. 道具欄表格（Inventory Slot Grid）：導入向量繪製的 Q 版冒險背包道具凹槽卡片（Slot Tile），具備 8px 圓角、1px 柔和暖金米棕邊框、微凹立體漸層與四角精緻微鉚釘裝飾；底色採用純淨淡米凹槽色（#F4E8D1），徹底摒除厚重綠草皮干擾，使任何色彩之應用程式圖示清晰度達 100%。
3. 文字與字體對比（Typography）：文字顏色全面統一為深墨炭黑（Ink: #1A120B，對比度 > 13:1），字重提升為 SemiBold，最多 2 行並以省略號截斷，純淨不遮蔽圖示；標題列文字增加立體文字陰影（DropShadow），宛如鑲嵌於皮革上的象牙立體刻字。
4. 標題列左右操作圖示對稱化：選單圖示（♥）與鎖定圖示（🛡️）分別配置於標題列 Grid 之左右專屬 30px 欄位內部並垂直置中，徹底解決原本 ♥ 飄出木框角飾之邊界問題。
5. 右鍵選單精緻化：採用 1.5px 柔和深木棕圓角邊框（#6E4F32）、象牙羊皮底色（#FFFDF7）、柔和淺暖金高亮底（#F3E2B6）與暖金刻線 Separator（#DFC8A0），完全符合 Q 版冒險遊戲風格，俐落不厚重。
6. 所有 152 個自動化測試通過，685 個繁中資源鍵 100% 覆蓋，正式自包含成品已更新至 dist/DesktopFramesPlus（Profiles 嚴格保留）。版本為 v2.10.0。

## 2026-10-21 開機未自動啟動（Claude，未提交、未同步 dist）
- 根因：HKCU `Run` 的「Desktop Frames +」命令正確且 EXE 存在，但 `Explorer\StartupApproved\Run` 同名值首位元組為 `03`（工作管理員／設定「啟動應用程式」停用，時間 2026-10-13 12:20）。Windows 因此略過；舊程式只檢查 Run，設定畫面仍顯示已啟用。
- 修正：`WindowsStartupRegistration` 新增 `ApprovedKey`／`IsBlockedByWindows`；`IsEnabled` 遇停用旗標回報未啟用；`SetEnabled(true)` 解除停用（寫 `02` + 11 個 0），`SetEnabled(false)` 一併刪除殘留狀態。測試 `RoundTwoChecks.cs` 改用 GUID 隔離的 Approved 機碼並新增 3 項檢查。
- 另替 `test-frame-item-transfer`、`test-managed-storage-fence`、`test-native-panel-integration`、`test-portable-runtime`、`verify-localization` 補 UTF-8 BOM（Windows PowerShell 5.1 原本無法解析）。
- 驗證：MSBuild Release 0 錯誤；`test-native-panel-integration`、`test-representative-functions` 全 PASS（含新增啟動檢查）；繁中 100%。`test-frame-item-transfer.ps1` 需 PowerShell 7 載入 .NET 8 組件，本機未安裝 pwsh，**未執行**。
- 使用者本機正式 StartupApproved 值仍為停用（未代改系統設定），dist 尚未更新。

## 2026-10-21 發布 v2.11.0-zh-TW（Claude）
- 依使用者指示（已自行使用一輪）：升版 2.10.0 → 2.11.0（`Desktop Frames.csproj` 三個版本欄位）、更新 README／CHANGELOG、新增 `docs/releases/v2.11.0-zh-TW.md`。v2.10.0 從未公開發布，本版一併包含。
- 驗證：自包含發布 → `run-all-tests.ps1` 152 PASS、`test-managed-storage-fence.ps1` 10 PASS、繁中 100%；`package-release.ps1 -RequireSelfContained` 打包。
- 為避免動到使用者的 `dist/DesktopFramesPlus`（可能有執行中程式與個人 Profiles），打包輸出在 `dist/release-v2.11.0/`；本機正式目錄尚未更新，需要時執行 `scripts/build_and_sync.ps1 -SkipBuild`（保留 Profiles）。
- ZIP SHA256：DD77B69570FAEAFA5F4512F505E3E260BA4E4229C674BBC42EF3E6ED62BB6B74。

## 2026-10-21 選單、設定與用詞整理（Claude，未建置）
- 範圍：resx 用詞（`Strings.zh-TW.resx`，新增 `MenuMore`、`SecAdvancedGroup`）、`FrameManager.cs`（♥ 選單與面板右鍵選單加「更多」）、`OptionsFormManager.cs`（`AddAdvancedSection`：提示聲音、自動隱藏／淡出收進預設收合的 Expander）。
- **已驗證（2026-10-21）**：MSBuild Release 0 錯誤；`tools/run-all-tests.ps1 -BinaryDir "Code/Desktop Frames/bin/Release/net8.0-windows7.0"` 152 PASS、0 FAIL；`tools/verify-localization.ps1` 繁中 100% 覆蓋；驗收程式實際渲染設定視窗，「進階（少用）」區預設收合。**尚未打包、未覆蓋 `dist/`**；♥ 選單與面板右鍵選單的「更多」尚未在真實桌面上目視確認。
- 注意：`FrameManager.cs` 有 `using Color = System.Drawing.Color` 別名，新增的紅字用 `System.Windows.Media.Color`。
- `CustomizeFrameFormManager.cs`：新增 `CreateAdvancedSection`，把 `_cmbCustomLaunchEffect`、`_chkDisableTextShadow`、`_chkGrayscaleIcons` 移入預設收合的 Expander（欄位與載入／儲存邏輯不變）；MSBuild 0 錯誤、152 PASS，但此視窗沒有自動化畫面檢查，尚未目視確認。
- 未做：設定頁籤合併（便箋、快捷鍵併入情境）；有需要再評估。

## 2026-10-20 接手收尾（Claude）
- 前一輪成果已驗收並一併提交：
  - Q 版素材與 `GameSkin`／`GameTheme`
  - 殘影修正、`ResizeSnapCalculator`、`WindowsStartupRegistration`
  - 測試與文件
- 本輪補齊設定視窗的 Q 版元件：
  - `Themes/CozyGame.xaml` 新增隱含 `CheckBox`、`ComboBox`／`ComboBoxItem`、`ScrollBar` 樣式。
  - 分頁選取狀態改為深墨粗框加綠色書籤條，並以粗體顯示。
- 灰色說明字改用 `GameTheme.InkSoft`（#5B4A3A，對比 7.5:1）；設定卡片改為撐滿寬度。
- 驗證：
  - 以 MSBuild 建置 Release，0 錯誤。
  - `tools/run-all-tests.ps1 -BinaryDir Code/Desktop Frames/bin/Release/net8.0-windows7.0`：152 個 PASS。
  - 檢視 WPF 實際渲染圖：收納面板、建立對話框（100／125／150%）、設定。
- 未觸碰的範圍：
  - 沒有重建或同步 `dist/`。使用者桌面上正在執行的 `dist` 版本仍是前一輪的成品，因此沒有本輪設定視窗的改善。
  - 沒有修改登錄、Profiles 或收納搬移邏輯。

## 本輪目標
使用者指出圖示底層有綠色草皮太影響辨識，要求分析其他影響辨識之狀況並進行修訂，避免「把單一遊戲材質套用到所有容器」，導致商業工具應有的銳利度、資訊可讀性與操作引導性被削弱。

## 基準與已確認事實 (Baseline & Confirmed Facts)
- main 上的未提交修改皆為本對話前輪工作，已確認後接續，未覆寫其他工作。
- 現行 Default 設定的 EnableDimensionSnap 與 IsSnapEnabled 均為 true。
- 一般唯讀沙箱的 HKCU 視角與實際使用者登錄環境不同。最初讀不到 Run 項目不能當成真實故障根因；沒有保留修改前實際環境的登錄快照。實際環境目前已寫入並讀回正確命令。
- 舊寫入流程以 OpenSubKey 開啟；機碼不存在時直接返回，後續仍會將 UI 狀態設為已啟用。舊讀取只確認有值，不驗證路徑。
- 舊尺寸吸附僅於結束操作時取整寬高，未追蹤拖曳邊，左邊縮放可能改動本應固定的右邊。移動也會進入同一結束處理。
- 保留 WPF、COM 與現行資料格式，不重構 Win32 主循環。

## 已完成 (Completed)
- 內建 image_gen 生成並嵌入 2D 靜態圖集；素材、最終提示詞與切片位置見 Code/Desktop Frames/Resources/GameArt/README.md。
- GameSkin 專注用於外層面板（Panel）、布面標題（Header）、皮革按鈕（Button）與紙張底板（Paper）；移除將圖示小槽位粗暴套用綠草皮厚木框（Slot）的過度包裝。
- 圖示項目（sp）底襯恢復為乾淨透明，避免遮蔽或干擾任何應用程式圖示輪廓；Hover 狀態採用柔和微透暖蜜金（ItemHover: #24C99757），Selected 採用典雅半透明墨綠（ItemSelected: #4D526E59）。
- 文字標籤（lbl）全面拔除生硬的直角黑底色塊；在淺色羊皮紙底板上自動套用深墨褐色（Ink: #493B35），文字清晰銳利如墨水印於紙上；在完全透明透光底維持白字加向量陰影。
- 圖示與文字垂直排版解耦：加大間距留白（ico.Margin 與 lbl.Margin 適當區隔），禁止文字區塊向上侵入或切斷圖示本體。
- 標題列右側操作項目（選單、篩選、鎖定）不透明度由原本極淡的 0.3 提升至保底 0.85（懸停 1.0），在深綠色標題列上對比度充足且清晰可辨。
- tools/*.ps1 加入 UTF-8 with BOM，消除 Windows PowerShell 5.1 雙位元組字串解析錯誤。
- 新增 ResizeSnapCalculator；左右邊及四角皆有原生縮放命中區。尺寸吸附沿用 10 DIP 網格，拖左邊固定右邊、拖右邊固定左邊。
- 新增 WindowsStartupRegistration：CreateSubKey、完整引用路徑、EXE 存在檢查、讀回驗證；設定儲存時同步實際註冊狀態，失敗不假報成功。
- 正式 Run 項目已重新啟用並讀回，指向 dist/DesktopFramesPlus/Desktop Frames.exe。
- 正式自包含成品已更新並同步至 dist/DesktopFramesPlus，使用者 Profiles 100% 保留。
- 新版正式啟動成功，PID 29944；日誌確認載入原本兩個面板與便箋。

## 異動檔案 (Changed Files)
- 新增：GameSkin.cs、GameTheme.cs、ResizeSnapCalculator.cs、WindowsStartupRegistration.cs。
- 新增：Themes/CozyGame.xaml、Resources/GameArt/cozy-inventory-atlas.png、Resources/GameArt/README.md。
- 修改：App.xaml、Desktop Frames.csproj、DrawFrameConfirmDialog.cs、FrameManager.cs、NonActivatingWindow.cs、OptionsFormManager.cs、TrayManager.cs、Utility.cs。
- 修改：Localization/Strings.resx、Strings.zh-TW.resx、Notes/Views/NoteWindow.xaml、NoteWindow.xaml.cs。
- 測試：tools/acceptance-tests/Program.cs、GameThemeChecks.cs、RoundTwoChecks.cs。
- 文件：README.md、IMPLEMENTATION_PLAN.md、HANDOFF.md。
- 上述原始碼均位於 Code/Desktop Frames/；測試與文件包含前輪延續修改。

## 刻意未修改 (Do Not Do / Deliberately Omitted)
- 不變更收納搬移、拆除、安全刪除、設定格式或個人 Profiles。
- 不新增等級、貨幣、獎勵或強制動畫。
- 不修改 Windows StartupApproved 二進位狀態，不建立排程或要求管理員權限。
- 不自動重開機；沒有建立公開 Release、Commit 或 Push。

## 尚未完成 (Remaining Work)
- **P1 (阻斷/必須)**：無。
- **P2 (重要/當次)**：Windows 啟動應用程式的實際啟停與重開機驗證待現場確認；使用者目前用手機遠端，已明確表示回辦公室再測試。
- **P3 (改善建議/暫緩)**：下次重新登入 Windows 確認真實自動啟動；實體多螢幕 DPI 與 Windows 10 人工驗收。

## 驗證結果 (Validation)
### 已執行測試與結果
- Visual Studio MSBuild 自包含 Publish：成功。
- tools/run-all-tests.ps1 -BinaryDir "Code/Desktop Frames/bin/PortablePublish/win-x64"：152 個 PASS，繁中 679 個英文基準鍵全部覆蓋。
- tools/test-managed-storage-fence.ps1 同一 BinaryDir：10 個 PASS。
- 覆蓋左右與左上角固定邊、負座標、最小尺寸、原生 WM_SIZING、依 ID 保存、鎖定與關閉吸附。
- 啟動回歸只寫入 GUID 專用測試機碼：缺少機碼、建立讀回、重複啟用、中文空白路徑、錯誤路徑、缺少 EXE、停用保留其他值皆通過。
- 正式啟動項目以相同服務寫入並讀回為 true。
- 同步前後 Profiles：206 個檔案一致；正式 DLL 與測試成品一致。
- 從 System32 工作目錄啟動成功，PID 25392；日誌確認完成首次 UI 閒置。
- git diff --check：通過。
- 日誌與部署摘要：Code/Desktop Frames/bin/ui-validation/round2-*。
- 圖片：tools/acceptance-tests/bin/Release/net8.0-windows7.0/中文 驗收-65c21b529d814a9dbe9e3d2d7cc56635/ui-evidence/。

### 尚未驗證項目
實際環境存在 StartupApproved 紀錄，但未解碼未公開定義的狀態，也未修改它。Computer Use 無法取得工作管理員／Windows 設定的可操作視窗；使用者已表示回辦公室再測試。尚未重新登入／重開機，不將註冊成功與手動啟動驗證冒稱為重開機實測。100%／125%／150% 為 WPF 圖片輸出倍率，並非實體 DPI 切換測試。

### 已知風險 (Known Risks)
編譯仍有 nullable 與未使用成員警告；無阻斷性錯誤。Run 項目由 Windows 登入後執行，啟動時間由系統安排。

## Git 狀態
- Commit：見 `git log -1`（基準 e317dc98ff477f6e2700f43bda95804eb108d8ee）
- Push：否
- Working Tree：Clean（已於 2026-10-20 提交前兩輪與本輪修改）
- Branch：main

## 下一步建議動作 (Next Recommended Action)
使用者回辦公室後體驗新版素材與左右吸附，查看 Windows「設定 → 應用程式 → 啟動」的 Desktop Frames + 狀態，再於正常重開機登入後確認自動啟動。暫時不再要求使用者操作現場 Windows。需要交付版本時再依要求提交，不擴大重構。

## 發布狀態 (Release Status)
本機正式可攜版已更新，公開 Release 未變。

## 技術與設計來源
- Run 機碼：https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
- WM_SIZING：https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-sizing
- 視覺方向參考：https://wiki.stardewvalley.net/Inventory 、https://www.cookierun-kingdom.com/en/
- 圖片由內建 image_gen 生成，未複製遊戲素材。
