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
            var ctx = new FrameBuildContext { Frame = frame, TargetChecker = targetChecker, TitleTextBox = titletb };
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
            Border cborder = BuildFrameBorder(ctx, dp);


            TextBlock heart = BuildMenuIcon(ctx);
            TextBlock lockIcon = BuildLockIcon(ctx);


            Grid titleGrid = BuildTitleGrid(heart);
                                                                                                                      // End of ctrl+click handler
            ContextMenu CnMnFramemanager = BuildPanelContextMenu(ctx, out MenuItem miMoreFrame, out MenuItem miHide);
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
            BuildPanelMenuItems(ctx, CnMnFramemanager, miMoreFrame, win, out MenuItem miClearDeadShortcuts, out Separator sepClearDead, out MenuItem miPasteItem);
            // CnMnFramemanager.Items.Add(new Separator());
            // CnMnFramemanager.Items.Add(new Separator());
            // CnMnFramemanager.Items.Add(miXT);
            BindWindowStateEvents(ctx, win, lockIcon, miHide);

            BindHoverAndAutoRoll(ctx, win);


            ShowWindowWithSizeHooks(win);


            // --- FIX END ---


            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
        $"Frame '{frame.Title}' successfully created and displayed");
  

            ctx.TitleLabel = BuildTitleLabel(ctx, titleGrid);

            BindPanelMenuOpened(ctx, CnMnFramemanager, win, miClearDeadShortcuts, sepClearDead, miPasteItem);


            Grid.SetColumn(titletb, 1);
            titleGrid.Children.Add(titletb);

            Grid filterBar = BuildFilterBar(ctx, titleGrid, win);


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


    }

}
