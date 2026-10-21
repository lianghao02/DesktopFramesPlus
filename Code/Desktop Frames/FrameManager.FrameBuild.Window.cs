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
    // FrameManager 分檔：CreateFrame 拆出的建構步驟－視窗狀態、滑鼠移入移出、圖示區與內容載入（自 FrameManager.FrameBuild.cs 原樣搬移；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
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
    }
}
