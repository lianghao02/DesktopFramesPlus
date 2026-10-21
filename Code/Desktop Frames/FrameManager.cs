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
    public static partial class Framemanager
    {

        // --- NEW: Centralized Presets ---
        private static readonly Dictionary<string, string> _standardPresets = new Dictionary<string, string>
        {
            { "Images", "*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp" },
            { "Documents", "*.doc*; *.pdf; *.txt; *.rtf; *.xls*; *.ppt*" },
            { "Executables", "*.exe; *.bat; *.msi; *.cmd; *.ps1" },
            { "Archives", "*.zip; *.rar; *.7z; *.tar; *.gz" },
            { "Media", "*.mp3; *.wav; *.mp4; *.mkv; *.avi" },
            { "Hide System", ">*.tmp; >desktop.ini; >~$*" }
        };
        // -------------------------------

        // Temporary override flag for the "Screen Bound" button
        public static bool IsManualRepositioning = false;


        // --- WM_GETMINMAXINFO Implementation ---
        private const int WM_GETMINMAXINFO = 0x0024;

        private const int WM_SYSCOMMAND = 0x0112; // <--- NEW
        private const int SC_MAXIMIZE = 0xF030;   // <--- NEW

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        // Tracks temporary navigation paths for Portal frames. Key: frameId, Value: CurrentPath
        private static Dictionary<string, string> _portalNavigationStates = new Dictionary<string, string>();

        // Track icon state to prevent GDI leaks from constant re-extraction
        // Key: FilePath
        // Value: (LastWriteTime of the .lnk/file, IsBroken state)
        private static Dictionary<string, (DateTime LastWrite, bool IsBroken)> _iconStates = new Dictionary<string, (DateTime, bool)>();

        private static dynamic _options;
        private static Dictionary<dynamic, PortalFramemanager> _portalFrames = new Dictionary<dynamic, PortalFramemanager>();
        private static bool _portalUnavailableNotified;

        // --- NEW: Active Plugins Registry ---
        public static Dictionary<string, IFramePlugin> _activePlugins = new Dictionary<string, IFramePlugin>();

        // Stores heart TextBlock references for each frame to enable efficient ContextMenu updates
        private static readonly Dictionary<dynamic, TextBlock> _heartTextBlocks = new Dictionary<dynamic, TextBlock>();

        //resize feedback
        private static Window _sizeFeedbackWindow;
        private static System.Windows.Threading.DispatcherTimer _hideTimer;

        // 圖示單點選取狀態
        private static StackPanel _currentlySelectedIconPanel = null;

        // Add near other static fields
        private static TargetChecker _currentTargetChecker;

        #region Auto-Hide frames Engine
        private static DispatcherTimer _masterAutoHideTimer;
        public static bool _areFramesAutoHidden = false;
        private static int _hideSequenceId = 0;


        #endregion

        // Track frame currently in rollup/rolldown transition to prevent event conflicts
        private static readonly HashSet<string> _framesInTransition = new HashSet<string>();
        // Emergency cleanup timer to prevent permanently stuck transition states
        private static System.Windows.Threading.DispatcherTimer _transitionCleanupTimer;

        // --- NEW: Auto Roll Tracking Engine ---
        private static readonly HashSet<string> _autoRolledFrames = new HashSet<string>();
        private static readonly Dictionary<string, DispatcherTimer> _autoRollTimers = new Dictionary<string, DispatcherTimer>();
        // --------------------------------------


        private static int _registryMonitorTickCount = 0;


        ///// <summary>
        ///// Validates if a string is a valid web URL
        ///// </summary>
        //private static bool IsValidWebUrl(string url)
        //{
        //    if (string.IsNullOrWhiteSpace(url)) return false;

        //    return Uri.TryCreate(url, UriKind.Absolute, out Uri validUri) &&
        //           (validUri.Scheme == Uri.UriSchemeHttp || validUri.Scheme == Uri.UriSchemeHttps ||
        //            validUri.Scheme.Equals("steam", StringComparison.OrdinalIgnoreCase));
        //}


        // TABS FEATURE: Simplified content refresh that reuses existing infrastructure


        // --- NEW: Draw Mode Integration ---


        public static void CreateFrame(dynamic frame, TargetChecker targetChecker)
        {
            // 原地面板只由共用原生核心顯示，同一 Id 不再建立捷徑面板視窗。
            string frameIdStr = frame?.Id?.ToString();
            if (!string.IsNullOrEmpty(frameIdStr) && FarmFences.FarmFenceHost.IsPanel(frameIdStr))
            {
                return;
            }

            // --- FIX: Declare Title TextBox EARLY ---
            TextBox titletb = new TextBox
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black,
                Padding = new Thickness(2)
            };

            // --- NEW: Declare Commit Action for robust saving ---
            Action CommitRename = null;
            Label titlelabel = null;
            // ---------------------------------------------------

            // Check for valid Portal Frame target folder
            if (frame.ItemsType?.ToString() == "Portal")
            {
                string targetPath = frame.Path?.ToString();
                if (string.IsNullOrEmpty(targetPath) || !System.IO.Directory.Exists(targetPath))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, $"Skipping creation of Portal Frame '{frame.Title}' due to missing target folder: {targetPath ?? "null"}");
                    return;
                }
            }
            DockPanel dp = new DockPanel { ClipToBounds = true };
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
                    CommitRename?.Invoke();
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
                        CommitRename?.Invoke();
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
                                                                                                                      // End of ctrl+click handler
            ContextMenu CnMnFramemanager = new ContextMenu();

            Style themedStyle = GetThemedContextMenuStyle(frame);
            if (themedStyle != null) CnMnFramemanager.Style = themedStyle;

            MenuItem miNewnoteFrame = new MenuItem { Header = Strings.MenuNewNoteFrame };

            // 面板右鍵選單：常用項目留在第一層，少用功能收進「更多」。
            // 注意：後面的動態項目都以「自訂外觀…」(Strings.MenuCustomize) 當定位點，該項必須維持在第一層。
            MenuItem miMoreFrame = new MenuItem { Header = Strings.Get("MenuMore", "更多") };

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

            MenuItem miHide = new MenuItem { Header = Strings.MenuHideFrame }; // New Hide Frame item
                                                                   
            CnMnFramemanager.Items.Add(miHide); // Add Hide Frame
                                                // Add Note Frame specific context menu items if this is a Note Frame
            if (frame.ItemsType?.ToString() == "Note")
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"Adding Note frame context menu items for '{frame.Title}'");
                // We'll add the TextBox reference after the window is created
                // For now, just mark that this will need Note menu items
            }
            NonActivatingWindow win = new NonActivatingWindow
            {
                ContextMenu = CnMnFramemanager,
                AllowDrop = true,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Title = frame.Title?.ToString() ?? "New Frame", // Handle null title
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = cborder,
                ResizeMode = frame.IsLocked?.ToString().ToLower() == "true" ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip,
                Topmost = frame.AlwaysOnTop?.ToString().ToLower() == "true", // --- NEW: Apply Always On Top ---
                // ResizeMode = ResizeMode.CanResizeWithGrip,
                Width = (double)frame.Width,
                Height = (double)frame.Height,
                Top = (double)frame.Y,
                Left = (double)frame.X,
                Tag = frame.Id?.ToString() ?? Guid.NewGuid().ToString() // Ensure ID exists
            };
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
            MenuItem miClearDeadShortcuts = null;
            Separator sepClearDead = null;

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
            MenuItem miPasteItem = new MenuItem { Header = Strings.MenuPasteItem, Visibility = Visibility.Collapsed };
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
                if (titlelabel != null && titletb != null)
                {
                    titletb.Text = titlelabel.Content?.ToString() ?? "";
                    titlelabel.Visibility = Visibility.Collapsed;
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
            // CnMnFramemanager.Items.Add(new Separator());
            // CnMnFramemanager.Items.Add(new Separator());
            // CnMnFramemanager.Items.Add(miXT);
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


            // --- FIX END ---


            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
        $"Frame '{frame.Title}' successfully created and displayed");
  

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
            titlelabel = new Label
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
                                            titlelabel.Content = newFolderName;
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
                            titlelabel.Content = targetPath;
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


            Grid.SetColumn(titletb, 1);
            titleGrid.Children.Add(titletb);

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


            // --- STEP 4 FIX: Centralized Rename Logic ---

            // 1. Define the Logic ONCE
            CommitRename = () =>
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
                titlelabel.Content = finalTitle;
                win.Title = finalTitle;
                titletb.Visibility = Visibility.Collapsed;
                titlelabel.Visibility = Visibility.Visible;

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
                    CommitRename(); // Call shared logic
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Cancel Logic
                    titletb.Text = frame.Title.ToString();
                    titletb.Visibility = Visibility.Collapsed;
                    titlelabel.Visibility = Visibility.Visible;
                    win.EndKeyboardInteractiveEdit();
                    e.Handled = true;
                }
            };

            titletb.LostFocus += (sender, e) =>
            {
                CommitRename(); // Call shared logic
            };


            //// --- STEP 4 START: Configure and Add Events ---
            //titletb.HorizontalContentAlignment = HorizontalAlignment.Center;
            //titletb.Visibility = Visibility.Collapsed;

            //// 1. Handle Keys (Enter = Save, Escape = Cancel)
            //titletb.KeyDown += (sender, e) =>
            //{
            //    if (e.Key == Key.Enter)
            //    {
            //        string originalTitle = frame.Title.ToString();
            //        string newTitle = titletb.Text;
            //        string finalTitle = InterCore.ProcessTitleChange(frame, newTitle, originalTitle);

            //        // Update LIVE Data
            //        string id = frame.Id?.ToString();
            //        var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);

            //        if (liveFrame != null)
            //        {
            //            if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
            //                jFrame["Title"] = finalTitle;
            //            else
            //                liveFrame.Title = finalTitle;
            //            frame.Title = finalTitle;
            //        }

            //        titlelabel.Content = finalTitle;
            //        win.Title = finalTitle;
            //        titletb.Visibility = Visibility.Collapsed;
            //        titlelabel.Visibility = Visibility.Visible;

            //        FrameDataManager.SaveFrameData();
            //        win.EndKeyboardInteractiveEdit();
            //    }
            //    else if (e.Key == Key.Escape)
            //    {
            //        // ESCAPE: Cancel and Revert
            //        titletb.Text = frame.Title.ToString();
            //        titletb.Visibility = Visibility.Collapsed;
            //        titlelabel.Visibility = Visibility.Visible;
            //        win.EndKeyboardInteractiveEdit();
            //        e.Handled = true;

            //        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Rename cancelled via Escape");
            //        }
            //    }
            //    ;

            //// 2. Handle Focus Loss (Save when clicking away)
            //titletb.LostFocus += (sender, e) =>
            //{
            //    // If invisible, we already handled it (e.g. via Escape)
            //    if (titletb.Visibility != Visibility.Visible) return;

            //    string originalTitle = frame.Title.ToString();
            //    string newTitle = titletb.Text;
            //    string finalTitle = InterCore.ProcessTitleChange(frame, newTitle, originalTitle);

            //    string id = frame.Id?.ToString();
            //    var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);

            //    if (liveFrame != null)
            //    {
            //        if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
            //            jFrame["Title"] = finalTitle;
            //        else
            //            liveFrame.Title = finalTitle;
            //        frame.Title = finalTitle;
            //    }

            //    titlelabel.Content = finalTitle;
            //    win.Title = finalTitle;
            //    titletb.Visibility = Visibility.Collapsed;
            //    titlelabel.Visibility = Visibility.Visible;

            //    FrameDataManager.SaveFrameData();
            //    win.EndKeyboardInteractiveEdit();
            //    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename saved via LostFocus: {finalTitle}");
            //};
            //// --- STEP 4 END ---


            // Move lockIcon to the Grid
            Grid.SetColumn(lockIcon, 3); // Moved to Column 3
            Grid.SetRow(lockIcon, 0);
            titleGrid.Children.Add(lockIcon);
            // Add the titleGrid to the DockPanel
            DockPanel.SetDock(titleGrid, Dock.Top);
            dp.Children.Add(titleGrid);
            if (filterBar != null)
            {
                DockPanel.SetDock(filterBar, Dock.Top);
                dp.Children.Add(filterBar);
            }


            // TABS FEATURE: Add TabStrip
            // We delegate to the shared method to ensure consistent "Scrollable" layout on startup.
            TabManager.RefreshTabStripUI(win, frame);

           string frameId = win.Tag?.ToString();
            if (string.IsNullOrEmpty(frameId))
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Frame Id is missing for window '{win.Title}'");
                return;
            }
            dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
            if (currentFrame == null)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Frame with Id '{frameId}' not found in FrameDataManager.FrameData");
                return;
            }
            bool isLocked = currentFrame.IsLocked?.ToString().ToLower() == "true";
            System.Windows.Point? titleMouseDownPoint = null;
            bool titleIsDraggingWindow = false;

            titlelabel.MouseDown += (sender, e) =>
            {
                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    CommitRename?.Invoke();
                    return;
                }

                if (e.ClickCount == 2)
                {
                    titleMouseDownPoint = null;
                    titleIsDraggingWindow = false;
                    // Roll-up/roll-down logic (swapped from Ctrl+Click)
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(titlelabel);
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
                        titletb.Text = titlelabel.Content.ToString();
                        titlelabel.Visibility = Visibility.Collapsed;
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

            titlelabel.MouseMove += (sender, e) =>
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

            titlelabel.MouseLeftButtonUp += (sender, e) =>
            {
                if (titleMouseDownPoint.HasValue && !titleIsDraggingWindow && !titletb.IsVisible)
                {
                    titletb.Text = titlelabel.Content.ToString();
                    titlelabel.Visibility = Visibility.Collapsed;
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


            //titletb.KeyDown += (sender, e) =>
            //{
            //    if (e.Key == Key.Enter)
            //    {
            //        string originalTitle = frame.Title.ToString();
            //        string newTitle = titletb.Text;

            //        // Process through InterCore for special triggers
            //        string finalTitle = InterCore.ProcessTitleChange(frame, newTitle, originalTitle);

            //        // --- FIX START: Update LIVE Data ---
            //        // Get the ID to find the fresh object in the global list
            //        string id = frame.Id?.ToString();
            //        var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);

            //        if (liveFrame != null)
            //        {
            //            // Update the live object in the list
            //            // Handle both JObject (JSON) and ExpandoObject (New frame)
            //            if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
            //                jFrame["Title"] = finalTitle;
            //            else
            //                liveFrame.Title = finalTitle;

            //            // Also update the local reference just in case
            //            frame.Title = finalTitle;
            //        }
            //        // --- FIX END ---

            //        // Update UI
            //        titlelabel.Content = finalTitle;
            //        win.Title = finalTitle;
            //        titletb.Visibility = Visibility.Collapsed;
            //        titlelabel.Visibility = Visibility.Visible;

            //        // Save the global list which now contains the updated title
            //        FrameDataManager.SaveFrameData();

            //        win.ShowActivated = false;
            //        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Exited edit mode via Enter, final title for frame: {finalTitle}");
            //        win.Focus();
            //    }
            //    else if (e.Key == Key.Escape)
            //    {
            //        // FIX: ESCAPE Logic (Cancel)
            //        titletb.Text = frame.Title.ToString(); // Revert text
            //        titletb.Visibility = Visibility.Collapsed;
            //        titlelabel.Visibility = Visibility.Visible;

            //        Keyboard.ClearFocus(); // Drop focus
            //        win.ShowActivated = false;
            //        e.Handled = true;

            //        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Rename cancelled via Escape");
            //    }
            //};

            //// 2. Handle Focus Loss (Auto-Save)
            //titletb.LostFocus += (sender, e) =>
            //{
            //    // Don't save if we are cancelling (Escape key handles UI)
            //    if (titletb.Visibility != Visibility.Visible) return;

            //    string originalTitle = frame.Title.ToString();
            //    string newTitle = titletb.Text;
            //    string finalTitle = InterCore.ProcessTitleChange(frame, newTitle, originalTitle);

            //    string id = frame.Id?.ToString();
            //    var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == id);

            //    if (liveFrame != null)
            //    {
            //        IDictionary<string, object> frameDict = liveFrame as IDictionary<string, object> ??
            //            ((JObject)liveFrame).ToObject<IDictionary<string, object>>();
            //        frameDict["Title"] = finalTitle;

            //        int index = FrameDataManager.FrameData.IndexOf(liveFrame);
            //        if (index >= 0) FrameDataManager.FrameData[index] = JObject.FromObject(frameDict);

            //        frame.Title = finalTitle;
            //    }

            //    titlelabel.Content = finalTitle;
            //    win.Title = finalTitle;
            //    titletb.Visibility = Visibility.Collapsed;
            //    titlelabel.Visibility = Visibility.Visible;

            //    FrameDataManager.SaveFrameData();

            //    win.ShowActivated = false;
            //    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename saved via LostFocus: {finalTitle}");
            //};


            // WrapPanel wpcont = new WrapPanel();

            // --- FIX START ---
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


            if (SettingsManager.EnableDimensionSnap)
            {
                // 尺寸提示僅在使用者實際拖曳邊界期間訂閱。
            }


            win.LocationChanged += (s, e) =>
            {
                // --- THE UNIVERSAL DRAG GATEKEEPER ---
                // Only save coordinates to JSON if the USER is physically dragging this window with their mouse!
                // Completely ignores programmatic movement from Auto-Roll, Manual Roll-Up, and CascadeStack.
                if (SnapManager.ActiveDragWindow != win) return;
                // -------------------------------------

                string frameId = win.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (currentFrame != null)
                {
                    currentFrame.X = win.Left;
                    currentFrame.Y = win.Top;
                    FrameDataManager.SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameUpdate, $"Position updated for frame '{currentFrame.Title}' to X={win.Left}, Y={win.Top}");
                }
            };


            InitContent();

            // 1. 點擊視窗任意空白區域（非圖示目標）→ 立即取消高亮選取
            win.PreviewMouseDown += (s, e) =>
            {
                if (_currentlySelectedIconPanel != null)
                {
                    DependencyObject source = e.OriginalSource as DependencyObject;
                    StackPanel clickedSp = FindVisualParent<StackPanel>(source);
                    if (clickedSp == null || clickedSp.Tag == null)
                    {
                        DeselectIcon();
                    }
                }
            };

            // 2. Delete 鍵快捷移除：按 Delete 時移除目前選取的圖示（限 Data frame，絕不碰實體檔案）
            if (frame.ItemsType?.ToString() == "Data")
            {
                win.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Delete && _currentlySelectedIconPanel != null)
                    {
                        RemoveSelectedIconFromFrame(_currentlySelectedIconPanel);
                        e.Handled = true;
                    }
                };
            }

            // Add Note frame specific context menu items after content is initialized
            if (frame.ItemsType?.ToString() == "Note")
            {
                // Use a small delay to ensure the TextBox is fully created and added to the visual tree
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Find the TextBox that was created in InitContent()
                        var border = win.Content as Border;
                        var dockPanel = border?.Child as DockPanel;
                        var noteTextBox = dockPanel?.Children.OfType<TextBox>().FirstOrDefault();
                        if (noteTextBox != null)
                        {
                            NoteFramemanager.AddNoteContextMenuItems(CnMnFramemanager, frame, noteTextBox);
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                                $"Added Note context menu items for frame '{frame.Title}'");
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                                $"CRITICAL: Could not find TextBox for Note frame '{frame.Title}' - checking DockPanel children");
                            // Debug: Log what children actually exist
                            if (dockPanel != null)
                            {
                                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                                    $"DockPanel has {dockPanel.Children.Count} children:");
                                for (int i = 0; i < dockPanel.Children.Count; i++)
                                {
                                    var child = dockPanel.Children[i];
                                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                                        $" Child {i}: {child.GetType().Name}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Error adding Note context menu: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            win.Show();


            // Check for persistent Legendary Mode (Nikos or >:)
            string fTitle = frame.Title?.ToString() ?? "";
            if (fTitle == "Nikos" || fTitle == "Nikos Georgousis" || fTitle.Contains(">:"))
            {
                // Defer slightly to ensure window is loaded
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // We call ProcessTitleChange to trigger the visual effect
                    InterCore.ProcessTitleChange(frame, fTitle, "");
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            {
                // Defer slightly to ensure window is loaded
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // We call ProcessTitleChange with same names just to trigger the logic
                    InterCore.ProcessTitleChange(frame, frame.Title.ToString(), "");
                }), System.Windows.Threading.DispatcherPriority.Background);
            }


            IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict ? dict : ((JObject)frame).ToObject<IDictionary<string, object>>();
            SnapManager.AddSnapping(win, frameDict);
            // Apply custom color if present, otherwise let Utility handle the fallback
            string customColor = frame.CustomColor?.ToString();
            Utility.ApplyTintAndColorToFrame(win, customColor);
            targetChecker.Start();
        }

        /// <summary>
        /// 自適應圖示網格排版：當收納框縮小或調整寬度時，平滑壓縮與均分每列圖示槽位寬度與左右間距，
        /// 避免傳統固定寬度 WrapPanel 提早換行造成右側大片留白或文字裁切。
        /// </summary>
        public static void UpdateAdaptiveIconGrid(WrapPanel wpcont)
        {
            if (wpcont == null || wpcont.Children.Count == 0) return;
            try
            {
                int iconSpacing = 5;
                double iconWidth = 40;

                dynamic settings = null;
                if (wpcont.Tag != null)
                {
                    string fId = wpcont.Tag.ToString();
                    settings = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == fId);
                }

                if (settings != null)
                {
                    try { iconSpacing = Convert.ToInt32(settings.IconSpacing?.ToString() ?? "5"); } catch { }
                    string sizeVal = settings.IconSize?.ToString() ?? "Medium";
                    switch (sizeVal)
                    {
                        case "Tiny": iconWidth = 24; break;
                        case "Small": iconWidth = 32; break;
                        case "Large": iconWidth = 48; break;
                        case "Huge": iconWidth = 64; break;
                        default: iconWidth = 40; break;
                    }
                }

                double baseCellWidth = Math.Max(62.0, iconWidth + 22.0);
                double baseMarginX = Math.Max(2.0, iconSpacing);
                double baseMarginY = Math.Max(2.0, iconSpacing - 1);
                double idealSlotWidth = baseCellWidth + (baseMarginX * 2.0);

                double minCellWidth = Math.Max(48.0, iconWidth + 10.0);
                double minMarginX = Math.Max(2.0, Math.Floor(baseMarginX * 0.5));
                double minSlotWidth = minCellWidth + (minMarginX * 2.0);

                double availWidth = wpcont.ActualWidth;
                if (availWidth <= 10)
                {
                    var sv = wpcont.Parent as ScrollViewer;
                    if (sv != null && sv.ActualWidth > 20)
                    {
                        availWidth = sv.ActualWidth - wpcont.Margin.Left - wpcont.Margin.Right - 4;
                    }
                    else
                    {
                        var win = FindVisualParent<NonActivatingWindow>(wpcont);
                        if (win != null)
                        {
                            double w = win.ActualWidth > 20 ? win.ActualWidth : win.Width;
                            if (w > 24) availWidth = w - 24;
                        }
                    }
                }

                var visiblePanels = wpcont.Children.OfType<StackPanel>()
                    .Where(p => p.Visibility != Visibility.Collapsed)
                    .ToList();

                if (visiblePanels.Count == 0) return;

                double targetCellWidth = baseCellWidth;
                double targetMarginX = baseMarginX;

                if (availWidth >= minSlotWidth)
                {
                    int idealCols = Math.Max(1, (int)Math.Floor((availWidth + 0.5) / idealSlotWidth));
                    int targetCols = idealCols;

                    // 若框稍微縮小但以緊湊間距仍可容納多 1 欄，優先維持原欄數並平滑收斂間距，避免突兀換行
                    if (visiblePanels.Count > idealCols && (idealCols + 1) * minSlotWidth <= availWidth + 0.5)
                    {
                        targetCols = idealCols + 1;
                    }

                    bool singleRowFitsIdeally = visiblePanels.Count <= targetCols &&
                                                (visiblePanels.Count * idealSlotWidth) <= (availWidth + 1.0);

                    if (!singleRowFitsIdeally)
                    {
                        int activeCols = Math.Max(1, Math.Min(visiblePanels.Count, targetCols));
                        double rawSlotWidth = Math.Floor((availWidth - 1.0) / activeCols);
                        double clampedSlotWidth = Math.Clamp(rawSlotWidth, minSlotWidth, idealSlotWidth + 14.0);

                        double span = Math.Max(1.0, idealSlotWidth - minSlotWidth);
                        double ratio = Math.Clamp((clampedSlotWidth - minSlotWidth) / span, 0.0, 1.25);

                        targetMarginX = Math.Round(Math.Clamp(minMarginX + (baseMarginX - minMarginX) * ratio, 2.0, baseMarginX + 3.0));
                        targetCellWidth = Math.Max(minCellWidth, clampedSlotWidth - (targetMarginX * 2.0));
                    }
                }

                foreach (var sp in visiblePanels)
                {
                    if (Math.Abs(sp.Width - targetCellWidth) > 0.5)
                        sp.Width = targetCellWidth;

                    var m = sp.Margin;
                    if (Math.Abs(m.Left - targetMarginX) > 0.5 || Math.Abs(m.Top - baseMarginY) > 0.5)
                        sp.Margin = new Thickness(targetMarginX, baseMarginY, targetMarginX, baseMarginY);

                    var lbl = sp.Children.OfType<TextBlock>().LastOrDefault();
                    if (lbl != null)
                    {
                        double lblMax = Math.Max(44.0, targetCellWidth - 2.0);
                        if (Math.Abs(lbl.MaxWidth - lblMax) > 0.5)
                            lbl.MaxWidth = lblMax;
                    }
                }
            }
            catch { }
        }

        public static void AddIcon(dynamic icon, WrapPanel wpcont, dynamic frameContext = null)
        {
            // 1. EXTRACT DATA
            IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                ? dict
                : ((JObject)icon).ToObject<IDictionary<string, object>>();

            string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
            bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];
            bool isLink = iconDict.ContainsKey("IsLink") && (bool)iconDict["IsLink"];

            // Fail-Safe Network Detection
            bool isNetwork = iconDict.ContainsKey("IsNetwork") && (bool)iconDict["IsNetwork"];
            if (!isNetwork && !string.IsNullOrEmpty(filePath))
            {
                isNetwork = IsNetworkPath(filePath);
            }

            // --- STEP 1: Determine Settings Context (Always prefer live FrameData by Id) ---
            dynamic settings = null;
            try
            {
                string frameId = frameContext?.Id?.ToString() ?? wpcont.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId))
                {
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(wpcont);
                    frameId = win?.Tag?.ToString();
                }
                if (!string.IsNullOrEmpty(frameId))
                {
                    settings = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId);
                }
            }
            catch { }
            if (settings == null) settings = frameContext;

            // --- NEW: Identify frame Type (Portal vs Data) ---
            bool isPortal = false;
            if (settings != null)
            {
                try
                {
                    // Handle both JObject (JSON) and ExpandoObject (Runtime)
                    if (settings is JObject jObj)
                        isPortal = jObj["ItemsType"]?.ToString() == "Portal";
                    else
                        isPortal = settings.ItemsType?.ToString() == "Portal";
                }
                catch { }
            }

            // --- STEP 2: Read Customization Settings ---
            int iconSpacing = 5;
            double iconWidth = 40, iconHeight = 40;
            bool grayscale = false;
            string textColorName = null;
            bool disableShadow = false;

            if (settings != null)
            {
                try { iconSpacing = Convert.ToInt32(settings.IconSpacing?.ToString() ?? "5"); } catch { }

                string sizeVal = settings.IconSize?.ToString() ?? "Medium";
                switch (sizeVal)
                {
                    case "Tiny": iconWidth = iconHeight = 24; break;
                    case "Small": iconWidth = iconHeight = 32; break;
                    case "Large": iconWidth = iconHeight = 48; break;
                    case "Huge": iconWidth = iconHeight = 64; break;
                    default: iconWidth = iconHeight = 40; break;
                }

                try { grayscale = settings.GrayscaleIcons?.ToString().ToLower() == "true"; } catch { }
                try { textColorName = settings.TextColor?.ToString(); } catch { }
                try { disableShadow = settings.DisableTextShadow?.ToString().ToLower() == "true"; } catch { }
            }

            // --- STEP 3: Create UI Elements ---
            double baseCellWidth = Math.Max(62.0, iconWidth + 22.0);
            double baseMarginY = Math.Max(2.0, iconSpacing - 1);
            StackPanel sp = new StackPanel
            {
                Margin = new Thickness(iconSpacing, baseMarginY, iconSpacing, baseMarginY),
                Width = baseCellWidth
            };

            System.Windows.Controls.Image ico = new System.Windows.Controls.Image
            {
                Width = iconWidth,
                Height = iconHeight,
                Margin = new Thickness(4, 4, 4, 2),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(ico, BitmapScalingMode.HighQuality);
            if (SettingsManager.IconVisibilityEffect != IconVisibilityEffect.None)
            {
                ico.Effect = Utility.CreateIconEffect(SettingsManager.IconVisibilityEffect);
            }
            else
            {
                // 環境微陰影：保護淺色與細線條圖示在羊皮紙底板上的清晰輪廓，避免邊界融底
                ico.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.22
                };
            }
            // --- NEW: Intercept Internal Blank Spacer ---
            if (filePath != null && filePath.StartsWith("INTERNAL_BLANK_"))
            {
                string resImage = filePath.StartsWith("INTERNAL_BLANK_DOT") ? "dot.png" : "empty.png";
                ico.Source = new BitmapImage(new Uri($"pack://application:,,,/Resources/{resImage}", UriKind.Absolute));
                sp.Children.Add(ico);

                // Add empty label to maintain exact grid alignment
                TextBlock blankLbl = new TextBlock { Text = " " };
                sp.Children.Add(blankLbl);

                // CRITICAL: Must be transparent to catch mouse clicks/drags
                sp.Background = System.Windows.Media.Brushes.Transparent;

                sp.Tag = new { FilePath = filePath, IsFolder = false, Arguments = (string)null };
                sp.ToolTip = new ToolTip { Content = Strings.TooltipBlankSpacer };
                wpcont.Children.Add(sp);
                UpdateAdaptiveIconGrid(wpcont);
                return; // Skip standard extraction
            }


            // --- LAZY LOAD PREPARATION ---
            bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
            bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";

            // 1. Apply base visual effects immediately so the UI draws fast
            double targetIconOpacity = 1.0;
            if (SettingsManager.ApplyTintToIcons)
            {
                targetIconOpacity = Math.Max(0.2, SettingsManager.TintValue / 100.0);
                ico.Opacity = targetIconOpacity;
            }

            if (grayscale)
            {
                ico.Opacity = 0.6;
                ico.Effect = new DropShadowEffect { Color = Colors.Gray, BlurRadius = 0, ShadowDepth = 0 };
            }

            //// 2. Set an instant placeholder to prevent blank spaces during load
            //ico.Source = isFolder
            //    ? new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"))
            //    : new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));

            // 2. Set an instant placeholder to prevent layout collapsing during load
            // OPTION A: We use your existing transparent 'empty.png' (or null).
            // This reserves the physical space for the grid and catches mouse clicks, 
            // but remains completely invisible until the real icon seamlessly fades in.
            ico.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/placeholder.png", UriKind.Absolute));


            // 3. Assemble Network Overlay
            if (isNetwork)
            {
                Grid iconGrid = new Grid { Width = iconWidth + 8, Height = iconHeight + 8 };
                iconGrid.Children.Add(ico);

                TextBlock networkIndicator = new TextBlock
                {
                    Text = "🔗",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 135, 225)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 2, 0, 0),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, Opacity = 0.7 }
                };
                iconGrid.Children.Add(networkIndicator);
                sp.Children.Add(iconGrid);
            }
            else
            {
                sp.Children.Add(ico);
            }

            // 4. Assemble Text Label
            string displayName = (!iconDict.ContainsKey("DisplayName") || iconDict["DisplayName"] == null)
                ? System.IO.Path.GetFileNameWithoutExtension(filePath)
                : (string)iconDict["DisplayName"];

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(filePath))
            {
                displayName = filePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                      .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                      .LastOrDefault();
                if (string.IsNullOrWhiteSpace(displayName)) displayName = "Unknown";
            }

            string originalDisplayName = displayName;
            if (displayName.Length > SettingsManager.MaxDisplayNameLength)
                displayName = displayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "...";

            // 預設字體顏色：淺色羊皮紙底板採用深墨炭黑 Ink，確保高對比銳利度與可讀性；透明透光底則維持白字
            System.Windows.Media.Brush textBrush = SettingsManager.TintValue > 0 ? GameTheme.Ink : System.Windows.Media.Brushes.White;
            if (!string.IsNullOrEmpty(textColorName) && textColorName != "Default")
            {
                try { textBrush = new SolidColorBrush(Utility.GetColorFromName(textColorName)); } catch { }
            }

            TextBlock lbl = new TextBlock
            {
                Text = displayName,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = textBrush,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft JhengHei UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                MaxWidth = baseCellWidth - 4.0,
                MaxHeight = 34, // 精確限制最多 2 行（行高 16px * 2 + 2px 容差），超過自動在第 2 行末端顯示省略號（...）
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(3, 2, 3, 4),
                Padding = new Thickness(2, 0, 2, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Opacity = 1.0 // 文字永遠維持 100% 實心，絕不打折以確保最高銳利度與可讀性
            };

            // 保持文字底板乾淨透明，絕不使用覆蓋圖示的矩形遮罩塊
            if (SettingsManager.TintValue == 0 && !disableShadow)
            {
                lbl.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.65 };
            }

            sp.Children.Add(lbl);
            var itemTile = GameTheme.ItemTile();
            sp.SetValue(GameTheme.ItemBackgroundProperty, itemTile);
            sp.Background = itemTile;
            sp.MouseEnter += (s, e) =>
            {
                if (_currentlySelectedIconPanel != sp)
                    sp.Background = GameTheme.ItemHoverBrush;
            };
            sp.MouseLeave += (s, e) =>
            {
                if (_currentlySelectedIconPanel != sp)
                    sp.Background = itemTile;
            };
            sp.Tag = new { FilePath = filePath, IsFolder = isFolder, Arguments = (string)(iconDict.ContainsKey("Arguments") ? iconDict["Arguments"] : null) };
            sp.ToolTip = new ToolTip { Content = originalDisplayName + "\nLoading target..." };

            wpcont.Children.Add(sp); // ADD TO UI INSTANTLY!
            UpdateAdaptiveIconGrid(wpcont);

            // --- 5. BACKGROUND RESOLVER TASK ---
            // This offloads the 30-second WshShell and Directory.Exists network hangs!
            Action backgroundLoader = () =>
            {
                ImageSource shortcutIcon = null;
                string targetPath = null;
                bool targetIsUncRoot = false;

                // Helper to safely create unfrozen background images for the UI
                BitmapImage CreateSafeBitmap(string uriPath)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(uriPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze(); // CRITICAL FIX: Allows passing to UI thread
                    return bmp;
                }

                if (isShortcut || isLink || isUrlFile)
                {
                    if (isUrlFile || isLink)
                    {
                        var urlIcon = GetUrlCustomIcon(filePath);
                        if (urlIcon.Path != null) shortcutIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                    }

                    if (shortcutIcon == null && isShortcut)
                    {
                        // 優先使用 Win32 IShellLinkW (Unicode-safe) 解析目標與自訂圖示，避免 WshShell 將 ● 等字元轉為 ?
                        if (FilePathUtilities.TryResolveShortcutWithShellLinkW(filePath, out string slTarget, out _, out _, out string slIconPath, out int slIconIdx))
                        {
                            targetPath = slTarget;
                            if (!string.IsNullOrEmpty(slIconPath) && System.IO.File.Exists(slIconPath))
                            {
                                shortcutIcon = IconManager.ExtractIconFromFile(slIconPath, slIconIdx);
                            }
                        }

                        if (shortcutIcon == null && string.IsNullOrEmpty(targetPath))
                        {
                            try
                            {
                                WshShell shell = new WshShell();
                                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(System.IO.Path.GetFullPath(filePath));
                                if (!string.IsNullOrEmpty(shortcut.TargetPath) && !shortcut.TargetPath.Contains("?"))
                                    targetPath = shortcut.TargetPath;

                                if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                                {
                                    string[] iconParts = shortcut.IconLocation.Split(',');
                                    string iconPath = iconParts[0];
                                    int iconIndex = 0;
                                    if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex)) iconIndex = parsedIndex;
                                    if (System.IO.File.Exists(iconPath)) shortcutIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (shortcutIcon == null)
                {
                    if (isLink || isUrlFile)
                    {
                        string urlTarget = targetPath;
                        if (string.IsNullOrEmpty(urlTarget))
                        {
                            try
                            {
                                var lines = System.IO.File.ReadAllLines(filePath);
                                var urlLine = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                                if (urlLine != null) urlTarget = urlLine.Substring(4).Trim();
                            }
                            catch { }
                        }

                        if (!string.IsNullOrEmpty(urlTarget) && !urlTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !urlTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            shortcutIcon = IconManager.GetIconForFile(targetPath, filePath, isFolder, isLink, isShortcut, iconDict);
                        }

                        if (shortcutIcon == null) shortcutIcon = CreateSafeBitmap("pack://application:,,,/Resources/link-White.png");
                    }
                    else if (isShortcut)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(targetPath) || targetPath.Contains("?"))
                                targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                            if (IsUncRoot(targetPath)) { targetIsUncRoot = true; isFolder = true; isNetwork = true; }
                        }
                        catch { }

                        bool targetIsFolder = false;
                        bool targetExists = false;
                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            if (targetIsUncRoot || targetPath.StartsWith(@"\\"))
                            {
                                targetIsFolder = true;
                                targetExists = true;
                            }
                            else
                            {
                                targetIsFolder = System.IO.Directory.Exists(targetPath);
                                targetExists = targetIsFolder || System.IO.File.Exists(targetPath);
                            }
                        }

                        if (!targetExists && ScrapeLnkForNetworkRoot(filePath))
                        {
                            targetIsFolder = true; isNetwork = true; isFolder = true; targetIsUncRoot = true;
                        }

                        if (targetExists) shortcutIcon = Utility.GetShellIcon(targetPath, targetIsFolder);
                        else shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
                    }
                    else
                    {
                        shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
                    }
                }

                if (shortcutIcon == null)
                {
                    if (isFolder)
                    {
                        bool isUncRoot = targetIsUncRoot || IsUncRoot(filePath);
                        bool valid = isUncRoot || FilePathUtilities.DoesFolderExist(filePath, isFolder);
                        if (!valid && isShortcut && !string.IsNullOrEmpty(targetPath)) valid = IsUncRoot(targetPath) || System.IO.Directory.Exists(targetPath);
                        shortcutIcon = valid ? CreateSafeBitmap("pack://application:,,,/Resources/folder-White.png") : CreateSafeBitmap("pack://application:,,,/Resources/folder-WhiteX.png");
                    }
                    else shortcutIcon = CreateSafeBitmap("pack://application:,,,/Resources/file-WhiteX.png");
                }

                // CRITICAL FIX: Freeze dynamically extracted shell icons as well
                if (shortcutIcon != null && shortcutIcon.CanFreeze && !shortcutIcon.IsFrozen)
                {
                    shortcutIcon.Freeze();
                }

                // [Tooltip Parsing]
                string toolTipText = originalDisplayName;
                string resolvedTarget = filePath;

                if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk") resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                else if (System.IO.Path.GetExtension(filePath).ToLower() == ".url")
                {
                    try { string content = System.IO.File.ReadAllText(filePath); var match = System.Text.RegularExpressions.Regex.Match(content, @"URL=([^\r\n]+)"); if (match.Success) resolvedTarget = match.Groups[1].Value.Trim(); } catch { }
                }
                if (string.IsNullOrEmpty(resolvedTarget)) resolvedTarget = filePath;

                string displayTarget = resolvedTarget;
                if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
                {
                    string packageId = displayTarget.Split('!')[0]; int hashIndex = packageId.IndexOf('_'); if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex); displayTarget = $"Windows App ({packageId})";
                }
                if (resolvedTarget != filePath) toolTipText += $"\nTarget: {displayTarget}"; else toolTipText += $"\nLocation: {displayTarget}";

                // 6. PUSH TO UI
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (shortcutIcon != null)
                    {
                        if (grayscale && shortcutIcon is BitmapSource bmp)
                        {
                            var formatBmp = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, BitmapPalettes.Gray256, 0);
                            formatBmp.Freeze(); // CRITICAL FIX
                            ico.Source = formatBmp;
                        }
                        else ico.Source = shortcutIcon;
                    }
                    sp.ToolTip = new ToolTip { Content = toolTipText };
                }), System.Windows.Threading.DispatcherPriority.Background);
            };


            // --- SAFE EXECUTION WRAPPER ---
            Action safeBackgroundExecution = () =>
            {
                try { backgroundLoader(); }
                catch { /* Catch all unexpected errors so they NEVER crash the app */ }
            };

            if (isNetwork)
            {
                // CRITICAL FIX: COM Objects (WshShell) MUST run on an STA thread. Task.Run is MTA!
                System.Threading.Thread staThread = new System.Threading.Thread(new System.Threading.ThreadStart(safeBackgroundExecution));
                staThread.SetApartmentState(System.Threading.ApartmentState.STA); // Set to Single Threaded Apartment
                staThread.IsBackground = true;
                staThread.Start();
            }
            else
            {
                // Local files are fast, run immediately to avoid visual "pop in" delays
                safeBackgroundExecution();
            }
        }


        //    // --- ICON EXTRACTION LOGIC ---
        //    ImageSource shortcutIcon = null;
        //    bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
        //    bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";

        //    // Variables to track target state
        //    string targetPath = null;
        //    bool targetIsUncRoot = false;

        //    // FIX: Unified Custom Icon Extraction for Links AND Shortcuts
        //    // We check for a custom icon FIRST. If one exists, we use it immediately.
        //    if (isShortcut || isLink || isUrlFile)
        //    {
        //        // NEW: Special handling for .url files using manual parser
        //        if (isUrlFile || isLink)
        //        {
        //            var urlIcon = GetUrlCustomIcon(filePath);
        //            if (urlIcon.Path != null)
        //            {
        //                shortcutIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
        //            }
        //        }

        //        // Existing .lnk logic
        //        if (shortcutIcon == null)
        //        {


        //            try
        //            {
        //                WshShell shell = new WshShell();
        //                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(System.IO.Path.GetFullPath(filePath));
        //                targetPath = shortcut.TargetPath;

        //                // 1. Try Custom Icon (Properties -> Change Icon)
        //                // Check if IconLocation is valid and NOT ",0" (which implies default)
        //                if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
        //                {
        //                    string[] iconParts = shortcut.IconLocation.Split(',');
        //                    string iconPath = iconParts[0];
        //                    int iconIndex = 0;
        //                    if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex))
        //                        iconIndex = parsedIndex;

        //                    if (System.IO.File.Exists(iconPath))
        //                    {
        //                        shortcutIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
        //                    }
        //                }
        //            }


        //            catch { }
        //        }
        //        }

        //    // 2. Fallback: Determine Icon if no custom icon found
        //    if (shortcutIcon == null)
        //    {
        //        if (isLink || isUrlFile)
        //        {
        //            // --- NEW: Smart Protocol Detection ---
        //            string urlTarget = targetPath; // Inherit from .lnk extraction if available

        //            if (string.IsNullOrEmpty(urlTarget))
        //            {
        //                try
        //                {
        //                    // Extract the actual URL from the .url file
        //                    var lines = System.IO.File.ReadAllLines(filePath);
        //                    var urlLine = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
        //                    if (urlLine != null) urlTarget = urlLine.Substring(4).Trim();
        //                }
        //                catch { }
        //            }

        //            // If it's a custom app protocol (e.g., spotify:, steam:), let IconManager grab the specific internal icon
        //            if (!string.IsNullOrEmpty(urlTarget) &&
        //                !urlTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        //                !urlTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        //            {
        //                shortcutIcon = IconManager.GetIconForFile(targetPath, filePath, isFolder, isLink, isShortcut, iconDict);
        //            }

        //            // If it's a standard web link, or the custom extraction failed, enforce the unified white theme
        //            if (shortcutIcon == null)
        //            {
        //                shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png")); //check
        //            }
        //        }
        //        else if (isShortcut)
        //        {
        //            try
        //            {
        //                // FIX: Use the robust Unicode-Safe reader (IShellLink)
        //                // This handles tricky paths better than WshShell
        //                if (string.IsNullOrEmpty(targetPath))
        //                    targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);

        //                // Fallback to WshShell only if utility failed and we haven't tried yet
        //                if (string.IsNullOrEmpty(targetPath))
        //                {
        //                    try
        //                    {
        //                        WshShell shell = new WshShell();
        //                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(System.IO.Path.GetFullPath(filePath));
        //                        targetPath = shortcut.TargetPath;
        //                    }
        //                    catch { }
        //                }

        //                // FIX 1: RUNTIME CORRECTION (Server Root)
        //                // If target is \\192.168.1.10, force isFolder=true immediately.
        //                if (IsUncRoot(targetPath))
        //                {
        //                    targetIsUncRoot = true;
        //                    isFolder = true;   // Force folder treatment!
        //                    isNetwork = true;  // Force network flag
        //                }
        //            }
        //            catch { }

        //            // A. Standard Checks
        //            bool targetIsFolder = false;
        //            bool targetExists = false;

        //            if (!string.IsNullOrEmpty(targetPath))
        //            {
        //                targetIsFolder = System.IO.Directory.Exists(targetPath) || targetIsUncRoot;
        //                targetExists = targetIsFolder || System.IO.File.Exists(targetPath);
        //            }

        //            // B. SCRAPING FALLBACK
        //            // If standard checks failed, scan the file content for \\Server pattern
        //            if (!targetExists)
        //            {
        //                if (ScrapeLnkForNetworkRoot(filePath))
        //                {
        //                    targetIsFolder = true;
        //                    isNetwork = true;      // Network Symbol
        //                    isFolder = true;       // Folder Shape
        //                    targetIsUncRoot = true; // CRITICAL: Force Validity (No White X)
        //                }
        //            }

        //            // C. Icon Selection Logic
        //            if (!isPortal && (isFolder || targetIsFolder))
        //            {
        //                shortcutIcon = null; // Force fall-through to White Theme
        //            }
        //            else if (targetExists)
        //            {
        //                shortcutIcon = Utility.GetShellIcon(targetPath, targetIsFolder);
        //            }
        //            else
        //            {
        //                shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
        //            }
        //        }
        //        else
        //        {
        //            // Standard File/Folder (Not a shortcut/link)
        //            if (!isPortal && isFolder)
        //            {
        //                shortcutIcon = null;
        //            }
        //            else
        //            {
        //                shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
        //            }
        //        }
        //    }

        //    // Final Fallback (The White Theme Logic)
        //    if (shortcutIcon == null)
        //    {
        //        if (isFolder)
        //        {
        //            // FIX 2: VALIDATION OVERRIDE
        //            // If it is a UNC Root (either detected via shortcut target or direct path), consider it "Valid"
        //            bool isUncRoot = targetIsUncRoot || IsUncRoot(filePath);
        //            bool valid = isUncRoot || FilePathUtilities.DoesFolderExist(filePath, isFolder);

        //            // Double check target if shortcut
        //            if (!valid && isShortcut && !string.IsNullOrEmpty(targetPath))
        //            {
        //                valid = IsUncRoot(targetPath) || System.IO.Directory.Exists(targetPath);
        //            }

        //            if (valid)
        //                shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
        //            else
        //                shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
        //        }
        //        else
        //        {
        //            shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
        //        }
        //    }

        //    ico.Source = shortcutIcon;


        //    // Apply Grayscale
        //    if (grayscale && shortcutIcon is BitmapSource bmp)
        //    {
        //        ico.Source = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, BitmapPalettes.Gray256, 0);
        //    }
        //    else if (grayscale)
        //    {
        //        ico.Opacity = 0.6;
        //        ico.Effect = new DropShadowEffect { Color = Colors.Gray, BlurRadius = 0, ShadowDepth = 0 };
        //    }

        //    // --- NEW: Apply Icon Tint ---
        //    double targetIconOpacity = 1.0;
        //    if (SettingsManager.ApplyTintToIcons)
        //    {
        //        // Convert TintValue (0-100) to Opacity (0.0-1.0)
        //        // We use a slight offset because 0% tint would make icons invisible.
        //        // A minimum of 20% opacity ensures they remain clickable.
        //        targetIconOpacity = Math.Max(0.2, SettingsManager.TintValue / 100.0);
        //        ico.Opacity = targetIconOpacity;
        //    }

        //    // --- STEP 4: Network Overlay ---
        //    if (isNetwork)
        //    {
        //        Grid iconGrid = new Grid { Width = iconWidth + 8, Height = iconHeight + 8 };
        //        iconGrid.Children.Add(ico);

        //        TextBlock networkIndicator = new TextBlock
        //        {
        //            Text = "🔗",
        //            FontSize = 14,
        //            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 135, 225)),
        //            HorizontalAlignment = HorizontalAlignment.Left,
        //            VerticalAlignment = VerticalAlignment.Top,
        //            Margin = new Thickness(2, 2, 0, 0),
        //            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, Opacity = 0.7 }
        //        };

        //        iconGrid.Children.Add(networkIndicator);
        //        sp.Children.Add(iconGrid);
        //    }
        //    else
        //    {
        //        sp.Children.Add(ico);
        //    }

        //    // --- STEP 5: Text Label ---
        //    string displayName = (!iconDict.ContainsKey("DisplayName") || iconDict["DisplayName"] == null)
        //        ? System.IO.Path.GetFileNameWithoutExtension(filePath)
        //        : (string)iconDict["DisplayName"];

        //    // --- BUG FIX: Recover missing names for UNC Roots and Drives ---
        //    if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(filePath))
        //    {
        //        displayName = filePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
        //                              .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
        //                              .LastOrDefault();
        //        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Unknown";
        //    }

        //    if (displayName.Length > SettingsManager.MaxDisplayNameLength)
        //        displayName = displayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "...";

        //    System.Windows.Media.Brush textBrush = System.Windows.Media.Brushes.White;
        //    if (!string.IsNullOrEmpty(textColorName))
        //    {
        //        try { textBrush = new SolidColorBrush(Utility.GetColorFromName(textColorName)); } catch { }
        //    }

        //    TextBlock lbl = new TextBlock
        //    {
        //        Text = displayName,
        //        TextWrapping = TextWrapping.Wrap,
        //        TextAlignment = TextAlignment.Center,
        //        HorizontalAlignment = HorizontalAlignment.Center,
        //        Foreground = textBrush,
        //        MaxWidth = 70,
        //        Opacity = SettingsManager.ApplyTintToIcons ? Math.Max(0.2, SettingsManager.TintValue / 100.0) : 1.0
        //    };

        //    if (!disableShadow)
        //    {
        //        lbl.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 315, ShadowDepth = 2, BlurRadius = 3, Opacity = 0.8 };
        //    }

        //    sp.Children.Add(lbl);
        //    sp.Tag = new { FilePath = filePath, IsFolder = isFolder, Arguments = (string)(iconDict.ContainsKey("Arguments") ? iconDict["Arguments"] : null) };

        //    // FIX: Show the final target in the tooltip instead of the internal shortcut path
        //    string toolTipText = displayName;
        //    string resolvedTarget = filePath;

        //    if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
        //    {
        //        resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
        //    }
        //    else if (System.IO.Path.GetExtension(filePath).ToLower() == ".url")
        //    {
        //        try
        //        {
        //            // Try to extract the true web URL or protocol from the file
        //            string content = System.IO.File.ReadAllText(filePath);
        //            var match = System.Text.RegularExpressions.Regex.Match(content, @"URL=([^\r\n]+)");
        //            if (match.Success) resolvedTarget = match.Groups[1].Value.Trim();
        //        }
        //        catch { }
        //    }

        //    // Fallback in case extraction fails
        //    if (string.IsNullOrEmpty(resolvedTarget)) resolvedTarget = filePath;

        //    string displayTarget = resolvedTarget;

        //    // Clean up MS Store App AUMID targets (e.g. Microsoft.WindowsCalculator_8wekyb3d8bbwe!App)
        //    if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
        //    {
        //        string packageId = displayTarget.Split('!')[0];
        //        int hashIndex = packageId.IndexOf('_');
        //        if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex);
        //        displayTarget = $"Windows App ({packageId})";
        //    }

        //    if (resolvedTarget != filePath)
        //    {
        //        toolTipText += $"\nTarget: {displayTarget}";
        //    }
        //    else
        //    {
        //        toolTipText += $"\nLocation: {displayTarget}";
        //    }

        //    sp.ToolTip = new ToolTip { Content = toolTipText };
        //    wpcont.Children.Add(sp);
        //}


        public static void CreateNativePanel() => CreateNewFrame("", "Data");


        /// <summary>
        /// HELPER: Extracts a Network Root path (e.g. \\Server) from a .lnk file by reading raw content.
        /// Used when Windows APIs fail to resolve the target.
        /// </summary>
        private static string GetScrapedNetworkPath(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath) || !System.IO.File.Exists(lnkPath)) return null;

                string fileContent;
                // Read with Share.ReadWrite to avoid locking issues
                using (var fs = new System.IO.FileStream(lnkPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.Default))
                {
                    fileContent = sr.ReadToEnd();
                }

                // Regex to capture \\Servername or \\1.2.3.4
                // Matches start of line or null-preceded, followed by \\, then valid host chars, ending with null or newline
                var regex = new System.Text.RegularExpressions.Regex(@"(^|\0)\\\\([a-zA-Z0-9\.\-_]+)(\x00|$)", System.Text.RegularExpressions.RegexOptions.Multiline);
                var match = regex.Match(fileContent);

                if (match.Success)
                {
                    // Clean up the match (remove leading nulls if any) to get just "\\Server"
                    string raw = match.Value.Trim('\0', '\r', '\n');
                    if (raw.StartsWith(@"\\")) return raw;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Scrape path failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// SAFE FALLBACK: Scans the raw binary content of a .lnk file for Network Root patterns (e.g. \\192.168.1.10).
        /// This bypasses Windows API validation failures for server roots or offline locations.
        /// </summary>
        private static bool ScrapeLnkForNetworkRoot(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath) || !System.IO.File.Exists(lnkPath)) return false;

                // 1. Read file safely with a timeout mechanism (handled by standard stream opening)
                // Use default encoding to capture ANSI/ASCII strings embedded in the binary
                string fileContent;
                using (var fs = new System.IO.FileStream(lnkPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.Default))
                {
                    fileContent = sr.ReadToEnd();
                }

                // 2. Define Regex for UNC Root: Matches \\Server or \\192.168.1.1
                // Explanation:
                // \\\\       -> Literal "\\"
                // [^\\]+     -> Any character that is NOT a backslash (The server name/IP)
                // (\x00|$)   -> Ends with a null byte or end of string (typical in binary formats)
                var networkRootRegex = new System.Text.RegularExpressions.Regex(@"^\\\\([a-zA-Z0-9\.\-_]+)(\x00|$)", System.Text.RegularExpressions.RegexOptions.Multiline);

                // 3. Scan the content
                // We scan for specific "unc" prefixes often found in LNK structures or just the raw path
                if (networkRootRegex.IsMatch(fileContent)) return true;

                // Fallback simple string check for the user's specific case (\\IP) if regex is too strict on binary noise
                // We look for "\\" followed by a digit (IP) that appears in the file
                int index = fileContent.IndexOf(@"\\");
                if (index >= 0 && index + 3 < fileContent.Length)
                {
                    char nextChar = fileContent[index + 2];
                    // If it looks like \\1... or \\a... it's likely a path. 
                    // To be safe, we check if it DOESN'T look like a file path (no ":\")
                    bool hasColon = fileContent.IndexOf(@":\", index) == index + 2; // e.g. C:\ check
                    if (!hasColon) return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // SANITIZATION: Never let a scraping error break the UI loading
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Lnk scraping suppressed error: {ex.Message}");
                return false;
            }
        }


        private static void UpdateLockState(TextBlock lockIcon, dynamic frame, bool? forceState = null, bool saveToJson = true)
        {
            // Get the actual frame from FrameDataManager.FrameData using Id to ensure correct reference
            string frameId = frame.Id?.ToString();
            if (string.IsNullOrEmpty(frameId))
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Frame '{frame.Title}' has no Id, cannot update lock state");
                return;
            }

            int index = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
            if (index < 0)
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Frame '{frame.Title}' not found in FrameDataManager.FrameData, cannot update lock state");
                return;
            }

            dynamic actualFrame = FrameDataManager.FrameData[index];
            bool isLocked = forceState ?? (actualFrame.IsLocked?.ToString().ToLower() == "true");

            // Only update JSON if explicitly requested (e.g., during toggle, not initialization)
            if (saveToJson)
            {
                UpdateFrameProperty(actualFrame, "IsLocked", isLocked.ToString().ToLower(), $"Frame {(isLocked ? "locked" : "unlocked")}");
            }

            // Update UI on the main thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>


            {
                // Update lock icon
                GameTheme.ApplyLockIconStyle(lockIcon, isLocked);
                lockIcon.ToolTip = isLocked ? "Frame is locked (click to unlock)" : "frame is unlocked (click to lock)";

                // Find the NonActivatingWindow
                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(lockIcon);
                if (win == null)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Could not find NonActivatingWindow for frame '{actualFrame.Title}'");
                    return;
                }

                // Update ResizeMode
                win.ResizeMode = isLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set ResizeMode to {win.ResizeMode} for frame '{actualFrame.Title}'");
            });

            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Updated lock state for frame '{actualFrame.Title}': IsLocked={isLocked}");
        }


        // --- HELPER: Manual Icon Extraction for .url Files ---
        private static (string Path, int Index) GetUrlCustomIcon(string urlPath)
        {
            try
            {
                if (!System.IO.File.Exists(urlPath)) return (null, 0);
                var lines = System.IO.File.ReadAllLines(urlPath);
                string iconFile = null;
                int iconIndex = 0;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconFile = trimmed.Substring(9).Trim();
                    else if (trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(trimmed.Substring(10).Trim(), out iconIndex);
                }

                if (!string.IsNullOrEmpty(iconFile)) return (iconFile, iconIndex);
            }
            catch { }
            return (null, 0);
        }


        public static void EditItem(dynamic icon, dynamic frame, NonActivatingWindow win)
        {
            IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();
            string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
            string displayName = iconDict.ContainsKey("DisplayName") ? (string)iconDict["DisplayName"] : System.IO.Path.GetFileNameWithoutExtension(filePath);

            // --- BUG FIX: Handle empty display names for UNC roots in editor ---
            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(filePath))
            {
                displayName = filePath.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault();
                if (string.IsNullOrWhiteSpace(displayName)) displayName = "Unknown";
            }

            // Allow .lnk and .url
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            bool isEditable = ext == ".lnk" || ext == ".url";

            if (!isEditable)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgEditNotAvailable, Strings.DlgInfo);
                return;
            }

            var editWindow = new EditShortcutWindow(filePath, displayName);

            // Note: The EditWindow calls 'UpdateFrameDataForIcon' internally when Save is clicked.
            // We don't need to duplicate the saving logic here.
            if (editWindow.ShowDialog() == true)
            {
                string newDisplayName = editWindow.NewDisplayName;

                // Update local memory reference immediately for responsiveness
                iconDict["DisplayName"] = newDisplayName;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"EditItem: Local display name updated to {newDisplayName}");

                // UI Refresh Logic
                if (win == null) return;

                WrapPanel wpcont = null;
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer != null) wpcont = scrollViewer.Content as WrapPanel;

                if (wpcont == null) wpcont = FrameUtilities.FindWrapPanel(win);

                if (wpcont != null)
                {
                    var sp = wpcont.Children.OfType<StackPanel>()
                        .FirstOrDefault(s => s.Tag != null && s.Tag.GetType().GetProperty("FilePath")?.GetValue(s.Tag)?.ToString() == filePath);

                    if (sp != null)
                    {
                        RefreshSingleIconComplete(sp, filePath, newDisplayName, win);
                    }
                    else
                    {
                        // Fallback: Rebuild if not found (e.g. Tab switch might have hidden it)
                        // In most cases RefreshSingleIconComplete handles it, or a full frame refresh will catch it later.
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, "StackPanel not found for immediate refresh, data is saved though.");
                    }
                }
            }
        }


        private static void BackupOrRestoreShortcut(string filePath, bool targetExists, bool isFolder)
        {
            // FIX: Use ProfileManager to get the path relative to the active profile
            string tempShortcutsDir = ProfileManager.GetProfileFilePath("Temp Shortcuts");

            string backupFileName = System.IO.Path.GetFileName(filePath);
            string backupPath = System.IO.Path.Combine(tempShortcutsDir, backupFileName);

            try
            {
                // Ensure TempShortcuts directory exists
                if (!Directory.Exists(tempShortcutsDir))
                {
                    Directory.CreateDirectory(tempShortcutsDir);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Created TempShortcuts directory: {tempShortcutsDir}");
                }

                if (!targetExists)
                {
                    // Backup shortcut if target is missing and not already backed up
                    if (System.IO.File.Exists(filePath) && !System.IO.File.Exists(backupPath))
                    {
                        System.IO.File.Copy(filePath, backupPath, true);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Backed up shortcut {filePath} to {backupPath}");
                    }
                }
                else
                {
                    // Restore shortcut from backup if target exists
                    if (System.IO.File.Exists(backupPath))
                    {
                        // Verify the backup has a custom icon before restoring
                        WshShell shell = new WshShell();
                        IWshShortcut backupShortcut = (IWshShortcut)shell.CreateShortcut(backupPath);
                        if (!string.IsNullOrEmpty(backupShortcut.IconLocation))
                        {
                            System.IO.File.Copy(backupPath, filePath, true);
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Restored shortcut {filePath} from {backupPath} with custom icon");
                        }
                        // Delete backup
                        System.IO.File.Delete(backupPath);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Deleted backup {backupPath} after restoration");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error in BackupOrRestoreShortcut for {filePath}: {ex.Message}");
            }
        }

       /// <summary>
        /// Refresh frame using the same approach as CustomizeFrameForm
        /// This ensures consistent sizing and behavior
        /// </summary>
        public static void RefreshFrameUsingFormApproach(NonActivatingWindow win, dynamic frame)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Refreshing frame using form approach for '{frame.Title}'");

                // Find the WrapPanel
                var wrapPanel = FindWrapPanel(win);
                if (wrapPanel == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Cannot find WrapPanel for frame refresh");
                    return;
                }

                // Clear existing icons
                wrapPanel.Children.Clear();

                // TABS FEATURE: Check if tabs are enabled and load from appropriate source
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";
                JArray items = null;

                if (tabsEnabled)
                {
                    // Load from current tab
                    var tabs = frame.Tabs as JArray ?? new JArray();
                    int currentTab = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");

                    if (currentTab >= 0 && currentTab < tabs.Count)
                    {
                        var activeTab = tabs[currentTab] as JObject;
                        if (activeTab != null)
                        {
                            items = activeTab["Items"] as JArray ?? new JArray();
                            string tabName = activeTab["TabName"]?.ToString() ?? $"Tab {currentTab}";
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                                $"Refreshing icons from tab '{tabName}' for frame '{frame.Title}'");
                        }
                    }
                }
                else
                {
                    // Load from main Items array (existing behavior)
                    items = frame.Items as JArray;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        $"Refreshing icons from main Items for frame '{frame.Title}'");
                }

                if (items != null)
                {
                    // Sort items by DisplayOrder and add them (SAME AS FORM)
                    var sortedItems = items
                        .OfType<JObject>()
                        .OrderBy(item => item["DisplayOrder"]?.Value<int>() ?? 0)
                        .ToList();

                    foreach (dynamic item in sortedItems)
                    {
                        // Use the SAME method as the working form
                        AddIcon(item, wrapPanel);

                        // Add basic event handlers (SAME AS FORM)
                        if (wrapPanel.Children.Count > 0)
                        {
                            var sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                string filePath = item.Filename?.ToString() ?? "Unknown";
                                bool isFolder = item.IsFolder?.ToString().ToLower() == "true";
                                ClickEventAdder(sp, filePath, isFolder);

                                // FIX: Attach the missing Context Menu
                                AttachIconContextMenu(sp, item, frame, win);
                            }
                        }

                    }

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Successfully refreshed {sortedItems.Count} icons for frame '{frame.Title}' using form approach");
                }

                // 刷新後完成版面更新；整區位圖快取的取捨由建立面板時處理。
                win.UpdateLayout();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing frame using form approach: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to find WrapPanel in frame window
        /// </summary>
        private static WrapPanel FindWrapPanel(NonActivatingWindow win)
        {
            try
            {
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                return scrollViewer?.Content as WrapPanel;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error finding WrapPanel: {ex.Message}");
                return null;
            }
        }


        // [TARGET VALIDATION ENGINE - START]
        private static void UpdateIcon(StackPanel sp, string filePath, bool isFolder, string resolvedTargetPath = null)
        {
            if (System.Windows.Application.Current == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (filePath != null && filePath.StartsWith("INTERNAL_BLANK_")) return;

                // 1. OPTIMIZATION: Cache Check (Ensures local proxy file exists)
                bool fileExists = System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath);
                if (!fileExists) return;

                DateTime currentLastWrite;
                try { currentLastWrite = System.IO.File.GetLastWriteTime(filePath); }
                catch { return; }

                bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
                bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";
                bool targetValid = true;

                // 2. TARGET VALIDATION LOGIC (Native File Polling, No COM)
                if (isShortcut && !Utility.IsStoreAppShortcut(filePath))
                {
                    string tPath = resolvedTargetPath ?? FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                    targetValid = !string.IsNullOrEmpty(tPath) &&
                                 (System.IO.File.Exists(tPath) || System.IO.Directory.Exists(tPath));
                }
                else if (!isShortcut)
                {
                    targetValid = System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath);
                }
                // [TARGET VALIDATION ENGINE - END]

                bool isNowBroken = !targetValid;

                if (_iconStates.TryGetValue(filePath, out var lastState))
                {
                    if (lastState.LastWrite == currentLastWrite && lastState.IsBroken == isNowBroken)
                        return; // Cache hit
                }
                _iconStates[filePath] = (currentLastWrite, isNowBroken);

                // 2. Identify Web Link (Do NOT return early anymore)
                bool isWebLink = isUrlFile;
                if (!isWebLink)
                {
                    try
                    {
                        foreach (var frame in FrameDataManager.FrameData)
                        {
                            if (frame.ItemsType?.ToString() == "Data")
                            {
                                bool CheckList(JArray list)
                                {
                                    if (list == null) return false;
                                    foreach (var item in list)
                                    {
                                        string itemPath = item["Filename"]?.ToString();
                                        if (!string.IsNullOrEmpty(itemPath) && string.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase))
                                            return item["IsLink"]?.ToObject<bool>() ?? false;
                                    }
                                    return false;
                                }
                                if (CheckList(frame.Items as JArray)) { isWebLink = true; break; }
                                var tabs = frame.Tabs as JArray;
                                if (tabs != null)
                                {
                                    foreach (var tab in tabs) if (CheckList(tab["Items"] as JArray)) { isWebLink = true; goto FoundLink; }
                                }
                            }
                        }
                    FoundLink:;
                    }
                    catch { }
                }

                System.Windows.Controls.Image ico = sp.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (ico == null) return;

                // 3. MAIN ICON LOGIC
                ImageSource newIcon = null;

                // [TARGET VALIDATION ENGINE - SECONDARY PATCH]
                string targetPath = isShortcut ? (resolvedTargetPath ?? FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath)) : filePath;

                bool targetExists = System.IO.File.Exists(targetPath) || System.IO.Directory.Exists(targetPath);
                bool isTargetFolder = System.IO.Directory.Exists(targetPath);
                if (isShortcut && isTargetFolder) isFolder = true;

                if (isShortcut) BackupOrRestoreShortcut(filePath, targetExists, isFolder);

                
                // --- PRIORITY LOGIC START ---
                // CASE A: WEB LINKS
                if (isWebLink)
                {
                    // 1. Try Custom Icon first
                    var urlIcon = GetUrlCustomIcon(filePath); // <--- USE NEW HELPER
                    if (urlIcon.Path != null)
                    {
                        newIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                    }
                    else
                    {
                        // Try WshShell only if manual failed (legacy fallback)
                        try { /* existing WshShell logic */ } catch { }
                    }

                    // 2. Fallback to Theme or Custom Protocol
                    if (newIcon == null)
                    {
                        // Recheck the actual target inside the file
                        string fileText = "";
                        try { fileText = System.IO.File.ReadAllText(filePath).ToLower(); } catch { }

                        if (fileText.Contains("spotify:") || fileText.Contains("spotify.com"))
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/spotify-White.png"));
                        }
                        else if (fileText.Contains("steam://"))
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/steam-White.png"));
                        }
                        else
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));
                        }
                    }
                }
                // CASE B: FOLDERS
                else if (isFolder)
                {
                    // 1. Broken Target -> White X
                    if (!FilePathUtilities.DoesFolderExist(filePath, isFolder))
                    {
                        newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                    }

                    // 2. Custom Icon
                    if (newIcon == null && isShortcut)
                    {
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                            if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                            {
                                string[] iconParts = shortcut.IconLocation.Split(',');
                                string iconPath = iconParts[0];
                                int iconIndex = 0;
                                if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex))
                                    iconIndex = parsedIndex;

                                if (System.IO.File.Exists(iconPath))
                                    newIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                            }
                        }
                        catch { }
                    }

                    // 3. Theme Fallback
                    if (newIcon == null)
                    {
                        NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                        string frameId = win?.Tag?.ToString();
                        var frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                        bool isPortal = frame != null && frame.ItemsType?.ToString() == "Portal";

                        newIcon = Utility.GetShellIcon(filePath, true);
                        if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                    }
                }
                // CASE C: SHORTCUT FILES (.lnk) - 優先解析，即使目標暫時不可達亦保留捷徑原始圖示
                else if (isShortcut)
                {
                    // 1. 嘗試由捷徑指定的 IconLocation 提取自訂圖示
                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                        if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                        {
                            string[] iconParts = shortcut.IconLocation.Split(',');
                            string iconPath = iconParts[0];
                            int iconIndex = 0;
                            if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex)) iconIndex = parsedIndex;

                            if (System.IO.File.Exists(iconPath))
                                newIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                        }
                    }
                    catch { }

                    // 2. 目標存在時，提取目標之高解析圖示
                    if (newIcon == null && targetPath != null && targetExists)
                    {
                        newIcon = Utility.GetShellIcon(targetPath, isTargetFolder);
                    }

                    // 3. 若目標暫時未就緒（網路磁碟機、NAS 待機、或權限路徑），從 .lnk 自身快取提取真實圖示，絕不隨意塗成紅 X
                    if (newIcon == null)
                    {
                        if (Utility.IsStoreAppShortcut(filePath))
                        {
                            try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                            catch { newIcon = Utility.GetShellIcon(filePath, isFolder); }
                        }
                        else
                        {
                            try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                            catch { }

                            if (newIcon == null)
                            {
                                newIcon = Utility.GetShellIcon(filePath, false);
                            }
                        }
                    }

                    // 4. 僅在捷徑本體完全損毀且目標確實不存在時，才顯示損毀警告圖示
                    if (newIcon == null && !targetExists)
                    {
                        newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                    }
                }
                // CASE D: BROKEN FILES (非捷徑且檔案不存在之一般檔案)
                else if (!targetExists)
                {
                    newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                }
                // CASE E: STANDARD FILES
                //else
                //{
                //    try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                //    catch { newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png")); }
                //}
                // CASE E: STANDARD FILES
                else
                {
                    // 優先使用系統 Shell 高清圖示（Jumbo 256x256 或 ExtraLarge 48x48）
                    newIcon = Utility.GetShellIcon(filePath, false);
                    if (newIcon == null)
                    {
                        try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                        catch
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                        }
                    }
                }


                // Apply
                if (ico.Source != newIcon && newIcon != null)
                {
                    ico.Source = newIcon;
                    if (IconManager.IconCache.ContainsKey(filePath))
                    {
                        IconManager.IconCache[filePath] = newIcon;
                    }
                }
            });
        }

        
        public static void UpdateOptionsAndClickEvents()
        {


            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Updating options, new singleClickToLaunch={SettingsManager.SingleClickToLaunch}");

            // Update _options
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
                CheckNetworkPaths = false // Keep this as is
            };


            if (System.Windows.Application.Current != null)
            {

                // Force UI update on the main thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                int updatedItems = 0;
                foreach (var win in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
                {
                    var wpcont = ((win.Content as Border)?.Child as DockPanel)?.Children
                        .OfType<ScrollViewer>().FirstOrDefault()?.Content as WrapPanel;
                    if (wpcont != null)
                    {
                        foreach (var sp in wpcont.Children.OfType<StackPanel>())
                        {
                            string path = sp.Tag as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                bool isFolder = Directory.Exists(path) ||
                                    (System.IO.Path.GetExtension(path).ToLower() == ".lnk" &&
                                     Directory.Exists(Utility.GetShortcutTarget(path)));
                                string arguments = null;
                                if (System.IO.Path.GetExtension(path).ToLower() == ".lnk")
                                {
                                    WshShell shell = new WshShell();
                                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(path);
                                    arguments = shortcut.Arguments;
                                }
                                ClickEventAdder(sp, path, isFolder, arguments);
                                updatedItems++;
                            }
                        }
                    }
                }
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Updated click events for {updatedItems} items");
            });
            }
            else
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, "Application.Current is null, cannot update icon.");
            }
        }


        /// <summary>高亮選取指定圖示面板（半透明藍色背景），同時獲取焦點以響應 Delete 快捷鍵</summary>
        private static void SetSelectedIcon(StackPanel sp)
        {
            DeselectIcon();
            _currentlySelectedIconPanel = sp;
            sp.Background = GameTheme.ItemSelectedBrush;

            try
            {
                sp.Focusable = true;
                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                if (win != null)
                {
                    win.EnableFocusPrevention(false);
                    win.Activate();
                }
                sp.Focus();
            }
            catch { }
        }

        /// <summary>取消目前選取的圖示高亮</summary>
        private static void DeselectIcon()
        {
            if (_currentlySelectedIconPanel != null)
            {
                try
                {
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(_currentlySelectedIconPanel);
                    if (win != null)
                    {
                        win.EnableFocusPrevention(true);
                    }
                }
                catch { }

                _currentlySelectedIconPanel.Background = GameTheme.GetItemBackground(_currentlySelectedIconPanel);
                _currentlySelectedIconPanel = null;
            }
        }

        /// <summary>自指定分區移除目前選取的圖示（僅移除捷徑或分區項目，絕不刪除實體檔案）</summary>
        public static void RemoveSelectedIconFromFrame(StackPanel selectedSp)
        {
            if (selectedSp == null) return;
            try
            {
                var tagObj = selectedSp.Tag;
                if (tagObj == null) return;
                string filePath = null;
                var tagType = tagObj.GetType();
                var fpProp = tagType.GetProperty("FilePath");
                if (fpProp != null) filePath = fpProp.GetValue(tagObj) as string;
                if (string.IsNullOrEmpty(filePath)) return;

                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(selectedSp);
                string frameId = win?.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (liveFrame == null) return;

                // 找到對應的 JArray
                JArray targetArray = liveFrame.Items as JArray;
                bool tabsEnabled = liveFrame.TabsEnabled?.ToString().ToLower() == "true";
                if (tabsEnabled)
                {
                    var tabs = liveFrame.Tabs as JArray;
                    int tabIdx = Convert.ToInt32(liveFrame.CurrentTab?.ToString() ?? "0");
                    if (tabs != null && tabIdx < tabs.Count)
                        targetArray = tabs[tabIdx]["Items"] as JArray;
                }
                if (targetArray == null) return;

                string displayName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                bool isSpacer = filePath.StartsWith("INTERNAL_BLANK_");
                string confirmMsg = isSpacer
                    ? Strings.Get("MsgConfirmRemoveItem", Strings.MenuSpacerBlank)
                    : Strings.Get("MsgConfirmRemoveItem", displayName);

                var result = System.Windows.MessageBox.Show(
                    confirmMsg + "\n\n" + Strings.Get("MsgRemoveFromFrameOnly"),
                    Strings.DlgInfo,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                // 找 liveItem：優先精確字串比對，次之以 GetFullPath 比對
                JToken liveItem = null;
                if (isSpacer)
                {
                    liveItem = targetArray.FirstOrDefault(i => i["Filename"]?.ToString() == filePath);
                }
                else
                {
                    liveItem = targetArray.FirstOrDefault(i => string.Equals(i["Filename"]?.ToString(), filePath, StringComparison.OrdinalIgnoreCase));
                    if (liveItem == null)
                    {
                        string normalizedFilePath = null;
                        try { normalizedFilePath = System.IO.Path.GetFullPath(filePath); } catch { }

                        if (!string.IsNullOrEmpty(normalizedFilePath))
                        {
                            liveItem = targetArray.FirstOrDefault(i =>
                            {
                                try
                                {
                                    string itemPath = i["Filename"]?.ToString();
                                    if (string.IsNullOrEmpty(itemPath)) return false;
                                    return string.Equals(System.IO.Path.GetFullPath(itemPath), normalizedFilePath, StringComparison.OrdinalIgnoreCase);
                                }
                                catch { return false; }
                            });
                        }
                    }
                }

                if (liveItem != null)
                {
                    RemoveDataItem(targetArray, liveItem);
                    var wp = VisualTreeHelper.GetParent(selectedSp) as WrapPanel;
                    if (wp != null)
                    {
                        wp.Children.Remove(selectedSp);
                        UpdateAdaptiveIconGrid(wp);
                    }
                    DeselectIcon();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Delete key remove error: {ex.Message}");
            }
        }

        internal static void RemoveDataItem(JArray items, JToken item)
        {
            if (items == null || item == null || !ReferenceEquals(item.Parent, items)) return;
            string path = item["Filename"]?.ToString() ?? string.Empty;
            items.Remove(item);
            FrameDataManager.SaveFrameData();
            CleanupUnreferencedShortcut(path);
        }

        internal static void CleanupUnreferencedShortcut(string path)
        {
            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProfileManager.CurrentProfileDir, "Shortcuts"))
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                string extension = System.IO.Path.GetExtension(fullPath);
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    !(extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))) return;
                if ((System.IO.File.GetAttributes(root) & System.IO.FileAttributes.ReparsePoint) != 0) return;
                // 讀取已保存配置，包含所有主區與分頁；保存失敗或仍有引用時不刪副本。
                var saved = JArray.Parse(System.IO.File.ReadAllText(FrameDataManager.JsonFilePath));
                if (saved.Descendants().OfType<JProperty>().Any(p => p.Name == "Filename" &&
                    string.Equals(System.IO.Path.GetFullPath(p.Value.ToString()), fullPath, StringComparison.OrdinalIgnoreCase))) return;
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"捷徑副本未清理：{ex.Message}");
            }
        }

        public static void ClickEventAdder(StackPanel sp, string path, bool isFolder, string arguments = null)
        {
            // Store only path, isFolder, and arguments in Tag
            sp.Tag = new { FilePath = path, IsFolder = isFolder, Arguments = arguments };

            // --- HANG FIX 1 (Startup): Removed synchronous Utility.GetShortcutTarget(path) from here. ---
            // The folder double-check is deferred until the exact moment of clicking to prevent startup freezing.
            bool isShortcut = System.IO.Path.GetExtension(path).ToLower() == ".lnk";

            // --- NAMED LOCAL FUNCTIONS FOR EVENTS ---
            System.Windows.Point? iconMouseDownPos = null;
            bool isIconDragging = false;

            void MouseDownHandler(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;

                // 1. Double click to launch / open
                if (e.ClickCount == 2)
                {
                    iconMouseDownPos = null;
                    isIconDragging = false;

                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                    string frameId = win?.Tag?.ToString();
                    dynamic frame = null;
                    if (!string.IsNullOrEmpty(frameId))
                        frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

                    if (frame != null && frame.ItemsType?.ToString() == "Portal")
                    {
                        // 僅在按住 Ctrl + 連點兩下時才執行「框內原地切換路徑」；
                        // 一般連點兩下直接由下方 LaunchItem 以檔案總管開啟資料夾（符合桌面直覺與詳細資料檢視行為）
                        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            string portalTargetFolder = isFolder ? path : null;
                            if (string.IsNullOrEmpty(portalTargetFolder) && isShortcut)
                            {
                                try
                                {
                                    string resolved = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                                    if (!string.IsNullOrEmpty(resolved) && System.IO.Directory.Exists(resolved))
                                        portalTargetFolder = resolved;
                                }
                                catch { }
                            }

                            if (!string.IsNullOrEmpty(portalTargetFolder) && System.IO.Directory.Exists(portalTargetFolder))
                            {
                                NavigatePortalFrame(frame, portalTargetFolder);
                                e.Handled = true;
                                return;
                            }
                        }
                    }

                    e.Handled = true; // Mark handled immediately so the UI thread is freed

                    // --- UX 優化：UI 執行緒立即播放點擊反饋動畫（0ms 零延遲手感，不等待背景解析） ---
                    TriggerLaunchEffect(sp, frame);

                    // --- HANG FIX 2 (Click): Move network resolution to an STA Background Thread ---
                    System.Threading.Thread launchThread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            bool isShortcutLocal = System.IO.Path.GetExtension(path).ToLower() == ".lnk";
                            bool targetExists;
                            string resolvedPath = path;
                            bool dynamicIsFolder = isFolder;

                            if (isShortcutLocal)
                            {
                                resolvedPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                                if (string.IsNullOrEmpty(resolvedPath))
                                {
                                    targetExists = false;
                                }
                                else
                                {
                                    targetExists = dynamicIsFolder ? System.IO.Directory.Exists(resolvedPath) : System.IO.File.Exists(resolvedPath);
                                    if (!dynamicIsFolder && System.IO.Directory.Exists(resolvedPath))
                                    {
                                        dynamicIsFolder = true;
                                        targetExists = true;
                                    }
                                }
                            }
                            else
                            {
                                targetExists = dynamicIsFolder ? System.IO.Directory.Exists(path) : System.IO.File.Exists(path);
                                if (!targetExists && !dynamicIsFolder && System.IO.Directory.Exists(path))
                                {
                                    dynamicIsFolder = true;
                                    targetExists = true;
                                }
                            }

                            bool isStoreApp = false;
                            string scrapedPath = null;

                            if (!targetExists && isShortcutLocal)
                            {
                                isStoreApp = Utility.IsStoreAppShortcut(path);
                                if (!isStoreApp)
                                {
                                    try { scrapedPath = GetScrapedNetworkPath(path); } catch { }
                                    if (!string.IsNullOrEmpty(scrapedPath))
                                    {
                                        targetExists = true;
                                    }
                                }
                            }

                            if (!targetExists && !isStoreApp) return;

                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { UpdateIcon(sp, path, dynamicIsFolder, resolvedPath); } catch { }
                                LaunchItem(sp, path, dynamicIsFolder, arguments, triggerEffect: false);
                            }));
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error checking target existence: {ex.Message}");
                        }
                    });

                    launchThread.SetApartmentState(System.Threading.ApartmentState.STA);
                    launchThread.IsBackground = true;
                    launchThread.Start();
                    return;
                }

                // 2. Single click: record position for drag detection
                if (e.ClickCount == 1)
                {
                    iconMouseDownPos = e.GetPosition(sp);
                    isIconDragging = false;
                }
            }

            void MouseMoveHandler(object sender, MouseEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed && iconMouseDownPos.HasValue && !isIconDragging && !IconDragDropManager.IsDragging)
                {
                    System.Windows.Point currentPos = e.GetPosition(sp);
                    double dragThresholdX = Math.Max(10.0, SystemParameters.MinimumHorizontalDragDistance * 2.5);
                    double dragThresholdY = Math.Max(10.0, SystemParameters.MinimumVerticalDragDistance * 2.5);
                    if (Math.Abs(currentPos.X - iconMouseDownPos.Value.X) >= dragThresholdX ||
                        Math.Abs(currentPos.Y - iconMouseDownPos.Value.Y) >= dragThresholdY)
                    {
                        NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                        string frameId = win?.Tag?.ToString();
                        dynamic frame = null;
                        if (!string.IsNullOrEmpty(frameId))
                            frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

                        if (frame != null && Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
                        {
                            isIconDragging = true;
                            try
                            {
                                var data = new DataObject(DataFormats.FileDrop, new string[] { path });
                                System.IO.MemoryStream dropEffect = new System.IO.MemoryStream(new byte[] { 2, 0, 0, 0 });
                                data.SetData("Preferred DropEffect", dropEffect);
                                DragDrop.DoDragDrop(sp, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"專用收納圖示拖曳異常: {ex.Message}");
                            }
                            finally
                            {
                                isIconDragging = false;
                                iconMouseDownPos = null;
                                try
                                {
                                    var pEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameId);
                                    pEntry.Value?.TriggerSync(immediate: true);
                                    Desktop_Frames.Services.FenceInventoryManager.RefreshDesktopShell();
                                }
                                catch { }
                            }
                            return;
                        }

                        isIconDragging = true;
                        IconDragDropManager.StartIconDrag(sp, iconMouseDownPos.Value);
                    }
                }

                if (IconDragDropManager.IsDragging)
                {
                    try
                    {
                        System.Windows.Point screenPosition = sp.PointToScreen(e.GetPosition(sp));
                        IconDragDropManager.HandleDragMove(screenPosition);
                    }
                    catch { }
                }
            }

            void MouseUpHandler(object sender, MouseButtonEventArgs e)
            {
                if (IconDragDropManager.IsDragging)
                {
                    try
                    {
                        System.Windows.Point screenPosition = sp.PointToScreen(e.GetPosition(sp));
                        IconDragDropManager.CompleteDrag(screenPosition);
                        e.Handled = true;
                    }
                    catch
                    {
                        IconDragDropManager.CancelDrag();
                    }
                }
                else if (e.ChangedButton == MouseButton.Left && iconMouseDownPos.HasValue && !isIconDragging)
                {
                    // 單點選取：左鍵放開且非拖曳 → 高亮此圖示
                    SetSelectedIcon(sp);
                }
                iconMouseDownPos = null;
                isIconDragging = false;
            }

            void KeyUpHandler(object sender, KeyEventArgs e)
            {
                if (IconDragDropManager.IsDragging && e.Key == Key.Escape)
                {
                    IconDragDropManager.CancelDrag();
                    e.Handled = true;
                }
            }

            void KeyDownHandler(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Delete && _currentlySelectedIconPanel == sp)
                {
                    RemoveSelectedIconFromFrame(sp);
                    e.Handled = true;
                }
            }

            // --- SAFELY REMOVE PREVIOUS HANDLERS (WPF APPROACH) ---
            sp.RemoveHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(MouseDownHandler));
            sp.RemoveHandler(UIElement.MouseMoveEvent, new MouseEventHandler(MouseMoveHandler));
            sp.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(MouseUpHandler));
            sp.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUpHandler));
            sp.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(KeyDownHandler));

            // --- ATTACH FRESH HANDLERS ---
            sp.MouseLeftButtonDown += MouseDownHandler;
            sp.MouseMove += MouseMoveHandler;
            sp.MouseLeftButtonUp += MouseUpHandler;
            sp.KeyUp += KeyUpHandler;
            sp.PreviewKeyDown += KeyDownHandler;
        }


        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }


        public static void TriggerLaunchEffect(StackPanel sp, dynamic frame = null)
        {
            if (sp == null) return;
            try
            {
                if (frame == null)
                {
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                    string frameId = win?.Tag?.ToString();

                    if (!string.IsNullOrEmpty(frameId))
                    {
                        frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    }

                    // Fallback to Title only if ID lookup failed
                    if (frame == null)
                    {
                        frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Title == win?.Title);
                    }
                }

                // FIX: Read LIVE Global Setting directly from SettingsManager
                // This bypasses the stale _options cache.
                LaunchEffectsManager.LaunchEffect effect = SettingsManager.LaunchEffect;

                // Check for frame-Specific Override
                string customEffect = frame?.CustomLaunchEffect?.ToString();
                if (!string.IsNullOrEmpty(customEffect))
                {
                    try
                    {
                        effect = (LaunchEffectsManager.LaunchEffect)Enum.Parse(typeof(LaunchEffectsManager.LaunchEffect), customEffect, true);
                    }
                    catch { }
                }

                LaunchEffectsManager.ExecuteLaunchEffect(sp, effect);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Launch effect error: {ex.Message}");
            }
        }

        public static void LaunchItem(StackPanel sp, string path, bool isFolder, string arguments = null, bool triggerEffect = true)
        {
            try
            {
                // --- NEW: Ignore clicks on internal placeholders ---
                if (path != null && path.StartsWith("INTERNAL_BLANK_")) return;

                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                string frameId = win?.Tag?.ToString();
                dynamic frame = null;

                if (!string.IsNullOrEmpty(frameId))
                {
                    frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                }

                // Fallback to Title only if ID lookup failed
                if (frame == null)
                {
                    frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Title == win?.Title);
                }

                // 1. Visual Feedback
                if (triggerEffect)
                {
                    TriggerLaunchEffect(sp, frame);
                }

                // 2. Path Resolution
                string fullPath = path;
                try { fullPath = System.IO.Path.GetFullPath(path); } catch { }

                string extension = System.IO.Path.GetExtension(fullPath).ToLower();
                bool isUrlFile = extension == ".url";
                bool isLnk = extension == ".lnk";

                string targetPath = fullPath;
                string workingDirectory = "";
                string finalArguments = arguments ?? "";

                // 3. Resolve Target
                if (isUrlFile)
                {
                    targetPath = ExtractUrlFromUrlFile(fullPath);
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgInvalidUrlFile", path), Strings.DlgError);
                        return;
                    }
                }
                else if (isLnk)
                {
                    if (FilePathUtilities.TryResolveShortcutWithShellLinkW(fullPath, out string slTarget, out string slArgs, out string slWorkDir, out _, out _))
                    {
                        targetPath = slTarget;
                        if (!slTarget.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(finalArguments) && !string.IsNullOrEmpty(slArgs)) finalArguments = slArgs;
                            if (!string.IsNullOrEmpty(slWorkDir)) workingDirectory = slWorkDir;
                        }
                    }
                    else
                    {
                        string resolved = FilePathUtilities.GetShortcutTargetUnicodeSafe(fullPath);
                        if (string.IsNullOrEmpty(resolved)) resolved = GetScrapedNetworkPath(fullPath);

                        if (!string.IsNullOrEmpty(resolved))
                        {
                            targetPath = resolved;
                            try
                            {
                                WshShell shell = new WshShell();
                                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(fullPath);
                                string rawTarget = shortcut.TargetPath?.ToLower() ?? "";
                                if (!rawTarget.EndsWith("explorer.exe"))
                                {
                                    if (string.IsNullOrEmpty(finalArguments)) finalArguments = shortcut.Arguments;
                                    workingDirectory = shortcut.WorkingDirectory;
                                }
                            }
                            catch { }
                        }
                    }
                }

                // 4. Classification
                bool isWebUrl = IsWebUrl(targetPath);
                bool isStoreApp = isLnk && Utility.IsStoreAppShortcut(fullPath);
                bool isNetworkRoot = !string.IsNullOrEmpty(targetPath) && targetPath.StartsWith(@"\\") && !targetPath.Contains(@":\");
                bool isSpecialPath = IsSpecialWindowsPath(targetPath);

                bool isTargetFolder = false;
                bool targetExists = false;

                if (!isWebUrl && !isSpecialPath && !isNetworkRoot && !isStoreApp)
                {
                    isTargetFolder = System.IO.Directory.Exists(targetPath);
                    targetExists = isTargetFolder || System.IO.File.Exists(targetPath);
                }

                // 5. Validation Guard
                if (!targetExists && !isWebUrl && !isSpecialPath && !isStoreApp && !isNetworkRoot)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Target not found: {targetPath}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgTargetNotFound", targetPath), Strings.DlgLaunchError);
                    return;
                }

                // 6. Admin & Different User Logic
                bool alwaysRunAsAdmin = false;
                bool alwaysRunAsDifferentUser = false;
                try
                {
                    if (frame != null && frame.ItemsType?.ToString() == "Data")
                    {
                        var items = frame.Items as JArray ?? new JArray();
                        bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                        if (tabsEnabled)
                        {
                            var tabs = frame.Tabs as JArray ?? new JArray();
                            int currentTabIndex = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");
                            if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                            {
                                var currentTab = tabs[currentTabIndex] as JObject;
                                items = currentTab?["Items"] as JArray ?? items;
                            }
                        }

                        var matchingItem = items.FirstOrDefault(i => string.Equals(
                            System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                            fullPath, StringComparison.OrdinalIgnoreCase));

                        if (matchingItem != null)
                        {
                            alwaysRunAsAdmin = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                            alwaysRunAsDifferentUser = Convert.ToBoolean(matchingItem["AlwaysRunAsDifferentUser"] ?? false);
                        }
                    }
                }
                catch { }

                // 7. Execution
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = true;

                if (!isWebUrl && !isSpecialPath && !isTargetFolder && !isNetworkRoot)
                {
                    if (alwaysRunAsAdmin)
                        psi.Verb = "runas";
                    else if (alwaysRunAsDifferentUser)
                        psi.Verb = "runasuser";
                }

                if (isStoreApp)
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = $"\"{fullPath}\"";
                    psi.WorkingDirectory = "";
                }
                else if (isWebUrl)
                {
                    psi.FileName = targetPath;
                }
                else if (isTargetFolder || isNetworkRoot)
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = $"\"{targetPath}\"";
                }
                else
                {
                    psi.FileName = targetPath;
                    if (!string.IsNullOrEmpty(finalArguments)) psi.Arguments = finalArguments;

                    if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
                        psi.WorkingDirectory = workingDirectory;
                    else
                    {
                        string dir = System.IO.Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                            psi.WorkingDirectory = dir;
                    }
                }

                Task.Run(() =>
                {
                    try
                    {
                        Process.Start(psi);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Launched: {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("canceled"))
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Launch Error: {ex.Message}");
                            if (!SettingsManager.SuppressLaunchWarnings)
                            {
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgLaunchFailed", ex.Message), Strings.DlgLaunchError);
                                }));
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("canceled"))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Launch Error: {ex.Message}");
                    if (!SettingsManager.SuppressLaunchWarnings)
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgLaunchFailed", ex.Message), Strings.DlgLaunchError);
                }
            }
        }


        //// Determines if a path is a web URL
        //private static bool IsWebUrl(string path)
        //{
        //    if (string.IsNullOrEmpty(path)) return false;

        //    return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        //   path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
        //}

        // Determines if a path is a web URL
        private static bool IsWebUrl(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult))
            {
                string scheme = uriResult.Scheme.ToLower();
                return scheme == Uri.UriSchemeHttp ||
                       scheme == Uri.UriSchemeHttps ||
                       scheme == Uri.UriSchemeFtp ||
                       scheme == Uri.UriSchemeMailto;
            }
            return false;
        }


        //// Determines if a path is a special Windows path
        //private static bool IsSpecialWindowsPath(string path)
        //{
        //    if (string.IsNullOrEmpty(path)) return false;

        //    return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
        //           path.Contains("::") ||
        //           path.StartsWith("control", StringComparison.OrdinalIgnoreCase);
        //}


        // Determines if a path is a special Windows path
        private static bool IsSpecialWindowsPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Support Custom App Protocols globally (steam://, spotify:, discord:, etc.)
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult))
            {
                string scheme = uriResult.Scheme.ToLower();
                if (scheme != Uri.UriSchemeFile &&
                    scheme != Uri.UriSchemeHttp &&
                    scheme != Uri.UriSchemeHttps &&
                    scheme != Uri.UriSchemeFtp &&
                    scheme != Uri.UriSchemeMailto)
                {
                    return true;
                }
            }

            return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("::") ||
                   IsControlPanelCommand(path);
        }

        // 控制台指令：control、control.exe，或其後接參數（例如 control /name Microsoft.Mouse）。
        // 以 control 開頭的一般檔名（例如 controller-說明.txt）不算，否則會跳過「目標是否存在」的檢查。
        private static bool IsControlPanelCommand(string path)
        {
            string trimmed = path.Trim();
            foreach (string command in new[] { "control.exe", "control" })
            {
                if (trimmed.Equals(command, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }


        // Extracts the URL from a .url file
        private static string ExtractUrlFromUrlFile(string urlFilePath)
        {
            try
            {
                if (!System.IO.File.Exists(urlFilePath))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"URL file not found: {urlFilePath}");
                    return null;
                }

                string[] lines = System.IO.File.ReadAllLines(urlFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = line.Substring(4).Trim(); // Remove "URL=" prefix
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Extracted URL from {urlFilePath}: {url}");
                        return url;
                    }
                }

                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"No URL= line found in .url file: {urlFilePath}");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error reading .url file {urlFilePath}: {ex.Message}");
                return null;
            }
        }

        // Helper method for network path detection
        // Helper method for network path detection
        public static bool IsNetworkPath(string filePath)
        {
            try
            {
                bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";

                if (isShortcut)
                {
                    // --- HANG FIX: Bypassed Utility.GetShortcutTarget(filePath) ---
                    // We use a fast binary scan to detect UNC paths without triggering Windows Network timeouts.
                    if (System.IO.File.Exists(filePath))
                    {
                        byte[] fileContent = System.IO.File.ReadAllBytes(filePath);
                        string asciiContent = System.Text.Encoding.ASCII.GetString(fileContent);
                        string unicodeContent = System.Text.Encoding.Unicode.GetString(fileContent);

                        bool isUncPath = asciiContent.Contains("\\\\") || unicodeContent.Contains("\\\\");

                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Shortcut {filePath} binary scan for UNC, IsUNC: {isUncPath}");
                        return isUncPath;
                    }
                }
                else
                {
                    // For direct paths, check if it's UNC
                    bool isUncPath = filePath.StartsWith("\\\\");
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Direct path {filePath}, IsUNC: {isUncPath}");
                    return isUncPath;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling, $"Error checking if {filePath} is network path: {ex.Message}");
            }
            return false;
        }

        // Public method to refresh an icon's click handlers after shortcut editing
        // Called by EditShortcutWindow to ensure immediate argument updates
        public static void RefreshIconClickHandlers(string shortcutPath, string newDisplayName)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Refreshing click handlers for edited shortcut: {shortcutPath}");

                // Find all frame windows and locate the icon
                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                bool iconFound = false;

                foreach (var window in windows)
                {
                    // Find the WrapPanel containing icons
                    var wrapPanel = FrameUtilities.FindWrapPanel(window);

                    if (wrapPanel == null) continue;

                    // Find the specific icon StackPanel
                    foreach (StackPanel iconPanel in wrapPanel.Children.OfType<StackPanel>())
                    {
                        var tagData = iconPanel.Tag;
                        if (tagData != null)
                        {
                            string filePath = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                            if (!string.IsNullOrEmpty(filePath) &&
                                string.Equals(System.IO.Path.GetFullPath(filePath), System.IO.Path.GetFullPath(shortcutPath), StringComparison.OrdinalIgnoreCase))
                            {
                                // Found it! Refresh this icon completely
                                RefreshSingleIconComplete(iconPanel, shortcutPath, newDisplayName, window);
                                iconFound = true;
                                break;
                            }
                        }
                    }
                    if (iconFound) break;
                }

                if (!iconFound)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"Could not find icon to refresh for: {shortcutPath}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error refreshing icon click handlers: {ex.Message}");
            }
        }

        // Completely refreshes a single icon with fresh data from the .lnk fil
        private static void RefreshSingleIconComplete(StackPanel iconPanel, string shortcutPath, string newDisplayName, NonActivatingWindow parentWindow)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Performing complete refresh of icon: {shortcutPath}");

                // 1. Read fresh data
                string freshTargetPath = shortcutPath;
                string freshArguments = "";
                bool isFolder = false;
                string workingDirectory = "";

                // Identify Type
                bool isUrl = System.IO.Path.GetExtension(shortcutPath).ToLower() == ".url";
                bool isLnk = System.IO.Path.GetExtension(shortcutPath).ToLower() == ".lnk";

                if (isLnk)
                {
                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                        freshTargetPath = shortcut.TargetPath ?? shortcutPath;
                        freshArguments = shortcut.Arguments ?? "";
                        workingDirectory = shortcut.WorkingDirectory ?? "";
                        isFolder = System.IO.Directory.Exists(freshTargetPath);
                    }
                    catch { }
                }
                else if (isUrl)
                {
                    // For URLs, we can try to extract the clean URL, but IsFolder is always false
                    string url = CoreUtilities.ExtractWebUrlFromFile(shortcutPath);
                    if (!string.IsNullOrEmpty(url)) freshTargetPath = url;
                }

                // 2. Update Tag
                iconPanel.Tag = new
                {
                    FilePath = shortcutPath,
                    IsFolder = isFolder,
                    Arguments = freshArguments
                };

                // 3. Update Text
                if (!string.IsNullOrEmpty(newDisplayName))
                {
                    var textBlock = iconPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null)
                    {
                        string displayText = newDisplayName.Length > SettingsManager.MaxDisplayNameLength
                            ? newDisplayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "..."
                            : newDisplayName;
                        textBlock.Text = displayText;
                    }
                }

                // 4. Update ToolTip
                string displayTarget = freshTargetPath;

                // Clean up MS Store App targets for the tooltip
                if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
                {
                    string packageId = displayTarget.Split('!')[0];
                    int hashIndex = packageId.IndexOf('_');
                    if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex);
                    displayTarget = $"Windows App ({packageId})";
                }

                string toolTipText = $"{newDisplayName ?? System.IO.Path.GetFileNameWithoutExtension(shortcutPath)}\nTarget: {displayTarget}";
                if (!string.IsNullOrEmpty(freshArguments)) toolTipText += $"\nArguments: {freshArguments}";
                iconPanel.ToolTip = new ToolTip { Content = toolTipText };

                // 5. UPDATE ICON IMAGE (The Critical Fix)
                var ico = iconPanel.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (ico != null)
                {
                    ImageSource newIcon = null;

                    // A. Try Manual Extraction for .url (Fixes Runtime Update)
                    if (isUrl)
                    {
                        var urlIcon = GetUrlCustomIcon(shortcutPath);
                        if (urlIcon.Path != null)
                        {
                            newIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                        }
                    }
                    // B. Try WshShell for .lnk
                    else if (isLnk)
                    {
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                            if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                            {
                                string[] parts = shortcut.IconLocation.Split(',');
                                if (System.IO.File.Exists(parts[0]))
                                {
                                    int idx = 0;
                                    if (parts.Length > 1) int.TryParse(parts[1], out idx);
                                    newIcon = IconManager.ExtractIconFromFile(parts[0], idx);
                                }
                            }
                        }
                        catch { }
                    }

                    // C. Fallback to Theme/Shell Icon
                    if (newIcon == null)
                    {
                        if (isUrl)
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));
                        }
                        else if (isFolder)
                        {
                            // Check for broken target
                            if (!FilePathUtilities.DoesFolderExist(shortcutPath, true))
                                newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                            else
                            {
                                newIcon = Utility.GetShellIcon(freshTargetPath ?? shortcutPath, true);
                                if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                            }
                        }
                        else
                        {
                            // Standard file fallback
                            newIcon = Utility.GetShellIcon(freshTargetPath, false);
                            if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                        }
                    }

                    // Apply and Update Cache
                    if (newIcon != null)
                    {
                        ico.Source = newIcon;
                        IconManager.IconCache[shortcutPath] = newIcon; // Force update cache
                    }
                }

                // 6. Re-attach Events and Update Data
                ClickEventAdder(iconPanel, shortcutPath, isFolder, freshArguments);
                UpdateFrameDataForIcon(shortcutPath, newDisplayName, parentWindow);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Complete icon refresh successful for: {shortcutPath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error in complete icon refresh: {ex.Message}");
            }
        }


        // Updates frame JSON data after icon refresh
        private static void UpdateFrameDataForIcon(string shortcutPath, string newDisplayName, NonActivatingWindow parentWindow)
        {
            try
            {
                if (string.IsNullOrEmpty(newDisplayName)) return;

                string frameId = parentWindow.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (frame == null) return;

                // FIX: Search for the item in BOTH Main Items and ALL Tabs
                bool found = false;

                // Helper function to search and update a list
                bool SearchList(JArray list)
                {
                    if (list == null) return false;
                    foreach (var item in list)
                    {
                        string itemFilename = item["Filename"]?.ToString();
                        if (!string.IsNullOrEmpty(itemFilename) &&
                            string.Equals(System.IO.Path.GetFullPath(itemFilename), System.IO.Path.GetFullPath(shortcutPath), StringComparison.OrdinalIgnoreCase))
                        {
                            item["DisplayName"] = newDisplayName;
                            return true; // Found and updated
                        }
                    }
                    return false;
                }

                // 1. Check Main Items
                if (SearchList(frame.Items as JArray)) found = true;

                // 2. Check Tabs (Always check tabs to keep Tab 0 and Main Items completely synced)
                var tabs = frame.Tabs as JArray;
                if (tabs != null)
                {
                    foreach (var tab in tabs)
                    {
                        if (SearchList(tab["Items"] as JArray))
                        {
                            found = true;
                            // We purposefully do NOT break here to ensure all instances across all tabs are updated
                        }
                    }
                }

                if (found)
                {
                    FrameDataManager.SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Updated JSON DisplayName for: {shortcutPath}");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Could not find item in JSON to update name: {shortcutPath}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error updating frame JSON: {ex.Message}");
            }
        }


        // Size feedback during resizing
        private static void ShowSizeFeedback(double width, double height, NonActivatingWindow frame = null)
        {
            if (_sizeFeedbackWindow == null)
            {
                _sizeFeedbackWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Width = 100,
                    Height = 30,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                var label = new Label
                {
                    Content = "",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 20, 20, 20)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                };
                _sizeFeedbackWindow.Content = label;
            }

            var labelContent = (Label)_sizeFeedbackWindow.Content;
            labelContent.Content = $"{Math.Round(width)} x {Math.Round(height)}";

            var mousePos = System.Windows.Forms.Cursor.Position;
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;

            try
            {
                if (frame != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(frame);
                    dpiScaleX = dpi.DpiScaleX;
                    dpiScaleY = dpi.DpiScaleY;
                }
                else if (_sizeFeedbackWindow != null && _sizeFeedbackWindow.IsLoaded)
                {
                    var dpi = VisualTreeHelper.GetDpi(_sizeFeedbackWindow);
                    dpiScaleX = dpi.DpiScaleX;
                    dpiScaleY = dpi.DpiScaleY;
                }
                else
                {
                    using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    {
                        dpiScaleX = graphics.DpiX / 96.0;
                        dpiScaleY = graphics.DpiY / 96.0;
                    }
                }
            }
            catch
            {
                dpiScaleX = 1.0;
                dpiScaleY = 1.0;
            }

            if (dpiScaleX <= 0) dpiScaleX = 1.0;
            if (dpiScaleY <= 0) dpiScaleY = 1.0;

            _sizeFeedbackWindow.Left = (mousePos.X / dpiScaleX) + 12;
            _sizeFeedbackWindow.Top = (mousePos.Y / dpiScaleY) + 12;

            _sizeFeedbackWindow.Show();

            // --- BUG FIX: Unified Debounce Timer ---
            // This guarantees the indicator will ALWAYS disappear 1.5 seconds 
            // after the last call, preventing orphaned windows on screen.
            if (_hideTimer == null)
            {
                _hideTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                _hideTimer.Tick += (s, e) => HideSizeFeedback();
            }
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private static void HideSizeFeedback()
        {
            if (_sizeFeedbackWindow != null)
            {
                _sizeFeedbackWindow.Hide();
            }
            if (_hideTimer != null)
            {
                _hideTimer.Stop();
            }
        }

        public static void OnResizingStarted(NonActivatingWindow frame)
        {
            if (SettingsManager.EnableDimensionSnap)
            {
                frame.SizeChanged -= UpdateSizeFeedback;
                frame.SizeChanged += UpdateSizeFeedback;
                ShowSizeFeedback(frame.Width, frame.Height, frame);
            }
        }

        public static void OnResizingEnded(NonActivatingWindow frame)
        {
            frame.SizeChanged -= UpdateSizeFeedback;
            if (!SettingsManager.EnableDimensionSnap || frame.ActiveResizeEdge == ResizeEdge.None) return;
            var snapped = ResizeSnapCalculator.Calculate(new Rect(frame.Left, frame.Top, frame.Width, frame.Height),
                frame.ActiveResizeEdge, frame.MinWidth, frame.MinHeight, frame.MaxWidth, frame.MaxHeight);
            frame.Left = snapped.Left;
            frame.Top = snapped.Top;
            frame.Width = snapped.Width;
            frame.Height = snapped.Height;
            string id = frame.Tag?.ToString();
            dynamic data = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);
            if (data != null)
            {
                data.X = snapped.Left; data.Y = snapped.Top;
                data.Width = snapped.Width; data.Height = snapped.Height;
                FrameDataManager.SaveFrameData();
            }
            ShowSizeFeedback(snapped.Width, snapped.Height, frame);
        }

        // --- HELPER: Efficient Dead Shortcut Check using Cached States ---
        private static bool HasDeadShortcuts(dynamic frame)
        {
            try
            {
                // --- BUG FIX: Fetch Live frame ---
                // The passed 'frame' object may be an orphaned JSON node if Tabs were recently modified.
                string frameId = frame.Id?.ToString();
                var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId) ?? frame;

                // Only Data frames track dead shortcuts
                if (liveFrame.ItemsType?.ToString() != "Data") return false;

                bool tabsEnabled = liveFrame.TabsEnabled?.ToString().ToLower() == "true";
                if (tabsEnabled)
                {
                    var tabs = liveFrame.Tabs as JArray;
                    if (tabs != null)
                    {
                        foreach (var tab in tabs)
                        {
                            var items = tab["Items"] as JArray;
                            if (items != null && CheckItemsForDead(items)) return true;
                        }
                    }
                }
                else
                {
                    var items = liveFrame.Items as JArray;
                    if (items != null && CheckItemsForDead(items)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool CheckItemsForDead(JArray items)
        {
            foreach (var item in items)
            {
                string path = item["Filename"]?.ToString();
                if (!string.IsNullOrEmpty(path))
                {
                    // Check our cache which is updated by the background TargetChecker
                    // This avoids scanning the disk on the UI thread
                    if (_iconStates.TryGetValue(path, out var state))
                    {
                        if (state.IsBroken) return true;
                    }
                }
            }
            return false;
        }


        // Add this static method inside your Framemanager class
        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;

            if (msg == WM_GETMINMAXINFO)
            {
                // Get the screen information for the current monitor
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (screen != null)
                {
                    // Define the limit: 90% of the working area
                    int maxWidth = (int)(screen.WorkingArea.Width * 0.90);
                    int maxHeight = (int)(screen.WorkingArea.Height * 0.90);

                    // Marshal the structure
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

                    // Force the "Maximized" size to be 90%, not 100%
                    mmi.ptMaxSize.x = maxWidth;
                    mmi.ptMaxSize.y = maxHeight;

                    // Force the user-draggable limit to be 90%
                    mmi.ptMaxTrackSize.x = maxWidth;
                    mmi.ptMaxTrackSize.y = maxHeight;

                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }


        private static string GetSafeProperty(dynamic obj, string propName)
        {
            try
            {
                if (obj is JObject jObj && jObj[propName] != null) return jObj[propName].ToString();
                return obj.GetType().GetProperty(propName)?.GetValue(obj, null)?.ToString() ?? "";
            }
            catch { return ""; }
        }


        private static void UpdateSizeFeedback(object sender, SizeChangedEventArgs e)
        {
            var frame = sender as NonActivatingWindow;
            if (frame != null)
            {
                // --- BUG FIX: Ignore programmatic animations ---
                // Do not show the resizing indicator if the frame is just rolling up or down
                string frameId = frame.Tag?.ToString();
                if (!string.IsNullOrEmpty(frameId) && _framesInTransition.Contains(frameId)) return;

                ShowSizeFeedback(frame.Width, frame.Height, frame);
            }
        }


    }

}
