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


            BindRenameEvents(ctx, win);


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
            BindTitleMouseEvents(ctx, titleGrid, win);


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
            (WrapPanel wpcont, ScrollViewer wpcontscr) = BuildIconArea(ctx, dp, win);



            BindDragDrop(ctx, cborder, win, wpcont);


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


            InitFrameContent(ctx, dp, win, wpcont, wpcontscr);

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
