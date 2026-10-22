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
    // FrameManager 分檔：Feedback（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
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
                // 執行中新建的面板是 DynamicFrameData（字典），沒有同名的 CLR 屬性；不處理會一律回傳空字串
                if (obj is IDictionary<string, object> dict)
                    return dict.TryGetValue(propName, out object value) ? value?.ToString() ?? "" : "";
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
