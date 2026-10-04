using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Diagnostics;
using Newtonsoft.Json;

namespace Desktop_Frames.Services
{
    public class FenceInventoryManager
    {
        private static readonly Lazy<FenceInventoryManager> _instance =
            new Lazy<FenceInventoryManager>(() => new FenceInventoryManager());

        public static FenceInventoryManager Instance => _instance.Value;

        private readonly object _lock = new object();
        private FenceInventoryState _state = new FenceInventoryState();

        private readonly string _primaryStoreDir;
        private readonly string _primaryJsonPath;
        private readonly string _backupJsonPath;
        private readonly string _managedShortcutsDir;

        #region Win32 Shell & Native File Attributes
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetFileAttributes(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNE_UPDATEITEM = 0x00002000;
        private const uint SHCNE_ATTRIBUTES = 0x00000800;
        private const uint SHCNF_FLUSH = 0x1000;
        private const uint SHCNF_PATHW = 0x0005;

        private const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        public static void RefreshDesktopItem(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                IntPtr pPath = Marshal.StringToHGlobalUni(path);
                try
                {
                    SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, pPath, IntPtr.Zero);
                    SHChangeNotify(SHCNE_ATTRIBUTES, SHCNF_PATHW, pPath, IntPtr.Zero);
                }
                finally
                {
                    Marshal.FreeHGlobal(pPath);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Failed to notify item change for '{path}': {ex.Message}");
            }
        }

        public static void RefreshDesktopShell()
        {
            try
            {
                // 1. 刷新使用者桌面目錄檢視
                string userDesk = UserDesktopPath;
                if (!string.IsNullOrEmpty(userDesk) && Directory.Exists(userDesk))
                {
                    IntPtr pDesk = Marshal.StringToHGlobalUni(userDesk);
                    try
                    {
                        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, pDesk, IntPtr.Zero);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pDesk);
                    }
                }

                // 2. 刷新公用桌面目錄檢視
                try
                {
                    string commonDesk = CommonDesktopPath;
                    if (!string.IsNullOrEmpty(commonDesk) && Directory.Exists(commonDesk))
                    {
                        IntPtr pCommon = Marshal.StringToHGlobalUni(commonDesk);
                        try
                        {
                            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, pCommon, IntPtr.Zero);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(pCommon);
                        }
                    }
                }
                catch { }

