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
    // FrameManager 分檔：Menus（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        public static Style GetThemedContextMenuStyle(dynamic frame)
        {
            return GameTheme.ContextMenuStyle;
        }

        // Builds the heart ContextMenu for a frame with consistent items and dynamic state
        // v2.5.4 .183: Swapped Tabs/Delete position for better UX safety
        private static ContextMenu BuildHeartContextMenu(dynamic frame, bool showTabsOption = false)
        {
            // DEBUG: Log menu building
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                $"Building heart context menu for frame '{frame.Title}'");

            var menu = new ContextMenu();

            Style themedStyle = GetThemedContextMenuStyle(frame);
            if (themedStyle != null) menu.Style = themedStyle;

            // --- AUTO-CLOSE TIMER ---
            System.Windows.Threading.DispatcherTimer menuTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };

            menuTimer.Tick += (s, e) =>
            {
                if (menu.IsOpen && !menu.IsMouseOver)
                {
                    menu.IsOpen = false;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Heart Menu auto-closed by timer");
                }
                menuTimer.Stop();
            };

            menu.Opened += (s, e) => { menuTimer.Start(); };
            menu.Closed += (s, e) => menuTimer.Stop();
            menu.MouseEnter += (s, e) => menuTimer.Stop();
            menu.MouseLeave += (s, e) => { menuTimer.Stop(); menuTimer.Start(); };
            // ------------------------

            // Desktop Layouts Submenu
            string currentProfile = ProfileManager.CurrentProfileName;
            var layoutsSubMenu = new MenuItem
            {
                Header = $"{Strings.MenuDesktopLayouts} ({currentProfile})"
            };

            var profiles = ProfileManager.GetProfiles();
            foreach (var profile in profiles)
            {
                var profileItem = new MenuItem
                {
                    Header = $"{profile.Name} [{profile.Id}]",
                    IsCheckable = true,
                    IsChecked = string.Equals(profile.Name, currentProfile, StringComparison.OrdinalIgnoreCase),
                    IsEnabled = !string.Equals(profile.Name, currentProfile, StringComparison.OrdinalIgnoreCase)
                };

                string targetProfileName = profile.Name;
                profileItem.Click += (s, e) =>
                {
                    ProfileManager.SetManualBaseProfile(targetProfileName);
                    ProfileManager.SwitchToProfile(targetProfileName);
                    TrayManager.Instance?.UpdateProfilesMenu();
                    TrayManager.Instance?.UpdateTrayIcon();
                };

                layoutsSubMenu.Items.Add(profileItem);
            }

            layoutsSubMenu.Items.Add(new Separator());

            var manageLayoutsItem = new MenuItem { Header = Strings.TrayManageProfiles };
            manageLayoutsItem.Click += (s, e) =>
            {
                var form = new ProfileManagerForm();
                form.ShowDialog();
                TrayManager.Instance?.UpdateProfilesMenu();
                TrayManager.Instance?.UpdateTrayIcon();
                UpdateAllHeartContextMenus();
            };
            layoutsSubMenu.Items.Add(manageLayoutsItem);

            // ♥ 選單：最常用的在第一層，少用功能收進「更多」，拆除面板放在「離開」上方並標紅。
            // 1. 新增
            var newFrameItem = new MenuItem { Header = Strings.MenuNewFrame };
            newFrameItem.Click += (s, e) =>
            {
                var mousePosition = System.Windows.Forms.Cursor.Position;
                CreateManagedStorageFence(mousePosition.X, mousePosition.Y);
            };
            menu.Items.Add(newFrameItem);

            var drawFrameItem = new MenuItem { Header = Strings.MenuDrawFrame };
            drawFrameItem.Click += (s, e) => StartDrawMode();
            menu.Items.Add(drawFrameItem);

            var newNoteItem = new MenuItem { Header = Strings.Get("MenuNewNote", "新增便箋 (Ctrl+Alt+N)") };
            newNoteItem.Click += (s, e) =>
            {
                Desktop_Frames.Notes.Services.NoteManager.Instance?.CreateNewNote();
            };
            menu.Items.Add(newNoteItem);

            menu.Items.Add(new Separator());

            // 2. 設定與桌面版面
            var optionsItem = new MenuItem { Header = Strings.MenuOptions };
            optionsItem.Click += (s, e) => OptionsFormManager.ShowOptionsForm();
            menu.Items.Add(optionsItem);

            menu.Items.Add(layoutsSubMenu);

            // 3. 更多（少用功能）
            var moreMenu = new MenuItem { Header = Strings.Get("MenuMore", "更多") };

            bool isDataFrame = frame.ItemsType?.ToString() == "Data";
            if (isDataFrame)
            {
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                var enableTabsItem = new MenuItem
                {
                    Header = Strings.MenuEnableTabs,
                    IsCheckable = true,   // Shows checkbox gutter
                    IsChecked = tabsEnabled // Visual checkmark
                };

                enableTabsItem.Click += (s, e) => ToggleFrameTabs(frame);
                moreMenu.Items.Add(enableTabsItem);
                moreMenu.Items.Add(new Separator());
            }

            var exportItem = new MenuItem { Header = Strings.MenuExportThisFrame };
            exportItem.Click += (s, e) => BackupManager.ExportFrame(frame);
            moreMenu.Items.Add(exportItem);

            var importItem = new MenuItem { Header = Strings.MenuImportFrame };
            importItem.Click += (s, e) => BackupManager.ImportFrame();
            moreMenu.Items.Add(importItem);

            // 只有最近刪除過面板時才有「還原」可用
            var restoreItem = new MenuItem
            {
                Header = Strings.MenuRestoreLastDeleted,
                Visibility = BackupManager.IsRestoreAvailable ? Visibility.Visible : Visibility.Collapsed
            };
            restoreItem.Click += (s, e) => BackupManager.RestoreLastDeletedFrame();
            moreMenu.Items.Add(restoreItem);

            moreMenu.Items.Add(new Separator());

            var aboutItem = new MenuItem { Header = Strings.MenuAbout };
            aboutItem.Click += (s, e) => AboutFormManager.ShowAboutForm();
            moreMenu.Items.Add(aboutItem);

            menu.Items.Add(moreMenu);

            menu.Items.Add(new Separator());

            // 4. 拆除／刪除此面板（紅字，與「離開」相鄰但中間有分隔線）
            bool isManagedStorageFrame = Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame);
            var deleteThisFrame = new MenuItem
            {
                Header = isManagedStorageFrame ? "拆除此面板並將內容移回桌面…" : Strings.MenuDeleteThisFrame,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C))
            };
            deleteThisFrame.Click += (s, e) => DeleteFrameWithConfirmation(frame);
            menu.Items.Add(deleteThisFrame);

            menu.Items.Add(new Separator());

            // 5. 離開
            var exitItem = new MenuItem { Header = Strings.MenuExit };
            exitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            return menu;
        }

        /// <summary>
        /// Centralized method to attach the standard Context Menu to an icon.
        /// Layout: Edit/Move/Remove -> Copy -> Admin -> Path
        /// Includes LIVE data lookup to ensure Checkable items stay synced.
        /// </summary>
        public static void AttachIconContextMenu(StackPanel sp, dynamic item, dynamic frame, NonActivatingWindow window)
        {
            try
            {
                // Initial snapshot for static properties (Path, Folder status)
                IDictionary<string, object> iconDict = item is IDictionary<string, object> dict ?
                    dict : ((JObject)item).ToObject<IDictionary<string, object>>();

                string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
                bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];

                // HELPER: Function to find the LIVE item in memory (Handles Tabs vs Main)
                // We need this because 'item' becomes stale after an update.
                Func<dynamic> GetLiveItem = () =>
                {
                    try
                    {
                        string frameId = frame.Id?.ToString();
                        var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                        if (liveFrame == null) return null;

                        JArray itemsArray = liveFrame.Items as JArray ?? new JArray();

                        // Handle Tabs
                        bool tabsEnabled = liveFrame.TabsEnabled?.ToString().ToLower() == "true";
                        if (tabsEnabled)
                        {
                            var tabs = liveFrame.Tabs as JArray ?? new JArray();
                            int currentTabIndex = Convert.ToInt32(liveFrame.CurrentTab?.ToString() ?? "0");
                            if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                            {
                                var activeTab = tabs[currentTabIndex] as JObject;
                                itemsArray = activeTab?["Items"] as JArray ?? itemsArray;
                            }
                        }

                        // Find specific item by filename
                        return itemsArray.FirstOrDefault(i => string.Equals(
                            System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                            System.IO.Path.GetFullPath(filePath),
                            StringComparison.OrdinalIgnoreCase));
                    }
                    catch { return null; }
                };

                ContextMenu iconContextMenu = new ContextMenu();

                Style themedStyle = GetThemedContextMenuStyle(frame);
                if (themedStyle != null) iconContextMenu.Style = themedStyle;

                // --- GROUP 1: MANIPULATION ---
                MenuItem miEdit = new MenuItem { Header = Strings.MenuEdit };
                MenuItem miMove = new MenuItem { Header = Strings.MenuMove };
                MenuItem miRemove = new MenuItem { Header = Strings.MenuRemove };

                iconContextMenu.Items.Add(miEdit);
                iconContextMenu.Items.Add(miMove);
                iconContextMenu.Items.Add(miRemove);

                bool isSpacer = filePath != null && filePath.StartsWith("INTERNAL_BLANK_");

                // --- GROUP 2: CLIPBOARD ---
                Separator sepClipboard = new Separator();
                MenuItem miCopyItem = new MenuItem { Header = Strings.MenuCopyItem };

                iconContextMenu.Items.Add(sepClipboard);
                iconContextMenu.Items.Add(miCopyItem);

                if (isSpacer)
                {
                    sepClipboard.Visibility = Visibility.Collapsed;
                    miCopyItem.Visibility = Visibility.Collapsed;
                }

                // --- GROUP 3: EXECUTION (Conditional) ---
                bool isEligibleForAdmin = !isFolder && !string.IsNullOrEmpty(filePath) &&
                    (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk" || System.IO.Path.GetExtension(filePath).ToLower() == ".exe");

                MenuItem miAlwaysAdmin = null;
                MenuItem miRunAsDifferentUser = null;
                MenuItem miAlwaysRunAsDifferentUser = null;

                if (isEligibleForAdmin)
                {
                    iconContextMenu.Items.Add(new Separator());

                    MenuItem miRunAsAdmin = new MenuItem { Header = Strings.MenuRunAsAdmin };
                    miAlwaysAdmin = new MenuItem
                    {
                        Header = Strings.MenuAlwaysRunAsAdmin,
                        IsCheckable = true
                    };

                    iconContextMenu.Items.Add(miRunAsAdmin);
                    iconContextMenu.Items.Add(miAlwaysAdmin);

                 
                    // Run as Admin Logic
                    miRunAsAdmin.Click += (s, e) => {
                        string target = Utility.GetShortcutTarget(filePath);
                        string args = Utility.GetShortcutArguments(filePath); // Extract arguments

                        ProcessStartInfo psi = new ProcessStartInfo { FileName = target, UseShellExecute = true, Verb = "runas" };

                        if (!string.IsNullOrEmpty(args))
                        {
                            psi.Arguments = args; // Pass arguments to admin launch
                        }

                        if (System.IO.File.Exists(target))
                        {
                            string wd = System.IO.Path.GetDirectoryName(target);
                            if (!string.IsNullOrEmpty(wd)) psi.WorkingDirectory = wd;
                        }
                        try { Process.Start(psi); }
                        catch (Exception ex)
                        {
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgRunAsAdminFailed", ex.Message), Strings.DlgError);
                        }
                    };

                    // START- HERE

                    // Toggle Always Admin Logic
                    miAlwaysAdmin.Click += (sender, e) => {
                        var liveItem = GetLiveItem();
                        if (liveItem != null)
                        {
                            bool newVal = !Convert.ToBoolean(liveItem["AlwaysRunAsAdmin"] ?? false);
                            liveItem["AlwaysRunAsAdmin"] = newVal;
                            miAlwaysAdmin.IsChecked = newVal;

                            // Mutually exclusive: Uncheck Different User if Admin is checked
                            if (newVal)
                            {
                                liveItem["AlwaysRunAsDifferentUser"] = false;
                                if (miAlwaysRunAsDifferentUser != null) miAlwaysRunAsDifferentUser.IsChecked = false;
                            }

                            FrameDataManager.SaveFrameData();
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Toggled AlwaysRunAsAdmin to {newVal} for {filePath}");
                        }
                    };
                }

                // --- GROUP 4: PATH / TARGET ---
                Separator sepPath = new Separator();
                iconContextMenu.Items.Add(sepPath);

                MenuItem miCopyPathRoot = new MenuItem { Header = Strings.MenuCopyPath };
                MenuItem miCopyFolder = new MenuItem { Header = Strings.MenuFolderPath };
                MenuItem miCopyFullPath = new MenuItem { Header = Strings.MenuFullPath };

                miCopyPathRoot.Items.Add(miCopyFolder);
                miCopyPathRoot.Items.Add(miCopyFullPath);

                MenuItem miFindTarget = new MenuItem { Header = Strings.MenuOpenTargetFolder };

                iconContextMenu.Items.Add(miCopyPathRoot);
                iconContextMenu.Items.Add(miFindTarget);

                if (isSpacer)
                {
                    sepPath.Visibility = Visibility.Collapsed;
                    miCopyPathRoot.Visibility = Visibility.Collapsed;
                    miFindTarget.Visibility = Visibility.Collapsed;
                }

                // --- EVENT HANDLERS ---
                miEdit.Click += (s, e) => EditItem(item, frame, window);
                miMove.Click += (s, e) => ItemMoveDialog.ShowMoveDialog(item, frame, window.Dispatcher);

                miRemove.Click += (s, e) =>
                {
                    try
                    {
                        // Use the LIVE lookup to find the item to remove
                        var liveItem = GetLiveItem(); // Returns the JObject from the array

                        // We need the Array itself to remove the item
                        string frameId = frame.Id?.ToString();
                        var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

                        if (liveFrame != null && liveItem != null)
                        {
                            // Logic to locate the array containing liveItem
                            JArray targetArray = liveFrame.Items as JArray; // Default to main

                            bool tabsEnabled = liveFrame.TabsEnabled?.ToString().ToLower() == "true";
                            if (tabsEnabled)
                            {
                                var tabs = liveFrame.Tabs as JArray;
                                int tabIdx = Convert.ToInt32(liveFrame.CurrentTab?.ToString() ?? "0");
                                if (tabs != null && tabIdx < tabs.Count)
                                {
                                    targetArray = tabs[tabIdx]["Items"] as JArray;
                                }
                            }

                            if (targetArray != null)
                            {
                                RemoveDataItem(targetArray, liveItem);
                                var wp = VisualTreeHelper.GetParent(sp) as WrapPanel;
                                if (wp != null) wp.Children.Remove(sp);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error removing: {ex.Message}");
                    }
                };

                miCopyItem.Click += (s, e) => CopyPasteManager.CopyItem(item, frame);

                miFindTarget.Click += (s, e) => {
                    string target = Utility.GetShortcutTarget(filePath);
                    if (!string.IsNullOrEmpty(target) && (System.IO.File.Exists(target) || System.IO.Directory.Exists(target)))
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
                };

                miCopyFolder.Click += (s, e) => {
                    string target = Utility.GetShortcutTarget(filePath);
                    if (!string.IsNullOrEmpty(target)) Clipboard.SetText(System.IO.Path.GetDirectoryName(target));
                };
                miCopyFullPath.Click += (s, e) => {
                    string target = Utility.GetShortcutTarget(filePath);
                    if (!string.IsNullOrEmpty(target)) Clipboard.SetText(target);
                };

                // --- DYNAMIC UPDATES (On Open) ---
                MenuItem miSendToDesktop = null;


                iconContextMenu.Opened += (s, e) => {
                    // --- LIVE THEME FIX: Refresh style right before opening ---
                    try
                    {
                        Style liveStyle = GetThemedContextMenuStyle(frame);
                        if (liveStyle != null) iconContextMenu.Style = liveStyle;
                    }
                    catch { }

                    // Fetch live item ONCE per open to prevent variable shadowing errors
                    var currentLiveItem = GetLiveItem();
                    bool isAlwaysDiffUser = currentLiveItem != null && Convert.ToBoolean(currentLiveItem["AlwaysRunAsDifferentUser"] ?? false);

                    bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                    // Clear dynamic items
                    if (miSendToDesktop != null && iconContextMenu.Items.Contains(miSendToDesktop))
                        iconContextMenu.Items.Remove(miSendToDesktop);
                    if (miRunAsDifferentUser != null && iconContextMenu.Items.Contains(miRunAsDifferentUser))
                        iconContextMenu.Items.Remove(miRunAsDifferentUser);
                    if (miAlwaysRunAsDifferentUser != null && iconContextMenu.Items.Contains(miAlwaysRunAsDifferentUser))
                        iconContextMenu.Items.Remove(miAlwaysRunAsDifferentUser);

                    // 1. Send to Desktop (Ctrl only, Skip for spacers)
                    if (isCtrl && !isSpacer)
                    {
                        miSendToDesktop = new MenuItem { Header = Strings.MenuSendToDesktop };
                        miSendToDesktop.Click += (sender, args) => CopyPasteManager.SendToDesktop(item);
                        int idxCopy = iconContextMenu.Items.IndexOf(miCopyItem);
                        if (idxCopy != -1) iconContextMenu.Items.Insert(idxCopy + 1, miSendToDesktop);
                    }

                    // 2. Different User Options (Ctrl OR if setting is already checked)
                    if (isEligibleForAdmin && (isCtrl || isAlwaysDiffUser))
                    {
                        miRunAsDifferentUser = new MenuItem { Header = Strings.MenuRunAsDifferentUser };
                        miAlwaysRunAsDifferentUser = new MenuItem { Header = Strings.MenuAlwaysRunAsDifferentUser, IsCheckable = true };

                        miRunAsDifferentUser.Click += (sender, args) => {
                            string target = Utility.GetShortcutTarget(filePath);
                            string argsStr = Utility.GetShortcutArguments(filePath);
                            ProcessStartInfo psi = new ProcessStartInfo { FileName = target, UseShellExecute = true, Verb = "runasuser" };
                            if (!string.IsNullOrEmpty(argsStr)) psi.Arguments = argsStr;
                            if (System.IO.File.Exists(target))
                            {
                                string wd = System.IO.Path.GetDirectoryName(target);
                                if (!string.IsNullOrEmpty(wd)) psi.WorkingDirectory = wd;
                            }
                            try { Process.Start(psi); }
                            catch (Exception ex) { if (!ex.Message.Contains("canceled")) LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error running as different user: {ex.Message}"); }
                        };

                        miAlwaysRunAsDifferentUser.Click += (sender, args) => {
                            var liveItem = GetLiveItem();
                            if (liveItem != null)
                            {
                                bool newVal = !Convert.ToBoolean(liveItem["AlwaysRunAsDifferentUser"] ?? false);
                                liveItem["AlwaysRunAsDifferentUser"] = newVal;
                                miAlwaysRunAsDifferentUser.IsChecked = newVal;

                                // Mutually exclusive: Uncheck Admin if Different User is checked
                                if (newVal && miAlwaysAdmin != null)
                                {
                                    liveItem["AlwaysRunAsAdmin"] = false;
                                    miAlwaysAdmin.IsChecked = false;
                                }
                                FrameDataManager.SaveFrameData();
                            }
                        };

                        int idxAdmin = iconContextMenu.Items.IndexOf(miAlwaysAdmin);
                        if (idxAdmin != -1)
                        {
                            iconContextMenu.Items.Insert(idxAdmin + 1, miRunAsDifferentUser);
                            iconContextMenu.Items.Insert(idxAdmin + 2, miAlwaysRunAsDifferentUser);
                        }
                    }

                    // 3. REFRESH CHECK STATES
                    if (currentLiveItem != null)
                    {
                        if (miAlwaysAdmin != null)
                            miAlwaysAdmin.IsChecked = Convert.ToBoolean(currentLiveItem["AlwaysRunAsAdmin"] ?? false);

                        if (miAlwaysRunAsDifferentUser != null && iconContextMenu.Items.Contains(miAlwaysRunAsDifferentUser))
                            miAlwaysRunAsDifferentUser.IsChecked = Convert.ToBoolean(currentLiveItem["AlwaysRunAsDifferentUser"] ?? false);
                    }
                };


                sp.ContextMenu = iconContextMenu;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error attaching context menu: {ex.Message}");
            }
        }

        // Updates all heart ContextMenus across all frames using stored TextBlock references
        public static void UpdateAllHeartContextMenus()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var entry in _heartTextBlocks)
                {
                    var frame = entry.Key;
                    var heart = entry.Value;
                    if (heart != null)
                    {
                        // heart.ContextMenu = BuildHeartContextMenu(frame);
                        heart.ContextMenu = BuildHeartContextMenu(frame, false); // Normal menu by default
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Updated heart ContextMenu for frame '{frame.Title}'");
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Skipped update for frame '{frame.Title}': heart TextBlock is null");
                    }
                }
            });
        }

        /// <summary>
        /// Creates basic context menu for URL shortcuts
        /// </summary>
        private static void CreateBasicContextMenu(StackPanel sp, dynamic item, string filePath)
        {
            try
            {
                ContextMenu iconContextMenu = new ContextMenu();

                // Fetch frame dynamically to apply style
                try
                {
                    NonActivatingWindow parentWin = FindVisualParent<NonActivatingWindow>(sp);
                    if (parentWin != null)
                    {
                        string frameId = parentWin.Tag?.ToString();
                        var frame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId);
                        if (frame != null)
                        {
                            Style themedStyle = GetThemedContextMenuStyle(frame);
                            if (themedStyle != null) iconContextMenu.Style = themedStyle;
                        }
                    }
                }
                catch { }

                MenuItem miRemove = new MenuItem { Header = Strings.MenuRemove };


                miRemove.Click += (sender, e) =>
                {
                    try
                    {
                        NonActivatingWindow parentWin = FindVisualParent<NonActivatingWindow>(sp);
                        if (parentWin != null)
                        {
                            string frameId = parentWin.Tag?.ToString();
                            var frame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId);
                            if (frame != null)
                            {
                                var items = frame.Items as JArray;
                                var itemToRemove = items?.FirstOrDefault(i => i["Filename"]?.ToString() == filePath);
                                if (itemToRemove != null)
                                {
                                    RemoveDataItem(items, itemToRemove);

                                    WrapPanel wrapPanel = FindVisualParent<WrapPanel>(sp);
                                    wrapPanel?.Children.Remove(sp);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Error removing URL shortcut: {ex.Message}");
                    }
                };


                iconContextMenu.Items.Add(miRemove);

                bool alwaysAdmin = Convert.ToBoolean(item["AlwaysRunAsAdmin"] ?? false);
                MenuItem miAlwaysAdmin = new MenuItem
                {
                    Header = Strings.MenuAlwaysRunAsAdmin,
                    IsCheckable = true,
                    IsChecked = alwaysAdmin
                };
                miAlwaysAdmin.Click += (sender, e) => {
                    try
                    {
                        NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(sp);
                        if (parentWindow != null)
                        {
                            string frameId = parentWindow.Tag?.ToString();
                            if (!string.IsNullOrEmpty(frameId))
                            {
                                var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                                if (currentFrame != null && currentFrame.ItemsType?.ToString() == "Data")
                                {
                                    var items = currentFrame.Items as JArray ?? new JArray();
                                    bool tabsEnabled = currentFrame.TabsEnabled?.ToString().ToLower() == "true";
                                    if (tabsEnabled)
                                    {
                                        var tabs = currentFrame.Tabs as JArray ?? new JArray();
                                        int currentTabIndex = Convert.ToInt32(currentFrame.CurrentTab?.ToString() ?? "0");
                                        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                                        {
                                            var currentTab = tabs[currentTabIndex] as JObject;
                                            items = currentTab?["Items"] as JArray ?? items;
                                        }
                                    }
                                    var matchingItem = items.FirstOrDefault(i => string.Equals(
                                        System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                                        System.IO.Path.GetFullPath(filePath),
                                        StringComparison.OrdinalIgnoreCase));
                                    if (matchingItem != null)
                                    {
                                        bool currentValue = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                                        bool newValue = !currentValue;
                                        matchingItem["AlwaysRunAsAdmin"] = newValue;
                                        miAlwaysAdmin.IsChecked = newValue;
                                        FrameDataManager.SaveFrameData();
                                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Toggled AlwaysRunAsAdmin to {newValue} for item: {filePath}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error toggling AlwaysRunAsAdmin for {filePath}: {ex.Message}");
                    }
                };
                // Refresh IsChecked on menu open
                iconContextMenu.Opened += (s, ev) => {
                    try
                    {
                        NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(sp);

                        // --- LIVE THEME FIX ---
                        if (parentWindow != null)
                        {
                            string frameId = parentWindow.Tag?.ToString();
                            var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId);
                            if (liveFrame != null)
                            {
                                Style liveStyle = GetThemedContextMenuStyle(liveFrame);
                                if (liveStyle != null) iconContextMenu.Style = liveStyle;
                            }
                        }
                        if (parentWindow != null)
                        {
                            string frameId = parentWindow.Tag?.ToString();
                            if (!string.IsNullOrEmpty(frameId))
                            {
                                var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                                if (currentFrame != null)
                                {
                                    var items = currentFrame.Items as JArray ?? new JArray();
                                    bool tabsEnabled = currentFrame.TabsEnabled?.ToString().ToLower() == "true";
                                    if (tabsEnabled)
                                    {
                                        var tabs = currentFrame.Tabs as JArray ?? new JArray();
                                        int currentTabIndex = Convert.ToInt32(currentFrame.CurrentTab?.ToString() ?? "0");
                                        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                                        {
                                            var currentTab = tabs[currentTabIndex] as JObject;
                                            items = currentTab?["Items"] as JArray ?? items;
                                        }
                                    }
                                    var matchingItem = items.FirstOrDefault(i => string.Equals(
                                        System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                                        System.IO.Path.GetFullPath(filePath),
                                        StringComparison.OrdinalIgnoreCase));
                                    if (matchingItem != null)
                                    {
                                        miAlwaysAdmin.IsChecked = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Error refreshing AlwaysRunAsAdmin IsChecked for {filePath}: {ex.Message}");
                    }
                };
                iconContextMenu.Items.Add(miAlwaysAdmin);


          sp.ContextMenu = iconContextMenu;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error creating context menu: {ex.Message}");
            }
        }
    }
}
