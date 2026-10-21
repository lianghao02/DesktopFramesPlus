using Desktop_Frames.Localization;
using IWshRuntimeLibrary;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;
using Color = System.Drawing.Color;
using Desktop_Frames.Plugins; // --- NEW: Plugins Support ---

namespace Desktop_Frames
{
    // FrameManager 分檔：Loading（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        private static void MigrateLegacyJson()
        {
            try
            {
                bool jsonModified = false;
                var validColors = new HashSet<string> { "Red", "Green", "Teal", "Blue", "Bismark", "White", "Beige", "Gray", "Black", "Purple", "Fuchsia", "Yellow", "Orange" };
                var validEffects = Enum.GetNames(typeof(LaunchEffectsManager.LaunchEffect)).ToHashSet();

                for (int i = 0; i < FrameDataManager.FrameData.Count; i++)
                {
                    dynamic frame = FrameDataManager.FrameData[i];
                    IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict
                        ? dict : ((JObject)frame).ToObject<IDictionary<string, object>>();

                    // --- 1. CORE VALIDATION (VITAL) ---
                    // Add GUID if missing
                    if (!frameDict.ContainsKey("Id"))
                    {
                        frameDict["Id"] = Guid.NewGuid().ToString();
                        jsonModified = true;
                    }

                    // --- 自動升級預設啟動提示便箋至本地化語言 ---
                    if (string.Equals(frameDict.ContainsKey("ItemsType") ? frameDict["ItemsType"]?.ToString() : null, "Note", StringComparison.OrdinalIgnoreCase))
                    {
                        string title = frameDict.ContainsKey("Title") ? frameDict["Title"]?.ToString() : null;
                        string noteContent = frameDict.ContainsKey("NoteContent") ? frameDict["NoteContent"]?.ToString() : null;
                        if ((title == Strings.Get("DefaultTipsTitle") || title == "Desktop Frames + 啟動提示" || title == "Desktop Frames + Startup Tips") &&
                            !string.IsNullOrEmpty(noteContent) &&
                            noteContent.StartsWith("WELCOME TO DESKTOP FRAMES", StringComparison.OrdinalIgnoreCase))
                        {
                            frameDict["Title"] = Strings.Get("DefaultTipsTitle");
                            frameDict["NoteContent"] = Strings.Get("DefaultTipsContent");
                            jsonModified = true;
                        }
                    }

                    // 舊 Portal 路徑離線不構成修改配置的理由。
                    if (frame.ItemsType?.ToString() != "Portal")
                    {
                        var items = frame.Items as JArray ?? new JArray();
                        bool itemsModified = false;
                        foreach (var item in items)
                        {
                            var itemDict = item as JObject;
                            if (itemDict != null)
                            {
                                if (itemDict["IsFolder"] == null)
                                {
                                    string path = itemDict["Filename"]?.ToString();
                                    itemDict["IsFolder"] = System.IO.Directory.Exists(path);
                                    itemsModified = true;
                                }
                                // Ensure IsLink exists
                                if (!itemDict.ContainsKey("IsLink"))
                                {
                                    itemDict["IsLink"] = false;
                                    itemsModified = true;
                                }
                                // Ensure IsNetwork exists
                                if (!itemDict.ContainsKey("IsNetwork"))
                                {
                                    string fname = itemDict["Filename"]?.ToString() ?? "";
                                    itemDict["IsNetwork"] = IsNetworkPath(fname);
                                    itemsModified = true;
                                }
                                // Ensure AlwaysRunAsAdmin exists
                                if (!itemDict.ContainsKey("AlwaysRunAsAdmin"))
                                {
                                    itemDict["AlwaysRunAsAdmin"] = false;
                                    itemsModified = true;
                                }
                            }
                        }
                        if (itemsModified)
                        {
                            frameDict["Items"] = items;
                            jsonModified = true;
                        }
                    }

                    // --- 3. PROPERTY INITIALIZATION (DEFAULTS) ---
                    // Visuals
                    if (!frameDict.ContainsKey("IsLocked")) { frameDict["IsLocked"] = "false"; jsonModified = true; }
                    if (!frameDict.ContainsKey("IsRolled")) { frameDict["IsRolled"] = "false"; jsonModified = true; }
                    if (!frameDict.ContainsKey("AutoRoll")) { frameDict["AutoRoll"] = "false"; jsonModified = true; } // --- NEW ---
                    if (!frameDict.ContainsKey("AlwaysOnTop")) { frameDict["AlwaysOnTop"] = "false"; jsonModified = true; } // --- NEW ---
                    if (!frameDict.ContainsKey("UnrolledHeight"))
                    {
                        double height = frameDict.ContainsKey("Height") ? Convert.ToDouble(frameDict["Height"]) : 130;
                        frameDict["UnrolledHeight"] = height;
                        jsonModified = true;
                    }

                    if (!frameDict.ContainsKey("IconSize")) { frameDict["IconSize"] = "Medium"; jsonModified = true; }
                    if (!frameDict.ContainsKey("IconSpacing")) { frameDict["IconSpacing"] = 5; jsonModified = true; }
                    if (!frameDict.ContainsKey("CustomColor")) { frameDict["CustomColor"] = null; jsonModified = true; }
                    if (!frameDict.ContainsKey("CustomLaunchEffect")) { frameDict["CustomLaunchEffect"] = null; jsonModified = true; }
                    if (!frameDict.ContainsKey("IsHidden")) { frameDict["IsHidden"] = "false"; jsonModified = true; }

                    // Text
                    if (!frameDict.ContainsKey("TextColor")) { frameDict["TextColor"] = null; jsonModified = true; }
                    if (!frameDict.ContainsKey("TitleTextColor")) { frameDict["TitleTextColor"] = null; jsonModified = true; }
                    if (!frameDict.ContainsKey("TitleTextSize")) { frameDict["TitleTextSize"] = "Medium"; jsonModified = true; }
                    if (!frameDict.ContainsKey("BoldTitleText")) { frameDict["BoldTitleText"] = "false"; jsonModified = true; }
                    if (!frameDict.ContainsKey("DisableTextShadow")) { frameDict["DisableTextShadow"] = "false"; jsonModified = true; }
                    if (!frameDict.ContainsKey("GrayscaleIcons")) { frameDict["GrayscaleIcons"] = "false"; jsonModified = true; }

                    // Border
                    if (!frameDict.ContainsKey("FrameBorderColor")) { frameDict["FrameBorderColor"] = null; jsonModified = true; }
                    if (!frameDict.ContainsKey("FrameBorderThickness")) { frameDict["FrameBorderThickness"] = 2; jsonModified = true; }

                    // --- 4. TABS FEATURE (Structure Only, No Merge) ---
                    if (!frameDict.ContainsKey("TabsEnabled"))
                    {
                        frameDict["TabsEnabled"] = "false";
                        jsonModified = true;
                    }
                    if (!frameDict.ContainsKey("CurrentTab"))
                    {
                        frameDict["CurrentTab"] = 0;
                        jsonModified = true;
                    }
                    if (!frameDict.ContainsKey("Tabs"))
                    {
                        frameDict["Tabs"] = new JArray();
                        jsonModified = true;
                    }

                    // --- 5. TAB VALIDATION (Structure Repair) ---
                    // Ensure existing tabs have valid structure (Name, Items Array)
                    // but DO NOT move items around or merge lists.
                    var tabs = frameDict["Tabs"] as JArray ?? new JArray();
                    if (tabs.Count > 0)
                    {
                        bool tabsModified = false;
                        for (int t = 0; t < tabs.Count; t++)
                        {
                            var tab = tabs[t] as JObject;
                            if (tab == null) continue;

                            if (tab["TabName"] == null)
                            {
                                tab["TabName"] = $"Tab {t + 1}";
                                tabsModified = true;
                            }
                            if (tab["Items"] == null || !(tab["Items"] is JArray))
                            {
                                tab["Items"] = new JArray();
                                tabsModified = true;
                            }
                        }
                        if (tabsModified)
                        {
                            frameDict["Tabs"] = tabs;
                            jsonModified = true;
                        }
                    }

                    // Update Master List
                    FrameDataManager.FrameData[i] = JObject.FromObject(frameDict);
                }

                if (jsonModified)
                {
                    FrameDataManager.SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Migrated fences.json with updated fields (Validations Only)");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Error migrating fences.json: {ex.Message}");
            }
        }

