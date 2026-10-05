# HANDOFF

## 核心元資料
- Repository：`lianghao02/DesktopFramesPlus`
- Branch：`main`
- Task Type：FEATURE / SECURITY_FIX / UX_REFACTOR / HANDOFF
- Version：`v2.9.2-zh-TW`
- Commit SHA 基線：`e74db7f`
- Local Path Hint：`D:\Development\GitHub\DesktopFramesPlus`

---

## 1. 專案修改了什麼（架構演進歷程）

1. **資料夾收納柵欄（Portal Frame）重構與在地化**：
   - **終結隱藏黑魔法**：淘汰過去以 `FILE_ATTRIBUTE_HIDDEN` 隱藏桌面圖示之危險做法，全面升級為桌面「實體資料夾即時投影（Portal Frame）」，檔案保持完全正常可見。
   - **智慧收納總目錄**：智慧偵測磁碟，優先使用 `D:\DesktopFrames_Storage`（防 C 槽系統碟塞滿），並於「選項 > 一般」提供圖形化路徑設定。
   - **收納搬移機制**：檔案拖入收納柵欄預設為 Windows 原生 Shell 搬移（`MoveFile`），桌面雜物自動歸位清空；按住 `Ctrl` 拖入維持複製（`CopyFile`）。
   - **選單便利化**：支援「開啟實體收納資料夾」與「變更收納資料夾位置…」。

2. **消除 D 槽多餘空資料夾殘留（邏輯缺陷修正）**：
   - 修正 `CreateNewFrame` 邏輯。以往在連線現成資料夾（如外接隨身碟 `F:\...013拘票`）時，因初始化過早而於 D 槽建立同名空資料夾。現已直接傳入 `portalPath`，連線既有資料夾時 100% 不會在 D 槽留下任何多餘空殼。

3. **徹底拔除背景批次 MoveFile，根除企業防毒勒索誤判**：
   - 拔除原本的「一鍵轉換為資料夾收納柵欄」功能與其背後的背景迴圈批次 `MoveFile`。
   - 確立架構邊界：一般柵欄（Data）純放捷徑、資料夾柵欄（Portal）純做目錄投影，嚴禁程式在背景私自批次搬運使用者公文檔案。

4. **非同步啟動解耦（滑鼠卡頓消除）**：
   - 捷徑啟動全面改採背景 `Task.Run`，啟動大型外部程式時 UI 執行緒零凍結、滑鼠零泥沼感。

---

## 2. 目前功能狀態（能力矩陣）

| 功能項目 | ① 一般收納柵欄（Data 柵欄） | ② 資料夾收納柵欄（Portal 柵欄） |
| :--- | :--- | :--- |
| **本質與角色** | 桌面快捷圖示看板，**原檔 100% 不動** | 實體資料夾即時觀景窗，**所見即所得** |
| **拖曳重排順序** | ✅ **支援**（拖曳有藍色指示條，自由自訂順序） | ❌ **不支援手動排**（順序依循系統檔案規則） |
| **跨柵欄移動** | ✅ **支援**（可從 A 柵欄直接拖移至 B 柵欄） | ❌ 不支援（需以剪下貼上操作實體檔案） |
| **外部拖入檔案** | ✅ 建立快捷分身，原檔留於桌面原處 | ✅ **實體收納搬移**進資料夾（Ctrl 拖入為複製） |
| **排序機制** | 記憶手動自訂順序（`DisplayOrder`） | ✅ **支援 4 種規則**（名稱、日期、類型、大小） |
| **單選 / 多選** | 支援單選點擊啟動；目前**不支援拉框或多選** | 支援單選；目前**不支援拉框或多選** |
| **刪除項目** | ✅ **100% 安全**（只移除桌面圖示，原檔不動） | ⚠️ **動到實體**（將實體檔案移入資源回收筒） |
| **刪除整個柵欄** | ✅ 只關閉桌面框框，原檔不受任何影響 | ✅ 只關閉桌面觀景窗，**硬碟實體資料夾 100% 保留** |

---

## 3. 曾經遇到的重大問題與踩坑記錄

1. **企業防毒（Apex One / CrowdStrike）勒索病毒行為攔截**：
   - **肇因**：「轉換為資料夾收納柵欄」在點擊時，於背景迴圈以毫秒級速度連續 `MoveFile` 搬移公文檔案，觸發防毒啟發式「未授權勒索篡改/搬移（Ransomware Behavior）」防護，執行檔遭防毒隔離殺除。
   - **處方**：徹底刪除該轉換功能與背景批次搬移邏輯。公務環境維持「Zero-Tamper 原則」，未經使用者手動原生拖曳，絕不在背景批次搬運檔案。

2. **D 槽空資料夾心理模型混淆**：
   - 使用者看到 `D:\DesktopFrames_Storage\013拘票` 為空，誤以為是「鏡像」或「紀錄」。
   - **處方**：已修正代碼，連線外部路徑時不再於本機建立空殼。

3. **使用者對刪除圖示傷及原檔之顧慮**：
   - 使用者希望在桌面放資料夾圖示，但不想把肚子裡 20 份公文全倒在桌面，更擔心移除圖示會刪除原檔。
   - **處方**：向使用者釐清模式邊界——純圖示整理請一律使用 **Data 柵欄**（原檔不動、刪除零風險）。

---

## 4. 給 Codex 的技術與架構建議

1. **防毒合規鐵律（Zero-Tamper Principle）**：
   - 本機為公務機關環境，由 Trend Micro Apex One 與 CrowdStrike Falcon 集中控管，使用者無權限加入排除名單。
   - **嚴禁**在背景撰寫批次移動、刪除或修改使用者檔案屬性（如隱藏屬性）的代碼。所有檔案異動必須由使用者手動觸發，並透過 `Microsoft.VisualBasic.FileIO.FileSystem` 原生 Shell 執行。

2. **功能強化方向建議**：
   - **多選功能（Multi-Select）評估**：
     目前兩者皆為單選。若後續欲支援多選框選（Rubber-band selection），需評估將 Data 柵欄的 WrapPanel 重構為支援多選的 ItemsControl/ListBox，但必須同時相容目前的自由拖曳排序指示條。
   - **Data 柵欄一鍵字母排序**：
     建議在 Data 柵欄右鍵選單增加「依名稱重新排序」功能，減輕手動排列負擔。
   - **拖出到桌面單檔復原**：
     目前 Data 柵欄僅支援「右鍵匯出全部圖示至桌面」，可評估支援「單一圖示按住拖出柵欄外即還原至桌面」的直覺體驗。

3. **編譯建置環境注意事項**：
   - 本專案因使用 COM Reference（`IWshRuntimeLibrary`），**嚴禁使用 `dotnet build`**（會報 `MSB4803` 錯誤）。
   - **必須使用 Visual Studio 2022 的 MSBuild**：
     ```powershell
     & "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" "Code\Desktop Frames\Desktop Frames.csproj" /p:Configuration=Release
     ```
   - 編譯完成後，需將 `Code\Desktop Frames\bin\Release\net8.0-windows7.0\` 中的 `Desktop Frames.dll` 與 `Desktop Frames.exe` 覆蓋至 `dist\DesktopFramesPlus\`。

4. **語言與在地化規範**：
   - 維持 100% 台灣繁體中文（公文、捷徑、資料夾、便箋、檔案總管、重新整理、設定、預設、登入、記憶體、硬碟）。
