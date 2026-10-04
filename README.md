# Desktop Frames + (台灣繁體中文版)

<h1 align="center">Desktop Frames +</h1>
<p align="center"><i>像魔法般整理你的 Windows 桌面！ / Organize your desktop like magic!</i></p>
<p align="center">
  <img src="https://img.shields.io/badge/Version-v2.8.1--zh--TW-blue?style=flat-square" alt="Version 2.8.1" />
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
  - **原生農場柵欄（Farm Fences）**：原檔不搬移、不建立第二入口、原生圖示原地託管、拖曳**所放即所得**（零彈走）。
  - 右鍵框選自訂柵欄（Draw Frame）原生自動接管。
  - 徹底防崩潰：實作 DLR 安全動態包裝（`DynamicFrameData`）與全域未處理例外攔截。
  - 資料安全 P1 加固：工作區隔離、便箋讀取損毀防覆寫、託管交易原子化存檔、打包發布本地 Profiles 零刪除保護。
  - 移除外部無授權贊助連結，杜絕背景非預期聯網。
- **授權與致謝**：
  - 本專案遵循 **[MIT License](LICENSE.md)** 開源授權。
  - 核心架構最初由 **HakanKokcu** 以 *BirdyFences* 之名發起。
  - 後續由 **Nikos Georgousis (Hand Water Pump 2025-2026)** 進行大規模效能優化與功能擴充。
  - 繁體中文版本由 **LiangHao** 進行多語系資源在地化、原生農場柵欄整合與發行治理維護。

---

## 🇹🇼 繁體中文說明

### 🌾 原生農場柵欄概念 (Farm Fences Philosophy)
> **「原檔原位、零副本入口、所放即所得」**

本專案引進全新之「農場柵欄」理念，回歸 Windows 桌面圖示組織的最純粹體驗：
1. **原地託管（In-Place Hosting）**：桌面上的真實檔案與原捷徑位置不搬動、不複製副本、不藏至螢幕外、不產生第二份影子入口。
2. **手動歸屬與所放即所得（Free Positioning）**：只有使用者親自拖入的項目才會納入管理；在柵欄內拖曳圖示時，**游標放開在哪裡，圖示就精準固定在哪邊**，廢除死板的強制緊湊九宮格覆蓋重排。
3. **視窗移動跟隨（Relative Panning）**：拖動柵欄視窗時，柵欄內的圖示隨著外框平移相同位移量，內部擺放配置分毫不變。
4. **取消柵欄項目留桌面（Safe Detachment）**：點擊關閉或取消柵欄時，只解除分組標記，所有桌面圖示原地保留在桌面上，絕不刪除檔案。
5. **重開機與更新 100% 記憶（State Persistence）**：所有柵欄數量、位置、尺寸與內部每個圖示的座標，皆持久化於 `Profiles/Default/farm-fences.json`，重啟或覆蓋更新執行檔永遠不丟失。

---

### 🌟 核心特色一覽

- **多元框架類型**：
  - **原生農場柵欄 (Farm Fences)**：原生圖示原地互斥託管、點擊與右鍵選單 100% 穿透至原生 Windows Shell。
  - **自訂捷徑面板 (Data Frames)**：集中收納軟體捷徑，支援滾動捲起與自訂色彩。
  - **資料夾鏡射傳送門 (Portal Frames)**：即時同步磁碟資料夾內容，支援內嵌路徑瀏覽與關鍵字篩選。
  - **便籤文字面板 (Note Frames)**：嵌入式便箋，支援自動折行與拼字檢查。
- **滑鼠右鍵框選自訂框架 (Draw Frame)**：
  - 於桌面空白處按滑鼠右鍵選擇「框選新增框架」，隨手一拉即可畫出心目中的尺寸與位置，自動無縫接入原生農場柵欄核心。
