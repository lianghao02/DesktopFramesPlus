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
    // FrameManager 分檔：CreateFrame 拆出的建構步驟－面板右鍵選單（自 FrameManager.FrameBuild.cs 原樣搬移；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        // 面板右鍵選單骨架：主題樣式、永遠置頂、隱藏，以及「更多」子選單（自動捲起）；其餘項目在視窗建立後加入
        private static ContextMenu BuildPanelContextMenu(FrameBuildContext ctx, out MenuItem miMoreFrame, out MenuItem miHide)
        {
            dynamic frame = ctx.Frame;
            ContextMenu CnMnFramemanager = new ContextMenu();

            Style themedStyle = GetThemedContextMenuStyle(frame);
            if (themedStyle != null) CnMnFramemanager.Style = themedStyle;

            MenuItem miNewnoteFrame = new MenuItem { Header = Strings.MenuNewNoteFrame };

            // 面板右鍵選單：常用項目留在第一層，少用功能收進「更多」。
            // 注意：後面的動態項目都以「自訂外觀…」(Strings.MenuCustomize) 當定位點，該項必須維持在第一層。
            miMoreFrame = new MenuItem { Header = Strings.Get("MenuMore", "更多") };

            // --- NEW: Auto Roll Menu Item ---
            MenuItem miAutoRoll = new MenuItem { Header = Strings.MenuAutoRoll, IsCheckable = true };
            miAutoRoll.IsChecked = frame.AutoRoll?.ToString().ToLower() == "true";
            miAutoRoll.Click += (s, e) =>
            {
                UpdateFrameProperty(frame, "AutoRoll", miAutoRoll.IsChecked.ToString().ToLower(), $"Set Auto Roll to {miAutoRoll.IsChecked}");

                // If turned off while currently auto-rolled, immediately unroll it
                string fId = frame.Id?.ToString();
                if (!miAutoRoll.IsChecked && !string.IsNullOrEmpty(fId) && _autoRolledFrames.Contains(fId))
                {
                    // Dynamically find the window since 'win' is out of scope here
                    var targetWin = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().FirstOrDefault(w => w.Tag?.ToString() == fId);
                    if (targetWin != null)
                    {
                        var args = new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent };
                        targetWin.RaiseEvent(args); // Simulate mouse enter to wake it up cleanly
                    }
                }
            };
            miMoreFrame.Items.Add(miAutoRoll);

            // --- NEW: Always On Top Menu Item ---
            MenuItem miAlwaysOnTop = new MenuItem { Header = Strings.MenuAlwaysOnTop, IsCheckable = true };
            miAlwaysOnTop.IsChecked = frame.AlwaysOnTop?.ToString().ToLower() == "true";
            miAlwaysOnTop.Click += (s, e) =>
            {
                UpdateFrameProperty(frame, "AlwaysOnTop", miAlwaysOnTop.IsChecked.ToString().ToLower(), $"Set Always on Top to {miAlwaysOnTop.IsChecked}");
            };
            CnMnFramemanager.Items.Add(miAlwaysOnTop);

            CnMnFramemanager.Items.Add(new Separator());
            // --------------------------------

            miHide = new MenuItem { Header = Strings.MenuHideFrame }; // New Hide Frame item
                                                                   
            CnMnFramemanager.Items.Add(miHide); // Add Hide Frame
                                                // Add Note Frame specific context menu items if this is a Note Frame
            if (frame.ItemsType?.ToString() == "Note")
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"Adding Note frame context menu items for '{frame.Title}'");
                // We'll add the TextBox reference after the window is created
                // For now, just mark that this will need Note menu items
            }
            return CnMnFramemanager;
        }

        // 面板右鍵選單項目（視窗建立後）：暫時看穿、清除失效捷徑、資料夾檢視切換、貼上、外掛設定、重新命名、自訂外觀、刪除面板
        private static void BuildPanelMenuItems(FrameBuildContext ctx, ContextMenu CnMnFramemanager, MenuItem miMoreFrame, NonActivatingWindow win,
            out MenuItem miClearDeadShortcuts, out Separator sepClearDead, out MenuItem miPasteItem)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            // Add Note frame specific context menu items after window creation
            if (frame.ItemsType?.ToString() == "Note")
            {
                // The TextBox will be created in InitContent(), so we need to add menu items after that
                // We'll modify the context menu after InitContent() is called
            }
            //Peek behind frame
            MenuItem miPeekBehind = new MenuItem { Header = Strings.MenuPeekBehind };
            miMoreFrame.Items.Add(miPeekBehind);
            miPeekBehind.Click += (s, e) =>
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Initiating Peek Behind for frame '{frame.Title}'");
                // Create a separate transparent window for the countdown
                var countdownWindow = new Window
                {
                    Width = 60,
                    Height = 40,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Left = win.Left + (win.Width - 60) / 2, // Center horizontally
                    Top = win.Top + (win.Height - 40) / 2 // Center vertically
                };
                // Create countdown label
                Label countdownLabel = new Label
                {
                    Content = "10",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = System.Windows.Media.Brushes.Black,
                    Opacity = 0.7,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4)
                };
                countdownWindow.Content = countdownLabel;
                // Show the countdown window
                countdownWindow.Show();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Created countdown window for frame '{frame.Title}' at ({countdownWindow.Left}, {countdownWindow.Top})");
                // Fade out animation for the frame
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                // Countdown timer
                int countdownSeconds = 10;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (timerSender, timerArgs) =>
                {
                    countdownSeconds--;
                    countdownLabel.Content = countdownSeconds.ToString();
                    if (countdownSeconds <= 0)
                    {
                        timer.Stop();
                        // Fade in animation for the frame
                        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.8))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        };
                        win.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        // Close the countdown window
                        countdownWindow.Close();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Peek Behind completed for frame '{frame.Title}'");
                    }
                };
                // Start fade out and timer
                win.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                timer.Start();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Started Peek Behind fade-out and countdown for frame '{frame.Title}'");
            };


            // --- DYNAMIC MENU ITEMS ---

            // 1. Clear Dead Shortcuts (Initially Hidden)
            miClearDeadShortcuts = null;
            sepClearDead = null;

            if (frame.ItemsType?.ToString() == "Data")
            {
                sepClearDead = new Separator { Visibility = Visibility.Collapsed };
                CnMnFramemanager.Items.Add(sepClearDead);

                miClearDeadShortcuts = new MenuItem { Header = Strings.MenuClearDeadShortcuts, Visibility = Visibility.Collapsed };
                CnMnFramemanager.Items.Add(miClearDeadShortcuts);

                miClearDeadShortcuts.Click += (s, e) =>
                {
                    // --- BUG FIX: Fetch Live Frame ---
                    // The 'frame' closure variable becomes stale when Tabs are toggled/modified
                    string liveId = frame.Id?.ToString();
                    var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == liveId) ?? frame;

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Clear Dead Shortcuts clicked for frame '{liveFrame.Title}'");
                    try
                    {
                        int removedCount = FilePathUtilities.ClearDeadShortcutsFromFrame(liveFrame);
                        if (removedCount > 0)
                        {
                            FrameDataManager.SaveFrameData();
                            RefreshFrameUsingFormApproach(win, liveFrame);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameUpdate, $"Error in Clear Dead Shortcuts: {ex.Message}");
                    }
                };
            }

            // 2. Open Folder & Live View Toggle (Portal Only)
            if (frame.ItemsType?.ToString() == "Portal")
            {
                MenuItem miOpenFolder = new MenuItem { Header = Strings.MenuOpenFrameFolder };
                miOpenFolder.Click += (s, e) =>
                {
                    try
                    {
                        string folderPath = frame.Path?.ToString();
                        if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                            Process.Start("explorer.exe", folderPath);
                    }
                    catch { }
                };
                // 「開啟收納資料夾」已由後方動態項目提供（對所有 Portal 面板有效），此處不重複顯示。
                // CnMnFramemanager.Items.Add(miOpenFolder);

                // --- LIVE VIEW MODE TOGGLE ---
                MenuItem miViewAsDetails = new MenuItem { Header = Strings.MenuViewAsDetails, IsCheckable = true };

                // --- ULTRA FIX: Re-evaluate checkmark state every single time the menu opens at runtime! ---
                CnMnFramemanager.Opened += (s, e) =>
                {
                    try
                    {
                        string fId = frame.Id?.ToString();
                        bool isDetails = false;

                        // 1. Read live running state directly from the active PortalFramemanager
                        var activePortalEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == fId);
                        if (activePortalEntry.Value != null)
                        {
                            isDetails = activePortalEntry.Value.IsDetailsView;
                        }
                        else
                        {
                            // 2. Fallback check from JSON if window isn't loaded yet
                            string currentViewMode = null;
                            try
                            {
                                IDictionary<string, object> viewModeDict = frame is IDictionary<string, object> vmd ? vmd : ((Newtonsoft.Json.Linq.JObject)frame).ToObject<IDictionary<string, object>>();
                                currentViewMode = viewModeDict.ContainsKey("ViewMode") ? viewModeDict["ViewMode"]?.ToString() : null;
                            }
                            catch { }
                            if (string.IsNullOrEmpty(currentViewMode)) currentViewMode = SettingsManager.DefaultPortalView;
                            isDetails = string.Equals(currentViewMode, "Details", StringComparison.OrdinalIgnoreCase);
                        }

                        miViewAsDetails.IsChecked = isDetails;
                    }
                    catch { }
                };

                miViewAsDetails.Click += (s, e) =>
                {
                    string newMode = miViewAsDetails.IsChecked ? "Details" : "Icons";
                    string clickId = frame.Id?.ToString();
                    var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == clickId) ?? frame;

                    UpdateFrameProperty(liveFrame, "ViewMode", newMode, $"Toggled Portal View Mode to {newMode}");

                    // Trigger instant live switch on the active portal manager without reloading the window
                    var portalEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == clickId);
                    if (portalEntry.Value != null)
                    {
                        portalEntry.Value.SetViewMode(newMode);
                    }
                };
                CnMnFramemanager.Items.Add(miViewAsDetails);
            }

            CnMnFramemanager.Items.Add(new Separator());

            // 3. Paste Item (Initially Hidden)
            miPasteItem = new MenuItem { Header = Strings.MenuPasteItem, Visibility = Visibility.Collapsed };
            miPasteItem.Click += (s, e) => CopyPasteManager.PasteItem(frame);
            CnMnFramemanager.Items.Add(miPasteItem);


            //  CnMnFramemanager.Items.Add(new Separator());

            // --- NEW: Plugin Settings Context Menu ---
            if (frame.ItemsType?.ToString() == "Plugin")
            {
                MenuItem miPluginSettings = new MenuItem { Header = Strings.MenuPluginSettings };
                miPluginSettings.Click += (s, e) =>
                {
                    string fId = frame.Id?.ToString();
                    if (!string.IsNullOrEmpty(fId) && _activePlugins.TryGetValue(fId, out var plugin))
                    {
                        plugin.ShowSettingsWindow(win, frame);
                    }
                };
                miMoreFrame.Items.Add(new Separator());
                miMoreFrame.Items.Add(miPluginSettings);
            }
            // ---------------------------------------

            // Rename item
            MenuItem miRename = new MenuItem { Header = Strings.MenuRenameFrame };
            miRename.Click += (s, e) =>
            {
                if (ctx.TitleLabel != null && titletb != null)
                {
                    titletb.Text = ctx.TitleLabel.Content?.ToString() ?? "";
                    ctx.TitleLabel.Visibility = Visibility.Collapsed;
                    titletb.Visibility = Visibility.Visible;
                    win.BeginKeyboardInteractiveEdit(titletb);
                    titletb.Focus();
                    titletb.SelectAll();
                }
            };
            CnMnFramemanager.Items.Add(miRename);

            // CnMnFramemanager.Items.Add(miNewCustomize);
            MenuItem miCustomize = new MenuItem { Header = Strings.MenuCustomize };
            miCustomize.Click += (s, e) =>
            {
                try
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Opening CustomizeFrameFormManager for frame '{frame.Title}'");
                    var customizeForm = new CustomizeFrameFormManager(frame);
                    customizeForm.ShowDialog();
                    if (customizeForm.DialogResult)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"User saved changes in CustomizeFrameFormManager for frame '{frame.Title}'");
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"User cancelled CustomizeFrameFormManager for frame '{frame.Title}'");
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening CustomizeFrameFormManager: {ex.Message}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgOpenCustomizeFailed", ex.Message), Strings.DlgFormError);
                }
            };
            CnMnFramemanager.Items.Add(miCustomize);
            CnMnFramemanager.Items.Add(miMoreFrame);

            // --- EXPLICIT DELETE FRAME MENU ITEM ---
            CnMnFramemanager.Items.Add(new Separator());
            MenuItem miDeleteThisFrame = new MenuItem
            {
                Header = Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame) ? "拆除此面板並將內容移回桌面…" : Strings.MenuDeleteThisFrame,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB9, 0x1C, 0x1C))
            };
            miDeleteThisFrame.Click += (s, e) => DeleteFrameWithConfirmation(frame, win);
            CnMnFramemanager.Items.Add(miDeleteThisFrame);
        }

        // 面板右鍵選單開啟時的動態項目：依面板類型與 Ctrl 狀態加入或顯示匯出、空白格、依路徑命名、符合內容大小、收納資料夾、圖示大小等
        private static void BindPanelMenuOpened(FrameBuildContext ctx, ContextMenu CnMnFramemanager, NonActivatingWindow win,
            MenuItem miClearDeadShortcuts, Separator sepClearDead, MenuItem miPasteItem)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            // --- DYNAMIC STATE UPDATES (ON MENU OPEN) ---
            MenuItem miExportAllToDesktop = null;
            MenuItem miAddSpacerMenu = null; // REPLACED: Spacer Submenu
            Separator sepAfterSpacer = null; // NEW: Layout Separator
            MenuItem miNameAfterPath = null; // New: For Portal Renaming
            Separator sepNameAfterPath = null; // New: Separator for layout
            MenuItem miFitToContent = null; // 調整至最適大小
            MenuItem miNewFolder = null; // NEW: 在收納框內直接新增資料夾
            MenuItem miOpenStorageFolder = null; // NEW: Portal 開啟實體收納資料夾
            MenuItem miChangeStoragePath = null; // NEW: Portal 變更收納資料夾位置
            MenuItem miIconSizeMenu = null;      // NEW: 快速調整圖示大小子選單
            Separator sepPortalStorage = null;   // NEW: Portal 功能分隔線

            CnMnFramemanager.Opened += (contextSender, contextArgs) =>
            {
                // --- LIVE THEME FIX: Main Frame Context Menu ---
                try
                {
                    string id = frame.Id?.ToString();
                    var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id) ?? frame;
                    Style liveStyle = GetThemedContextMenuStyle(liveFrame);
                    if (liveStyle != null) CnMnFramemanager.Style = liveStyle;
                }
                catch { }

                // A. Update Paste Visibility
                bool hasCopiedItem = CopyPasteManager.HasCopiedItem();
                miPasteItem.Visibility = hasCopiedItem ? Visibility.Visible : Visibility.Collapsed;

                // B. Update Clear Dead Shortcuts Visibility
                if (miClearDeadShortcuts != null)
                {
                    bool hasDead = HasDeadShortcuts(frame);
                    miClearDeadShortcuts.Visibility = hasDead ? Visibility.Visible : Visibility.Collapsed;
                    if (sepClearDead != null) sepClearDead.Visibility = hasDead ? Visibility.Visible : Visibility.Collapsed;
                }

                // --- CTRL + RIGHT CLICK LOGIC ---
                bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool isDataFrame = frame.ItemsType?.ToString() == "Data";
                bool isPortalFrame = frame.ItemsType?.ToString() == "Portal";

                // Clean up previous dynamic items to prevent duplicates
                if (miExportAllToDesktop != null && CnMnFramemanager.Items.Contains(miExportAllToDesktop))
                    CnMnFramemanager.Items.Remove(miExportAllToDesktop);

                if (miAddSpacerMenu != null && CnMnFramemanager.Items.Contains(miAddSpacerMenu))
                    CnMnFramemanager.Items.Remove(miAddSpacerMenu);

                if (sepAfterSpacer != null && CnMnFramemanager.Items.Contains(sepAfterSpacer))
                    CnMnFramemanager.Items.Remove(sepAfterSpacer);

                if (miFitToContent != null && CnMnFramemanager.Items.Contains(miFitToContent))
                    CnMnFramemanager.Items.Remove(miFitToContent);

                if (miNewFolder != null && CnMnFramemanager.Items.Contains(miNewFolder))
                    CnMnFramemanager.Items.Remove(miNewFolder);

                if (miNameAfterPath != null && CnMnFramemanager.Items.Contains(miNameAfterPath))
                    CnMnFramemanager.Items.Remove(miNameAfterPath);

                if (sepNameAfterPath != null && CnMnFramemanager.Items.Contains(sepNameAfterPath))
                    CnMnFramemanager.Items.Remove(sepNameAfterPath);

                if (miOpenStorageFolder != null && CnMnFramemanager.Items.Contains(miOpenStorageFolder))
                    CnMnFramemanager.Items.Remove(miOpenStorageFolder);

                if (miChangeStoragePath != null && CnMnFramemanager.Items.Contains(miChangeStoragePath))
                    CnMnFramemanager.Items.Remove(miChangeStoragePath);

                if (miIconSizeMenu != null && CnMnFramemanager.Items.Contains(miIconSizeMenu))
                    CnMnFramemanager.Items.Remove(miIconSizeMenu);

                if (sepPortalStorage != null && CnMnFramemanager.Items.Contains(sepPortalStorage))
                    CnMnFramemanager.Items.Remove(sepPortalStorage);

                // C0. 圖示大小快速選單 (Data 與 Portal 皆可用)
                if (isDataFrame || isPortalFrame)
                {
                    string id = frame.Id?.ToString();
                    var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id) ?? frame;
                    string currentIconSize = GetSafeProperty(liveFrame, "IconSize");
                    if (string.IsNullOrEmpty(currentIconSize)) currentIconSize = "Medium";

                    string menuHeader = (Strings.LblIconSize ?? "圖示大小").TrimEnd(':', '：', ' ');
                    miIconSizeMenu = new MenuItem { Header = menuHeader };

                    var sizeOptions = new (string Key, string Label)[]
                    {
                        ("Tiny", Strings.Get("OptTiny", "極小 (Tiny)")),
                        ("Small", Strings.Get("OptSmall", "小 (Small)")),
                        ("Medium", Strings.Get("OptMedium", "中 (Medium)")),
                        ("Large", Strings.Get("OptLarge", "大 (Large)")),
                        ("Huge", Strings.Get("OptHuge", "特大 (Huge)"))
                    };

                    foreach (var opt in sizeOptions)
                    {
                        string sizeKey = opt.Key;
                        var subItem = new MenuItem
                        {
                            Header = opt.Label,
                            IsCheckable = true,
                            IsChecked = string.Equals(currentIconSize, sizeKey, StringComparison.OrdinalIgnoreCase)
                        };
                        subItem.Click += (s, e) => SetFrameIconSizeLive(win, liveFrame, sizeKey);
                        miIconSizeMenu.Items.Add(subItem);
                    }

                    int sizeInsertIndex = CnMnFramemanager.Items.Count - 1;
                    var customizeItemForSize = CnMnFramemanager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Strings.MenuCustomize);
                    if (customizeItemForSize != null) sizeInsertIndex = CnMnFramemanager.Items.IndexOf(customizeItemForSize);
                    CnMnFramemanager.Items.Insert(sizeInsertIndex, miIconSizeMenu);
                }

                // C. Export All (Data Frame + Ctrl 才顯示)
                if (isCtrlPressed && isDataFrame)
                {
                    miExportAllToDesktop = new MenuItem { Header = Strings.MenuExportAllIcons };
                    miExportAllToDesktop.Click += (s, e) => ExportAllIconsToDesktop(frame);

                    // Insert before Customize (safe lookup)
                    int exportInsertIndex = CnMnFramemanager.Items.Count - 1;
                    var customizeItemForExport = CnMnFramemanager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Strings.MenuCustomize);
                    if (customizeItemForExport != null) exportInsertIndex = CnMnFramemanager.Items.IndexOf(customizeItemForExport);
                    CnMnFramemanager.Items.Insert(exportInsertIndex, miExportAllToDesktop);
                }

                // C2. Add Spacer Menu (Data Frame 即可，不需 Ctrl)
                if (isDataFrame)
                {
                    miAddSpacerMenu = new MenuItem { Header = Strings.MenuAddSpacer };

                    MenuItem miSpacerBlank = new MenuItem { Header = Strings.MenuSpacerBlank };
                    MenuItem miSpacerDot = new MenuItem { Header = Strings.MenuSpacerDot };

                    // Centralized Spacer Creation Logic
                    Action<string> CreateSpacer = (prefix) =>
                    {
                        try
                        {
                            string id = frame.Id?.ToString();
                            var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);
                            if (liveFrame == null) return;

                            JArray items = null;
                            bool tabsEnabled = liveFrame.TabsEnabled?.ToString().ToLower() == "true";
                            if (tabsEnabled)
                            {
                                var tabs = liveFrame.Tabs as JArray ?? new JArray();
                                int currentTab = Convert.ToInt32(liveFrame.CurrentTab?.ToString() ?? "0");
                                if (currentTab >= 0 && currentTab < tabs.Count)
                                {
                                    var activeTab = tabs[currentTab] as JObject;
                                    items = activeTab?["Items"] as JArray ?? new JArray();
                                }
                            }
                            if (items == null) items = liveFrame.Items as JArray ?? new JArray();

                            int nextDisplayOrder = items.Count > 0 ? items.Max(i => i["DisplayOrder"]?.Value<int>() ?? 0) + 1 : 0;

                            dynamic newItem = new System.Dynamic.ExpandoObject();
                            IDictionary<string, object> newItemDict = newItem;
                            newItemDict["Filename"] = prefix + "_" + Guid.NewGuid().ToString();
                            newItemDict["DisplayName"] = "";
                            newItemDict["IsFolder"] = false;
                            newItemDict["IsLink"] = false;
                            newItemDict["IsNetwork"] = false;
                            newItemDict["AlwaysRunAsAdmin"] = false;
                            newItemDict["DisplayOrder"] = nextDisplayOrder;

                            items.Add(JObject.FromObject(newItem));

                            if (tabsEnabled && Convert.ToInt32(liveFrame.CurrentTab?.ToString() ?? "0") == 0)
                                TabManager.SynchronizeTab0Content(id, "tab0", "add");
                            else if (!tabsEnabled)
                                TabManager.SynchronizeTab0Content(id, "main", "add");

                            FrameDataManager.SaveFrameData();
                            RefreshFrameUsingFormApproach(win, liveFrame);
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error adding spacer: {ex.Message}");
                        }
                    };

                    miSpacerBlank.Click += (s, e) => CreateSpacer("INTERNAL_BLANK_EMPTY");
                    miSpacerDot.Click += (s, e) => CreateSpacer("INTERNAL_BLANK_DOT");

                    miAddSpacerMenu.Items.Add(miSpacerBlank);
                    miAddSpacerMenu.Items.Add(miSpacerDot);

                    sepAfterSpacer = new Separator();

                    // Insert before Customize (safe lookup)
                    int insertIndex = CnMnFramemanager.Items.Count - 1;
                    var customizeItem = CnMnFramemanager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Strings.MenuCustomize);
                    if (customizeItem != null) insertIndex = CnMnFramemanager.Items.IndexOf(customizeItem);

                    CnMnFramemanager.Items.Insert(insertIndex, miAddSpacerMenu);
                    CnMnFramemanager.Items.Insert(insertIndex + 1, sepAfterSpacer);
                }

                // C3. Fit to Content (Data Frame 即可)
                if (isDataFrame)
                {
                    miFitToContent = new MenuItem { Header = Strings.MenuFitToContent };
                    miFitToContent.Click += (s, e) =>
                    {
                        try
                        {
                            // 執行時期從 win 的 VisualTree 找 WrapPanel
                            WrapPanel wp = FindVisualChild<WrapPanel>(win);
                            if (wp == null) return;

                            // 強制 WrapPanel 先完成排版，才能取得正確的 ActualWidth/Height
                            wp.UpdateLayout();

                            double totalContentHeight = 0;
                            double rowWidth = 0;
                            double rowHeight = 0;
                            double availableWidth = win.Width - 20; // 預留滾動條/邊界

                            foreach (UIElement child in wp.Children)
                            {
                                if (child.Visibility == Visibility.Collapsed) continue;
                                child.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                                double cw = child.DesiredSize.Width;
                                double ch = child.DesiredSize.Height;

                                if (rowWidth + cw > availableWidth && rowWidth > 0)
                                {
                                    totalContentHeight += rowHeight;
                                    rowWidth = cw;
                                    rowHeight = ch;
                                }
                                else
                                {
                                    rowWidth += cw;
                                    rowHeight = Math.Max(rowHeight, ch);
                                }
                            }
                            totalContentHeight += rowHeight; // 最後一行

                            // 視窗 chrome 高度（標題列等）
                            double chromeHeight = win.ActualHeight - (win.Content as FrameworkElement)?.ActualHeight ?? 0;
                            if (chromeHeight <= 0) chromeHeight = 50; // 保守估計

                            double targetHeight = totalContentHeight + chromeHeight + 16; // 加上 padding
                            targetHeight = Math.Max(targetHeight, 80); // 最小高度

                            // DoubleAnimation 動畫縮放
                            var anim = new System.Windows.Media.Animation.DoubleAnimation
                            {
                                To = targetHeight,
                                Duration = TimeSpan.FromMilliseconds(180),
                                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                            };
                            win.BeginAnimation(Window.HeightProperty, anim);

                            // 存檔
                            string frameId2 = frame.Id?.ToString();
                            var liveFrame2 = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId2);
                            if (liveFrame2 is JObject jf2)
                            {
                                jf2["Height"] = targetHeight;
                                FrameDataManager.SaveFrameData();
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"FitToContent error: {ex.Message}");
                        }
                    };

                    // Insert before Customize
                    int fitInsertIndex = CnMnFramemanager.Items.Count - 1;
                    var customizeItemFit = CnMnFramemanager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Strings.MenuCustomize);
                    if (customizeItemFit != null) fitInsertIndex = CnMnFramemanager.Items.IndexOf(customizeItemFit);

                    miNewFolder = new MenuItem { Header = Strings.MenuNewFolder };
                    miNewFolder.Click += (s, e) =>
                    {
                        try
                        {
                            string baseRoot = SettingsManager.DefaultPortalStorageRoot;
                            string frameTitle = frame.Title?.ToString() ?? "收納區";
                            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
                            string cleanTitle = new string(frameTitle.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
                            if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = "收納區";
                            string targetParentDir = System.IO.Path.Combine(baseRoot, cleanTitle);

                            string created = Desktop_Frames.Services.StorageFenceService.PromptAndCreateNewFolder(targetParentDir, win);
                            if (!string.IsNullOrEmpty(created))
                            {
                                string id = frame.Id?.ToString();
                                var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id) ?? frame;
                                AddItemToDataFrame(liveFrame, created);
                                FrameDataManager.SaveFrameData();
                                RefreshFrameUsingFormApproach(win, liveFrame);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Data frame NewFolder error: {ex.Message}");
                        }
                    };
                    CnMnFramemanager.Items.Insert(fitInsertIndex, miNewFolder);
                    CnMnFramemanager.Items.Insert(fitInsertIndex + 1, miFitToContent);
                }

                // D1. Portal Frame 操作功能（無需按 Ctrl，一般右鍵直接可用）
                if (isPortalFrame)
                {
                    miNewFolder = new MenuItem { Header = Strings.MenuNewFolder };
                    miNewFolder.Click += (s, e) =>
                    {
                        try
                        {
                            string fId = frame.Id?.ToString();
                            var pEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == fId);
                            string targetDir = pEntry.Value?.CurrentTargetFolderPath ?? frame.Path?.ToString();
                            if (!string.IsNullOrWhiteSpace(targetDir))
                            {
                                string created = Desktop_Frames.Services.StorageFenceService.PromptAndCreateNewFolder(targetDir, win);
                                if (!string.IsNullOrEmpty(created))
                                {
                                    pEntry.Value?.TriggerSync(immediate: true);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Portal frame NewFolder error: {ex.Message}");
                        }
                    };

                    miOpenStorageFolder = new MenuItem { Header = Strings.Get("MenuOpenStorageFolder", "開啟收納資料夾") };
                    miOpenStorageFolder.Click += (s, e) =>
                    {
                        try
                        {
                            string targetPath = frame.Path?.ToString();
                            if (!string.IsNullOrEmpty(targetPath) && Directory.Exists(targetPath))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = targetPath,
                                    UseShellExecute = true
                                });
                            }
                            else
                            {
                                string msg = !string.IsNullOrEmpty(targetPath)
                                    ? string.Format(Strings.MsgTargetNotFound, targetPath)
                                    : "找不到指定的收納資料夾。";
                                MessageBox.Show(msg, "Desktop Frames +", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Failed to open storage folder: {ex.Message}");
                        }
                    };

                    miChangeStoragePath = new MenuItem { Header = Strings.MenuChangeStoragePath };
                    miChangeStoragePath.Click += (s, e) =>
                    {
                        try
                        {
                            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                            {
                                fbd.Description = Strings.MenuChangeStoragePath;
                                fbd.UseDescriptionForTitle = true;
                                string currentPath = frame.Path?.ToString();
                                if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                                {
                                    fbd.SelectedPath = currentPath;
                                }

                                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                                {
                                    string newPath = fbd.SelectedPath;
                                    string id = frame.Id?.ToString();
                                    var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);
                                    if (liveFrame != null)
                                    {
                                        string oldPath = GetSafeProperty(liveFrame, "Path") ?? currentPath ?? "";
                                        string oldFolderName = "";
                                        try { if (!string.IsNullOrEmpty(oldPath)) oldFolderName = new DirectoryInfo(oldPath).Name; } catch { }
                                        string newFolderName = "";
                                        try { newFolderName = new DirectoryInfo(newPath).Name; } catch { }

                                        string curTitle = GetSafeProperty(liveFrame, "Title") ?? "";
                                        bool shouldAutoRenameTitle = !string.IsNullOrEmpty(newFolderName) &&
                                            (string.IsNullOrWhiteSpace(curTitle) ||
                                             curTitle == "捷徑" ||
                                             curTitle == "Shortcuts" ||
                                             curTitle == "收納區" ||
                                             string.Equals(curTitle, oldFolderName, StringComparison.OrdinalIgnoreCase));

                                        if (liveFrame is JObject jFrame)
                                        {
                                            jFrame["Path"] = newPath;
                                            if (shouldAutoRenameTitle) jFrame["Title"] = newFolderName;
                                        }
                                        else
                                        {
                                            liveFrame.Path = newPath;
                                            if (shouldAutoRenameTitle) liveFrame.Title = newFolderName;
                                        }
                                        frame.Path = newPath;
                                        if (shouldAutoRenameTitle)
                                        {
                                            frame.Title = newFolderName;
                                            ctx.TitleLabel.Content = newFolderName;
                                            win.Title = newFolderName;
                                            titletb.Text = newFolderName;
                                        }
                                        if (!string.IsNullOrEmpty(id))
                                        {
                                            _portalNavigationStates.Remove(id);
                                        }
                                        FrameDataManager.SaveFrameData();
                                        NavigatePortalFrame(liveFrame, newPath);
                                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Changed portal frame storage path to: {newPath}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Failed to change storage folder: {ex.Message}");
                        }
                    };

                    sepPortalStorage = new Separator();

                    int portalInsertIndex = CnMnFramemanager.Items.Count - 1;
                    var customizeItemPortal = CnMnFramemanager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Strings.MenuCustomize);
                    if (customizeItemPortal != null) portalInsertIndex = CnMnFramemanager.Items.IndexOf(customizeItemPortal);

                    CnMnFramemanager.Items.Insert(portalInsertIndex, miNewFolder);
                    CnMnFramemanager.Items.Insert(portalInsertIndex + 1, miOpenStorageFolder);
                    CnMnFramemanager.Items.Insert(portalInsertIndex + 2, miChangeStoragePath);
                    CnMnFramemanager.Items.Insert(portalInsertIndex + 3, sepPortalStorage);
                }

                // D2. Name After Target (Portal Frame + Ctrl)
                if (isCtrlPressed && isPortalFrame)
                {
                    miNameAfterPath = new MenuItem { Header = Strings.MenuNameAfterPath };
                    miNameAfterPath.Click += (s, e) =>
                    {
                        // Get Base Path (Not navigation path)
                        string targetPath = frame.Path?.ToString();

                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            // 1. Update Live Object (Global List)
                            string id = frame.Id?.ToString();
                            var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);

                            if (liveFrame != null)
                            {
                                if (liveFrame is JObject jFrame) jFrame["Title"] = targetPath;
                                else liveFrame.Title = targetPath;

                                frame.Title = targetPath; // Update local reference
                            }

                            // 2. Update UI
                            ctx.TitleLabel.Content = targetPath;
                            win.Title = targetPath;
                            titletb.Text = targetPath; // Update hidden textbox too

                            // 3. Save
                            FrameDataManager.SaveFrameData();
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Renamed portal frame to target: {targetPath}");
                        }
                    };

                    sepNameAfterPath = new Separator();

                    // Insert before Customize
                    int insertIndex = CnMnFramemanager.Items.Count - 1;
                    var customizeItem = CnMnFramemanager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Strings.MenuCustomize);

                    if (customizeItem != null)
                    {
                        insertIndex = CnMnFramemanager.Items.IndexOf(customizeItem);
                        // Insert Order: Name -> Separator -> Customize
                        CnMnFramemanager.Items.Insert(insertIndex, miNameAfterPath);
                        CnMnFramemanager.Items.Insert(insertIndex + 1, sepNameAfterPath);
                    }
                    else
                    {
                        CnMnFramemanager.Items.Add(miNameAfterPath);
                        CnMnFramemanager.Items.Add(sepNameAfterPath);
                    }
                }

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Updated frame menu - Ctrl:{isCtrlPressed}");
            };
        }
    }
}