        public static void LoadAndCreateFrames(TargetChecker targetChecker)
        {
            // Get current program version from assembly
            string currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

            // --- EXECUTABLE MIGRATION ---
            RegistryHelper.PerformRenameMigration();

            RegistryHelper.SetProgramManagementValues(currentVersion);

            // CRITICAL FIX: Load settings EARLY so the single instance check knows the user's preference
            FrameDataManager.Initialize();
            SettingsManager.LoadSettings();


            // --- NEW: Register internal plugins before loading frames ---
            PluginManager.Initialize();

            // --- ONE-TIME ALTGR KEYBOARD CONFLICT WARNING FOR EXISTING USERS ---
            if (!SettingsManager.AltGrWarningShown && SettingsManager.EnableProfileHotkeys)
            {
                // Check if they are actually using the problematic Ctrl+Alt combination
                string mods = SettingsManager.ProfileSwitchModifier?.ToLower() ?? "";
                if (mods.Contains("control") && mods.Contains("alt"))
                {
                    bool disableHotkeys = MessageBoxesManager.ShowCustomYesNoMessageBox(
                                  Strings.MsgConfirmDisableCtrlAltHotkeys,
                                  Strings.DlgKeyboardCheck,
                                  NotificationSound.NadaAlert); // Override the user's sound preference for this critical alert

                    if (disableHotkeys)
                    {
                        SettingsManager.EnableProfileHotkeys = false;
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings, "User opted to disable Ctrl+Alt profile hotkeys due to AltGr conflict.");
                    }
                }

                // Mark as shown so we never bother them again, regardless of their choice
                SettingsManager.AltGrWarningShown = true;
                SettingsManager.SaveSettings();
                SettingsManager.BroadcastHotkeysToAllProfiles(); // Sync this decision across profiles
            }
            // -------------------------------------------------------------------

      
            // === PRE-FLIGHT CHECK FOR SINGLE INSTANCE SETTING ===
            // Directly read JSON to ensure we get the active profile's preference before UI fully boots
            bool disableSingleInstance = SettingsManager.DisableSingleInstance;
            try
            {
                string baseDir = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string optionsPath = System.IO.Path.Combine(baseDir, "options.json"); // Legacy fallback
                string masterOptionsPath = System.IO.Path.Combine(baseDir, "MasterOptions.json");

                if (System.IO.File.Exists(masterOptionsPath))
                {
                    optionsPath = masterOptionsPath;
                }
                else
                {
                    string activeProfilePath = System.IO.Path.Combine(baseDir, "ActiveProfile.txt");
                    if (System.IO.File.Exists(activeProfilePath))
                    {
                        string activeProfile = System.IO.File.ReadAllText(activeProfilePath).Trim();
                        string profileOptionsPath = System.IO.Path.Combine(baseDir, "Profiles", activeProfile, "options.json");
                        if (System.IO.File.Exists(profileOptionsPath))
                        {
                            optionsPath = profileOptionsPath;
                        }
                    }
                }

                if (System.IO.File.Exists(optionsPath))
                {
                    string jsonContent = System.IO.File.ReadAllText(optionsPath);
                    var jObj = JObject.Parse(jsonContent);
                    if (jObj["DisableSingleInstance"] != null)
                    {
                        disableSingleInstance = jObj["DisableSingleInstance"].Value<bool>();
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Pre-flight check failed: {ex.Message}");
            }

            // === SINGLE INSTANCE CHECK (WITH DISABLE OPTION) ===
            try
            {
                // Check if single instance enforcement is disabled
                if (disableSingleInstance)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "SingleInstance: Single instance enforcement disabled. Multiple instances allowed.");
                }
                else if (App.IsPrimaryInstance)
                {
                    // App.xaml.cs 啟動時已藉由系統 Mutex 原子性確認為首個執行個體，略過耗時的全行程掃描與寫死休眠
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "SingleInstance: Primary instance verified via Mutex. Continuing with normal startup.");
                }
                else
                {
                    Process currentProcess = Process.GetCurrentProcess();
                    string processName = Path.GetFileNameWithoutExtension(currentProcess.ProcessName);
                    Process[] allInstances = Process.GetProcessesByName(processName);

                    // Check if this is a duplicate instance
                    if (allInstances.Length > 1)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                            $"SingleInstance: Duplicate instance detected. Found {allInstances.Length} instances. Writing trigger and exiting.");