                // 3. 發送全域快取清空與關聯變更通知
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Failed to notify shell change: {ex.Message}");
            }
        }

        public static bool RemoveHiddenAttribute(string path, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            bool success = false;
            try
            {
                if (isDirectory)
                {
                    if (Directory.Exists(path))
                    {
                        var attr = File.GetAttributes(path);
                        var newAttr = (attr & ~FileAttributes.Hidden);
                        if (newAttr == 0) newAttr = FileAttributes.Directory;
                        File.SetAttributes(path, newAttr);
                        success = true;
                    }
                }
                else
                {
                    if (File.Exists(path))
                    {
                        var attr = File.GetAttributes(path);
                        var newAttr = (attr & ~FileAttributes.Hidden);
                        if (newAttr == 0) newAttr = FileAttributes.Normal;
                        File.SetAttributes(path, newAttr);
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                    $"RemoveHiddenAttribute (.NET) for '{path}' encountered: {ex.Message}");
            }

            // 第二道原生防線：Win32 Kernel32 API 強制拔除 Hidden 旗標
            try
            {
                uint nativeAttr = GetFileAttributes(path);
                if (nativeAttr != INVALID_FILE_ATTRIBUTES && (nativeAttr & FILE_ATTRIBUTE_HIDDEN) != 0)
                {
                    uint newNative = nativeAttr & ~FILE_ATTRIBUTE_HIDDEN;
                    if (newNative == 0)
                    {
                        newNative = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
                    }
                    if (SetFileAttributes(path, newNative))
                    {
                        success = true;
                    }
                }
            }
            catch { }

            RefreshDesktopItem(path);
            return success;
        }

        public static bool AddHiddenAttribute(string path, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            bool success = false;
            try
            {
                if (isDirectory)
                {
                    if (Directory.Exists(path))
                    {
                        var attr = File.GetAttributes(path);
                        File.SetAttributes(path, attr | FileAttributes.Hidden);
                        success = true;
                    }
                }
                else
                {
                    if (File.Exists(path))
                    {
                        var attr = File.GetAttributes(path);
                        File.SetAttributes(path, attr | FileAttributes.Hidden);
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                    $"AddHiddenAttribute (.NET) for '{path}' encountered: {ex.Message}");
            }

            // 第二道原生防線：Win32 Kernel32 API 強制加上 Hidden 旗標
            try
            {
                uint nativeAttr = GetFileAttributes(path);
                if (nativeAttr != INVALID_FILE_ATTRIBUTES && (nativeAttr & FILE_ATTRIBUTE_HIDDEN) == 0)
                {
                    uint newNative = nativeAttr | FILE_ATTRIBUTE_HIDDEN;
                    if (SetFileAttributes(path, newNative))
                    {
                        success = true;
                    }
                }
            }
            catch { }

            RefreshDesktopItem(path);
            return success;
        }
        #endregion

        public FenceInventoryManager()
        {
            // 完全收斂於程式自帶目錄（Portable 免安裝，不污染 LocalAppData）
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            string profileDir = null;
            try
            {
                profileDir = ProfileManager.CurrentProfileDir;
            }
            catch { }

            _primaryStoreDir = !string.IsNullOrEmpty(profileDir) && Directory.Exists(profileDir)
                ? profileDir
                : appBaseDir;

            _primaryJsonPath = Path.Combine(_primaryStoreDir, "fence_inventory.json");
            _backupJsonPath = Path.Combine(appBaseDir, "fence_inventory.json");

            // 託管捷徑直接使用程式自帶的 Shortcuts 目錄
            string shortcutsDir = Path.Combine(_primaryStoreDir, "Shortcuts");
            if (!Directory.Exists(shortcutsDir))
            {
                shortcutsDir = Path.Combine(appBaseDir, "Shortcuts");
            }
            _managedShortcutsDir = shortcutsDir;

            try
            {
                if (!Directory.Exists(_primaryStoreDir)) Directory.CreateDirectory(_primaryStoreDir);
                if (!Directory.Exists(_managedShortcutsDir)) Directory.CreateDirectory(_managedShortcutsDir);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Failed to create inventory directories: {ex.Message}");
            }

            LoadState();
        }

        #region State Persistence (WAL Atomicity)
        private void LoadState()
        {
            lock (_lock)
            {
                string jsonToLoad = null;

                if (File.Exists(_primaryJsonPath))
                {
                    try { jsonToLoad = File.ReadAllText(_primaryJsonPath); }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                            $"Failed to read primary inventory JSON: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(jsonToLoad) && File.Exists(_backupJsonPath))
                {
                    try { jsonToLoad = File.ReadAllText(_backupJsonPath); }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                            $"Failed to read backup inventory JSON: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(jsonToLoad))
                {
                    try
                    {
                        _state = JsonConvert.DeserializeObject<FenceInventoryState>(jsonToLoad) ?? new FenceInventoryState();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            $"Failed to deserialize inventory state: {ex.Message}");
                        _state = new FenceInventoryState();
                    }
                }
                else
                {
                    _state = new FenceInventoryState();
                }

                // 舊版 LocalAppData 自動遷移與合併（若存在舊記錄）
                MigrateLegacyLocalAppDataInventory();
            }
        }

        private void MigrateLegacyLocalAppDataInventory()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string legacyPath = Path.Combine(localAppData, "DesktopFramesPlus", "Inventory", "fence_inventory.json");
                if (File.Exists(legacyPath))
                {
                    string legacyJson = File.ReadAllText(legacyPath);
                    var legacyState = JsonConvert.DeserializeObject<FenceInventoryState>(legacyJson);
                    if (legacyState?.Items != null && legacyState.Items.Count > 0)
                    {
                        bool modified = false;
                        foreach (var legacyItem in legacyState.Items)
                        {
                            if (!_state.Items.Any(i => string.Equals(i.OriginalFullPath, legacyItem.OriginalFullPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                _state.Items.Add(legacyItem);
                                modified = true;
                            }
                        }
                        if (modified)
                        {
                            SaveState();
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                                $"Migrated legacy inventory items from '{legacyPath}' into local portable inventory.");
                        }
                    }

                    try
                    {
                        File.Move(legacyPath, legacyPath + ".migrated", true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Legacy inventory migration encountered: {ex.Message}");
            }
        }

        public void UpdateItemStoragePath(string itemId, string newManagedStoragePath)
        {
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(newManagedStoragePath)) return;
            lock (_lock)
            {
                var item = _state.Items.FirstOrDefault(i => i.Id == itemId);
                if (item != null)
                {
                    item.ManagedStoragePath = newManagedStoragePath;
                    SaveState();
                }
            }
        }

        private void SaveState()
        {
            lock (_lock)
            {
                _state.LastUpdated = DateTime.UtcNow;
                string json = JsonConvert.SerializeObject(_state, Formatting.Indented);

                bool primarySuccess = SaveAtomic(_primaryJsonPath, json);
                if (!string.Equals(_primaryJsonPath, _backupJsonPath, StringComparison.OrdinalIgnoreCase))
                {
                    SaveAtomic(_backupJsonPath, json);
                }
                if (!primarySuccess)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                        "Warning: Primary inventory state file failed to save atomically.");
                }
            }
        }

        private bool SaveAtomic(string targetPath, string content)
        {
            try
            {
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string tempPath = targetPath + ".tmp";
                File.WriteAllText(tempPath, content, System.Text.Encoding.UTF8);

                if (File.Exists(targetPath))
                {
                    string backupPath = targetPath + ".bak";
                    try
                    {
                        File.Replace(tempPath, targetPath, backupPath, true);
                    }
                    catch
                    {
                        File.Copy(targetPath, backupPath, true);
                        File.Copy(tempPath, targetPath, true);
                        try { File.Delete(tempPath); } catch { }
                    }
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Failed to atomically save inventory to '{targetPath}': {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Desktop Detection & Preflight Checks
        public static string UserDesktopPath =>
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        public static string CommonDesktopPath =>
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        public static bool IsFromDesktop(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
                string userDesk = Path.GetFullPath(UserDesktopPath).TrimEnd('\\', '/');
                string commonDesk = string.Empty;
                try { commonDesk = Path.GetFullPath(CommonDesktopPath).TrimEnd('\\', '/'); } catch { }

                bool inUserDesk = fullPath.StartsWith(userDesk, StringComparison.OrdinalIgnoreCase);
                bool inCommonDesk = !string.IsNullOrEmpty(commonDesk) &&
                                    fullPath.StartsWith(commonDesk, StringComparison.OrdinalIgnoreCase);

                return inUserDesk || inCommonDesk;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsInCommonDesktop(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
                string commonDesk = Path.GetFullPath(CommonDesktopPath).TrimEnd('\\', '/');
                return !string.IsNullOrEmpty(commonDesk) &&
                       fullPath.StartsWith(commonDesk, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCurrentProcessAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool HasCommonDesktopWritePermission()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CommonDesktopPath) || !Directory.Exists(CommonDesktopPath))
                    return false;

                string testFile = Path.Combine(CommonDesktopPath, $".dfp_perm_check_{Guid.NewGuid():N}.tmp");
                using (var fs = new FileStream(testFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] testByte = new byte[] { 0 };
                    fs.Write(testByte, 0, testByte.Length);
                }
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool GrantCommonDesktopPermission()
        {
            try
            {
                string username = Environment.UserName;
                string commonPath = CommonDesktopPath;
                if (string.IsNullOrWhiteSpace(commonPath) || !Directory.Exists(commonPath))
                    return false;

                var psi = new ProcessStartInfo
                {
                    FileName = "icacls.exe",
                    Arguments = $"\"{commonPath}\" /grant \"{username}:(OI)(CI)M\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    proc.WaitForExit();
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                    $"Grant common desktop permission failed or user canceled UAC: {ex.Message}");
                return false;
            }
        }

        public bool PreflightCheck(string path, out string failureReason)
        {
            failureReason = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                failureReason = "路徑為空。";
                return false;
            }

            bool isFile = File.Exists(path);
            bool isDir = Directory.Exists(path);
            if (!isFile && !isDir)
            {
                failureReason = $"找不到目標檔案或資料夾：{path}";
                return false;
            }

            // 2. 檔案鎖定與寫入權限檢查
            if (isFile)
            {
                try
                {
                    // 測試檔案鎖定
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    {
                        // 能夠正常開啟，未被排他獨佔
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    if (IsInCommonDesktop(path))
                    {
                        failureReason = "此圖示位於系統「公用桌面」(Public Desktop)，Windows 預設限制一般權限搬移。\n\n" +
                                        "建議解決方式（請擇一）：\n" +
                                        "1. 手動將該圖示「剪下」並貼到「個人桌面」（Windows 會提示一次提權），之後即可正常拖入柵欄。\n" +
                                        "2. 以系統管理員身分開啟 PowerShell 執行一次性目錄授權：\n" +
                                        "   icacls \"C:\\Users\\Public\\Desktop\" /grant \"$($env:USERNAME):(OI)(CI)M\"\n" +
                                        "   授權後本程式便能直接無痛收納公用桌面圖示。\n\n" +
                                        "※ 請勿將本程式以「系統管理員身分」啟動，否則 Windows UIPI 機制會全面封鎖桌面拖曳。";
                    }
                    else
                    {
                        failureReason = "存取遭拒：目前使用者對該檔案缺少寫入或修改屬性之權限。";
                    }
                    return false;
                }
                catch (IOException ioEx)
                {
                    failureReason = $"檔案目前被其他應用程式獨佔鎖定中：{ioEx.Message}";
                    return false;
                }
                catch (Exception ex)
                {
                    failureReason = $"無法存取檔案：{ex.Message}";
                    return false;
                }

                // 測試屬性寫入能力
                try
                {
                    var attr = File.GetAttributes(path);
                    File.SetAttributes(path, attr);
                }
                catch (Exception ex)
                {
                    failureReason = $"無法修改檔案屬性（缺少 FILE_WRITE_ATTRIBUTES 權限）：{ex.Message}";
                    return false;
                }
            }

            return true;
        }
        #endregion

        #region Adoption Operations
        public bool TryAdopt(string originalPath, string frameId, out ManagedItemRecord adoptedRecord, out string errorMessage)
        {
            adoptedRecord = null;
            errorMessage = string.Empty;

            if (!IsFromDesktop(originalPath))
            {
                // 非來自桌面，不納入農場圍籬所有權互斥模型
                return false;
            }

            if (!PreflightCheck(originalPath, out errorMessage))
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                    $"Adoption preflight check rejected for '{originalPath}': {errorMessage}");
                return false;
            }

            string fullPath = Path.GetFullPath(originalPath);
            bool isDirectory = Directory.Exists(fullPath);
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            bool isShortcut = ext == ".lnk" || ext == ".url";

            AdoptionType type = isShortcut ? AdoptionType.ShortcutMove : AdoptionType.InPlaceHidden;
            FileAttributes origAttr = isDirectory ? new DirectoryInfo(fullPath).Attributes : File.GetAttributes(fullPath);

            var record = new ManagedItemRecord
            {
                OriginalFullPath = fullPath,
                Type = type,
                OriginalAttributes = origAttr,
                BelongingFrameId = frameId ?? string.Empty,
                Phase = AdoptionPhase.Pending,
                DisplayName = Path.GetFileNameWithoutExtension(fullPath),
                IsDirectory = isDirectory
            };

            lock (_lock)
            {
                // WAL Step 1: 登記 Pending
                _state.Items.Add(record);
                SaveState();
            }

            try
            {
                // WAL Step 2: 執行接管
                record.Phase = AdoptionPhase.Executing;

                if (type == AdoptionType.ShortcutMove)
                {
                    string fileName = Path.GetFileName(fullPath);
                    string destPath = Path.Combine(_managedShortcutsDir, fileName);
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        string nameNoExt = Path.GetFileNameWithoutExtension(fullPath);
                        destPath = Path.Combine(_managedShortcutsDir, $"{nameNoExt} ({counter++}){ext}");
                    }

                    File.Move(fullPath, destPath);
                    record.ManagedStoragePath = destPath;
                }
                else
                {
                    // InPlaceHidden: 原地增加 Hidden 屬性（雙重防線：.NET API + Win32 Kernel32 API）
                    AddHiddenAttribute(fullPath, isDirectory);
                    record.ManagedStoragePath = fullPath;
                }

                // WAL Step 3: 標記 Committed
                record.Phase = AdoptionPhase.Committed;
                lock (_lock)
                {
                    SaveState();
                }

                RefreshDesktopShell();
                adoptedRecord = record;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Successfully adopted '{fullPath}' as {type} into frame '{frameId}'");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"接管執行失敗：{ex.Message}";
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Failed during adoption execution of '{fullPath}': {ex.Message}");

                // WAL Rollback
                record.Phase = AdoptionPhase.RollingBack;
                try
                {
                    if (type == AdoptionType.ShortcutMove && !string.IsNullOrEmpty(record.ManagedStoragePath) && File.Exists(record.ManagedStoragePath))
                    {
                        File.Move(record.ManagedStoragePath, record.OriginalFullPath);
                    }
                    else if (type == AdoptionType.InPlaceHidden)
                    {
                        RemoveHiddenAttribute(fullPath, isDirectory);
                    }
                }
                catch (Exception rollbackEx)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                        $"Rollback failed for '{fullPath}': {rollbackEx.Message}");
                }

                lock (_lock)
                {
                    _state.Items.Remove(record);
                    SaveState();
                }

                RefreshDesktopShell();
                return false;
            }
        }
        #endregion

        #region Safe Release Protocol (Back to Desktop)
        public bool ReleaseItemToDesktop(ManagedItemRecord record)
        {
            if (record == null) return false;

            try
            {
                if (record.Type == AdoptionType.ShortcutMove)
                {
                    if (!string.IsNullOrEmpty(record.ManagedStoragePath) && File.Exists(record.ManagedStoragePath))
                    {
                        string destDesktopPath = record.OriginalFullPath;
                        string dir = Path.GetDirectoryName(destDesktopPath);
                        string nameNoExt = Path.GetFileNameWithoutExtension(destDesktopPath);
                        string ext = Path.GetExtension(destDesktopPath);
                        int counter = 1;

                        // 同名防覆寫安全網
                        while (File.Exists(destDesktopPath))
                        {
                            destDesktopPath = Path.Combine(dir, $"{nameNoExt} ({counter++}){ext}");
                        }

                        File.Move(record.ManagedStoragePath, destDesktopPath);
                    }
                }
                else if (record.Type == AdoptionType.InPlaceHidden)
                {
                    // 強制拔除 Hidden 屬性（雙重防線：.NET API + Win32 Kernel32 API）
                    RemoveHiddenAttribute(record.OriginalFullPath, record.IsDirectory);

                    // 若原託管捷徑存在於 Shortcuts 目錄中，刪除之，避免殘留
                    if (!string.IsNullOrEmpty(record.ManagedStoragePath) &&
                        !string.Equals(record.ManagedStoragePath, record.OriginalFullPath, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(record.ManagedStoragePath))
                    {
                        try { File.Delete(record.ManagedStoragePath); } catch { }
                    }
                }

                record.Phase = AdoptionPhase.Released;
                lock (_lock)
                {
                    _state.Items.Remove(record);
                    SaveState();
                }

                RefreshDesktopShell();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Safely released item '{record.OriginalFullPath}' back to desktop.");
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Failed to release item '{record.OriginalFullPath}': {ex.Message}");
                return false;
            }
        }

        public int ReleaseFrameItemsToDesktop(string frameId)
        {
            if (string.IsNullOrEmpty(frameId)) return 0;

            List<ManagedItemRecord> itemsToRelease;
            lock (_lock)
            {
                itemsToRelease = _state.Items
                    .Where(i => i.BelongingFrameId == frameId && 
                               (i.Phase == AdoptionPhase.Committed || i.Phase == AdoptionPhase.Suspended))
                    .ToList();
            }

            int count = 0;
            foreach (var item in itemsToRelease)
            {
                if (ReleaseItemToDesktop(item)) count++;
            }

            RefreshDesktopShell();
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                $"Released {count} items of frame '{frameId}' back to desktop.");
            return count;
        }

        public bool ReleaseItemByPath(string managedOrOriginalPath)
        {
            if (string.IsNullOrWhiteSpace(managedOrOriginalPath)) return false;

            ManagedItemRecord target = null;
            string fullPathNorm = null;
            try { fullPathNorm = Path.GetFullPath(managedOrOriginalPath).TrimEnd('\\', '/'); } catch { }

            string targetFromShortcut = null;
            if (File.Exists(managedOrOriginalPath) && string.Equals(Path.GetExtension(managedOrOriginalPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    targetFromShortcut = FilePathUtilities.GetShortcutTargetUnicodeSafe(managedOrOriginalPath);
                    if (!string.IsNullOrEmpty(targetFromShortcut))
                    {
                        targetFromShortcut = Path.GetFullPath(targetFromShortcut).TrimEnd('\\', '/');
                    }
                }
                catch { }
            }

            string fileName = Path.GetFileName(managedOrOriginalPath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(managedOrOriginalPath);

            lock (_lock)
            {
                // 優先度 1: 捷徑目標路徑精確比對 OriginalFullPath
                if (!string.IsNullOrEmpty(targetFromShortcut))
                {
                    target = _state.Items.FirstOrDefault(i =>
                        string.Equals(i.OriginalFullPath?.TrimEnd('\\', '/'), targetFromShortcut, StringComparison.OrdinalIgnoreCase));
                }

                // 優先度 2: 直接路徑比對（包含標準化完整路徑與原字串）
                if (target == null)
                {
                    target = _state.Items.FirstOrDefault(i =>
                        string.Equals(i.ManagedStoragePath, managedOrOriginalPath, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(i.OriginalFullPath, managedOrOriginalPath, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(fullPathNorm) && (
                            string.Equals(i.ManagedStoragePath?.TrimEnd('\\', '/'), fullPathNorm, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(i.OriginalFullPath?.TrimEnd('\\', '/'), fullPathNorm, StringComparison.OrdinalIgnoreCase))));
                }

                // 優先度 3: 檔名與 DisplayName 模糊匹配
                if (target == null)
                {
                    target = _state.Items.FirstOrDefault(i =>
                        string.Equals(i.DisplayName, nameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(i.OriginalFullPath), fileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileNameWithoutExtension(i.OriginalFullPath), nameWithoutExt, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (target != null)
            {
                bool released = ReleaseItemToDesktop(target);
                // 若傳入的是面板捷徑，確保將其從磁碟刪除
                if (File.Exists(managedOrOriginalPath))
                {
                    try { File.Delete(managedOrOriginalPath); } catch { }
                }
                return released;
            }

            // 兜底防護：若在庫存中未找到任何記錄，但傳入的捷徑指向桌面實體檔案，仍主動嘗試解除該檔案的 Hidden 屬性
            if (!string.IsNullOrEmpty(targetFromShortcut) && IsFromDesktop(targetFromShortcut))
            {
                bool isDir = Directory.Exists(targetFromShortcut);
                bool isFile = File.Exists(targetFromShortcut);
                if (isDir || isFile)
                {
                    RemoveHiddenAttribute(targetFromShortcut, isDir);
                    if (File.Exists(managedOrOriginalPath))
                    {
                        try { File.Delete(managedOrOriginalPath); } catch { }
                    }
                    RefreshDesktopShell();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                        $"Fallback unhidden performed for desktop item '{targetFromShortcut}' referenced by shortcut '{managedOrOriginalPath}'");
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Cross-frame Ownership Transfer
        public bool TransferItemOwnership(string itemPath, string newFrameId)
        {
            if (string.IsNullOrWhiteSpace(itemPath) || string.IsNullOrEmpty(newFrameId)) return false;

            lock (_lock)
            {
                var record = _state.Items.FirstOrDefault(i =>
                    string.Equals(i.ManagedStoragePath, itemPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.OriginalFullPath, itemPath, StringComparison.OrdinalIgnoreCase));

                if (record != null)
                {
                    record.BelongingFrameId = newFrameId;
                    SaveState();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                        $"Transferred ownership of '{record.OriginalFullPath}' to frame '{newFrameId}'.");
                    return true;
                }
            }
            return false;
        }

        public ManagedItemRecord FindRecord(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            lock (_lock)
            {
                return _state.Items.FirstOrDefault(i =>
                    string.Equals(i.ManagedStoragePath, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.OriginalFullPath, path, StringComparison.OrdinalIgnoreCase));
            }
        }
        #endregion

        #region Startup Crash Recovery & Reconciliation
        public void ReconcileOnStartup()
        {
            lock (_lock)
            {
                var pendingOrRolling = _state.Items
                    .Where(i => i.Phase == AdoptionPhase.Pending || i.Phase == AdoptionPhase.RollingBack)
                    .ToList();

                foreach (var item in pendingOrRolling)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        $"Startup reconciliation: rolling back interrupted item '{item.OriginalFullPath}'");
                    ReleaseItemToDesktop(item);
                }

                var executingItems = _state.Items
                    .Where(i => i.Phase == AdoptionPhase.Executing)
                    .ToList();

                foreach (var item in executingItems)
                {
                    // 檢查是否已成功移動或隱藏
                    if (item.Type == AdoptionType.ShortcutMove && File.Exists(item.ManagedStoragePath))
                    {
                        item.Phase = AdoptionPhase.Committed;
                    }
                    else if (item.Type == AdoptionType.InPlaceHidden && (File.Exists(item.OriginalFullPath) || Directory.Exists(item.OriginalFullPath)))
                    {
                        item.Phase = AdoptionPhase.Committed;
                    }
                    else
                    {
                        ReleaseItemToDesktop(item);
                    }
                }

                SaveState();
            }

            RefreshDesktopShell();
        }
        #endregion

        #region Session Lifecycle: Exit Suspend & Startup Resume
        /// <summary>
        /// 當使用者關閉整個專案（退出程式）時，將所有在柵欄裡的東西安全還原回桌面，並保持休眠清單
        /// </summary>
        public void SuspendAllItemsToDesktop()
        {
            lock (_lock)
            {
                var committedItems = _state.Items
                    .Where(i => i.Phase == AdoptionPhase.Committed)
                    .ToList();

                if (committedItems.Count == 0) return;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Suspending {committedItems.Count} fence items back to desktop on application exit...");

                foreach (var record in committedItems)
                {
                    try
                    {
                        if (record.Type == AdoptionType.ShortcutMove)
                        {
                            if (!string.IsNullOrEmpty(record.ManagedStoragePath) && File.Exists(record.ManagedStoragePath))
                            {
                                string destDesktopPath = record.OriginalFullPath;
                                string dir = Path.GetDirectoryName(destDesktopPath);
                                string nameNoExt = Path.GetFileNameWithoutExtension(destDesktopPath);
                                string ext = Path.GetExtension(destDesktopPath);
                                int counter = 1;

                                while (File.Exists(destDesktopPath))
                                {
                                    destDesktopPath = Path.Combine(dir, $"{nameNoExt} ({counter++}){ext}");
                                }

                                File.Move(record.ManagedStoragePath, destDesktopPath);
                                record.OriginalFullPath = destDesktopPath;
                            }
                        }
                        else if (record.Type == AdoptionType.InPlaceHidden)
                        {
                            RemoveHiddenAttribute(record.OriginalFullPath, record.IsDirectory);
                        }

                        record.Phase = AdoptionPhase.Suspended;
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Failed to suspend item '{record.OriginalFullPath}': {ex.Message}");
                    }
                }

                SaveState();
            }

            RefreshDesktopShell();
        }

        /// <summary>
        /// 當使用者開啟專案時，自動將休眠的動物重新收納回柵欄
        /// </summary>
        public void ResumeSuspendedItemsFromDesktop()
        {
            lock (_lock)
            {
                var suspendedItems = _state.Items
                    .Where(i => i.Phase == AdoptionPhase.Suspended)
                    .ToList();

                if (suspendedItems.Count == 0) return;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Resuming {suspendedItems.Count} suspended items back into fences on startup...");

                var existingFrameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (FrameDataManager.FrameData != null)
                {
                    foreach (var f in FrameDataManager.FrameData)
                    {
                        string id = f.Id?.ToString();
                        if (!string.IsNullOrEmpty(id)) existingFrameIds.Add(id);
                    }
                }

                var itemsToRemove = new List<ManagedItemRecord>();

                foreach (var record in suspendedItems)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(record.BelongingFrameId) && !existingFrameIds.Contains(record.BelongingFrameId))
                        {
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                                $"Frame '{record.BelongingFrameId}' no longer exists. Leaving '{record.OriginalFullPath}' permanently on desktop.");
                            itemsToRemove.Add(record);
                            continue;
                        }

                        if (record.Type == AdoptionType.ShortcutMove)
                        {
                            if (File.Exists(record.OriginalFullPath))
                            {
                                string dest = record.ManagedStoragePath;
                                string dir = Path.GetDirectoryName(dest);
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                                int counter = 1;
                                string nameNoExt = Path.GetFileNameWithoutExtension(dest);
                                string ext = Path.GetExtension(dest);
                                while (File.Exists(dest))
                                {
                                    dest = Path.Combine(dir, $"{nameNoExt} ({counter++}){ext}");
                                }

                                File.Move(record.OriginalFullPath, dest);
                                record.ManagedStoragePath = dest;
                                record.Phase = AdoptionPhase.Committed;
                            }
                            else if (File.Exists(record.ManagedStoragePath))
                            {
                                record.Phase = AdoptionPhase.Committed;
                            }
                            else
                            {
                                itemsToRemove.Add(record);
                            }
                        }
                        else if (record.Type == AdoptionType.InPlaceHidden)
                        {
                            if ((record.IsDirectory && Directory.Exists(record.OriginalFullPath)) ||
                                (!record.IsDirectory && File.Exists(record.OriginalFullPath)))
                            {
                                AddHiddenAttribute(record.OriginalFullPath, record.IsDirectory);
                                record.Phase = AdoptionPhase.Committed;
                            }
                            else
                            {
                                itemsToRemove.Add(record);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Failed to resume item '{record.OriginalFullPath}': {ex.Message}");
                    }
                }

                foreach (var rm in itemsToRemove)
                {
                    _state.Items.Remove(rm);
                }

                SaveState();
            }

            RefreshDesktopShell();
        }
        #endregion
    }
}
