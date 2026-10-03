# Desktop Frames + (台灣繁體中文版)

<h1 align="center">Desktop Frames +</h1>
<p align="center"><i>像魔法般整理你的 Windows 桌面！ / Organize your desktop like magic!</i></p>
<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0--windows-512bd4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Language-繁體中文%20%7C%20English-blue?style=flat-square" alt="Language" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078d4?style=flat-square&logo=windows" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="License" />
</p>

<p align="center">
  <img width="150" height="150" alt="Desktop Frames150" src="https://github.com/user-attachments/assets/a88f7771-8ae8-4be8-86dc-4e8aabfa5a77" />
</p>

---

## 📌 關於此分支與來源引註 (About This Fork & Upstream Attribution)

- **專案定位**：本專案為 [limbo666/DesktopFramesPlus](https://github.com/limbo666/DesktopFramesPlus) 之開源維護分支。
- **維護重點**：提供完整的台灣繁體中文 (zh-TW) 介面支援、解除未經授權之外部個人捐款連結，並在未設定下游更新發行伺服器前停用預設自動更新檢查，防止使用者本機配置被覆蓋或中斷。
- **授權與致謝**：
  - 本專案與原專案均遵循 **[MIT License](License.md)** 授權。
  - 核心架構最初由 **HakanKokcu** 以 *BirdyFences* 之名發起。
  - 後續由 **Nikos Georgousis (Hand Water Pump 2025-2026)** 進行大規模效能優化與功能擴充。
  - 繁體中文版本由 **LiangHao** 進行多語系資源在地化與發行治理維護。

---

## 🇹🇼 繁體中文說明

### 🌟 核心特色
- **多元面板類型**：支援自訂捷徑資料面板 (Data Frames)、資料夾即時鏡射面板 (Portal Frames，支援內部瀏覽與篩選) 以及便籤文字面板 (Note Frames)。
- **分頁管理引擎**：單一面板內支援建立多個分頁，告別擁擠桌面。
- **工作區設定檔 (Profiles)**：可為工作、遊戲等建立獨立佈局，並支援關聯應用程式啟動時自動切換。
- **智慧桌面引擎 (Smart Desktop)**：可自訂規則，自動將桌面新檔案分門別類歸檔至指定資料夾或面板。
- **SpotSearch 快速搜尋**：按下快速鍵即刻呼叫搜尋面板，秒級尋找並啟動所有面板中的捷徑。
- **全域免安裝可攜架構 (Portable)**：配置與捷徑皆使用相對路徑儲存於 `Profiles/` 資料夾，隨身碟即開即用。
- **桌面便箋 (Desktop Notes)**：輕量極簡桌面便利貼，支援即時自動存檔、`Ctrl + Alt + N` 快速建立、4 段字級（12/14/16/18）與多字型切換、置頂、鎖定與 6 款莫蘭迪配色。
- **農場圍籬 (原地分組實驗沙盒)**：回歸原生桌面組織核心——原檔原處、零第二入口、點擊與右鍵 100% 穿透原生 Explorer，支援拖曳同步座標與退出零遺失。完整技術規格詳見 [FARM_FENCE_SPEC.md](FARM_FENCE_SPEC.md)。
- **清晰分類之卡片式現代化設定介面**：設定視窗全 7 大分頁（一般、樣式與效果、工具、工作區、快捷鍵、智慧桌面、高階日誌）全面導入現代化圓角卡片群組與主題色視覺指標，徹底告別舊版雜亂無章的條目堆疊，階層清爽、一目了然。
- **高 DPI 縮放座標精確修復**：全面感知螢幕真實縮放係數（125%、150% 等），將物理游標像素精準換算為 WPF DIPs，徹底解決拉動面板邊界時尺寸反饋數字偏離至螢幕遠處、以及圖示拖曳半透明預覽嚴重脫離游標的頑疾。
- **公用桌面智慧授權與拖曳保護**：自動偵測 Windows 公用桌面（Steam、PotPlayer 等受保護目錄）捷徑，拖曳當下提供即時 UAC 授權並自動接續收納；主程式維持安全一般使用者權限，徹底杜絕 Windows UIPI 介面隔離造成的拖曳阻斷。
- **純淨桌面與浮動圓點開關**：支援雙擊桌面空白處一鍵隱藏桌面所有圖示，並可在設定中自由切換螢幕底部浮動小圓點顯示開關。
- **極致視覺與主題**：支援變色龍模式 (Chameleon Mode，自動擷取桌布主色)、圖示光暈陰影效果、6 種應用程式啟動動畫與面板自動捲起。

### 📥 下載與安裝
1. 前往 **[Releases 發行頁面](https://github.com/lianghao02/DesktopFramesPlus/releases)** 下載最新免安裝 ZIP 壓縮包。
2. 解壓縮至任何具備使用者寫入權限的資料夾（如 `D:\Tools\DesktopFramesPlus` 或桌面，請勿放於唯讀的 `Program Files`）。
3. 執行 `Desktop Frames.exe` 即可直接使用，首次啟動自動建立繁體中文設定環境。

---

## 🇬🇧 English Description

### 🌟 Key Features
- **Multiple Frame Types**: Create Data Frames for custom shortcuts, Portal Frames that actively mirror folder contents (with internal navigation and filters), and Note Frames for quick text.
- **Desktop Notes**: Lightweight, always-on sticky notes featuring instant auto-save, `Ctrl + Alt + N` shortcut creation, 4 font sizes (12/14/16/18), customizable fonts, pin-to-top, position lock, and 6 soft Morandi color palettes.
- **Farm Fence (In-Place Grouping Sandbox)**: Organizes original desktop icons directly in place without duplicating shortcuts or moving real files. Full hit-test transparency for native Windows shell interaction. See [FARM_FENCE_SPEC.md](FARM_FENCE_SPEC.md).
- **Tabs Engine**: Keep your desktop clean by organizing shortcuts into multiple tabs within a single frame, complete with tab overflow management.
- **Workspace Profiles**: Create independent layouts for different workflows (e.g., Work, Gaming). Switch profiles manually via hotkeys, or use Profile Automation to switch automatically when specific programs are launched.
- **Smart Desktop Engine**: Automatically sort and move incoming files into specific Portal Frames or folders based on custom user rules.
- **SpotSearch**: A built-in quick-search pane invoked by a hotkey to instantly find and launch shortcuts across all your frames.
- **Fully Portable**: Utilizes relative paths for files and folders to ensure seamless operation across different environments.
- **Theming & Effects**: Customize individual frame background colors, set global tint levels, use Chameleon Mode to match wallpaper colors, and pick from multiple launch animations.

### 📥 Download & Installation
1. Get the latest release package from the **[Releases Section](https://github.com/lianghao02/DesktopFramesPlus/releases)**.
2. Extract the ZIP package into any user-writable folder (avoid write-protected paths like `Program Files`).
3. Run `Desktop Frames.exe`. All necessary configuration files and folders will be created automatically on first run.

---

## 📄 License & Credits

- **License**: Licensed under the [MIT License](License.md).
- **Original Project**: Forked from [limbo666/DesktopFramesPlus](https://github.com/limbo666/DesktopFramesPlus), which was originally based on **BirdyFences** by HakanKokcu.
- **Upstream Authors**: Nikos Georgousis (Hand Water Pump 2025-2026).
- **Localization & Maintenance**: LiangHao.
