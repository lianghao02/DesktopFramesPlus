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
    // FrameManager 分檔：AutoHide（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        public static void InitializeAutoHideTimer()
        {
            if (_masterAutoHideTimer == null)
            {
                _masterAutoHideTimer = new DispatcherTimer();
                _masterAutoHideTimer.Tick += (s, e) => ExecuteAutoHideSequence();
            }
            ResetAutoHideTimer();
        }

        public static void ResetAutoHideTimer()
        {
            if (_masterAutoHideTimer == null) return;
            _masterAutoHideTimer.Stop();

            if (SettingsManager.AutoHideFrames && SettingsManager.AutoHideTime > 0)
            {
                _masterAutoHideTimer.Interval = TimeSpan.FromSeconds(SettingsManager.AutoHideTime);
                _masterAutoHideTimer.Start();
            }
        }

        public static void WakeUpFrames()
        {
            _hideSequenceId++;
            _areFramesAutoHidden = false;
            ResetAutoHideTimer();

            // --- NEW: Restore Native Desktop Icons if synced ---
            // BUG FIX: Only restore them if the user hasn't enforced the permanent "Always Hide" rule!
            if (SettingsManager.HideDesktopElementsOnAllFramesHide && !SettingsManager.HideDesktopElementsOnStart)
            {
                DesktopIconManager.SetDesktopIconsVisible(true);
            }

            var frames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
            foreach (var frame in frames)
            {
                if (frame.Visibility == Visibility.Visible && frame.Opacity >= 0.99) continue;

                // --- BUG FIX: Skip Individually Hidden frames (Safe Extraction) ---
                string frameId = frame.Tag?.ToString();
                if (!string.IsNullOrEmpty(frameId))
                {
                    var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    if (currentFrame != null)
                    {
                        bool isIndividuallyHidden = false;
                        try
                        {
                            if (currentFrame is Newtonsoft.Json.Linq.JObject jFrame)
                                isIndividuallyHidden = jFrame["IsHidden"]?.ToString().ToLower() == "true";
                            else
                                isIndividuallyHidden = (currentFrame.IsHidden?.ToString() ?? "false").ToLower() == "true";
                        }
                        catch { }

                        if (isIndividuallyHidden) continue; // Leave it hidden in the tray
                    }
                }
                // ------------------------------------------------------------------

                // ------------------------------------------------------------------
                // --- DWM & WPF BUFFER FLASH FIX ---
                // 1. Wipe the slate clean of any old animations from the previous hide
                frame.BeginAnimation(UIElement.OpacityProperty, null);

                // 2. Hard-lock the base opacity to exactly 0.0
                frame.Opacity = 0.0;

                // 3. Make visible
                frame.Visibility = Visibility.Visible;

                // 4. THE MAGIC BULLET: Force WPF to synchronously flush the UI queue and render the invisible 0.0 frame right now.
                // This overwrites the Windows DWM cache *before* the new animation is permitted to begin.
                frame.UpdateLayout();

                // 5. Begin the smooth fade-in
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(250)
                };

                frame.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                // --- BUG FIX: Sync localized idle timers with global wake-up ---
                frame.TriggerWakeUpIdleReset();
            }

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Frames faded in and restored.");
        }

        private static async void ExecuteAutoHideSequence()
        {
            _masterAutoHideTimer.Stop();
            if (_areFramesAutoHidden) return;

            int currentSequence = ++_hideSequenceId;

            bool isDialogOpen = System.Windows.Application.Current.Windows
                .OfType<System.Windows.Window>()
                .Any(w => w.Visibility == Visibility.Visible
                       && !(w is NonActivatingWindow)
                       && w.Width > 0
                       && !string.IsNullOrWhiteSpace(w.Title));

            if (isDialogOpen)
            {
                ResetAutoHideTimer();
                return;
            }

            var visibleFrames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>()
                .Where(w => w.Visibility == Visibility.Visible)
                .ToList();

            if (visibleFrames.Count == 0) return;
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Auto-Hide: Executing hide sequence.");

            // --- BUG FIX: Clear Held Animations ---
            // Because the Wake-Up fade-in natively holds at 1.0, we MUST release the 
            // animation lock before manually toggling local Opacity values. Otherwise, 
            // the animation engine fights the flash effect, causing visual residue.
            foreach (var win in visibleFrames)
            {
                win.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                win.Opacity = 1.0;
            }

            if (SettingsManager.HideFlashEffect)
            {
                for (int i = 0; i < 5; i++)
                {
                    foreach (var win in visibleFrames) win.Opacity = 0.3;
                    await Task.Delay(30);
                    if (currentSequence != _hideSequenceId) return;

                    foreach (var win in visibleFrames) win.Opacity = 1.0;
                    await Task.Delay(30);
                    if (currentSequence != _hideSequenceId) return;
                }
            }

            var fadeOutAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new System.Windows.Duration(TimeSpan.FromMilliseconds(200))
            };

            foreach (var win in visibleFrames)
            {
                win.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOutAnim);
            }

            await Task.Delay(200);

            if (currentSequence != _hideSequenceId) return;

            foreach (var win in visibleFrames)
            {
                // --- WPF 1-FRAME FLASH FIX ---
                // Must hard-set to 0.0 BEFORE removing the animation precedence!
                win.Opacity = 0.0;
                win.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                win.Visibility = Visibility.Hidden;
            }

            _areFramesAutoHidden = true;

            // --- NEW: Hide Native Desktop Icons if synced ---
            if (SettingsManager.HideDesktopElementsOnAllFramesHide)
            {
                DesktopIconManager.SetDesktopIconsVisible(false);
            }
        }

        public static void RefreshScrollbarSettings()
        {
            var allFrames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
            foreach (var win in allFrames)
            {
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();

                if (scrollViewer != null)
                {
                    scrollViewer.VerticalScrollBarVisibility = SettingsManager.DisableFrameScrollbars ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
                }
            }
        }

        public static void RefreshAllIconsTint()
        {
            // Calculate the target opacity exactly as we do in AddIcon
            double targetOpacity = SettingsManager.ApplyTintToIcons ? Math.Max(0.2, SettingsManager.TintValue / 100.0) : 1.0;

            var allFrames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
            foreach (var win in allFrames)
            {
                // --- FIX: Update Plugin Frame Opacity instantly ---
                string frameId = win.Tag?.ToString();
                var currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

                if (currentFrame != null && currentFrame.ItemsType?.ToString() == "Plugin")
                {
                    // CRITICAL: Release the WPF animation lock first, or the opacity change is ignored
                    win.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                    win.Opacity = targetOpacity;
                    continue; // Plugins don't use the WrapPanel, move to the next window
                }
                // --------------------------------------------------

                var wrapPanel = FindWrapPanel(win);
                if (wrapPanel != null)
                {
                    foreach (var sp in wrapPanel.Children.OfType<StackPanel>())
                    {
                        // 1. Update the Image (Checking direct child, then checking inside Grid for Network Icons)
                        var img = sp.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                        if (img == null)
                        {
                            var grid = sp.Children.OfType<Grid>().FirstOrDefault();
                            img = grid?.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                        }
                        if (img != null) img.Opacity = targetOpacity;

                        // 2. Update the TextLabel(s)
                        foreach (var txt in sp.Children.OfType<TextBlock>())
                        {
                            txt.Opacity = targetOpacity;
                        }
                    }
                }
            }
        }

        public static void RefreshAutoRollSettings()
        {
            int autoRollDelay = 2000;
            try { if (SettingsManager.AutoRollTime > 0) autoRollDelay = SettingsManager.AutoRollTime * 1000; } catch { }
            foreach (var timer in _autoRollTimers.Values)
            {
                if (timer != null)
                {
                    bool wasRunning = timer.IsEnabled;
                    timer.Stop();
                    timer.Interval = TimeSpan.FromMilliseconds(autoRollDelay);
                    if (wasRunning) timer.Start();
                }
            }
        }

        // Safety method to ensure no frames are stuck in transition state
        public static void ClearAllTransitionStates()
        {
            if (_framesInTransition.Count > 0)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameUpdate, $"Clearing {_framesInTransition.Count} stuck transition states");
                _framesInTransition.Clear();
            }
        }

        // --- NEW: Unified Manual Hide (Used by Tray Icon) ---
        public static void ForceHideFrames()
        {
            _areFramesAutoHidden = true;

            // --- NEW: Hide Native Desktop Icons if synced ---
            if (SettingsManager.HideDesktopElementsOnAllFramesHide)
            {
                DesktopIconManager.SetDesktopIconsVisible(false);
            }

            var frames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
            foreach (var frame in frames)
            {
                // --- ANTI-FLICKER SAFEGUARD ---
                // Skip frames that are already hidden or currently invisible
                if (frame.Visibility != Visibility.Visible || frame.Opacity == 0.0) continue;

                // Animate the fade out
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(250)
                };

                // When the animation finishes, physically hide the window
                fadeOut.Completed += (s, e) =>
                {
                    // --- DWM CACHE FIX ---
                    // DO NOT strip the animation here! Let WPF's FillBehavior.HoldEnd keep it firmly at 0.0.
                    // Stripping the animation here is what causes Windows DWM to snapshot the window at 1.0 opacity.
                    frame.Visibility = Visibility.Hidden;
                };

                frame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Frames faded out and hidden.");
        }
    }
}
