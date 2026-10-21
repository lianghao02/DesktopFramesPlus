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

        // 資料夾面板的篩選列：標題列篩選圖示、下拉選單與套用按鈕；非資料夾面板回傳 null
        private static Grid BuildFilterBar(FrameBuildContext ctx, Grid titleGrid, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            // --- FILTER UI START ---
            // Only add Filter UI for Portal Frames
            Grid filterBar = null;
            if (frame.ItemsType?.ToString() == "Portal")
            {
                // 1. The Filter Icon (Funnel)
                // 1. The Filter Icon (Funnel)
                string initialFilter = GetSafeProperty(frame, "FilterString");
                bool hasInitialFilter = !string.IsNullOrWhiteSpace(initialFilter);

                TextBlock filterIcon = new TextBlock
                {
                    Name = "FrameFilterIcon", // New! Name
                    Text = "❖",
                    FontSize = 18,
                    Foreground = hasInitialFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = Strings.TooltipFilterFiles,
                    Opacity = hasInitialFilter ? 1.0 : Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100)
                };

                filterIcon.MouseEnter += (s, e) =>
                {
                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = 1.0;
                };

                filterIcon.MouseLeave += (s, e) =>
                {
                    // Skip fade out if a filter is actively applied
                    if (filterIcon.Foreground != System.Windows.Media.Brushes.Orange)
                    {
                        double targetOpacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100);
                        DoubleAnimation fadeBack = new DoubleAnimation
                        {
                            From = 1.0,
                            To = targetOpacity,
                            Duration = TimeSpan.FromMilliseconds(300),
                            BeginTime = TimeSpan.FromMilliseconds(800)
                        };
                        filterIcon.BeginAnimation(UIElement.OpacityProperty, fadeBack);
                    }
                };

                Grid.SetColumn(filterIcon, 2);
                titleGrid.Children.Add(filterIcon);


                // 2. The Filter Bar (Stable Version)
                filterBar = new Grid
                {
                    Height = 26,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                ComboBox cmbFilter = new ComboBox
                {
                    IsEditable = true,
                    Height = 24,
                    StaysOpenOnEdit = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(5, 0, 5, 0),
                    Text = GetSafeProperty(frame, "FilterString")
                };

                // Helper: Only Repopulate when needed (prevents freeze loop)
                Action repopulateDropdown = () =>
                {
                    cmbFilter.Items.Clear();

                    // A. History
                    try
                    {
                        string currentId = frame.Id?.ToString();
                        var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == currentId);
                        if (liveFrame != null)
                        {
                            Newtonsoft.Json.Linq.JArray history = null;
                            if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
                                history = jFrame["FilterHistory"] as Newtonsoft.Json.Linq.JArray;
                            else
                                history = liveFrame.GetType().GetProperty("FilterHistory")?.GetValue(liveFrame) as Newtonsoft.Json.Linq.JArray;

                            if (history != null)
                            {
                                foreach (var item in history)
                                {
                                    cmbFilter.Items.Add(new ComboBoxItem
                                    {
                                        Content = item.ToString(),
                                        Tag = item.ToString(),
                                        FontWeight = FontWeights.Bold
                                    });
                                }
                            }
                        }
                    }
                    catch { }

                    // B. Separator (if needed)
                    if (cmbFilter.Items.Count > 0) cmbFilter.Items.Add(new Separator());

                    // C. Standard Presets (From static dictionary)
                    foreach (var kvp in _standardPresets)
                    {
                        cmbFilter.Items.Add(new ComboBoxItem { Content = kvp.Key, Tag = kvp.Value });
                    }
                };

                // FIX 1: Only populate on OPEN, never during selection/typing
                cmbFilter.DropDownOpened += (s, e) => repopulateDropdown();

                // Clear Button
                Button btnClearFilter = new Button
                {
                    Content = "✕",
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Width = 20,
                    ToolTip = Strings.TooltipClearFilter
                };

                filterBar.Children.Add(cmbFilter);
                filterBar.Children.Add(btnClearFilter);
                Grid.SetColumn(btnClearFilter, 1);

                // --- LOGIC SETUP ---

                // Helper to execute/save
                Action<string> commitFilter = (text) =>
                {
                    bool hasFilter = !string.IsNullOrWhiteSpace(text);
                    filterIcon.Foreground = hasFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White;

                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = hasFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100);

                    if (_portalFrames.ContainsKey(frame))
                        _portalFrames[frame].ApplyFilter(text);

                    UpdateFrameProperty(frame, "FilterString", text, "Updated filter");

                    // Update history (logic now handles ignoring presets)
                    UpdateFilterHistory(frame, text);
                };

                // Typing Timer
                System.Windows.Threading.DispatcherTimer debounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                debounceTimer.Tick += (s, e) =>
                {
                    debounceTimer.Stop();
                    // Visual update only while typing
                    if (_portalFrames.ContainsKey(frame)) _portalFrames[frame].ApplyFilter(cmbFilter.Text);

                    bool hasFilter = !string.IsNullOrWhiteSpace(cmbFilter.Text);
                    filterIcon.Foreground = hasFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White;

                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = hasFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100);
                };

                // 1. STYLE FIX (Internal TextBox)
                cmbFilter.Loaded += (s, e) =>
                {
                    var textBox = (TextBox)cmbFilter.Template.FindName("PART_EditableTextBox", cmbFilter);
                    if (textBox != null)
                    {
                        textBox.Background = System.Windows.Media.Brushes.Transparent;
                        textBox.Foreground = System.Windows.Media.Brushes.White;
                        textBox.CaretBrush = System.Windows.Media.Brushes.White;
                        textBox.BorderThickness = new Thickness(0);
                    }
                };

                // 2. TYPING LOGIC
                cmbFilter.KeyUp += (s, e) =>
                {
                    if (e.Key == Key.Up || e.Key == Key.Down) return;

                    if (e.Key == Key.Enter)
                    {
                        commitFilter(cmbFilter.Text); // Save history on Enter
                        cmbFilter.IsDropDownOpen = false;
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                        e.Handled = true;
                        return;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                        e.Handled = true;
                        return;
                    }

                    debounceTimer.Stop();
                    debounceTimer.Start();
                };

                // 3. SELECTION LOGIC (FIXED)
                // We do NOT repopulate here. We just apply the value.
                cmbFilter.SelectionChanged += (s, e) =>
                {
                    if (cmbFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
                    {
                        string selectedFilter = item.Tag.ToString();

                        // Break the event loop by running this later
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            cmbFilter.IsDropDownOpen = false;

                            // Important: Don't set SelectedItem = null here, it causes flickering.
                            // Just force the text and run logic.
                            cmbFilter.Text = selectedFilter;

                            debounceTimer.Stop();
                            commitFilter(selectedFilter);
                        }));
                    }
                };

                // 4. CLEAR LOGIC
                btnClearFilter.Click += (s, e) =>
                {
                    cmbFilter.Text = "";
                    if (_portalFrames.ContainsKey(frame)) _portalFrames[frame].ApplyFilter("");
                    UpdateFrameProperty(frame, "FilterString", "", "Cleared filter");
                    filterIcon.Foreground = System.Windows.Media.Brushes.White;
                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = (double)SettingsManager.MenuTintValue / 100;
                    filterBar.Visibility = Visibility.Collapsed;
                    win.EndKeyboardInteractiveEdit();
                };

                // 5. TOGGLE BAR
                filterIcon.MouseLeftButtonDown += (s, e) =>
                {
                    if (filterBar.Visibility == Visibility.Visible)
                    {
                        if (!string.IsNullOrWhiteSpace(cmbFilter.Text)) commitFilter(cmbFilter.Text); // Save on close
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                    }
                    else
                    {
                        filterBar.Visibility = Visibility.Visible;
                        win.BeginKeyboardInteractiveEdit(cmbFilter);
                    }
                    e.Handled = true;
                };

                // Initial Apply
                if (!string.IsNullOrEmpty(cmbFilter.Text))
                {
                    filterIcon.Foreground = System.Windows.Media.Brushes.Orange;
                    filterIcon.Opacity = 1.0; // Ensure it starts fully visible if active

                    win.Loaded += (s, e) =>
                    {
                        if (_portalFrames.ContainsKey(frame))
                            _portalFrames[frame].ApplyFilter(cmbFilter.Text);
                    };
                }
            }
            // --- FILTER UI END ---
            return filterBar;
        }

        // 重新命名：定義提交動作（寫入面板資料與視窗標題），輸入框 Enter 保存、Esc 取消、失去焦點保存
        private static void BindRenameEvents(FrameBuildContext ctx, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            // --- STEP 4 FIX: Centralized Rename Logic ---

            // 1. Define the Logic ONCE
            ctx.CommitRename = () =>
            {
                // Only run if actually editing
                if (titletb.Visibility != Visibility.Visible) return;

                string originalTitle = frame.Title.ToString();
                string newTitle = titletb.Text;
                string finalTitle = InterCore.ProcessTitleChange(frame, newTitle, originalTitle);

                // Update Data
                string id = frame.Id?.ToString();
                var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);

                if (liveFrame != null)
                {
                    if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
                        jFrame["Title"] = finalTitle;
                    else
                        liveFrame.Title = finalTitle;
                    frame.Title = finalTitle;
                }

                // Update UI
                ctx.TitleLabel.Content = finalTitle;
                win.Title = finalTitle;
                titletb.Visibility = Visibility.Collapsed;
                ctx.TitleLabel.Visibility = Visibility.Visible;

                // Save
                FrameDataManager.SaveFrameData();
                win.EndKeyboardInteractiveEdit();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename committed: {finalTitle}");
            };

            // 2. Wire up Events to use the central logic
            titletb.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ctx.CommitRename(); // Call shared logic
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Cancel Logic
                    titletb.Text = frame.Title.ToString();
                    titletb.Visibility = Visibility.Collapsed;
                    ctx.TitleLabel.Visibility = Visibility.Visible;
                    win.EndKeyboardInteractiveEdit();
                    e.Handled = true;
                }
            };

            titletb.LostFocus += (sender, e) =>
            {
                ctx.CommitRename(); // Call shared logic
            };
        }

        // 標題列滑鼠：雙擊捲起／展開、單擊放開進入改名、拖曳移動面板（鎖定時不移動）
        private static void BindTitleMouseEvents(FrameBuildContext ctx, Grid titleGrid, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            System.Windows.Point? titleMouseDownPoint = null;
            bool titleIsDraggingWindow = false;

            ctx.TitleLabel.MouseDown += (sender, e) =>
            {
                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    ctx.CommitRename?.Invoke();
                    return;
                }

                if (e.ClickCount == 2)
                {
                    titleMouseDownPoint = null;
                    titleIsDraggingWindow = false;
                    // Roll-up/roll-down logic (swapped from Ctrl+Click)
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(ctx.TitleLabel);
                    string frameId = win?.Tag?.ToString();
                    // DebugLog("IMMEDIATE", frameId ?? "UNKNOWN", $"Ctrl+Click FIRST LINE - win.Height:{win?.Height ?? -1:F1}");
                    if (string.IsNullOrEmpty(frameId) || win == null)
                    {
                        // DebugLog("ERROR", frameId ?? "UNKNOWN", "Missing window or frameId in Ctrl+Click");
                        return;
                    }
              
                    dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    if (currentFrame == null)
                    {
                        // DebugLog("ERROR", frameId, "Frame not found in FrameDataManager.FrameData for Ctrl+Click");
                        return;
                    }
                    bool isRolled = currentFrame.IsRolled?.ToString().ToLower() == "true";
                    // Get frame data height (always accurate from SizeChanged handler)
                    double frameHeight = Convert.ToDouble(currentFrame.Height?.ToString() ?? "130");
                    double windowHeight = win.Height;
                    // DebugLog("SYNC_CHECK", frameId, $"Before sync - frameHeight:{frameHeight:F1} | WindowHeight:{windowHeight:F1} | IsRolled:{isRolled}");
                    if (!isRolled)
                    {
                        // ROLLUP: Use frame data height (always current)
                        // DebugLog("ACTION", frameId, "Starting ROLLUP");
                        _framesInTransition.Add(frameId);
                        // DebugLog("TRANSITION", frameId, "Added to transition state");
                        // FINAL FIX: Use frame data height which is always accurate from SizeChanged handler
                        double currentHeight = frameHeight;

                        // --- BUG FIX: The "Stuck at 28" Safeguard ---
                        // If currentHeight is suspiciously small (like the title bar height), it means the 
                        // WPF layout data is stale. We reject it and use the last known good UnrolledHeight.
                        if (currentHeight <= 35)
                        {
                            currentHeight = Convert.ToDouble(currentFrame.UnrolledHeight?.ToString() ?? "130");
                            if (currentHeight <= 35) currentHeight = 130; // Ultimate fallback
                        }

                        // DebugLog("ROLLUP_HEIGHT_SOURCE", frameId, $"Using frame.Height:{frameHeight:F1} (win.Height was stale:{win.Height:F1})");
                        // Save current frame height as UnrolledHeight
                        IDictionary<string, object> frameDict = currentFrame as IDictionary<string, object> ?? ((JObject)currentFrame).ToObject<IDictionary<string, object>>();
                        frameDict["UnrolledHeight"] = currentHeight;
                        frameDict["IsRolled"] = "true";
                        int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                        if (frameIndex >= 0)
                        {
                            FrameDataManager.FrameData[frameIndex] = JObject.FromObject(frameDict);
                        }
                        FrameDataManager.SaveFrameData();
                        // DebugLog("SAVE", frameId, $"Saved ROLLUP state | UnrolledHeight:{currentHeight:F1} | IsRolled:true");
                        // Roll up animation - starts from current height
                        double targetHeight = 28;   //rolled height
                        var heightAnimation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            // DebugLog("ANIMATION", frameId, "ROLLUP animation completed");
                            // Update WrapPanel visibility
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
                                            wpcont.Visibility = Visibility.Collapsed;
                                            // DebugLog("UI", frameId, "Set WrapPanel visibility to Collapsed");
                                        }
                                    }

                                    // --- NEW: Plugin Pause & Hide ---
                                    if (_activePlugins.TryGetValue(frameId, out var plugin))
                                    {
                                        plugin.Pause();
                                        foreach (UIElement child in dockPanel.Children)
                                        {
                                            if (child is FrameworkElement fe && fe.Name == "FrameMenuIcon") continue;
                                            if (DockPanel.GetDock(child) == Dock.Top) continue;
                                            child.Visibility = Visibility.Collapsed;
                                        }
                                    }
                                }
                            }
                            _framesInTransition.Remove(frameId);

                            // FIX: Release animation lock so manual resizes track correctly later
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = targetHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                        // DebugLog("ANIMATION", frameId, $"Started ROLLUP animation from {currentHeight:F1} to height {targetHeight:F1}");
                    }
                    else
                    {
                        // ROLLDOWN: Roll down to UnrolledHeight
                        double unrolledHeight = Convert.ToDouble(currentFrame.UnrolledHeight?.ToString() ?? "130");
                        // // DebugLog("ACTION", frameId, $"Starting ROLLDOWN to {unrolledHeight:F1}");
                        _framesInTransition.Add(frameId);
                        // DebugLog("TRANSITION", frameId, "Added to transition state");
                        IDictionary<string, object> frameDict = currentFrame as IDictionary<string, object> ?? ((JObject)currentFrame).ToObject<IDictionary<string, object>>();
                        frameDict["IsRolled"] = "false";
                        int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                        if (frameIndex >= 0)
                        {
                            FrameDataManager.FrameData[frameIndex] = JObject.FromObject(frameDict);
                        }
                        FrameDataManager.SaveFrameData();
                        // DebugLog("SAVE", frameId, $"Saved ROLLDOWN state | IsRolled:false | TargetHeight:{unrolledHeight:F1}");
                        var heightAnimation = new DoubleAnimation(win.Height, unrolledHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            // DebugLog("ANIMATION", frameId, "ROLLDOWN animation completed");
                            // Update WrapPanel visibility
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
                                            wpcont.Visibility = Visibility.Visible;

                                            // --- MULTI-MONITOR INVISIBLE ICON FIX ---
                                            wpcont.UpdateLayout();
                                            wpcont.InvalidateVisual();
                                            wpcont.Opacity = 0.99;
                                            wpcont.Dispatcher.BeginInvoke(new Action(() => { wpcont.Opacity = 1.0; }), System.Windows.Threading.DispatcherPriority.Render);
                                            // DebugLog("UI", frameId, "Set WrapPanel visibility to Visible");
                                        }
                                    }

                                    // --- NEW: Plugin Resume & Show ---
                                    if (_activePlugins.TryGetValue(frameId, out var plugin))
                                    {
                                        foreach (UIElement child in dockPanel.Children)
                                        {
                                            if (child is FrameworkElement fe && fe.Name == "FrameMenuIcon") continue;
                                            if (DockPanel.GetDock(child) == Dock.Top) continue;
                                            child.Visibility = Visibility.Visible;
                                        }
                                        plugin.Resume();
                                    }
                                }
                            }
                            _framesInTransition.Remove(frameId);
                            // FIX: Release animation lock and force real height so SizeChanged updates the JSON properly
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = unrolledHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                        // DebugLog("ANIMATION", frameId, $"Started ROLLDOWN animation to height {unrolledHeight:F1}");
                    }
                    e.Handled = true;
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        titleMouseDownPoint = null;
                        titleIsDraggingWindow = false;
                        titletb.Text = ctx.TitleLabel.Content.ToString();
                        ctx.TitleLabel.Visibility = Visibility.Collapsed;
                        titletb.Visibility = Visibility.Visible;
                        win.BeginKeyboardInteractiveEdit(titletb);
                        titletb.SelectAll();
                        titletb.Focus();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Focus set to title textbox for frame: {frame.Title}");
                        e.Handled = true;
                    }
                    else
                    {
                        titleMouseDownPoint = e.GetPosition(win);
                        titleIsDraggingWindow = false;
                    }
                }
            };

            ctx.TitleLabel.MouseMove += (sender, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && titleMouseDownPoint.HasValue && !titleIsDraggingWindow)
                {
                    System.Windows.Point curPoint = e.GetPosition(win);
                    if (Math.Abs(curPoint.X - titleMouseDownPoint.Value.X) > 4 || Math.Abs(curPoint.Y - titleMouseDownPoint.Value.Y) > 4)
                    {
                        titleIsDraggingWindow = true;
                        string frameId = win.Tag?.ToString();
                        dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                        bool isLockedFrame = currentFrame?.IsLocked?.ToString().ToLower() == "true";
                        if (!isLockedFrame)
                        {
                            SnapManager.StartDrag(win);
                            try
                            {
                                win.DragMove();
                            }
                            finally
                            {
                                SnapManager.EndDrag(win);
                            }
                        }
                    }
                }
            };

            ctx.TitleLabel.MouseLeftButtonUp += (sender, e) =>
            {
                if (titleMouseDownPoint.HasValue && !titleIsDraggingWindow && !titletb.IsVisible)
                {
                    titletb.Text = ctx.TitleLabel.Content.ToString();
                    ctx.TitleLabel.Visibility = Visibility.Collapsed;
                    titletb.Visibility = Visibility.Visible;
                    win.BeginKeyboardInteractiveEdit(titletb);
                    titletb.SelectAll();
                    titletb.Focus();
                    e.Handled = true;
                }
                titleMouseDownPoint = null;
                titleIsDraggingWindow = false;
            };

            titleGrid.MouseDown += (sender, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && (e.OriginalSource == titleGrid || e.OriginalSource is Border))
                {
                    string frameId = win.Tag?.ToString();
                    dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    bool isLockedFrame = currentFrame?.IsLocked?.ToString().ToLower() == "true";
                    if (!isLockedFrame)
                    {
                        SnapManager.StartDrag(win);
                        try
                        {
                            win.DragMove();
                        }
                        finally
                        {
                            SnapManager.EndDrag(win);
                        }
                    }
                }
            };
        }

        // 圖示區：WrapPanel 與捲動容器、自適應欄寬、Ctrl+滾輪調整圖示大小、資料夾面板浮水印、點空白處取消選取
        private static (WrapPanel wpcont, ScrollViewer wpcontscr) BuildIconArea(FrameBuildContext ctx, DockPanel dp, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            WrapPanel wpcont = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = double.NaN,
                ItemHeight = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 4, 6, 6),
                ClipToBounds = true
            };

            // CRITICAL: Tag the panel with the frame ID so AddIcon can find settings
            // even before the panel is attached to the window.
            wpcont.Tag = frame.Id?.ToString();
            // --- FIX END ---

            ScrollViewer wpcontscr = new ScrollViewer
            {
                Content = wpcont,
                VerticalScrollBarVisibility = SettingsManager.DisableFrameScrollbars ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                ClipToBounds = true
            };

            wpcont.SizeChanged += (s, e) =>
            {
                if (e.WidthChanged) UpdateAdaptiveIconGrid(wpcont);
                wpcont.InvalidateVisual();
            };
            wpcontscr.SizeChanged += (s, e) =>
            {
                if (e.WidthChanged) UpdateAdaptiveIconGrid(wpcont);
                wpcont.InvalidateVisual();
            };
            wpcontscr.PreviewMouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    try
                    {
                        string id = frame.Id?.ToString() ?? win.Tag?.ToString();
                        var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id) ?? frame;
                        string itemsType = GetSafeProperty(liveFrame, "ItemsType");
                        if (itemsType == "Data" || itemsType == "Portal")
                        {
                            string[] sizes = { "Tiny", "Small", "Medium", "Large", "Huge" };
                            string curSize = GetSafeProperty(liveFrame, "IconSize");
                            if (string.IsNullOrEmpty(curSize)) curSize = "Medium";
                            int idx = Array.FindIndex(sizes, sz => string.Equals(sz, curSize, StringComparison.OrdinalIgnoreCase));
                            if (idx < 0) idx = 2;

                            int nextIdx = e.Delta > 0
                                ? Math.Min(sizes.Length - 1, idx + 1)
                                : Math.Max(0, idx - 1);

                            e.Handled = true;
                            if (nextIdx != idx)
                            {
                                SetFrameIconSizeLive(win, liveFrame, sizes[nextIdx]);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Ctrl+Wheel icon zoom error: {ex.Message}");
                    }
                }
            };

            // --- PERFORMANCE TWEAK: Hardware Acceleration & Safe UI Culling ---
            IconManager.OptimizeFramePanel(wpcont, wpcontscr, frame.ItemsType?.ToString() != "Data");

            // 專用收納柵欄不顯示傳送門浮水印背景圖；舊版 Portal 依設定決定
            if (frame.ItemsType?.ToString() == "Portal" && !Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
            {
                if (_options.ShowBackgroundImageOnPortalFences ?? false)
                {
                    double opacity = (SettingsManager.PortalBackgroundOpacity / 100.0);
                    wpcontscr.Background = new ImageBrush
                    {
                        ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/portal.png")),
                        Opacity = opacity,
                        Stretch = Stretch.UniformToFill
                    };
                }
            }
            dp.Children.Add(wpcontscr);

            // 點擊 WrapPanel 空白區域（未命中圖示）→ 取消選取
            wpcont.MouseLeftButtonDown += (s, e) =>
            {
                // 若點到的是 WrapPanel 本身（非子圖示），取消選取
                if (e.OriginalSource == wpcont)
                    DeselectIcon();
            };
            return (wpcont, wpcontscr);
        }

        // 載入面板內容（便箋、外掛、資料夾、捷徑項目）。原為 CreateFrame 的區域函式，連同呼叫一起搬入，呼叫時機不變
        private static void InitFrameContent(FrameBuildContext ctx, DockPanel dp, NonActivatingWindow win, WrapPanel wpcont, ScrollViewer wpcontscr)
        {
            dynamic frame = ctx.Frame;
            TargetChecker targetChecker = ctx.TargetChecker;
            void InitContent()
            {
                // 1. Handle Note frames - they don't use WrapPanel
                if (frame.ItemsType?.ToString() == "Note")
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Creating Note frame content for '{frame.Title}'");
                    dp.Children.Remove(wpcontscr); // Remove ScrollViewer
                    TextBox noteTextBox = NoteFramemanager.CreateNoteContent(frame, dp);

                    bool isNoteRolled = frame.IsRolled?.ToString().ToLower() == "true";
                    noteTextBox.Visibility = isNoteRolled ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }

                // --- NEW: Handle Plugin frames ---
                if (frame.ItemsType?.ToString() == "Plugin")
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Creating Plugin frame content for '{frame.Title}'");
                    dp.Children.Remove(wpcontscr); // Remove default Icon ScrollViewer

                    string pluginId = frame.PluginId?.ToString();
                    var pluginInstance = PluginManager.CreatePluginInstance(pluginId);

                    bool isPluginRolled = frame.IsRolled?.ToString().ToLower() == "true";

                    if (pluginInstance != null)
                    {
                        var visualElement = pluginInstance.CreateVisualElement();
                        if (visualElement != null)
                        {
                            visualElement.Visibility = isPluginRolled ? Visibility.Collapsed : Visibility.Visible;
                            dp.Children.Add(visualElement); // Inject plugin UI into frame

                            // --- CLEAN OPACITY FIX: Animate the Window, not the Element ---
                            // By fading the Window, the image fades to the desktop cleanly
                            // without the frame's background color bleeding through it.
                            if (SettingsManager.ApplyTintToIcons)
                            {
                              

                                double targetOpacity = Math.Max(0.2, SettingsManager.TintValue / 100.0);
                                win.Opacity = targetOpacity;

                                win.MouseEnter += (s, ev) =>
                                {
                                    win.BeginAnimation(Window.OpacityProperty, null);
                                    win.Opacity = 1.0;
                                };

                                win.MouseLeave += (s, ev) =>
                                {
                                    System.Windows.Media.Animation.DoubleAnimation fadeBack = new System.Windows.Media.Animation.DoubleAnimation
                                    {
                                        From = 1.0,
                                        To = targetOpacity,
                                        Duration = TimeSpan.FromMilliseconds(300),
                                        BeginTime = TimeSpan.FromMilliseconds(500)
                                    };
                                    win.BeginAnimation(Window.OpacityProperty, fadeBack);
                                };
                            }
                            // --------------------------------------------------------------

                            // Extract settings safely into Dictionary
                            Dictionary<string, object> pluginSettings = new Dictionary<string, object>();
                            try
                            {
                                if (frame is JObject jFrame && jFrame["PluginSettings"] is JObject settingsObj)
                                {
                                    pluginSettings = settingsObj.ToObject<Dictionary<string, object>>();
                                }
                                else if (frame.PluginSettings != null)
                                {
                                    var settingsDict = frame.PluginSettings as IDictionary<string, object>;
                                    if (settingsDict != null) pluginSettings = new Dictionary<string, object>(settingsDict);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation, $"Failed to parse PluginSettings: {ex.Message}");
                            }

                            // Track active plugin instance and initialize it
                            string fId = frame.Id?.ToString();
                            if (!string.IsNullOrEmpty(fId))
                            {
                                _activePlugins[fId] = pluginInstance;
                            }

                            pluginInstance.Initialize(visualElement, pluginSettings);
                            if (isPluginRolled) pluginInstance.Pause();
                        }
                    }
                    else
                    {
                        // Safe fallback UI if plugin is missing/unregistered
                        TextBlock errorText = new TextBlock
                        {
                            Text = $"Plugin '{pluginId}' missing or failed to load.",
                            Foreground = System.Windows.Media.Brushes.Red,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        };
                        errorText.Visibility = isPluginRolled ? Visibility.Collapsed : Visibility.Visible;
                        dp.Children.Add(errorText);
                    }
                    return;
                }
                // ---------------------------------

                // 2. Handle Data/Portal frames


                bool isRolled = frame.IsRolled?.ToString().ToLower() == "true";
                wpcont.Visibility = isRolled ? Visibility.Collapsed : Visibility.Visible;
                wpcont.Children.Clear();

                // 2a. Data frames (The complex logic)
                if (frame.ItemsType?.ToString() == "Data")
                {
                    JArray items = null;
                    bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                    // Load Items (Tabs vs Main)
                    if (tabsEnabled)
                    {
                        try
                        {
                            var tabs = frame.Tabs as JArray ?? new JArray();
                            int currentTab = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");
                            if (currentTab >= 0 && currentTab < tabs.Count)
                            {
                                var activeTab = tabs[currentTab] as JObject;
                                items = activeTab?["Items"] as JArray ?? new JArray();
                            }
                            else
                            {
                                items = frame.Items as JArray ?? new JArray();
                            }
                        }
                        catch { items = frame.Items as JArray ?? new JArray(); }
                    }
                    else
                    {
                        items = frame.Items as JArray ?? new JArray();
                    }

                    if (items != null)
                    {
                        // Sort
                        var sortedItems = items.OfType<JObject>()
                            .OrderBy(item => item["DisplayOrder"]?.Type == JTokenType.Integer ? item["DisplayOrder"].Value<int>() : 0)
                            .ToList();

                        foreach (dynamic icon in sortedItems)
                        {
                            // FIX: Pass 'frame' context for customization
                            AddIcon(icon, wpcont, frame);

                            StackPanel sp = wpcont.Children[wpcont.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                // --- 1. DEFINE VARIABLES (Scope Fix) ---
                                // We extract these immediately so they are available for ALL blocks below
                                IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                                    ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();

                                string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";

                                // CRITICAL: Define these here to avoid CS0103 errors
                                bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];
                                bool isLink = iconDict.ContainsKey("IsLink") && (bool)iconDict["IsLink"];

                                // --- FIX: MOVE NETWORK CHECK UP ---
                                bool isNetwork = iconDict.ContainsKey("IsNetwork") && (bool)iconDict["IsNetwork"];
                                if (!isNetwork && !string.IsNullOrEmpty(filePath)) isNetwork = IsNetworkPath(filePath);

                                //// --- 2. Extract Arguments ---
                                //string arguments = null;
                                //if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
                                //{
                                //    // FIX: Do NOT use WshShell on network links during startup to prevent 30-second hangs!
                                //    if (!isNetwork)
                                //    {
                                //        try
                                //        {
                                //            WshShell shell = new WshShell();
                                //            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                                //            arguments = shortcut.Arguments;
                                //        }
                                //        catch { }
                                //    }
                                //}

                                // --- 2. Extract Arguments ---
                                // Completely removed WshShell from main thread to prevent local .lnk files 
                                // from auto-resolving dead network targets during startup.
                                string arguments = iconDict.ContainsKey("Arguments") ? (string)iconDict["Arguments"] : null;

                                // --- 3. Attach Click Event ---
                                ClickEventAdder(sp, filePath, isFolder, arguments);

                                // [TARGET VALIDATION ENGINE - INIT CONTENT]
                                bool allowNetworkChecking = _options.CheckNetworkPaths ?? false;
                                bool isShortcutFile = filePath != null && filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

                                if (!isLink && (!isNetwork || allowNetworkChecking))
                                {
                                    string resolvedTarget = filePath;
                                    bool excludeFromCheck = false;

                                    if (isShortcutFile)
                                    {
                                        resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath) ?? filePath;

                                        if (resolvedTarget.StartsWith(@"\\") ||
                                            resolvedTarget.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
                                            Utility.IsStoreAppShortcut(filePath))
                                        {
                                            excludeFromCheck = true;
                                        }
                                    }

                                    if (!excludeFromCheck)
                                    {
                                        targetChecker.AddCheckAction(filePath, () => UpdateIcon(sp, filePath, isFolder, resolvedTarget), isFolder);
                                        if (isNetwork && allowNetworkChecking) LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Monitoring network path {filePath}");
                                    }
                                    else
                                    {
                                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Excluded shortcut {filePath} from background checking to protect UI thread/avoid exclusions.");
                                    }
                                }
                                else if (isLink || (isNetwork && !allowNetworkChecking))
                                {
                                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Safety Exclusion: Skipped background check for {filePath}");
                                }

                                // --- 5. Attach Context Menu ---
                                AttachIconContextMenu(sp, icon, frame, win);
                            }
                        }
                    }
                }
                // 2b. Portal frames
                else if (frame.ItemsType?.ToString() == "Portal")
                {
                    try
                    {
                        var portalManager = new PortalFramemanager(frame, wpcont);
                        _portalFrames[frame] = portalManager;

                        // --- BULLETPROOF FIX: Decoupled Sort Cycle on CTRL + Click ---
                        // We use the Window's PreviewMouseDown to fire BEFORE any UI is destroyed.
                        // We trace the Visual Tree to ensure the click hit empty space, not an icon.
                        win.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                            {
                                // --- STEP 6 FIX: Abort CTRL+Click background sorting completely if in Details/List View ---
                                if (portalManager.IsDetailsView) return;

                                DependencyObject parent = e.OriginalSource as DependencyObject;
                                bool isBackground = true;

                                while (parent != null && parent != win)
                                {
                                    // Hit an icon? Abort and let the folder navigate!
                                    if (parent is StackPanel sp && sp.Tag != null) { isBackground = false; break; }
                                    // Hit a UI Control (Button, Scrollbar, Text, Title Label)? Abort!
                                    if (parent is Button || parent is System.Windows.Controls.Primitives.ScrollBar || parent is TextBox || parent is Label || parent is TextBlock) { isBackground = false; break; }
                                    // Hit the Nav Bar? Abort!
                                    if (parent is Grid g && g.Tag?.ToString() == "PORTAL_NAV_BAR") { isBackground = false; break; }

                                    // --- FIX: Safely step out of non-Visual text elements (like 'Run') ---
                                    if (parent is FrameworkContentElement fce)
                                    {
                                        parent = fce.Parent;
                                    }
                                    else if (parent is Visual || parent is System.Windows.Media.Media3D.Visual3D)
                                    {
                                        parent = VisualTreeHelper.GetParent(parent);
                                    }
                                    else
                                    {
                                        parent = null;
                                    }
                                }

                                if (isBackground)
                                {
                                    string newMode = portalManager.CycleSortMode();
                                    ShowPortalToast(win, $"Sorted by: {newMode}");
                                    e.Handled = true; // Stop event from triggering anything else
                                }
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        TrayManager.Instance?.ShowFarmFenceWarning(Strings.Get("MsgPortalInitFailed", ex.Message));
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                            $"Portal 建立失敗，配置已保留：{frame.Title} / {frame.Path}: {ex.Message}");
                        win.Close();
                    }
                }
            }
            InitContent();
        }

        // 拖放：拖入時外框提示與手勢判斷、離開還原、放下時依面板類型搬移／複製／建立捷徑或網址
        private static void BindDragDrop(FrameBuildContext ctx, Border cborder, NonActivatingWindow win, WrapPanel wpcont)
        {
            dynamic frame = ctx.Frame;
            TargetChecker targetChecker = ctx.TargetChecker;
            System.Windows.Media.Brush defaultFrameBorderBrush = cborder.BorderBrush;
            win.DragOver += (sender, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                if (frame.ItemsType?.ToString() == "Portal")
                {
                    if (!Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
                    {
                        e.Effects = DragDropEffects.None;
                        cborder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 220, 80, 80));
                        e.Handled = true;
                        return;
                    }

                    // --- 專用收納柵欄 (Managed Storage Fence) ---
                    string[] portalDragFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
                    string commonDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                    bool isAllCommonDesktop = portalDragFiles != null && portalDragFiles.Length > 0 &&
                        portalDragFiles.All(f => !string.IsNullOrEmpty(commonDesktopDir) &&
                            System.IO.Path.GetFullPath(f).StartsWith(commonDesktopDir, StringComparison.OrdinalIgnoreCase));

                    string gestureHint = "";
                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                    {
                        e.Effects = DragDropEffects.Link;
                        gestureHint = "在收納面板建立捷徑";
                    }
                    else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        e.Effects = DragDropEffects.Copy;
                        gestureHint = "複製到收納面板";
                    }
                    else if (isAllCommonDesktop)
                    {
                        e.Effects = DragDropEffects.Copy;
                        gestureHint = "從公用桌面複製捷徑";
                    }
                    else
                    {
                        e.Effects = DragDropEffects.Move;
                        gestureHint = "搬進收納面板";
                    }

                    cborder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 0, 150, 255));
                    win.ToolTip = gestureHint;
                    e.Handled = true;
                    return;
                }

                string[] dragFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool isDesktopShortcutMove = false;
                if (dragFiles != null && dragFiles.Length > 0)
                {
                    string userDesk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    isDesktopShortcutMove = dragFiles.All(f =>
                    {
                        string ext = System.IO.Path.GetExtension(f);
                        string parent = System.IO.Path.GetDirectoryName(f);
                        return (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase)) &&
                               string.Equals(parent, userDesk, StringComparison.OrdinalIgnoreCase);
                    });
                }

                e.Effects = isDesktopShortcutMove ? DragDropEffects.Move : DragDropEffects.Link;
                cborder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 110, 185, 255));
                e.Handled = true;
            };

            win.DragLeave += (sender, e) =>
            {
                cborder.BorderBrush = defaultFrameBorderBrush;
                win.ToolTip = null;
            };

            win.Drop += (sender, e) =>
            {
                cborder.BorderBrush = defaultFrameBorderBrush;
                win.ToolTip = null;
                e.Handled = true;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] droppedFiles = null;
                    try
                    {
                        droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                            $"Drop handler received {droppedFiles?.Length ?? 0} files");
                    }
                    catch (Exception dataEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Error getting drop data: {dataEx.Message}");
                        return;
                    }
                    if (droppedFiles == null) return;
                    if (frame.ItemsType?.ToString() == "Portal")
                    {
                        if (!Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
                        {
                            e.Effects = DragDropEffects.None;
                            e.Handled = true;
                            TrayManager.Instance?.ShowFarmFenceWarning(Strings.Get("MsgPortalReadOnly"));
                            return;
                        }

                        // --- 專用收納柵欄 Drop 執行 ---
                        var gesture = Desktop_Frames.Services.StorageDropAction.Move;
                        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                        {
                            gesture = Desktop_Frames.Services.StorageDropAction.Link;
                        }
                        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            gesture = Desktop_Frames.Services.StorageDropAction.Copy;
                        }

                        int successCount = 0;
                        int noopCount = 0;
                        var failedItems = new System.Collections.Generic.List<string>();

                        foreach (string item in droppedFiles)
                        {
                            Desktop_Frames.Services.IngestResult res = Desktop_Frames.Services.StorageFenceService.IngestItem(frame, item, gesture);
                            if (res.Status == Desktop_Frames.Services.IngestResultStatus.Completed)
                            {
                                successCount++;
                            }
                            else if (res.Status == Desktop_Frames.Services.IngestResultStatus.SkippedSameFolder)
                            {
                                noopCount++;
                            }
                            else
                            {
                                failedItems.Add($"{System.IO.Path.GetFileName(item)}: {res.ErrorMessage}");
                            }
                        }

                        // 觸發該 Portal 的即時刷新與桌面 Shell 刷新
                        string fId = frame.Id?.ToString();
                        var portalEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == fId);
                        portalEntry.Value?.TriggerSync(immediate: true);
                        Desktop_Frames.Services.FenceInventoryManager.RefreshDesktopShell();

                        if (failedItems.Count > 0)
                        {
                            string alert = $"部分項目收納未完成：\n\n" + string.Join("\n", failedItems.Take(5));
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm(alert, "收納未完成");
                        }
                        return;
                    }

                    int portalMovedCount = 0;  // 追蹤成功移動的項目數量（收納）
                    int portalCopiedCount = 0; // 追蹤成功複製的項目數量

                    foreach (string droppedFile in droppedFiles)
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                            $"Processing dropped item: '{droppedFile}'");
                        try
                        {
                            // Path Validation (Existence / Folder Check)
                            bool fileExists = false;
                            bool directoryExists = false;
                            bool isFolderFlag = false;

                            try
                            {
                                FileAttributes attrs = System.IO.File.GetAttributes(droppedFile);
                                isFolderFlag = attrs.HasFlag(FileAttributes.Directory);
                                directoryExists = isFolderFlag;
                                fileExists = !isFolderFlag;
                            }
                            catch (Exception pathEx)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation, $"Path validation error: {pathEx.Message}");
                                continue;
                            }

                            if (!fileExists && !directoryExists)
                            {
                                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgInvalidFileOrFolder", droppedFile), Strings.DlgError);
                                continue;
                            }

                     
                            // --- DATA FRAME LOGIC ---
                            if (frame.ItemsType?.ToString() == "Data")
                            {
                                string targetFrameId = frame.Id?.ToString() ?? string.Empty;
                                string userDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                                string fullDroppedPath = System.IO.Path.GetFullPath(droppedFile);
                                bool isFromUserDesktop = !string.IsNullOrEmpty(userDesktopDir) &&
                                                         fullDroppedPath.StartsWith(userDesktopDir, StringComparison.OrdinalIgnoreCase);

                                if (!System.IO.Directory.Exists("Shortcuts")) System.IO.Directory.CreateDirectory("Shortcuts");
                                string baseShortcutName = System.IO.Path.Combine("Shortcuts", System.IO.Path.GetFileName(droppedFile));
                                string shortcutName = baseShortcutName;
                                int counter = 1;

                                bool isDroppedShortcut = System.IO.Path.GetExtension(droppedFile).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                                bool isDroppedUrlFile = System.IO.Path.GetExtension(droppedFile).Equals(".url", StringComparison.OrdinalIgnoreCase);
                                bool isUserDesktopShortcut = isFromUserDesktop && (isDroppedShortcut || isDroppedUrlFile);

                                // FIX: Trust the extension. If it ends in .url, it IS a link.
                                // This bypasses content checks that fail on files with custom headers.
                                bool isWebLink = isDroppedUrlFile || CoreUtilities.IsWebLinkShortcut(droppedFile);

                                string targetPath;
                                bool isFolder = false;
                                string webUrl = null;

                                if (isWebLink)
                                {
                                    // Try to extract clean URL, but if it fails (weird header), fallback to file path
                                    try { webUrl = CoreUtilities.ExtractWebUrlFromFile(droppedFile); } catch { }

                                    targetPath = !string.IsNullOrEmpty(webUrl) ? webUrl : droppedFile;
                                    isFolder = false;
                                }
                                else
                                {
                                    if (isDroppedShortcut)
                                    {
                                        targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(droppedFile);
                                        if (string.IsNullOrEmpty(targetPath))
                                        {
                                            isFolder = false;
                                        }
                                        else
                                        {
                                            isFolder = System.IO.Directory.Exists(targetPath);
                                            if (!isFolder && string.IsNullOrEmpty(System.IO.Path.GetExtension(targetPath)))
                                            {
                                                isFolder = System.IO.Directory.Exists(targetPath);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        targetPath = droppedFile;
                                        isFolder = System.IO.Directory.Exists(targetPath);
                                    }
                                }

                                if (!isDroppedShortcut && !isDroppedUrlFile)
                                {
                                    // CASE A: Creating new shortcut from raw file/folder
                                    string rawFileName = isFolder ? System.IO.Path.GetFileName(droppedFile.TrimEnd('\\', '/')) : System.IO.Path.GetFileName(droppedFile);
                                    if (string.IsNullOrEmpty(rawFileName)) rawFileName = "Item";
                                    shortcutName = System.IO.Path.Combine("Shortcuts", rawFileName + ".lnk");
                                    while (System.IO.File.Exists(shortcutName))
                                    {
                                        shortcutName = System.IO.Path.Combine("Shortcuts", $"{rawFileName} ({counter++}).lnk");
                                    }

                                    try
                                    {
                                        WshShell shell = new WshShell();
                                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutName);
                                        shortcut.TargetPath = droppedFile;
                                        if (isFolder) shortcut.WorkingDirectory = droppedFile;
                                        shortcut.Save();
                                    }
                                    catch { continue; }
                                }
                                else
                                {
                                    // CASE B: Copying or moving existing shortcut (LNK or URL)
                                    string ext = isWebLink ? ".url" : ".lnk";

                                    // Ensure base name has correct extension
                                    if (!baseShortcutName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                                        baseShortcutName = System.IO.Path.ChangeExtension(baseShortcutName, ext);

                                    shortcutName = baseShortcutName;

                                    // Handle Duplicates while preserving extension
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

                                // --- 顯示名稱智能處理：實體檔案保留副檔名便於辨識，捷徑/目錄則維持不顯示副檔名 ---
                                string displayFileName;
                                if (isDroppedShortcut || isDroppedUrlFile || isFolder)
                                {
                                    displayFileName = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                }
                                else
                                {
                                    // 實體檔案保留副檔名（如 .csv, .svg, .pdf, .zip 等），徹底解決同主檔名無法區分的盲區
                                    displayFileName = System.IO.Path.GetFileName(droppedFile);
                                }

                                // If Path.GetFileName fails (happens for UNC roots like \\Server\Share or C:\)
                                if (string.IsNullOrWhiteSpace(displayFileName))
                                {
                                    displayFileName = droppedFile.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                                                 .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                                                 .LastOrDefault();

                                    if (string.IsNullOrWhiteSpace(displayFileName)) displayFileName = droppedFile;
                                }

                                newItemDict["DisplayName"] = displayFileName;
                                newItemDict["AlwaysRunAsAdmin"] = false;

                                // TABS FEATURE: Get fresh framne data
                                string frameId = frame.Id?.ToString();
                                var freshFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                                JArray items = null;
                                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";
                                bool addedToTab0 = false;

                                if (tabsEnabled && freshFrame != null)
                                {
                                    int currentTab = Convert.ToInt32(freshFrame.CurrentTab?.ToString() ?? "0");
                                    var tabs = freshFrame.Tabs as JArray ?? new JArray();
                                    if (currentTab >= 0 && currentTab < tabs.Count)
                                    {
                                        var activeTab = tabs[currentTab] as JObject;
                                        if (activeTab != null)
                                        {
                                            items = activeTab["Items"] as JArray ?? new JArray();
                                            addedToTab0 = (currentTab == 0);
                                        }
                                    }
                                    if (items == null) items = freshFrame.Items as JArray ?? new JArray();
                                }
                                else
                                {
                                    items = (freshFrame ?? frame).Items as JArray ?? new JArray();
                                }

                                int nextDisplayOrder = items.Count;
                                newItemDict["DisplayOrder"] = nextDisplayOrder;
                                items.Add(JObject.FromObject(newItem));

                                if (!string.IsNullOrEmpty(frameId))
                                {
                                    if (addedToTab0) TabManager.SynchronizeTab0Content(frameId, "tab0", "add");
                                    else if (!tabsEnabled) TabManager.SynchronizeTab0Content(frameId, "main", "add");
                                }

                                if (freshFrame != null)
                                {
                                    int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                                    if (frameIndex >= 0)
                                    {
                                        FrameDataManager.FrameData[frameIndex] = freshFrame;
                                        FrameDataManager.SaveFrameData();
                                    }
                                }

                                AddIcon(newItem, wpcont);
                                StackPanel sp = wpcont.Children[wpcont.Children.Count - 1] as StackPanel;
                                if (sp != null)
                                {
                                    // FIX: Extract arguments for the newly created shortcut
                                    string args = Utility.GetShortcutArguments(shortcutName);

                                    ClickEventAdder(sp, shortcutName, isFolder, args);

                                    // [TARGET VALIDATION ENGINE - DROP CONTENT]
                                    bool isRealNetwork = IsNetworkPath(shortcutName);
                                    bool allowNetworkChecking = _options.CheckNetworkPaths ?? false;
                                    bool isDroppedShortcutFile = shortcutName != null && shortcutName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

                                    if (!isWebLink && (!isRealNetwork || allowNetworkChecking))
                                    {
                                        string resolvedTarget = shortcutName;
                                        bool excludeFromCheck = false;

                                        if (isDroppedShortcutFile)
                                        {
                                            resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(shortcutName) ?? shortcutName;

                                            if (resolvedTarget.StartsWith(@"\\") ||
                                                resolvedTarget.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
                                                Utility.IsStoreAppShortcut(shortcutName))
                                            {
                                                excludeFromCheck = true;
                                            }
                                        }

                                        if (!excludeFromCheck)
                                        {
                                            targetChecker.AddCheckAction(shortcutName, () => UpdateIcon(sp, shortcutName, isFolder, resolvedTarget), isFolder);
                                            if (isRealNetwork) LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Monitoring network path: {shortcutName}");
                                        }
                                        else
                                        {
                                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Skipped continuous background check for newly dropped shortcut/link: {shortcutName}");
                                        }
                                    }

                                    // Attach Context Menu
                                    AttachIconContextMenu(sp, newItem, frame, win);

                                    // --- TWO-PHASE TRANSACTIONAL ADOPTION: 個人桌面捷徑收納 ---
                                    if (isUserDesktopShortcut && System.IO.File.Exists(shortcutName))
                                    {
                                        try
                                        {
                                            if (System.IO.File.Exists(droppedFile))
                                            {
                                                System.IO.File.Delete(droppedFile);
                                                Services.FenceInventoryManager.RefreshDesktopShell();
                                            }
                                        }
                                        catch (Exception delEx)
                                        {
                                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                                                $"Two-phase cleanup: Failed to delete desktop shortcut '{droppedFile}': {delEx.Message}");
                                        }
                                    }
                                }
                            }


                            // --- PORTAL FRAME LOGIC (RESTORED) ---
                            else if (frame.ItemsType?.ToString() == "Portal")
                            {
                                IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict ? dict : ((JObject)frame).ToObject<IDictionary<string, object>>();
                                string destinationFolder = frameDict.ContainsKey("Path") ? frameDict["Path"]?.ToString() : null;

                                if (string.IsNullOrEmpty(destinationFolder))
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgNoPortalDestination, Strings.DlgError);
                                    continue;
                                }
                                if (!System.IO.Directory.Exists(destinationFolder))
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgDestinationFolderGone", destinationFolder), Strings.DlgError);
                                    continue;
                                }

                                string destinationPath = System.IO.Path.Combine(destinationFolder, System.IO.Path.GetFileName(droppedFile));
                                int counter = 1;
                                string baseName = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                string extension = System.IO.Path.GetExtension(droppedFile);

                                while (System.IO.File.Exists(destinationPath) || System.IO.Directory.Exists(destinationPath))
                                {
                                    destinationPath = System.IO.Path.Combine(destinationFolder, $"{baseName} ({counter++}){extension}");
                                }

                                bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                                bool shouldMove = !isCtrlPressed; // 預設拖曳為收納搬移（Move），按住 Ctrl 則維持複製（Copy）

                                if (System.IO.File.Exists(droppedFile))
                                {
                                    if (shouldMove)
                                    {
                                        try
                                        {
                                            FileSystem.MoveFile(droppedFile, destinationPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
                                            portalMovedCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Shell MoveFile fallback: {ex.Message}");
                                            System.IO.File.Copy(droppedFile, destinationPath, true);
                                            portalCopiedCount++;
                                        }
                                    }
                                    else
                                    {
                                        FileSystem.CopyFile(droppedFile, destinationPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
                                        portalCopiedCount++;
                                    }
                                }
                                else if (System.IO.Directory.Exists(droppedFile))
                                {
                                    if (shouldMove)
                                    {
                                        try
                                        {
                                            FileSystem.MoveDirectory(droppedFile, destinationPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
                                            portalMovedCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Shell MoveDirectory fallback: {ex.Message}");
                                            BackupManager.CopyDirectory(droppedFile, destinationPath);
                                            portalCopiedCount++;
                                        }
                                    }
                                    else
                                    {
                                        BackupManager.CopyDirectory(droppedFile, destinationPath);
                                        portalCopiedCount++;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgAddItemFailed", droppedFile, ex.Message), Strings.DlgError);
                        }
                    }

                    // --- NEW: Visual Feedback for Portal Frames ---
                    if (frame.ItemsType?.ToString() == "Portal")
                    {
                        string frameTitle = frame.Title?.ToString() ?? "收納面板";
                        if (portalMovedCount > 0)
                        {
                            ShowPortalToast(win, $"已搬進 [{frameTitle}]：{portalMovedCount} 個項目");
                            Services.FenceInventoryManager.RefreshDesktopShell();
                        }
                        else if (portalCopiedCount > 0)
                        {
                            ShowPortalToast(win, $"已複製到 [{frameTitle}]：{portalCopiedCount} 個項目");
                        }
                    }

                    FrameDataManager.SaveFrameData();
                }
                // --- URL DROP LOGIC (RESTORED) ----
                else if (e.Data.GetDataPresent(DataFormats.Text) ||
                         e.Data.GetDataPresent(DataFormats.Html) ||
                         e.Data.GetDataPresent("UniformResourceLocator"))
                {
                    try
                    {
                        string droppedUrl = ExtractUrlFromDropData(e.Data);
                        if (!string.IsNullOrEmpty(droppedUrl) && IsValidWebUrl(droppedUrl))
                        {
                            if (frame.ItemsType?.ToString() == "Data")
                            {
                                AddUrlShortcutToFrame(droppedUrl, frame, wpcont);
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, "URL drops not supported for Portal frames");
                            }
                        }
                    }
                    catch (Exception urlEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation, $"Error processing URL drop: {urlEx.Message}");
                    }
                }
            };
        }
    }
}