                        // Write registry trigger for the original instance
                        bool registryWritten = RegistryHelper.WriteTrigger();

                        if (registryWritten)
                        {
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                                "SingleInstance: Registry trigger written successfully. Exiting duplicate instance.");
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                                "SingleInstance: Failed to write registry trigger. Still exiting duplicate instance.");
                        }

                        Environment.Exit(0);
                        return;
                    }

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "SingleInstance: This is the first instance. Continuing with normal startup.");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"SingleInstance: Error in single instance check: {ex.Message}. Continuing startup.");
            }


            // REMOVED: Legacy path override. 
            // FrameDataManager.Initialize() now sets the correct Profile path internally.

            // Below added for reload function
            _currentTargetChecker = targetChecker;

            // Delete previous log file if setting is enabled
            if (SettingsManager.DeletePreviousLogOnStart)
            {
                try
                {
                    string logPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Desktop_Frames.log");
                    if (System.IO.File.Exists(logPath))
                    {
                        System.IO.File.Delete(logPath);
                        // Note: Can't log this deletion since we just deleted the log file
                    }
                }
                catch
                {
                    // Silently ignore deletion errors to avoid startup issues
                }
            }


            BackupManager.CleanLastDeletedFolder();
            InterCore.Initialize();

            _options = new
            {
                IsSnapEnabled = SettingsManager.IsSnapEnabled,
                ShowBackgroundImageOnPortalFences = SettingsManager.ShowBackgroundImageOnPortalFrames,
                Showintray = SettingsManager.ShowInTray,
                EnableSounds = SettingsManager.EnableSounds,
                TintValue = SettingsManager.TintValue,
                MenuTintValue = SettingsManager.MenuTintValue,
                MenuIcon = SettingsManager.MenuIcon,
                LockIcon = SettingsManager.LockIcon,
                SelectedColor = SettingsManager.SelectedColor,
                IsLogEnabled = SettingsManager.IsLogEnabled,
                singleClickToLaunch = SettingsManager.SingleClickToLaunch,
                LaunchEffect = SettingsManager.LaunchEffect,
                CheckNetworkPaths = false 
            };

            bool jsonLoadSuccessful = false;

            if (System.IO.File.Exists(FrameDataManager.JsonFilePath))
            {
                jsonLoadSuccessful = LoadFrameDataFromJson();
            }

            // 既有檔案讀取失敗不得走預設配置寫入路徑。
            if (FrameDataManager.IsWriteProtected)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    "面板設定讀取失敗，已保留原檔並略過建立面板視窗。");
                return;
            }

            // If JSON loading failed or file doesn't exist, initialize with defaults
            if (!jsonLoadSuccessful || FrameDataManager.FrameData == null || FrameDataManager.FrameData.Count == 0)
            {
                InitializeDefaultFrame();
            }
            else
            {
                // Only migrate if we successfully loaded existing data
                MigrateLegacyJson();
            }

            // 離線 Portal 只略過視窗，保留所有配置；提示狀態僅存在記憶體。
            var invalidFrames = new List<dynamic>();
            foreach (dynamic frame in FrameDataManager.FrameData.ToList()) // Use ToList to avoid collection modification issues
            {
                if (frame.ItemsType?.ToString() == "Portal")
                {
                    string targetPath = frame.Path?.ToString();
                    if (string.IsNullOrEmpty(targetPath) || !System.IO.Directory.Exists(targetPath))
                    {
                        invalidFrames.Add(frame);
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation, $"Portal 路徑無法存取，配置已保留：{frame.Title} / {targetPath ?? "null"}");
                    }
                }
            }

            // 每次啟動至多一次非阻塞提示，不因離線而寫入配置。
            if (invalidFrames.Any() && !_portalUnavailableNotified)
            {
                _portalUnavailableNotified = true;
                var missing = string.Join("\n", invalidFrames.Select(frame => $"{frame.Title} — {frame.Path}"));
                // 使用程式內非阻塞視窗，避免托盤通知未顯示；仍每次啟動至多一次。
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var notice = new PortalUnavailableWindow(Strings.Get("MsgPortalUnavailable", missing));
                    notice.Show();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "[PortalNotice] 已開啟非阻塞提示視窗。");
                }),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            // Clear any stuck transition states from previous session
            ClearAllTransitionStates();

            // Start emergency cleanup timer if not already running
            if (_transitionCleanupTimer == null)
            {
                _transitionCleanupTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(10) // Check every 10 seconds
                };
                _transitionCleanupTimer.Tick += (s, e) =>
                {
                    if (_framesInTransition.Count > 0)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameUpdate, $"Emergency cleanup: Found {_framesInTransition.Count} frames stuck in transition state");
                        ClearAllTransitionStates();
                    }
                };
                _transitionCleanupTimer.Start();
            }

            // --- RESTORE PERSISTENT ACCORDION STACK LINKS FROM JSON ---
            FrameDataManager.RebuildDockingMap();

            foreach (dynamic frame in FrameDataManager.FrameData.ToList())
            {
                CreateFrame(frame, targetChecker);
            }

            // --- NEW: Initialize Auto-Hide Engine ---
            InitializeAutoHideTimer();

            // --- NEW: Initialize Desktop Icon Manager ---
            DesktopIconManager.Initialize();

            // --- BUG FIX: Enforce Profile Switching State ---
            // Strictly apply the incoming profile's setting. If false, we explicitly restore visibility!
            DesktopIconManager.SetDesktopIconsVisible(!SettingsManager.HideDesktopElementsOnStart);

            // Initialize Auto-Backup Timer
            BackupManager.InitializeAutoBackup();

        }

        public static void StartDrawMode()
        {
            // Called by App.xaml.cs (Context Menu) or InterCore (IPC)
            var overlay = new DrawFrameOverlay();
            overlay.Show();
        }

        public static void CreateFrameFromDraw(Rect r)
        {
            try
            {
                string defaultName = CoreUtilities.GenerateUniqueFrameName();

                var dialog = new DrawFrameConfirmDialog(r, defaultName);
                dialog.ShowDialog();

                if (!dialog.Confirmed) return;

                string name = dialog.FrameTitle;
                string storagePath = dialog.StoragePath;
                if (!Directory.Exists(storagePath))
                {
                    Directory.CreateDirectory(storagePath);
                }

                var frame = FrameDataManager.CreateNewFrame(
                    name,
                    "Portal",
                    r.X,
                    r.Y,
                    width: r.Width,
                    height: r.Height,
                    portalPath: storagePath,
                    isManagedStorage: true,
                    storageRootPath: SettingsManager.DefaultPortalStorageRoot);

                var frameDict = frame as System.Collections.Generic.IDictionary<string, object>;
                if (frameDict != null)
                {
                    frameDict["Width"] = r.Width;
                    frameDict["Height"] = r.Height;
                    frameDict["Title"] = name;
                    frameDict["Path"] = storagePath;
                    frameDict["IsManagedStorage"] = "true";
                    frameDict["StorageRootPath"] = SettingsManager.DefaultPortalStorageRoot;
                    FrameDataManager.SaveFrameData();
                }

                // 建立專用收納柵欄
                CreateFrame(frame, _currentTargetChecker ?? new TargetChecker(1000));
                UpdateAllHeartContextMenus();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"CreateFrameFromDraw failed: {ex}");
                MessageBox.Show($"建立面板失敗：{ex.Message}", Strings.DlgError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void CreateManagedStorageFence(double x = 40, double y = 40)
        {
            try
            {
                string defaultName = CoreUtilities.GenerateUniqueFrameName();
                Rect r = new Rect(x, y, 360, 260);

                var dialog = new DrawFrameConfirmDialog(r, defaultName);
                dialog.ShowDialog();

                if (!dialog.Confirmed) return;

                string name = dialog.FrameTitle;
                string storagePath = dialog.StoragePath;
                if (!Directory.Exists(storagePath))
                {
                    Directory.CreateDirectory(storagePath);
                }

                var frame = FrameDataManager.CreateNewFrame(
                    name,
                    "Portal",
                    r.X,
                    r.Y,
                    width: r.Width,
                    height: r.Height,
                    portalPath: storagePath,
                    isManagedStorage: true,
                    storageRootPath: SettingsManager.DefaultPortalStorageRoot);

                var frameDict = frame as System.Collections.Generic.IDictionary<string, object>;
                if (frameDict != null)
                {
                    frameDict["Width"] = r.Width;
                    frameDict["Height"] = r.Height;
                    frameDict["Title"] = name;
                    frameDict["Path"] = storagePath;
                    frameDict["IsManagedStorage"] = "true";
                    frameDict["StorageRootPath"] = SettingsManager.DefaultPortalStorageRoot;
                    FrameDataManager.SaveFrameData();
                }

                CreateFrame(frame, _currentTargetChecker ?? new TargetChecker(1000));
                UpdateAllHeartContextMenus();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"CreateManagedStorageFence failed: {ex}");
                MessageBox.Show($"建立收納面板失敗：{ex.Message}", Strings.DlgError, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static bool AddItemToDataFrame(dynamic frame, string droppedFile)
        {
            if (frame == null || string.IsNullOrWhiteSpace(droppedFile)) return false;


            bool fileExists = System.IO.File.Exists(droppedFile);
            bool directoryExists = System.IO.Directory.Exists(droppedFile);
            if (!fileExists && !directoryExists) return false;

            bool isFolder = directoryExists;
            if (!System.IO.Directory.Exists("Shortcuts")) System.IO.Directory.CreateDirectory("Shortcuts");
            string baseShortcutName = System.IO.Path.Combine("Shortcuts", System.IO.Path.GetFileName(droppedFile));
            string shortcutName = baseShortcutName;
            int counter = 1;

            bool isDroppedShortcut = System.IO.Path.GetExtension(droppedFile).ToLower() == ".lnk";
            bool isDroppedUrlFile = System.IO.Path.GetExtension(droppedFile).ToLower() == ".url";
            bool isWebLink = isDroppedUrlFile || CoreUtilities.IsWebLinkShortcut(droppedFile);

            if (!isDroppedShortcut && !isDroppedUrlFile)
            {
                // CASE A: Creating new shortcut from raw file/folder
                shortcutName = baseShortcutName + ".lnk";
                while (System.IO.File.Exists(shortcutName))
                {
                    shortcutName = System.IO.Path.Combine("Shortcuts", $"{System.IO.Path.GetFileNameWithoutExtension(droppedFile)} ({counter++}).lnk");
                }

                try
                {
                    WshShell shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutName);
                    shortcut.TargetPath = droppedFile;
                    if (isFolder) shortcut.WorkingDirectory = droppedFile;
                    shortcut.Save();
                }
                catch { return false; }
            }
            else
            {
                // CASE B: Copying existing shortcut (LNK or URL)
                string ext = isWebLink ? ".url" : ".lnk";
                if (!baseShortcutName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    baseShortcutName = System.IO.Path.ChangeExtension(baseShortcutName, ext);

                shortcutName = baseShortcutName;
                while (System.IO.File.Exists(shortcutName))
                {
                    string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                    shortcutName = System.IO.Path.Combine("Shortcuts", $"{nameNoExt} ({counter++}){ext}");
                }

                System.IO.File.Copy(droppedFile, shortcutName, true);
            }

            dynamic newItem = new System.Dynamic.ExpandoObject();
            IDictionary<string, object> newItemDict = newItem;

            newItemDict["Filename"] = shortcutName;
            newItemDict["IsFolder"] = isFolder;
            newItemDict["IsLink"] = isWebLink;
            newItemDict["IsNetwork"] = IsNetworkPath(shortcutName);

            string displayFileName = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
            if (string.IsNullOrWhiteSpace(displayFileName))
            {
                displayFileName = droppedFile.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                             .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                             .LastOrDefault();
                if (string.IsNullOrWhiteSpace(displayFileName)) displayFileName = droppedFile;
            }

            newItemDict["DisplayName"] = displayFileName;
            newItemDict["AlwaysRunAsAdmin"] = false;

            // Add to Items list
            var frameDict = frame as IDictionary<string, object>;
            JArray items = null;
            if (frame.Items is JArray jArray)
            {
                items = jArray;
            }
            else if (frameDict != null && frameDict.ContainsKey("Items") && frameDict["Items"] is JArray dictArray)
            {
                items = dictArray;
            }
            else
            {
                items = new JArray();
                if (frameDict != null) frameDict["Items"] = items;
            }

            items.Add(JObject.FromObject(newItem));
            return true;
        }

        private static bool LoadFrameDataFromJson()
        {
            bool loaded = false;
            try
            {
                string jsonContent = System.IO.File.ReadAllText(FrameDataManager.JsonFilePath);
                FrameDataManager.ValidateFrameJson(jsonContent);

                // Check if the file is empty or contains only whitespace
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        "JSON file is empty or contains only whitespace. Using default frame configuration.");
                    return false;
                }

                // First, try to parse as a list of frames
                try
                {
                    FrameDataManager.FrameData = JsonConvert.DeserializeObject<List<dynamic>>(jsonContent);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        $"Successfully loaded {FrameDataManager.FrameData?.Count ?? 0} frame from JSON array.");
                    return loaded = FrameDataManager.FrameData != null;
                }
                catch (JsonSerializationException)
                {
                    // If that fails, try to parse as a single frame object
                    try
                    {
                        var singleFrame = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                        if (singleFrame != null)
                        {
                            FrameDataManager.FrameData = new List<dynamic> { singleFrame };
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                                "Successfully loaded single frame from JSON object.");
                            return loaded = true;
                        }
                    }
                    catch (JsonSerializationException innerEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Failed to parse JSON as single frame object: {innerEx.Message}");
                    }
                }
            }
            catch (System.IO.IOException ioEx)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"IO error reading frames.json: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException accessEx)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Access denied reading frames.json: {accessEx.Message}");
            }
            catch (JsonReaderException jsonEx)
            {
                // Handle malformed JSON (syntax errors, invalid characters, etc.)
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Malformed JSON detected in frames.json: {jsonEx.Message}");

                // Optionally, create a backup of the corrupted file
                CreateCorruptedFileBackup();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Unexpected error loading frames.json: {ex.Message}");
            }
            finally
            {
                FrameDataManager.SetLoadProtection(!loaded);
                if (!loaded) FrameDataManager.FrameData = new List<dynamic>();
            }

            return false;
        }

        private static void CreateCorruptedFileBackup()
        {
            try
            {
                string backupPath = FrameDataManager.JsonFilePath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                System.IO.File.Copy(FrameDataManager.JsonFilePath, backupPath, true);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Created backup of corrupted JSON file: {backupPath}");
            }
            catch (Exception backupEx)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Failed to create backup of corrupted JSON file: {backupEx.Message}");
            }
        }

        private static void InitializeDefaultFrame()
        {
            try
            {
                FrameDataManager.EnsureCanSave();
                // 1. Create the Standard Data Frames (For user icons)
                var dataFrame = new
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = Strings.Get("DefaultFrameTitle"),
                    X = 20.0,
                    Y = 20.0,
                    Width = 360.0,
                    Height = 180.0,
                    ItemsType = "Data",
                    Items = new JArray(),

                    // Defaults
                    IsLocked = "false",
                    IsHidden = "false",
                    IsRolled = "false",
                    AutoRoll = "false", // --- NEW: Auto Roll ---
                    AlwaysOnTop = "false", // --- NEW ---
                    UnrolledHeight = 130.0,
                    TabsEnabled = "false",
                    CurrentTab = 0,
                    Tabs = new JArray(),

                    // Visual Defaults
                    CustomColor = (string)null,
                    FrameBorderThickness = 2
                };


                // 2. Create the "Startup Tips" Note Frame
                var noteFrame = new
                {
                    Id = Guid.NewGuid().ToString(), // Unique ID
                    Title = Strings.Get("DefaultTipsTitle"), // Explicit Name
                    X = 20.0,   // Positioned below the data frame
                    Y = 200.0,  // Data frame ends then this frame begins
                    Width = 555.0,
                    Height = 318.0,
                    ItemsType = "Note",
                    Items = new JArray(),

                    // Visuals from your spec
                    CustomColor = (string)null, // Default
                    TextColor = "Teal",         // Teal text

                    // Note Settings
                    NoteContent = Strings.Get("DefaultTipsContent"),

                    NoteFontSize = "Medium",
                    NoteFontFamily = "Segoe UI",
                    WordWrap = "true",
                    SpellCheck = "false",

                    // Standard Properties
                    IsHidden = "false",
                    IsLocked = "false",
                    IsRolled = "false",
                    AutoRoll = "false", // --- NEW: Auto Roll ---
                    AlwaysOnTop = "false", // --- NEW ---
                    UnrolledHeight = 318.0,
                    BoldTitleText = "false",
                    DisableTextShadow = "false",
                    IconSize = "Medium",
                    IconSpacing = 5,
                    FrameBorderThickness = 2
                };

                // 3. Combine and Save
                var frames = new List<object> { dataFrame, noteFrame };
                string defaultJson = JsonConvert.SerializeObject(frames, Formatting.Indented);

                System.IO.File.WriteAllText(FrameDataManager.JsonFilePath, defaultJson);

                // Load into memory
                FrameDataManager.FrameData = JsonConvert.DeserializeObject<List<dynamic>>(defaultJson);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "Initialized default configuration with Data Frame and Startup Tips");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error initializing default frames: {ex.Message}");

                // Fallback to empty list if something explodes
                FrameDataManager.FrameData = new List<dynamic>();
            }
        }

        public static void CreateNewFrame(string title, string itemsType, double x = 20, double y = 20, string customColor = null, string customLaunchEffect = null, string pluginId = null)
        {
            string selectedPath = null;
            if (itemsType == "Portal")
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Select the folder to monitor for this Portal Frame";
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        selectedPath = dialog.SelectedPath;
                        string folderName = System.IO.Path.GetFileName(selectedPath);
                        title = !string.IsNullOrEmpty(folderName) ? folderName : title;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            var newFrame = FrameDataManager.CreateNewFrame(title, itemsType, x, y, customColor, customLaunchEffect, portalPath: selectedPath);
            if (newFrame != null)
            {
                IDictionary<string, object> dict = newFrame;
                bool needsSave = false;
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    dict["Path"] = selectedPath;
                    needsSave = true;
                }
                if (itemsType == "Plugin" && !string.IsNullOrEmpty(pluginId))
                {
                    dict["PluginId"] = pluginId;
                    dict["PluginSettings"] = new JObject();
                    needsSave = true;
                }
                if (needsSave)
                {
                    FrameDataManager.SaveFrameData();
                }
                CreateFrame(newFrame, new TargetChecker(1000));
            }
        }
    }
}
