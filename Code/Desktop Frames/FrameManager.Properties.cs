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
    // FrameManager 分檔：Properties（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        public static List<dynamic> GetFrameData()
        {
            return FrameDataManager.FrameData;
        }

        /// <summary>
        /// Updates the filter history for a frame using LRU (Least Recently Used) logic.
        /// Max 5 items. Duplicates move to top.
        /// </summary>
        private static void UpdateFilterHistory(dynamic frame, string newFilter)
        {
            if (string.IsNullOrWhiteSpace(newFilter)) return;

            // Don't save if it matches a Standard Preset value exactly
            if (_standardPresets.ContainsValue(newFilter)) return;

            try
            {
                // 1. GET FRESH DATA (Fixes the "Replace" bug)
                // We cannot rely on the 'frame' parameter because it might be stale 
                // (referencing the state from when the window opened).
                string frameId = frame.Id?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                int index = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                if (index < 0) return;

                // Grab the LIVE object from the master list
                dynamic liveFrame = FrameDataManager.FrameData[index];

                // 2. Convert to Dictionary for modification
                IDictionary<string, object> frameDict = liveFrame is IDictionary<string, object> dict
                    ? dict
                    : ((JObject)liveFrame).ToObject<IDictionary<string, object>>();

                // 3. Get existing history
                JArray history;
                if (frameDict.ContainsKey("FilterHistory") && frameDict["FilterHistory"] is JArray existingArr)
                {
                    history = existingArr;
                }
                else
                {
                    history = new JArray();
                }

                // 4. Update Logic (LRU)
                // Remove existing if present (to bump to top)
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(history[i].ToString(), newFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        history.RemoveAt(i);
                    }
                }

                // Insert at top
                history.Insert(0, newFilter);

                // Cap at 5 items
                while (history.Count > 5)
                {
                    history.RemoveAt(history.Count - 1);
                }

                // 5. Save back to Master List
                frameDict["FilterHistory"] = history;
                FrameDataManager.FrameData[index] = JObject.FromObject(frameDict);
                FrameDataManager.SaveFrameData();

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings,
                    $"Updated Filter History for {frameId}: {string.Join(", ", history)}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"History Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets visual customizations for all frames to default values.
        /// Preserves position, size, content, and system state (Locked/Rolled).
        /// </summary>
        public static void ResetAllCustomizations()
        {
            try
            {
                bool modificationsMade = false;

                // FIX: Use .ToList() to create a snapshot copy for iteration.
                // This prevents "Collection was modified" errors when we update the real list inside the loop.
                var framesSnapshot = FrameDataManager.FrameData.ToList();

                foreach (dynamic frame in framesSnapshot)
                {
                    // Convert to JObject/Dictionary for safe property access
                    IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict
                        ? dict
                        : ((JObject)frame).ToObject<IDictionary<string, object>>();

                    // --- RESET VISUALS TO DEFAULTS ---

                    // Frame Appearance
                    frameDict["CustomColor"] = null;
                    frameDict["CustomLaunchEffect"] = null;
                    frameDict["FrameBorderColor"] = null;
                    frameDict["FrameBorderThickness"] = 2; // Default thickness

                    // Title Appearance
                    frameDict["TitleTextColor"] = null;
                    frameDict["TitleTextSize"] = "Medium";
                    frameDict["BoldTitleText"] = "false";

                    // Icon Appearance
                    frameDict["TextColor"] = null;
                    frameDict["DisableTextShadow"] = "false";
                    frameDict["IconSize"] = "Medium";
                    frameDict["IconSpacing"] = 5;
                    frameDict["GrayscaleIcons"] = "false";

                    // Update the object in the REAL list
                    int index = FrameDataManager.FrameData.IndexOf(frame);
                    if (index >= 0)
                    {
                        FrameDataManager.FrameData[index] = JObject.FromObject(frameDict);
                        modificationsMade = true;
                    }
                }

                if (modificationsMade)
                {
                    FrameDataManager.SaveFrameData();

                    // Reload to reflect changes immediately
                    ReloadFrames();

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings,
                        "All frame customizations have been reset to defaults.");

                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                        Strings.MsgCustomizationsReset,
                        Strings.DlgResetComplete);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Error,
                    $"Error resetting customizations: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgResetCustomizationsFailed", ex.Message), Strings.DlgError);
            }
        }

        // Update frame property, save to JSON, and apply runtime changes
        public static void UpdateFrameProperty(dynamic frame, string propertyName, string value, string logMessage)
        {
            try
            {


                string frameId = frame.Id?.ToString();
                if (string.IsNullOrEmpty(frameId))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Frame '{frame.Title}' has no Id");
                    return;
                }

                // Skip updates if frame is in transition (except for IsRolled and UnrolledHeight which are rollup-specific)
                if (_framesInTransition.Contains(frameId) && propertyName != "IsRolled" && propertyName != "UnrolledHeight")
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameUpdate, $"Skipping {propertyName} update for frame '{frameId}' - in transition");
                    return;
                }

                // Find by GUID instead of title or reference
                int index = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                if (index >= 0)
                {
                    // Get the frame from FrameDataManager.FrameData
                    dynamic actualFrame = FrameDataManager.FrameData[index];


                    // Convert to dictionary safely
                    IDictionary<string, object> frameDict = actualFrame as IDictionary<string, object> ?? ((JObject)actualFrame).ToObject<IDictionary<string, object>>();

                    object finalVal = value;
                    // Handle IsHidden specifically to store as string to match JSON format
                    if (propertyName == "IsHidden")
                    {
                        // Convert boolean-like string input to string "true" or "false"
                        bool parsedValue = value?.ToLower() == "true";
                        finalVal = parsedValue.ToString().ToLower(); // Store as "true" or "false"
                        frameDict[propertyName] = finalVal;
                    }
                    else if (propertyName == "FrameBorderThickness")
                    {
                        // BUG FIX: Enforce integer serialization to prevent JSON type wiping/fallback
                        if (int.TryParse(value, out int parsedThickness))
                        {
                            finalVal = parsedThickness;
                        }
                        else
                        {
                            finalVal = 2; // Failsafe
                        }
                        frameDict[propertyName] = finalVal;
                    }
                    else
                    {
                        // Update other properties as provided
                        frameDict[propertyName] = value;
                    }

                    // Keep existing JObject references (e.g. PortalFramemanager._frame and window closures) in sync in-place
                    JObject actualJObj = (object)actualFrame as JObject;
                    if (actualJObj != null)
                    {
                        actualJObj[propertyName] = finalVal != null ? JToken.FromObject(finalVal) : JValue.CreateNull();
                    }
                    else
                    {
                        FrameDataManager.FrameData[index] = JObject.FromObject(frameDict);
                    }

                    JObject callerJObj = (object)frame as JObject;
                    if (callerJObj != null && !ReferenceEquals(callerJObj, (object)actualFrame))
                    {
                        callerJObj[propertyName] = finalVal != null ? JToken.FromObject(finalVal) : JValue.CreateNull();
                    }
                    else if ((object)frame is IDictionary<string, object> callerDict && !ReferenceEquals(callerDict, frameDict))
                    {
                        callerDict[propertyName] = finalVal;
                    }

                    FrameDataManager.SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"{logMessage} for frame '{frame.Title}'");

                    // Find the window to apply runtime changes
                    var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                    //var win = windows.FirstOrDefault(w => w.Title == frame.Title.ToString());
                    var win = windows.FirstOrDefault(w => w.Tag?.ToString() == frameId);
                    if (win != null)
                    {
                        // Apply runtime changes
                        if (propertyName == "CustomColor")
                        {
                            Utility.ApplyTintAndColorToFrame(win, value);
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Applied color '{value ?? "Default"}' to frame '{frame.Title}' at runtime");
                        }
                        else if (propertyName == "IsHidden")
                        {
                            // Update visibility based on IsHidden
                            bool isHidden = value?.ToLower() == "true";
                            win.Visibility = isHidden ? Visibility.Hidden : Visibility.Visible;
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set visibility to {(isHidden ? "Hidden" : "Visible")} for frame '{frame.Title}'");
                        }
                        else if (propertyName == "AlwaysOnTop")
                        {
                            bool alwaysOnTop = value?.ToLower() == "true";
                            win.Topmost = alwaysOnTop;

                            // --- BULLETPROOF FIX: Delay Native OS Call to beat WPF's internal queue ---
                            win.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                // Brute-force WPF to re-evaluate the handle state
                                if (alwaysOnTop) { win.Topmost = false; win.Topmost = true; }

                                var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                                if (hwnd != IntPtr.Zero)
                                {
                                    IntPtr HWND_TOPMOST = new IntPtr(-1);
                                    IntPtr HWND_NOTOPMOST = new IntPtr(-2);
                                    uint SWP_NOMOVE = 0x0002;
                                    uint SWP_NOSIZE = 0x0001;
                                    uint SWP_NOACTIVATE = 0x0010;

                                    SetWindowPos(hwnd, alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                                }
                            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set Topmost to {alwaysOnTop} for frame '{frame.Title}'");
                        }
                        //step 7
                        else if (propertyName == "IsRolled")
                        {
                            bool isRolled = value?.ToLower() == "true";
                            double targetHeight = isRolled ? 28 : Convert.ToDouble(actualFrame.UnrolledHeight?.ToString() ?? "130"); //rolled height
                            var heightAnimation = new DoubleAnimation(win.Height, targetHeight, TimeSpan.FromSeconds(0.3))
                            {
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                            };
                            win.BeginAnimation(Window.HeightProperty, heightAnimation);
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
                                            wpcont.Visibility = isRolled ? Visibility.Collapsed : Visibility.Visible;
                                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set WrapPanel visibility to {(isRolled ? "Collapsed" : "Visible")} for frame '{actualFrame.Title}'");

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
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set height to {targetHeight} for frame '{actualFrame.Title}' (IsRolled={isRolled})");
                        }
                        else if (propertyName == "UnrolledHeight")
                        {
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set UnrolledHeight to {value} for frame '{actualFrame.Title}'");
                        }
                        else if (propertyName == "FrameBorderThickness")
                        {
                            if (win.Content is Border frameBorder)
                            {
                                int thickness = 2;
                                int.TryParse(value, out thickness);
                                frameBorder.BorderThickness = new Thickness(thickness);

                                if (thickness > 0 && frameBorder.BorderBrush == null)
                                {
                                    string borderColorName = actualFrame.FrameBorderColor?.ToString();
                                    if (!string.IsNullOrEmpty(borderColorName))
                                        frameBorder.BorderBrush = new SolidColorBrush(Utility.GetColorFromName(borderColorName));
                                    else
                                        frameBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                                }
                                else if (thickness == 0)
                                {
                                    frameBorder.BorderBrush = null;
                                }
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Set runtime FrameBorderThickness to {thickness} for frame '{actualFrame.Title}'");
                            }
                        }

                        // 已移除：原本依英文標題 "Customize"／"Color"／"Launch Effect" 更新子選單勾選。
                        // 目前任何語言的面板選單都沒有這些子選單（外觀改由「自訂外觀…」對話框設定），該段程式不會執行。
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameUpdate, $"Failed to find window for frame '{frame.Title}' to apply {propertyName}");
                    }
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameUpdate, $"Failed to find frame '{frame.Title}' in FrameDataManager.FrameData for {propertyName} update");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameUpdate, $"Error updating {propertyName} for frame '{frame.Title}': {ex.Message}");
            }
        }

        public static void SetFrameIconSizeLive(NonActivatingWindow win, dynamic frame, string newSize)
        {
            try
            {
                string frameId = frame?.Id?.ToString() ?? win?.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId) ?? frame;
                if (liveFrame == null) return;

                UpdateFrameProperty(liveFrame, "IconSize", newSize, $"IconSize updated to '{newSize}'");

                string itemsType = GetSafeProperty(liveFrame, "ItemsType");
                if (itemsType == "Portal")
                {
                    var pEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameId);
                    if (pEntry.Value != null)
                    {
                        string currentView = GetSafeProperty(liveFrame, "ViewMode");
                        if (string.IsNullOrEmpty(currentView)) currentView = SettingsManager.DefaultPortalView;
                        pEntry.Value.SetViewMode(currentView);
                    }
                }
                else if (itemsType == "Data" && win != null)
                {
                    RefreshFrameUsingFormApproach(win, liveFrame);
                    win.InvalidateVisual();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"SetFrameIconSizeLive error: {ex.Message}");
            }
        }
    }
}