- **桌面便箋 (Desktop Sticky Notes)**：
  - 隨時按下 `Ctrl + Alt + N` 秒級建立，支援 4 段字級（12/14/16/18）、多字型自訂、置頂固定、位置鎖定與 6 款柔和莫蘭迪配色。
  - 本地繁中化啟動提示，自動識別舊設定並升級為繁體中文。
- **分頁管理引擎 (Tabs Engine)**：單一框架內可容納多個分頁標籤，告別桌面擁擠。
- **工作區設定檔 (Profiles)**：可為上班工作、影音娛樂、遊戲等建立獨立桌面佈局；切換工作區時便箋與面板資料完全隔離，安全無交叉污染。
- **SpotSearch 秒級搜尋**：按下快速鍵即刻呼叫搜尋條，快速索引並啟動所有框架內的項目。
- **全域免安裝綠色架構 (Portable)**：
  - 所有設定檔均收納於程式目錄下的 `Profiles/`，隨身碟即插即用。
  - 打包發布腳本具備**本機設定隔離保護**，任何時候覆蓋更新絕不抹除個人設定檔。
- **極致視覺與主題**：變色龍模式 (Chameleon Mode，自動擷取桌布主色)、圖示光暈陰影效果、6 款應用程式啟動動畫、雙擊桌面空白處一鍵隱藏全桌面圖示。

---

### 📥 下載與安裝

1. 前往 **[Releases 發行頁面](https://github.com/lianghao02/DesktopFramesPlus/releases)** 下載最新免安裝版 `DesktopFramesPlus-v2.8.1-zh-TW.zip`。
2. 解壓縮至任何具備使用者寫入權限的資料夾（如 `D:\Tools\DesktopFramesPlus` 或桌面，請勿放於唯讀的 `Program Files`）。
3. 執行 `Desktop Frames.exe` 即可直接使用，首次啟動自動建立繁體中文設定環境。
4. **升級方式**：直接下載新版 ZIP，解壓縮覆蓋原資料夾內之檔案即可；原本的 `Profiles/` 設定將完整保留。

---

## 🇬🇧 English Description

### 🌾 Farm Fences Concept
> **"In-place hosting, zero duplicated entries, drop-where-you-want."**

- **In-Place Hosting**: Desktop icons stay in their true physical state. No file relocation, no duplicated shortcut files, no hiding off-screen.
- **Free Placement**: Icons stay exactly where you drop them inside a fence with zero coordinate deviation. No forced tight-grid rearrangement.
- **Relative Panning**: Moving a fence translates all nested icons by the same delta vector, preserving relative arrangements.
- **Safe Detachment**: Dismissing a fence only removes grouping boundaries—all files remain intact on the desktop.
- **State Persistence**: The number of fences, window bounds, and individual icon positions are reliably persisted in `Profiles/Default/farm-fences.json`.

### 🌟 Key Features
- **Native Farm Fences, Data Frames, Portal Frames & Note Frames**.
- **Draw Frame Overlay**: Drag-to-create custom fences anywhere on your desktop via the desktop context menu.
- **Desktop Sticky Notes**: Lightweight sticky notes (`Ctrl + Alt + N`), with Morandi color palettes, customizable font sizes, and strict profile isolation.
- **Tabs Engine**: Organize shortcuts into multiple tabs within a single frame.
- **SpotSearch**: Fast keyboard-driven shortcut launcher across all frames.
- **Fully Portable & Safe**: Relative path architecture stored in `Profiles/`. Release script includes local profile preservation so that user data is never wiped during updates.
- **Visuals**: Chameleon wallpaper theme synchronization, launch animations, and desktop double-click auto-hide.

---

## 📄 License & Credits

- **License**: Licensed under the [MIT License](LICENSE.md).
- **Original Project**: Forked from [limbo666/DesktopFramesPlus](https://github.com/limbo666/DesktopFramesPlus), originally based on **BirdyFences** by HakanKokcu.
- **Upstream Authors**: Nikos Georgousis (Hand Water Pump 2025-2026).
- **Localization & Maintenance**: LiangHao.
