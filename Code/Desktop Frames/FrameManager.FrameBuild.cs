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
    // FrameManager 分檔：CreateFrame 拆出的建構步驟（第二階段；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        // CreateFrame 各建構步驟共用的資料。原本是 CreateFrame 的區域變數，被多個事件處理器捕捉；
        // 改放在同一個物件上，事件觸發時讀到的仍是同一份最新值，行為與捕捉區域變數相同。
        private sealed class FrameBuildContext
        {
            public dynamic Frame;
            public TargetChecker TargetChecker;
            public TextBox TitleTextBox;
            // 重新命名的提交動作：在 CreateFrame 後段才指定，♥、鎖頭與標題的點擊事件在觸發當下讀取
            public Action CommitRename;
            // 標題文字：在 CreateFrame 中段才建立，面板選單「重新命名」等事件在觸發當下讀取
            public Label TitleLabel;
        }

        // 外框：邊框顏色與粗細、Q 版面板底、尺寸變化時的內距、雙擊開啟搜尋
        private static Border BuildFrameBorder(FrameBuildContext ctx, DockPanel dp)
        {
            dynamic frame = ctx.Frame;
            // Get frame border color and thickness from frame data
            System.Windows.Media.Brush borderBrush = null;
            double borderThickness = 0;
            try
            {
                string borderColorName = frame.FrameBorderColor?.ToString();
                int customThickness = Convert.ToInt32(frame.FrameBorderThickness?.ToString() ?? "2");
                if (!string.IsNullOrEmpty(borderColorName))
                {
                    var borderColor = Utility.GetColorFromName(borderColorName);
                    borderBrush = new SolidColorBrush(borderColor);
                    borderThickness = customThickness; // Use custom thickness
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Applied border color '{borderColorName}' with thickness {borderThickness} to frame '{frame.Title}'");
                }
                else if (customThickness > 0)
                {
                    // Even without color, apply thickness with default color
                    borderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                    borderThickness = customThickness;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Applied default border with thickness {borderThickness} to frame '{frame.Title}'");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Error applying border: {ex.Message}");
                borderBrush = null;
                borderThickness = 0;
            }
            Border cborder = new GameSkin
            {
                Skin = GameSkinKind.Panel,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0)),
                // --- 現代磨砂 Dock 風格：預設 14px 柔和圓角；直角模式維持 0px ---
                CornerRadius = SettingsManager.FramesWithNoRoundCorners ? new CornerRadius(0) : new CornerRadius(14),
                BorderBrush = borderBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), // 晶透微高光透光邊框
                BorderThickness = new Thickness(borderThickness > 0 ? borderThickness : 1), // 預設 1px 細膩邊界
                Child = dp
            };
            cborder.BorderBrush = string.IsNullOrEmpty(GetSafeProperty(frame, "FrameBorderColor")) ? GameTheme.Outline : borderBrush;
            cborder.BorderThickness = new Thickness(0);
            cborder.Padding = new Thickness(16, 8, 16, 12);
            cborder.ClipToBounds = true;
            cborder.SizeChanged += (sender, args) =>
            {
                cborder.Padding = cborder.ActualHeight < 60 ? new Thickness(8, 1, 8, 0) : new Thickness(16, 8, 16, 12);
                cborder.InvalidateVisual();
            };
            cborder.SnapsToDevicePixels = true;
            // Add Double Click Handler
            cborder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SearchFormManager.ToggleSearch();
                    e.Handled = true;
                }
            };
            return cborder;
        }

        // 標題列左側的選單圖示（♥）：外觀、滑鼠移入移出淡入淡出、點擊開啟 ♥ 選單（按住 Ctrl 為延伸選單）
        private static TextBlock BuildMenuIcon(FrameBuildContext ctx)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            //  Add heart symbol in top-left corner
            string MenuSymbol = "♥";

            if (SettingsManager.MenuIcon == 0)
            {
                MenuSymbol = "♥";
              
            }
            else if (SettingsManager.MenuIcon == 1)
            {
                MenuSymbol = "☰";
            }
            else if (SettingsManager.MenuIcon == 2)
            {
                MenuSymbol = "≣";
            }
            else if (SettingsManager.MenuIcon == 3)
            {
                MenuSymbol = "𓃑";
            }

            TextBlock heart = new TextBlock
            {
                // Text = "♥",

                Name = "FrameMenuIcon", // New! Name
                Text = MenuSymbol,
                FontSize = 18,
                Foreground = System.Windows.Media.Brushes.White, // Match title and icon text color
                // 透明底加內距：圖示字形本身很小，擴大可點擊範圍（左右 6、上下 3 px）
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(2, 0, 0, 0), // Position neatly inside banner left column
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Opacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(11, 35, 20),
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.75
                }
            };
  
            _heartTextBlocks[frame] = heart;


            heart.MouseEnter += (s, e) =>
            {
                // Remove previous animation 
                heart.BeginAnimation(UIElement.OpacityProperty, null);

                heart.Opacity = 1.0;
            };

            heart.MouseLeave += (s, e) =>
            {
                double targetOpacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100);

                DoubleAnimation fadeBack = new DoubleAnimation
                {
                    From = 1.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromMilliseconds(800)
                };

                heart.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            };

            // Store heart TextBlock reference for this frame
            _heartTextBlocks[frame] = heart;
            // Create and assign heart ContextMenu using centralized builder
      
            heart.ContextMenu = BuildHeartContextMenu(frame, false); // Normal menu by default
                                                                     //// Handle left-click to open heart context menu
                                                                     //heart.MouseLeftButtonDown += (s, e) =>
                                                                     //{
                                                                     // if (e.ChangedButton == MouseButton.Left && heart.ContextMenu != null)
                                                                     // {
                                                                     // heart.ContextMenu.IsOpen = true;
                                                                     // e.Handled = true;
                                                                     // }
                                                                     //};
                                                                     // Handle left-click to open heart context menu
            heart.MouseLeftButtonDown += (s, e) =>
            {


                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    ctx.CommitRename?.Invoke();
                    return;
                }


                if (e.ChangedButton == MouseButton.Left && heart.ContextMenu != null)
                {
                    // Check if Ctrl is pressed for extended menu
                    bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    // DEBUG: Log the Ctrl state
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Heart clicked - Ctrl pressed: {isCtrlPressed}");
                    // Rebuild context menu based on Ctrl state
                    heart.ContextMenu = BuildHeartContextMenu(frame, isCtrlPressed);
                    heart.ContextMenu.IsOpen = true;
                    e.Handled = true;
                }
            };
            return heart;
        }

        // 標題列右側的鎖頭：外觀、滑鼠移入移出淡入淡出、點擊切換鎖定並寫入設定
        private static TextBlock BuildLockIcon(FrameBuildContext ctx)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            // Add a protection symbol in top-right corner


            string LockSymbol = "🛡️";

            if (SettingsManager.LockIcon == 0)
            {
                LockSymbol = "🛡️";
            }
            else if (SettingsManager.LockIcon == 1)
            {
                LockSymbol = "🔑";
            }
            else if (SettingsManager.LockIcon == 2)
            {
                LockSymbol = "🔐";
            }
            else if (SettingsManager.LockIcon == 3)
            {
                LockSymbol = "🔒";
            }

            //MessageBox.Show(SettingsManager.LockIcon +" " + LockSymbol.ToString());


            TextBlock lockIcon = new TextBlock
           
            {

                //     Text = "🔐",
                //     Text = "🔑",
                //     Text = "🔒",
                //     Text = "🔓",
                Name = "FrameLockIcon", // New! Name
                Text = LockSymbol,//"🛡️",
                FontSize = 14,
                Foreground = frame.IsLocked?.ToString().ToLower() == "true" ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 8, 0), // Adjusted for top-right positioning neatly inside banner
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = frame.IsLocked?.ToString().ToLower() == "true" ? "Frame is locked (click to unlock)" : "Frame is unlocked (click to lock)",
                Opacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(11, 35, 20),
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.75
                }
            };

  
            GameTheme.ApplyLockIconStyle(lockIcon, frame.IsLocked?.ToString().ToLower() == "true");

            lockIcon.MouseEnter += (s, e) =>
            {
                // Remove previous animation
                lockIcon.BeginAnimation(UIElement.OpacityProperty, null);

                lockIcon.Opacity = 1.0;
            };

            lockIcon.MouseLeave += (s, e) =>
            {
                double targetOpacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100);

                DoubleAnimation fadeBack = new DoubleAnimation
                {
                    From = 1.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromMilliseconds(800)
                };

                lockIcon.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            };


            // Set initial state without saving to JSON
            UpdateLockState(lockIcon, frame, null, saveToJson: false);
            // Lock icon click handler
            lockIcon.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {

                    // FIX: Directly call CommitRename logic
                    if (titletb.IsVisible)
                    {
                        ctx.CommitRename?.Invoke();
                        return;
                    }

                    // Get the NonActivatingWindow to find the frame by Id
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(lockIcon);
                    if (win == null)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Could not find NonActivatingWindow for lock icon click in frame '{frame.Title}'");
                        return;
                    }
                    // Find the frame in FrameDataManager.FrameData using the window's Tag (Id)
                    string frameId = win.Tag?.ToString();
                    if (string.IsNullOrEmpty(frameId))
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Frame Id is missing for window '{win.Title}'");
                        return;
                    }
                    dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    if (currentFrame == null)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Frame with Id '{frameId}' not found in FrameDataManager.FrameData");
                        return;
                    }
                    // Toggle the lock state
                    bool currentState = currentFrame.IsLocked?.ToString().ToLower() == "true";
                    bool newState = !currentState;
                    // Update UI and JSON on the main thread
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateLockState(lockIcon, currentFrame, newState, saveToJson: true);
                    });
                }
            };
            return lockIcon;
        }

        // 標題列底板：四欄（選單圖示、標題、篩選、鎖頭），先放入選單圖示
        private static Grid BuildTitleGrid(TextBlock heart)
        {
            // Create a Grid for the titlebar - move here to ensure it is created before mouse handler
            Grid titleGrid = new Grid
            {
                Background = GameTheme.Accent
            };
            var headerSkin = new GameSkin { Skin = GameSkinKind.Header, IsHitTestVisible = false };
            Grid.SetColumnSpan(headerSkin, 4);
            titleGrid.Children.Add(headerSkin);
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Pixel) }); // Col 0: Menu Icon
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Title
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // Col 2: Filter Icon (Auto width)
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Pixel) }); // Col 3: Lock Icon
            Grid.SetColumn(heart, 0);
            titleGrid.Children.Add(heart);
            return titleGrid;
        }

        // 標題文字：粗體、顏色、字級依面板設定，放在標題列第 2 欄
        private static Label BuildTitleLabel(FrameBuildContext ctx, Grid titleGrid)
        {
            dynamic frame = ctx.Frame;
            bool isBoldTitle = false;
            try
            {
                isBoldTitle = frame.BoldTitleText?.ToString().ToLower() == "true";
            }
            catch { /* Safe fallback */ }
            // Get title text color or use default white
            System.Windows.Media.Brush titleTextBrush = System.Windows.Media.Brushes.White; // Default
            try
            {
                string titleColorName = frame.TitleTextColor?.ToString();
                if (!string.IsNullOrEmpty(titleColorName))
                {
                    var titleColor = Utility.GetColorFromName(titleColorName);
                    titleTextBrush = new SolidColorBrush(titleColor);
                }
            }
            catch
            {
                titleTextBrush = System.Windows.Media.Brushes.White; // Fallback
            }
            // Get title text size from frame data
            double titleFontSize = 12; // Default Medium size
            try
            {
                string titleSizeValue = frame.TitleTextSize?.ToString() ?? "Medium";
                switch (titleSizeValue)
                {
                    case "Small":
                        titleFontSize = 10;
                        break;
                    case "Large":
                        titleFontSize = 16;
                        break;
                    default: // Medium
                        titleFontSize = 12;
                        break;
                }
            }
            catch
            {
                titleFontSize = 12; // Fallback to Medium
            }
            Label titlelabel = new Label
            {
                Content = frame.Title.ToString(),
                Foreground = titleTextBrush, // Changed from hardcoded White
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.SizeAll,
                FontWeight = isBoldTitle ? FontWeights.Bold : FontWeights.SemiBold,
                FontSize = titleFontSize, // Apply custom title text size
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft JhengHei UI"),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(11, 35, 20),
                    BlurRadius = 2.5,
                    ShadowDepth = 1.2,
                    Direction = 270,
                    Opacity = 0.85
                }
            };
            Grid.SetColumn(titlelabel, 1);
            titleGrid.Children.Add(titlelabel);
            return titlelabel;
        }

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

        // 視窗狀態：讀取隱藏設定、拉回螢幕內、載入時套用鎖定與捲起高度、隱藏選單、縮放寫檔、拖曳中按 Esc 取消、自動隱藏計時重設
        private static void BindWindowStateEvents(FrameBuildContext ctx, NonActivatingWindow win, TextBlock lockIcon, MenuItem miHide)
        {
            dynamic frame = ctx.Frame;
            // Handle both JObject and ExpandoObject access
            string isHiddenString = "false"; // Default value
            try
            {
                if (frame is JObject jFrame)
                {
                    // Existing Frame from JSON
                    isHiddenString = jFrame["IsHidden"]?.ToString().ToLower() ?? "false";
                }
                else
                {
                    // New ExpandoObject Frame
                    isHiddenString = (frame.IsHidden?.ToString() ?? "false").ToLower();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Error reading IsHidden: {ex.Message}");
            }
            bool isHidden = isHiddenString == "true";
            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Frame '{frame.Title}' IsHidden state: {isHidden}");
            // Adjust the frame position to ensure it fits within screen bounds
            AdjustFramePositionToScreen(win);
            win.Loaded += (s, e) =>
            {
                UpdateLockState(lockIcon, frame, null, saveToJson: false);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Applied lock state for frame '{frame.Title}' on load: IsLocked={frame.IsLocked?.ToString().ToLower()}");
                // Apply IsRolled state
                bool isRolled = frame.IsRolled?.ToString().ToLower() == "true";
                double targetHeight = 28; // Default for rolled-up state  //rolled height
                if (!isRolled)
                {
                    double unrolledHeight = (double)frame.Height; // Default to frame.Height
                    if (frame.UnrolledHeight != null)
                    {
                        if (double.TryParse(frame.UnrolledHeight.ToString(), out double parsedHeight))
                        {
                            if (parsedHeight > 0)
                            {
                                unrolledHeight = parsedHeight;
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"UnrolledHeight {parsedHeight} is invalid (non-positive) for frame '{frame.Title}', using Height={unrolledHeight}");
                            }
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Failed to parse UnrolledHeight '{frame.UnrolledHeight}' for frame '{frame.Title}', using Height={unrolledHeight}");
                        }
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"UnrolledHeight is null for frame '{frame.Title}', using Height={unrolledHeight}");
                    }
                    targetHeight = unrolledHeight;
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, $"Applied rolled-down state for frame '{frame.Title}' on load: Height={targetHeight}");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, $"Applied rolled-up state for frame '{frame.Title}' on load: Height={targetHeight}");
                }
                win.Height = targetHeight;
                // Apply WrapPanel visibility
                var border = win.Content as Border;
                if (border != null)
                {
                    var dockPanel = border.Child as DockPanel;
                    if (dockPanel != null)
                    {
                        var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                        if (scrollViewer != null)
                        {
                            var wpcont = scrollViewer.Content as WrapPanel;
                            if (wpcont != null)
                            {
                                wpcont.Visibility = isRolled ? Visibility.Collapsed : Visibility.Visible;
                                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Set initial WrapPanel visibility to {(isRolled ? "Collapsed" : "Visible")} for frame '{frame.Title}'");

                                // --- MULTI-MONITOR INVISIBLE ICON FIX ---
                                if (!isRolled)
                                {
                                    wpcont.UpdateLayout();
                                    wpcont.InvalidateVisual();
                                    wpcont.Opacity = 0.99;
                                    wpcont.Dispatcher.BeginInvoke(new Action(() => { wpcont.Opacity = 1.0; }), System.Windows.Threading.DispatcherPriority.Render);
                                }
                            }
                        }
                    }
                }
                if (isHidden)
                {
                    win.Visibility = Visibility.Hidden;
                    TrayManager.AddHiddenFrame(win);
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, $"Hid frame '{frame.Title}' after loading at startup");
                }
            };
            // Step 5
            if (isHidden)
            {
                TrayManager.AddHiddenFrame(win);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Added frame '{frame.Title}' to hidden list at startup");
            }
            // Hide click
            miHide.Click += (s, e) =>
            {
                UpdateFrameProperty(frame, "IsHidden", "true", $"Hid frame '{frame.Title}'");
                TrayManager.AddHiddenFrame(win);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Triggered Hide frame for '{frame.Title}'");
            };
            // Handle manual resize to update both Height and UnrolledHeight
            win.SizeChanged += (s, e) =>
            {
                // Get current frame reference by ID to avoid stale references
                string frameId = win.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId))
                {

                    return;
                }

                // --- ACCORDION STACK CASCADE HOOK ---
                // Triggers on manual grip resizing AND smooth roll-up/roll-down animations
                if (win.IsLoaded && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.1)
                {
                    double deltaY = e.NewSize.Height - e.PreviousSize.Height;
                    SnapManager.CascadeStack(frameId, deltaY);
                }
                // ------------------------------------

                // Skip updates if this frame is currently in a rollup/rolldown transition
                // --- NEW: Also skip if it is Auto-Rolled (so we don't save the rolled-up height) ---
                if (_framesInTransition.Contains(frameId) || _autoRolledFrames.Contains(frameId))
                {
                    return;
                }
                // Find the current frame in FrameDataManager.FrameData using ID
                // Find the current frame in FrameDataManager.FrameData using ID
                var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (currentFrame != null)
                {

                    double newHeight = e.NewSize.Height;
                    double newWidth = e.NewSize.Width;
                    double oldHeight = Convert.ToDouble(currentFrame.Height?.ToString() ?? "0");
                    double oldUnrolledHeight = Convert.ToDouble(currentFrame.UnrolledHeight?.ToString() ?? "0");
                    bool isRolled = currentFrame.IsRolled?.ToString().ToLower() == "true";

                    // Update Width and Height with the actual new values
                    currentFrame.Width = newWidth;
                    currentFrame.Height = newHeight;

                    // Handle UnrolledHeight update
                    if (!isRolled)
                    {

                        if (Math.Abs(newHeight - 28) > 5) // Only if height is significantly different from rolled-up height   //rolled height
                        {
                            double heightDifference = Math.Abs(newHeight - oldUnrolledHeight);
                            // DebugLog("LOGIC", frameId, $"Height difference from old UnrolledHeight: {heightDifference:F1}");
                            currentFrame.UnrolledHeight = newHeight;
                            // DebugLog("UPDATE", frameId, $"UPDATED UnrolledHeight from {oldUnrolledHeight:F1} to {newHeight:F1}");
                        }
                        else
                        {

                        }
                    }
                    else
                    {

                    }
                    // Save to JSON
                    //   MessageBox.Show("Debug: SizeChanged handler called. Saving frame data.");
                    FrameDataManager.SaveFrameData();
                    var verifyFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    double verifyHeight = Convert.ToDouble(verifyFrame.Height?.ToString() ?? "0");
                    double verifyUnrolled = Convert.ToDouble(verifyFrame.UnrolledHeight?.ToString() ?? "0");

                }
                else
                {

                }
            };
            win.KeyDown += (sender, e) =>
            {
                if (IconDragDropManager.IsDragging && e.Key == Key.Escape)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Escape pressed during drag, cancelling operation");
                    IconDragDropManager.CancelDrag();
                    e.Handled = true;
                }


            };

            // --- NEW: Auto-Hide Interaction Hook ---
            win.PreviewMouseDown += (s, e) => { if (SettingsManager.AutoResetHideTimer) ResetAutoHideTimer(); };
            win.PreviewMouseWheel += (s, e) => { if (SettingsManager.AutoResetHideTimer) ResetAutoHideTimer(); };
        }

        // 滑鼠移入移出：Ctrl 圖示覆蓋層（原生鍵盤事件）、自動捲起計時器、移入展開與移出重新計時
        private static void BindHoverAndAutoRoll(FrameBuildContext ctx, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            // --- NEW: Smart CTRL Overlay Engine (Event-Driven, Zero Polling) ---
            bool isOverlayActive = false;

            void ToggleOverlays(bool turnOn)
            {
                if (isOverlayActive == turnOn) return; // Prevent redundant drawing
                isOverlayActive = turnOn;

                var borderNode = win.Content as Border;
                var dockNode = borderNode?.Child as DockPanel;
                var scrollNode = dockNode?.Children.OfType<ScrollViewer>().FirstOrDefault();
                var wpNode = scrollNode?.Content as WrapPanel;

                if (wpNode == null) return;

                foreach (StackPanel itemPanel in wpNode.Children.OfType<StackPanel>())
                {
                    dynamic tagData = itemPanel.Tag;
                    if (tagData == null) continue;

                    string fp = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();

                    if (turnOn)
                    {
                        if (fp != null && fp.StartsWith("INTERNAL_BLANK_"))
                        {
                            // Spacer Overlay: Faint White/Gray
                            itemPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
                        }
                        else
                        {
                            // Real Icon Overlay: Faint Blue
                            itemPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 100, 150, 255));
                        }
                    }
                    else
                    {
                        // Revert to transparent (but still hit-testable)
                        itemPanel.Background = System.Windows.Media.Brushes.Transparent;
                    }
                }
            }

            // --- BUG FIX: Native Keyboard Hooks (Replaces the 50ms Timer) ---
            if (frame.ItemsType?.ToString() != "Portal")
            {
                win.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                    {
                        if (win.IsMouseOver) ToggleOverlays(true);
                    }
                };

                win.PreviewKeyUp += (s, e) =>
                {
                    if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                    {
                        ToggleOverlays(false);
                    }
                };
            }
            // ----------------------------------------------------------------

            // --- NEW: Setup Auto Roll Timer ---
            string frameIdForTimer = frame.Id?.ToString() ?? win.Tag?.ToString();
            if (!string.IsNullOrEmpty(frameIdForTimer))
            {
                // Pull from options if it exists, otherwise strictly default to 2 seconds (hidden setting)
                int autoRollDelay = 2000;
                try { if (SettingsManager.AutoRollTime > 0) autoRollDelay = SettingsManager.AutoRollTime * 1000; } catch { }

                var autoRollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoRollDelay) };
                _autoRollTimers[frameIdForTimer] = autoRollTimer;

                autoRollTimer.Tick += (s, e) =>
                {
                    autoRollTimer.Stop();

                    // --- BUG FIX: Prevent rolling up if the mouse is currently resting on the frame ---
                    if (win.IsMouseOver)
                    {
                        // We just abort. The MouseLeave event will restart the timer when they finally move away.
                        return;
                    }

                    var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameIdForTimer);
                    if (currentFrame == null) return;
                    bool isManuallyRolled = currentFrame.IsRolled?.ToString().ToLower() == "true";

                    // Don't auto-roll if it's already rolled manually, or caught in transition
                    if (isManuallyRolled || _framesInTransition.Contains(frameIdForTimer)) return;

                    _autoRolledFrames.Add(frameIdForTimer);
                    _framesInTransition.Add(frameIdForTimer);

                    double currentHeight = win.Height;
                    double targetHeight = 28;

                    var heightAnimation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromSeconds(0.3))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                    };
                    heightAnimation.Completed += (animSender, animArgs) =>
                    {
                        var borderNode = win.Content as Border;
                        if (borderNode?.Child is DockPanel dockNode)
                        {
                            var scrollNode = dockNode.Children.OfType<ScrollViewer>().FirstOrDefault();
                            if (scrollNode?.Content is WrapPanel wpNode) wpNode.Visibility = Visibility.Collapsed;
                            else if (dockNode.Children.OfType<TextBox>().FirstOrDefault() is TextBox tbNode) tbNode.Visibility = Visibility.Collapsed;

                            // --- NEW: Plugin Pause & Hide ---
                            if (_activePlugins.TryGetValue(frameIdForTimer, out var plugin))
                            {
                                plugin.Pause();
                                foreach (UIElement child in dockNode.Children)
                                {
                                    if (child is FrameworkElement fe && fe.Name == "FrameMenuIcon") continue;
                                    if (DockPanel.GetDock(child) == Dock.Top) continue;
                                    child.Visibility = Visibility.Collapsed;
                                }
                            }
                        }
                        _framesInTransition.Remove(frameIdForTimer);
                        win.BeginAnimation(Window.HeightProperty, null);
                        win.Height = targetHeight;
                    };
                    win.BeginAnimation(Window.HeightProperty, heightAnimation);
                };

                // --- BUG FIX: Start timer immediately on startup if Auto Roll is enabled ---
                var currentFrameSetup = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameIdForTimer);
                if (currentFrameSetup != null)
                {
                    bool isAutoRollEnabled = currentFrameSetup.AutoRoll?.ToString().ToLower() == "true";
                    bool isManuallyRolled = currentFrameSetup.IsRolled?.ToString().ToLower() == "true";

                    if (isAutoRollEnabled && !isManuallyRolled)
                    {
                        autoRollTimer.Start();
                    }
                }
            }
            // ----------------------------------

            win.MouseEnter += (s, e) =>
            {
                if (SettingsManager.AutoResetHideTimer) ResetAutoHideTimer();

                // --- NEW: Auto Roll Wake Up ---
                if (!string.IsNullOrEmpty(frameIdForTimer))
                {
                    if (_autoRollTimers.ContainsKey(frameIdForTimer)) _autoRollTimers[frameIdForTimer].Stop();

                    if (_autoRolledFrames.Contains(frameIdForTimer))
                    {
                        _autoRolledFrames.Remove(frameIdForTimer);
                        _framesInTransition.Add(frameIdForTimer);

                        var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameIdForTimer);
                        double unrolledHeight = currentFrame != null ? Convert.ToDouble(currentFrame.UnrolledHeight?.ToString() ?? "130") : 130;

                        // --- UX 優化：展開動畫開始時立即恢復可見，配合邊界遮罩呈現絲滑拉開抽屜的效果 ---
                        var borderNodePre = win.Content as Border;
                        if (borderNodePre?.Child is DockPanel dockNodePre)
                        {
                            var scrollNodePre = dockNodePre.Children.OfType<ScrollViewer>().FirstOrDefault();
                            if (scrollNodePre?.Content is WrapPanel wpNodePre)
                            {
                                wpNodePre.Visibility = Visibility.Visible;
                            }
                            else if (dockNodePre.Children.OfType<TextBox>().FirstOrDefault() is TextBox tbNodePre)
                            {
                                tbNodePre.Visibility = Visibility.Visible;
                            }
                        }

                        var heightAnimation = new DoubleAnimation(win.Height, unrolledHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            var borderNode = win.Content as Border;
                            if (borderNode?.Child is DockPanel dockNode)
                            {
                                var scrollNode = dockNode.Children.OfType<ScrollViewer>().FirstOrDefault();
                                if (scrollNode?.Content is WrapPanel wpNode)
                                {
                                    wpNode.Visibility = Visibility.Visible;

                                    // --- MULTI-MONITOR INVISIBLE ICON FIX ---
                                    wpNode.UpdateLayout();
                                    wpNode.InvalidateVisual();
                                    wpNode.Opacity = 0.99;
                                    wpNode.Dispatcher.BeginInvoke(new Action(() => { wpNode.Opacity = 1.0; }), System.Windows.Threading.DispatcherPriority.Render);
                                }
                                else if (dockNode.Children.OfType<TextBox>().FirstOrDefault() is TextBox tbNode)
                                {
                                    tbNode.Visibility = Visibility.Visible;
                                }

                                // --- NEW: Plugin Resume & Show ---
                                if (_activePlugins.TryGetValue(frameIdForTimer, out var plugin))
                                {
                                    foreach (UIElement child in dockNode.Children)
                                    {
                                        if (child is FrameworkElement fe && fe.Name == "FrameMenuIcon") continue;
                                        if (DockPanel.GetDock(child) == Dock.Top) continue;
                                        child.Visibility = Visibility.Visible;
                                    }
                                    plugin.Resume();
                                }
                            }
                            _framesInTransition.Remove(frameIdForTimer);
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = unrolledHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                    }
                }
                // ------------------------------

                // --- BUG FIX: Event-Driven Overlay Trigger (Zero Polling) ---
                if (frame.ItemsType?.ToString() != "Portal")
                {
                    bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    if (isCtrl) ToggleOverlays(true);
                }
            };

            win.MouseLeave += (s, e) =>
            {
                if (frame.ItemsType?.ToString() != "Portal")
                {
                    ToggleOverlays(false); // Instantly hide if mouse leaves the frame
                }

                // --- NEW: Auto Roll Trigger ---
                if (!string.IsNullOrEmpty(frameIdForTimer))
                {
                    var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameIdForTimer);
                    if (currentFrame != null)
                    {
                        bool isAutoRollEnabled = currentFrame.AutoRoll?.ToString().ToLower() == "true";
                        bool isManuallyRolled = currentFrame.IsRolled?.ToString().ToLower() == "true";

                        // If Auto Roll is ON, it's not manually rolled up, and not already auto-rolled
                        if (isAutoRollEnabled && !isManuallyRolled && !_autoRolledFrames.Contains(frameIdForTimer))
                        {
                            if (_autoRollTimers.TryGetValue(frameIdForTimer, out var timer))
                            {
                                timer.Stop();
                                timer.Start();
                            }
                        }
                    }
                }
                // ------------------------------
            };
        }

        // 顯示視窗並掛上視窗訊息（限制最大化尺寸）與最大化狀態修正
        private static void ShowWindowWithSizeHooks(NonActivatingWindow win)
        {
            // Make window focusable for key events during drag
            win.Focusable = true;
            win.Show();


            // --- FIX START ---

            // 1. Install the Message Hook (Enforces the 90% size limit)
            win.SourceInitialized += (s, e) =>
            {
                var source = HwndSource.FromHwnd(new WindowInteropHelper(win).Handle);
                source?.AddHook(WndProc);
            };
            // (Safety: if already initialized)
            if (PresentationSource.FromVisual(win) is HwndSource existingSource)
            {
                existingSource.AddHook(WndProc);
            }

            // 2. Reactive State Fix (The "Trick")
            // If Snap Assist sets state to Maximized, we immediately force it back to Normal.
            // This ensures the Resize Grip (bottom-right) remains visible and functional.
            win.StateChanged += (sender, args) =>
            {
                if (win.WindowState == WindowState.Maximized)
                {
                    // We use Dispatcher to let the OS finish its "snap" calculation first, 
                    // then we immediately override the mode back to Normal.
                    win.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        win.WindowState = WindowState.Normal;

                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            "Snap Assist intercepted: Reverted Maximized state to Normal to preserve controls.");
                    }));
                }
            };
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
