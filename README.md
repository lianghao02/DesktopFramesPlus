# Desktop Frames + (台灣繁體中文版)

<h1 align="center">Desktop Frames +</h1>
<p align="center"><i>像魔法般整理你的 Windows 桌面！ / Organize your desktop like magic!</i></p>
<p align="center">
  <img src="https://img.shields.io/badge/Maintenance-v2.9.4--zh--TW-blue?style=flat-square" alt="維護紀錄 v2.9.4；執行檔版號另行確認" />
  <img src="https://img.shields.io/badge/.NET-8.0--windows-512bd4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Language-繁體中文%20%7C%20English-blue?style=flat-square" alt="Language" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078d4?style=flat-square&logo=windows" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="License" />
</p>

<p align="center">
  <img width="150" height="150" alt="Desktop Frames Logo" src="https://github.com/user-attachments/assets/a88f7771-8ae8-4be8-86dc-4e8aabfa5a77" />
</p>

---

## 📌 關於此分支與來源引註 (About This Fork & Upstream Attribution)

- **專案定位**：本專案為 [limbo666/DesktopFramesPlus](https://github.com/limbo666/DesktopFramesPlus) 之開源維護分支。
- **維護重點**：
  - 完整繁體中文（zh-TW）在地化介面與 100% 繁中指引。
  - 現行產品聚焦 **桌面分類面板（Data）＋獨立桌面便箋**；框選新增直接建立分類面板。
  - 個人桌面捷徑採兩階段收納；真實文件與資料夾以捷徑參照，不搬動來源。
  - 程式啟動與退出不進行桌面檔案對帳、搬移或還原。
  - 保留 DLR 動態包裝、工作區隔離、損毀便箋讀取防覆寫與本機 Profiles 發布保護。
  - 遠端更新依專案規則停用；現行操作邊界以原始碼與 [HANDOFF.md](HANDOFF.md) 為準。
- **授權與致謝**：
  - 本專案遵循 **[MIT License](LICENSE.md)** 開源授權。
  - 核心架構最初由 **HakanKokcu** 以 *BirdyFences* 之名發起。
  - 後續由 **Nikos Georgousis (Hand Water Pump 2025-2026)** 進行大規模效能優化與功能擴充。
  - 繁體中文版本由 **LiangHao** 進行多語系資源在地化、桌面分類面板與便箋維護；農場柵欄屬於先前的架構演進。

---

## 🇹🇼 繁體中文說明

## 專案概念與開發原因

本分支以桌面分類面板收納常用入口，另以獨立便箋保存短期記事。開發動機是 Windows 桌面容易同時堆積捷徑、文件與待辦，需要便於分類、切換工作區且不任意搬動真實文件的介面。

現行架構沿用上游 .NET／WPF 的 Data 面板，停止將農場柵欄與背景桌面對帳作為主流程，降低 Shell 託管與生命週期檔案操作的複雜度。這是維護方向的收斂，不是把舊功能全部列為仍可新建的產品能力。

**典型流程**：建立分類面板 → 拖入捷徑或文件參照 → 調整分頁／順序 → 建立獨立便箋 → 保存並備份 Profiles。

### 拖入與刪除的實際邊界

| 操作 | 現行行為 | 使用前要知道 |
|---|---|---|
| 拖入個人桌面的 .lnk／.url | 先複製捷徑並寫入面板資料，確認成功後才清理桌面原捷徑。 | 桌面原捷徑可能被移除；不能宣稱所有拖曳都不變更來源。 |
| 拖入實體文件、資料夾或外部檔案 | 建立指向原位置的捷徑參照。 | 來源仍在原處；之後搬移來源可能讓參照失效。 |
| 拖入公用桌面捷徑 | 建立捷徑副本，不要求提權清理公用桌面。 | 原公用捷徑保留。 |
| 移除圖示／刪除面板 | 移除面板項目或配置及相關本機捷徑副本。 | 不刪除真實文件，也不把已收納捷徑自動還原回桌面。 |
| 開啟／退出程式 | 不執行桌面檔案背景對帳、搬移或還原。 | 這與使用者主動拖曳捷徑的收納行為不同。 |

---

### 🌟 核心特色一覽

- **桌面分類面板（Data Frames）**：集中收納捷徑，支援分頁、捲動與自訂色彩。現行新建入口不提供 Farm、Portal 或嵌入式 Note 面板。
- **滑鼠右鍵框選自訂框架 (Draw Frame)**：
  - 框選大小與位置後直接建立 Data 分類面板，不接管原生桌面圖示。
- **桌面便箋 (Desktop Sticky Notes)**：
  - 隨時按下 `Ctrl + Alt + N` 秒級建立，支援 4 段字級（12/14/16/18）、多字型自訂、置頂固定、位置鎖定與 6 款柔和莫蘭迪配色。
  - 本地繁中化啟動提示，自動識別舊設定並升級為繁體中文。
- **分頁管理引擎 (Tabs Engine)**：單一框架內可容納多個分頁標籤，告別桌面擁擠。
- **工作區設定檔 (Profiles)**：可為上班工作、影音娛樂、遊戲等建立獨立桌面佈局；切換工作區時便箋與面板資料完全隔離，安全無交叉污染。
- **SpotSearch 秒級搜尋**：按下快速鍵即刻呼叫搜尋條，快速索引並啟動所有框架內的項目。
- **全域免安裝綠色架構 (Portable)**：
  - 所有設定檔均收納於程式目錄下的 `Profiles/`，隨身碟即插即用。
  - 打包腳本具有本機設定隔離保護；更新前仍應關閉程式並備份自己的 Profiles，勿用他人的個人設定覆蓋。
- **極致視覺與主題**：變色龍模式 (Chameleon Mode，自動擷取桌布主色)、圖示光暈陰影效果、6 款應用程式啟動動畫、雙擊桌面空白處一鍵隱藏全桌面圖示。

---

### 📥 下載與安裝

1. 前往 **[Releases 發行頁面](https://github.com/lianghao02/DesktopFramesPlus/releases)**，依該版說明選擇完整免安裝成品；原始碼維護狀態與已發布成品可能不同。
2. 解壓縮至任何具備使用者寫入權限的資料夾（如 `D:\Tools\DesktopFramesPlus` 或桌面，請勿放於唯讀的 `Program Files`）。
3. 執行 `Desktop Frames.exe` 即可直接使用，首次啟動自動建立繁體中文設定環境。
4. **升級方式**：關閉程式、備份 `Profiles/` 後依該版說明替換程式檔；不要覆蓋或清除自己的個人設定。更新後確認面板、便箋與捷徑內容。

---

## 版本沿革與開發入口

Farm Fences、Portal 與嵌入式 Note 曾出現在先前版本；現行新建入口聚焦 Data 面板與獨立便箋。舊設定格式和相容性邊界見 [HANDOFF.md](HANDOFF.md)，完整版本演進見 [CHANGELOG.md](CHANGELOG.md)，不要依舊版截圖推定目前選單。

原始碼開發可使用 [Run-Latest.bat](Run-Latest.bat)，其呼叫 [tools/run-app.ps1](tools/run-app.ps1)。專案含 Windows COM 參照，建置需使用 Visual Studio 的 MSBuild；單靠 dotnet build 可能出現 MSB4803。既有成品與重新編譯的版本需分別驗證。

## 已知 Bug、限制與疑難排解

| 狀態 | 情境 | 處理方式 |
|---|---|---|
| 已確認待整理（版本） | 交接維護紀錄為 v2.9.4-zh-TW，csproj 的 AssemblyVersion／FileVersion／Version 仍為 2.8.1。 | 以實際使用的成品和來源識別回報；本輪不變更程式版號，也不宣稱已發布新版本。 |
| 參照限制 | 原始文件移動或捷徑目標失效。 | 更新捷徑指向；面板參照不是文件副本或備份。 |
| 資料限制 | 個人 Profiles 損毀、被其他版本覆蓋或放在不可寫位置。 | 先保留現場及備份；不要用清除全部資料作為第一個除錯步驟。 |
| 環境限制 | Windows COM 建置或不同電腦啟動尚未驗證。 | 依建置工具與該版成品需求檢查，不以本機文件更新代替乾淨環境驗證。 |

歷史修正包含 DLR 動態資料包裝、便箋讀取防覆寫與選項版面整理。既有修正不等於所有舊發布包已更新；本輪僅校正 README，未改動桌面行為或個人配置。

### 問題回報

請提供成品名稱／實際版號、Windows 版本、重現步驟，以及拖入項目的類型（個人捷徑、公用捷徑或實體文件）。設定或日誌請先去識別，勿直接附整份 Profiles；檔案操作問題先保留來源與現場，再使用假資料重現。

---

## 📄 License & Credits

- **License**: Licensed under the [MIT License](LICENSE.md).
- **Original Project**: Forked from [limbo666/DesktopFramesPlus](https://github.com/limbo666/DesktopFramesPlus), originally based on **BirdyFences** by HakanKokcu.
- **Upstream Authors**: Nikos Georgousis (Hand Water Pump 2025-2026).
- **Localization & Maintenance**: LiangHao.
