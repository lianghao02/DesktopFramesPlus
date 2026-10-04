# 升級與版本更新指南 (Upgrade & Migration Guide)

> **核心保證**：本軟體為 100% 免安裝綠色便攜架構（Portable）。更新版本**絕對不會丟失**原本已建立的柵欄、面板、便箋內容、數量與位置！

---

## 🇹🇼 繁體中文升級說明

### 一、 核心原則：資料與程式碼完全分離
- **個人資料唯一存放處**：所有農場柵欄、自訂面板、桌面便箋、圖示分組與自訂坐標，一律持久化儲存於程式目錄下的 `Profiles/` 資料夾（如 `Profiles/Default/farm-fences.json`）。
- **乾淨發布包防護**：官方釋出的發布檔（如 `DesktopFramesPlus-v2.8.1-zh-TW.zip`）經過發布管線嚴格過濾，**內部預設完全不包含 `Profiles/` 資料夾**。因此下載新版本解壓縮覆蓋時，原本的設定檔絕不會被覆寫或刪除。

---

### 二、 快速升級步驟（三分鐘無痛覆蓋）

#### 步驟 1：完整結束執行中的舊版程式
- 在 Windows 右下角系統工作列常駐圖示上按滑鼠右鍵，選擇**「結束」**（Exit）。
- 確認桌面所有外框與便箋已安全儲存並關閉。

#### 步驟 2：下載最新發布檔
- 前往 **[GitHub Releases 發行頁面](https://github.com/lianghao02/DesktopFramesPlus/releases)**。
- 下載最新版本壓縮檔（例如 `DesktopFramesPlus-v2.8.1-zh-TW.zip`）。

#### 步驟 3：解壓縮並覆蓋程式主檔
- 將下載的 ZIP 檔案解壓縮。
- 將解壓縮出來的所有檔案（`Desktop Frames.exe`、`.dll`、語系檔等）複製並貼上覆蓋原有的程式資料夾。
- **重要檢查**：只要原本資料夾內的 `Profiles` 資料夾維持原樣，所有資料就會 100% 完整保留。

#### 步驟 4：重新啟動程式
- 雙擊執行 `Desktop Frames.exe`。
- 程式啟動時會自動讀取既有的 `Profiles/`，自動執行舊設定格式升級，所有柵欄、框架、便箋的**數量、位置與內部圖示座標均原封不動呈現**！

---

### 三、 關於「檢查更新」與「一鍵更新」

- **檢查更新**：
  - 隨時可在工作列常駐圖示按右鍵，點擊「關於」或透過 GitHub Releases 頁面檢視是否有新版本釋出。
- **為什麼不採用背景自動強制安裝更新？**
  - 本工具秉持**「零背景未授權聯網、零後門服務、純本機執行」**的最高資安原則。
  - 背景靜默更新需要常駐 Windows 服務或高權限排程，容易被防毒軟體誤判且可能在無預警狀態下覆寫記憶體。
  - 綠色覆蓋法是目前 Windows 桌面極客工具最乾淨、最透明且最可靠的更新方式。

---

## 🇬🇧 English Upgrade Guide

### 1. Separation of Data and Binaries
- All user fences, data frames, sticky notes, and icon coordinates are stored strictly within the `Profiles/` directory.
- Official release ZIP archives (e.g. `DesktopFramesPlus-v2.8.1-zh-TW.zip`) are packaged without any `Profiles/` directory. Extracting and overwriting files will never erase your existing configurations.

### 2. Upgrade Steps
1. **Exit the Application**: Right-click the system tray icon and choose **Exit**.
2. **Download Release**: Grab the latest `DesktopFramesPlus-v2.8.1-zh-TW.zip` from GitHub Releases.
3. **Overwrite Files**: Extract and copy all executable and DLL files into your existing program folder. Ensure your `Profiles/` folder remains intact.
4. **Launch**: Run `Desktop Frames.exe`. The application will automatically inherit all existing configurations, preserving all fences, positions, and icon layouts.
