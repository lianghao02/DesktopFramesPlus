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
    // FrameManager 分檔：Reload（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        public static void ReloadFrames()
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Reloading all frames...");

                // 1. Life Support
                Window lifeSupport = MessageBoxesManager.CreateWaitWindow("Desktop Frames +", "Refreshing configuration...");
                lifeSupport.Show();
                System.Windows.Forms.Application.DoEvents();

                // 2. Clean up Old State
                if (TrayManager.Instance != null)
                {
                    TrayManager.Instance.ClearHiddenFrames(); // Ensure Tray is clean
                }

                var openFrames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                foreach (var win in openFrames) win.Close();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                _heartTextBlocks.Clear();
                foreach (var portal in _portalFrames.Values) try { portal.Dispose(); } catch { }
                _portalFrames.Clear();
                FrameDataManager.FrameData?.Clear();
                _currentTargetChecker?.Stop();

                // Clear Auto Roll tracking to prevent memory leaks
                foreach (var timer in _autoRollTimers.Values) timer.Stop();
                _autoRollTimers.Clear();
                _autoRolledFrames.Clear();

                // 3. Re-Load
                _currentTargetChecker = new TargetChecker(1000);
                LoadAndCreateFrames(_currentTargetChecker);

                // Allow a brief moment for WPF to register the new windows
                System.Windows.Forms.Application.DoEvents();

                // ==================== DEBUG FIX START ====================
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Starting Hidden Frame Registration...");

                if (FrameDataManager.FrameData != null)
                {
                    // Use 'dynamic' to bypass JValue compile errors
                    foreach (dynamic data in FrameDataManager.FrameData)
                    {
                        // A. Safe Title Extraction (Trim spaces)
                        string jsonTitle = (string)data.Title;
                        if (jsonTitle != null) jsonTitle = jsonTitle.Trim();

                        // B. Safe 'IsHidden' Extraction
                        // This handles: true (bool), "true" (string), 1 (int)
                        string rawHidden = (string)data.IsHidden?.ToString();
                        bool isHidden = false;
                        if (!string.IsNullOrEmpty(rawHidden))
                        {
                            bool.TryParse(rawHidden, out isHidden);
                        }

                        // C. Debug Log
                        if (isHidden)
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Frame '{jsonTitle}' is marked HIDDEN in JSON. Searching for window...");

                            // D. Find Window
                            var frameWindow = System.Windows.Application.Current.Windows
                                .OfType<NonActivatingWindow>()
                                .FirstOrDefault(w => w.Title != null && w.Title.Trim() == jsonTitle);

                            if (frameWindow != null)
                            {
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"-> MATCH FOUND. Hiding '{jsonTitle}'");
                                TrayManager.AddHiddenFrame(frameWindow);
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"-> WARNING: Could not find open window for '{jsonTitle}'. Window list count: {System.Windows.Application.Current.Windows.Count}");
                            }
                        }
                    }
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, "FrameDataManager.FrameData is NULL after load.");
                }
                // ==================== DEBUG FIX END ====================

                lifeSupport.Close();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Frames reloaded successfully.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Error reloading frames: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgReloadingFramesFailed", ex.Message), Strings.DlgError);
            }
        }

        /// <summary>
        /// Helper to detect UNC Roots (e.g. \\Server or \\192.168.1.10).
        /// These technically aren't "Directories" in .NET, but act as Folders in Windows.
        /// </summary>
        private static bool IsUncRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Must start with \\ and NOT contain a drive letter (like C:\)
            if (path.StartsWith(@"\\") && !path.Contains(@":\"))
            {
                string clean = path.Substring(2);
                // If no more slashes (Server) or just one trailing slash (Server\), it's a root.
                int slash = clean.IndexOf('\\');
                return (slash < 0 || slash == clean.Length - 1);
            }
            return false;
        }

        // PORTAL FEATURE: Logic to switch folders
        public static void NavigatePortalFrame(dynamic frame, string newPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath) || !System.IO.Directory.Exists(newPath)) return;

                string frameId = frame.Id?.ToString();

                // --- BUG FIX: Pull Live frame for Accurate Base Path ---
                var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId) ?? frame;
                string basePath = liveFrame.Path?.ToString(); // The permanent "Home"

                // --- BUG FIX: Strict Path Normalization ---
                string normNew = newPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                string normBase = basePath?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "";

                // Update State
                if (string.Equals(normNew, normBase, StringComparison.OrdinalIgnoreCase))
                {
                    _portalNavigationStates.Remove(frameId); // We returned Home
                }
                else
                {
                    _portalNavigationStates[frameId] = normNew; // We are Deep
                }

                // Tell the Manager to switch
                var portalEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameId);
                if (portalEntry.Value != null)
                {
                    portalEntry.Value.NavigateTo(normNew);
                }

                // Update UI (Show/Hide Bar)
                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                var win = windows.FirstOrDefault(w => w.Tag?.ToString() == frameId);
                if (win != null)
                {
                    RefreshPortalNavBar(win, liveFrame);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Portal Navigation Error: {ex.Message}");
            }
        }

        // PORTAL FEATURE: The UI Strip [ <  Path...  ⚓ ]
        // v2.5.4 .186: Precision Alignment (Anchor under Filter)
        public static void RefreshPortalNavBar(NonActivatingWindow frameWindow, dynamic frame)
        {
            try
            {
                var border = frameWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                if (dockPanel == null) return;

                if (frame.ItemsType?.ToString() != "Portal") return;

                string frameId = frame.Id?.ToString();
                var liveFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId) ?? frame;
                string basePath = liveFrame.Path?.ToString();

                string normBase = basePath?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "";
                string currentPath = _portalNavigationStates.ContainsKey(frameId) ? _portalNavigationStates[frameId] : normBase;
                string normCurrent = currentPath?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "";

                bool isNavigating = !string.Equals(normCurrent, normBase, StringComparison.OrdinalIgnoreCase);

                // 1. Locate existing bar
                var existingBar = dockPanel.Children.OfType<Grid>().FirstOrDefault(g => g.Tag?.ToString() == "PORTAL_NAV_BAR");

                if (!isNavigating)
                {
                    if (existingBar != null) dockPanel.Children.Remove(existingBar);
                    return;
                }

                // Generate Display Path
                string displayPath = new System.IO.DirectoryInfo(normCurrent).Name;
                try
                {
                    if (normCurrent.StartsWith(normBase, StringComparison.OrdinalIgnoreCase))
                    {
                        string rootName = new System.IO.DirectoryInfo(normBase).Name;
                        string relativePart = normCurrent.Substring(normBase.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        displayPath = string.IsNullOrEmpty(relativePart) ? rootName : System.IO.Path.Combine(rootName, relativePart);
                    }
                }
                catch { }

                // 2. BULLETPROOF UPDATE: Inject Data Directly Instead of Destroying
                // By targeting the grid columns directly, we completely avoid WPF rendering caches and stale closures.
                if (existingBar != null && existingBar.Children.Count >= 3)
                {
                    if (existingBar.Children[0] is Button btnBack) btnBack.Tag = normCurrent;
                    if (existingBar.Children[1] is TextBlock lblPath)
                    {
                        lblPath.Text = displayPath;
                        lblPath.ToolTip = normCurrent;
                    }
                    if (existingBar.Children[2] is Button btnSetBase) btnSetBase.Tag = normCurrent;

                    return; // Done! UI is instantly updated.
                }

                // 3. CREATE NEW BAR (First time only)
                Grid navGrid = new Grid
                {
                    Tag = "PORTAL_NAV_BAR",
                    Height = 24,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    Margin = new Thickness(5, 0, 35, 2)
                };

                navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
                navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });

                Button newBtnBack = new Button
                {
                    Content = "‹",
                    Tag = normCurrent, // Stores state dynamically
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = Strings.TooltipGoUp
                };
                newBtnBack.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag != null)
                    {
                        string parentPath = System.IO.Directory.GetParent(btn.Tag.ToString())?.FullName;
                        if (!string.IsNullOrEmpty(parentPath))
                            NavigatePortalFrame(liveFrame, parentPath);
                    }
                };

                TextBlock newLblPath = new TextBlock
                {
                    Text = displayPath,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = normCurrent,
                    Margin = new Thickness(5, 0, 5, 0)
                };

                Button newBtnSetBase = new Button
                {
                    Content = "⚓",
                    Tag = normCurrent, // Stores state dynamically
                    FontSize = 10,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.Orange,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = Strings.TooltipSetHome
                };
                newBtnSetBase.Click += (s, e) =>
                {
                    if (s is Button btn && btn.Tag != null)
                    {
                        string target = btn.Tag.ToString();
                        if (MessageBoxesManager.ShowCustomYesNoMessageBox(Strings.Get("MsgConfirmSetPortalHome", target), Strings.DlgUpdatePortalFrame))
                        {
                            UpdateFrameProperty(liveFrame, "Path", target, "Updated Portal Path");
                            _portalNavigationStates.Remove(frameId);
                            RefreshPortalNavBar(frameWindow, liveFrame);
                        }
                    }
                };

                navGrid.Children.Add(newBtnBack); Grid.SetColumn(newBtnBack, 0);
                navGrid.Children.Add(newLblPath); Grid.SetColumn(newLblPath, 1);
                navGrid.Children.Add(newBtnSetBase); Grid.SetColumn(newBtnSetBase, 2);

                DockPanel.SetDock(navGrid, Dock.Top);

                int insertIndex = 0;
                bool titleFound = false;
                for (int i = 0; i < dockPanel.Children.Count; i++)
                {
                    if (dockPanel.Children[i] is Grid g && g.Children.OfType<TextBlock>().Any(tb => tb.Name == "FrameLockIcon"))
                    {
                        insertIndex = i + 1;
                        titleFound = true;
                        break;
                    }
                }
                if (!titleFound) insertIndex = 1;
                if (insertIndex > dockPanel.Children.Count) insertIndex = dockPanel.Children.Count;

                dockPanel.Children.Insert(insertIndex, navGrid);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing Nav Bar: {ex.Message}");
            }
        }

        public static void ShowPortalToast(NonActivatingWindow win, string message)
        {
            try
            {
                var cborder = win.Content as Border;
                if (cborder == null) return;

                Grid mainGrid = cborder.Child as Grid;
                if (mainGrid == null)
                {
                    // Convert DockPanel to Grid on the fly to support overlays
                    var dp = cborder.Child as DockPanel;
                    if (dp != null)
                    {
                        mainGrid = new Grid();
                        cborder.Child = mainGrid;
                        mainGrid.Children.Add(dp);
                    }
                    else return;
                }

                // Find or create toast
                Border toast = mainGrid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "PortalToast");
                TextBlock toastText;

                if (toast == null)
                {
                    toast = new Border
                    {
                        Name = "PortalToast",
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 30, 30, 30)),
                        CornerRadius = new CornerRadius(15),
                        Padding = new Thickness(15, 6, 15, 6),
                        Margin = new Thickness(0, 0, 0, 15),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        IsHitTestVisible = false, // Prevents blocking mouse clicks
                        Opacity = 0
                    };

                    // Add subtle shadow for a premium feel
                    toast.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 };

                    toastText = new TextBlock
                    {
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center
                    };
                    toast.Child = toastText;
                    mainGrid.Children.Add(toast);
                }
                else
                {
                    toastText = toast.Child as TextBlock;
                }

                if (toastText != null) toastText.Text = message;

                // Stop any running animation
                toast.BeginAnimation(UIElement.OpacityProperty, null);

                // Create the Fade In -> Hold -> Fade Out animation (2 seconds total)
                var fadeInOut = new DoubleAnimationUsingKeyFrames();
                fadeInOut.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fadeInOut.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                fadeInOut.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)))); // Hold for 1.5s
                fadeInOut.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000)), new QuadraticEase { EasingMode = EasingMode.EaseIn })); // Fade out

                toast.BeginAnimation(UIElement.OpacityProperty, fadeInOut);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error showing toast: {ex.Message}");
            }
        }

        /// <summary>
        /// Surgically reloads only specific frames by closing and recreating their windows
        /// Used by: ItemMoveDialog to prevent duplicate windows during move operations
        /// Category: Selective Window Management
        /// </summary>
        public static void ReloadSpecificFrames(dynamic sourceFrame, dynamic targetFrame)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Reloading specific frames: '{sourceFrame?.Title}' and '{targetFrame?.Title}'");

                // Get frame IDs for identification
                string sourceframeId = sourceFrame?.Id?.ToString();
                string targetFrameId = targetFrame?.Id?.ToString();

                // Check if we're about to close all frames (which would terminate the app)
                bool isSourceTargetSame = sourceframeId == targetFrameId;
                int totalFrameCount = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().Count();
                int framesToClose = isSourceTargetSame ? 1 : 2;
                bool needsLifeSupport = (framesToClose >= totalFrameCount);

                Window lifeSupportWindow = null;

                // Show "life support" wait window if we're about to close all frames
                if (needsLifeSupport)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Creating life support window - about to close {framesToClose} of {totalFrameCount} total frames");

                    lifeSupportWindow = MessageBoxesManager.CreateWaitWindow("Desktop Frames +", "Refreshing frames, please wait...");
                    lifeSupportWindow.Show();
                }

                // Find and close only the specific frame windows
                var allWindows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                var windowsToClose = new List<NonActivatingWindow>();

                foreach (var window in allWindows)
                {
                    string windowframeId = window.Tag?.ToString();
                    if (!string.IsNullOrEmpty(windowframeId) &&
                        (windowframeId == sourceframeId || windowframeId == targetFrameId))
                    {
                        windowsToClose.Add(window);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            $"Marking frame window for reload: {window.Title} (ID: {windowframeId})");
                    }
                }

                // Close the specific windows
                foreach (var window in windowsToClose)
                {
                    try
                    {
                        window.Close();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            $"Closed frame window: {window.Title}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                            $"Error closing window {window.Title}: {ex.Message}");
                    }
                }

                // Clean up portal frame managers for the specific frames
                var portalFramesToRemove = _portalFrames.Keys
                    .Where(frame => frame?.Id?.ToString() == sourceframeId || frame?.Id?.ToString() == targetFrameId)
                    .ToList();

                foreach (var frame in portalFramesToRemove)
                {
                    _portalFrames.Remove(frame);
                }

                // Recreate only the specific frames with proper TargetChecker
                var FrameData = FrameDataManager.FrameData;
                var framesToRecreate = FrameData.Where(f =>
                    f.Id?.ToString() == sourceframeId || f.Id?.ToString() == targetFrameId).ToList();

                foreach (var frame in framesToRecreate)
                {
                    try
                    {
                        CreateFrame(frame, _currentTargetChecker ?? new TargetChecker(1000));
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate,
                            $"Recreated frame: {frame.Title}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                            $"Error recreating frame {frame.Title}: {ex.Message}");
                    }
                }

                // Close life support window if we used it
                if (lifeSupportWindow != null)
                {
                    try
                    {
                        lifeSupportWindow.Close();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            "Closed life support window");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                            $"Error closing life support window: {ex.Message}");
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    "Specific frame reload completed successfully");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in ReloadSpecificFrames: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Public access to portal frames dictionary for CustomizeFrameForm
        /// </summary>
        public static Dictionary<dynamic, PortalFramemanager> GetPortalFrames()
        {
            return _portalFrames;
        }
    }
}
