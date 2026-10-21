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
    // FrameManager 分檔：Tabs（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        // TABS FEATURE: Toggle tabs with strict Data Migration and Effect Cleanup
        private static void ToggleFrameTabs(dynamic frame)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Toggling tabs for frame '{frame.Title}'");

                string frameId = frame.Id?.ToString();
                int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                if (frameIndex < 0) return;

                dynamic liveFrame = FrameDataManager.FrameData[frameIndex];
                IDictionary<string, object> frameDict = liveFrame is IDictionary<string, object> dict
                    ? dict : ((JObject)liveFrame).ToObject<IDictionary<string, object>>();

                bool currentTabsEnabled = frameDict.ContainsKey("TabsEnabled") && frameDict["TabsEnabled"]?.ToString().ToLower() == "true";
                bool newTabsEnabled = !currentTabsEnabled;

                var mainItems = frameDict["Items"] as JArray ?? new JArray();
                var tabs = frameDict["Tabs"] as JArray ?? new JArray();

                // 1. Data Migration Logic
                if (newTabsEnabled)
                {
                    // ENABLING TABS: Move Main -> Tab
                    if (tabs.Count == 0)
                    {
                        // Create first tab with Herb Naming
                        dynamic newTab = new JObject();
                        string herbName = FrameUtilities.GenerateRandomHerbName();
                        newTab.TabName = $"0. {herbName}";
                        newTab.Items = mainItems;
                        tabs.Add(newTab);
                        frameDict["CurrentTab"] = 0;
                    }
                    else
                    {
                        int targetIdx = Convert.ToInt32(frameDict["CurrentTab"]?.ToString() ?? "0");
                        if (targetIdx < 0 || targetIdx >= tabs.Count) targetIdx = 0;

                        var targetTab = tabs[targetIdx] as JObject;
                        var targetItems = targetTab["Items"] as JArray ?? new JArray();

                        foreach (var item in mainItems) targetItems.Add(item);
                        targetTab["Items"] = targetItems;
                        frameDict["CurrentTab"] = targetIdx;
                    }
                    // Clear main items after moving them to tab
                    frameDict["Items"] = new JArray();
                    frameDict["Tabs"] = tabs;
                }
                else
                {
                    // DISABLING TABS: Move Active Tab -> Main
                    int currentTabIdx = Convert.ToInt32(frameDict["CurrentTab"]?.ToString() ?? "0");
                    if (tabs.Count > 0 && currentTabIdx < tabs.Count && currentTabIdx >= 0)
                    {
                        var activeTab = tabs[currentTabIdx] as JObject;
                        var activeItems = activeTab["Items"] as JArray;
                        if (activeItems != null)
                        {
                            // FIX: Clear mainItems first! 
                            // This prevents duplication because mainItems might already contain stale copies 
                            // (especially from Tab 0 syncing).
                            mainItems.Clear();

                            foreach (var item in activeItems) mainItems.Add(item);
                            activeTab["Items"] = new JArray();
                        }
                    }
                    frameDict["Items"] = mainItems;
                }

                // 2. Save Updates
                frameDict["TabsEnabled"] = newTabsEnabled.ToString().ToLower();

                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                var currentWindow = windows.FirstOrDefault(w => w.Tag?.ToString() == frameId);
                if (currentWindow != null)
                {
                    frameDict["X"] = currentWindow.Left;
                    frameDict["Y"] = currentWindow.Top;
                    frameDict["Width"] = currentWindow.Width;
                    frameDict["Height"] = currentWindow.Height;
                }

                FrameDataManager.FrameData[frameIndex] = JObject.FromObject(frameDict);
                FrameDataManager.SaveFrameData();

                // 3. Reload Window & CLEANUP EFFECTS
                if (currentWindow != null)
                {
                    // Cleanup Legendary Effects
                    string currentTitle = frame.Title?.ToString() ?? "";
                    if (currentTitle == "Nikos" || currentTitle == "Nikos Georgousis" || currentTitle.Contains(">:"))
                    {
                        InterCore.ProcessTitleChange(liveFrame, "RESET_EFFECT_TEMP", "");
                    }

                    _heartTextBlocks.Remove(frame);
                    if (_portalFrames.ContainsKey(frame))
                    {
                        _portalFrames[frame].Dispose();
                        _portalFrames.Remove(frame);
                    }
                    currentWindow.Close();
                }

                var updatedFrame = FrameDataManager.FrameData[frameIndex];

                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CreateFrame(updatedFrame, new TargetChecker(1000));
                }));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error toggling tabs: {ex.Message}");
            }
        }

        public static void RefreshFrameContentSimple(NonActivatingWindow frameWindow, dynamic frame, int tabIndex)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"RefreshFrameContentSimple: switching to tab {tabIndex}");

                // 1. Find the WrapPanel
                var border = frameWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                var wrapPanel = scrollViewer?.Content as WrapPanel;

                if (wrapPanel == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Cannot find WrapPanel for content refresh");
                    return;
                }

                // 2. Clear existing content
                wrapPanel.Children.Clear();

                // 3. Get items from the specified tab
                var tabs = frame.Tabs as JArray ?? new JArray();
                if (tabIndex >= 0 && tabIndex < tabs.Count)
                {
                    var activeTab = tabs[tabIndex] as JObject;
                    if (activeTab != null)
                    {
                        var items = activeTab["Items"] as JArray ?? new JArray();
                        string tabName = activeTab["TabName"]?.ToString() ?? $"Tab {tabIndex}";

                        // 4. Sort items by DisplayOrder
                        var sortedItems = items
                            .OfType<JObject>()
                            .OrderBy(item => item["DisplayOrder"]?.Type == JTokenType.Integer ? item["DisplayOrder"].Value<int>() : 0)
                            .ToList();

                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Loading {sortedItems.Count} items from tab '{tabName}'");

                        // 5. Iterate and Add Icons (Using Unified Logic)
                        foreach (dynamic icon in sortedItems)
                        {
                            // FIX A: Use the main Framemanager.AddIcon method!
                            // This ensures network indicators, clean icons, and custom sizes are applied.
                            AddIcon(icon, wrapPanel, frame);

                            StackPanel sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                // FIX B: Extract Properties safely
                                IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                                    ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();

                                string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
                                bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];

                                // FIX C: Extract Arguments
                                string arguments = null;
                                if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
                                {
                                    try
                                    {
                                        WshShell shell = new WshShell();
                                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                                        arguments = shortcut.Arguments;
                                    }
                                    catch { }
                                }

                                // FIX D: Attach Events
                                ClickEventAdder(sp, filePath, isFolder, arguments);

                                // FIX E: Attach Centralized Context Menu (Crucial for Right-Click)
                                AttachIconContextMenu(sp, icon, frame, frameWindow);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in RefreshFrameContentSimple: {ex.Message}");
            }
        }

        // TABS FEATURE: Remove item from current tab
        private static void RemoveItemFromTab(dynamic item, dynamic frame, int tabIndex, WrapPanel wrapPanel)
        {
            try
            {
                var tabs = frame.Tabs as JArray ?? new JArray();
                if (tabIndex >= 0 && tabIndex < tabs.Count)
                {
                    var activeTab = tabs[tabIndex] as JObject;
                    if (activeTab != null)
                    {
                        var items = activeTab["Items"] as JArray ?? new JArray();
                        string itemFilename = item.Filename?.ToString();

                        // Find and remove the item
                        for (int i = items.Count - 1; i >= 0; i--)
                        {
                            var currentItem = items[i] as JObject;
                            if (currentItem != null && currentItem["Filename"]?.ToString() == itemFilename)
                            {
                                items.RemoveAt(i);
                                break;
                            }
                        }

                        // Update the tab
                        activeTab["Items"] = items;

                        // Save changes
                        string frameId = frame.Id?.ToString();
                        int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                        if (frameIndex >= 0)
                        {
                            FrameDataManager.FrameData[frameIndex] = frame;
                            FrameDataManager.SaveFrameData();
                            // ENHANCED: Add Tab0 synchronization after removal
                            if (tabIndex == 0)
                            {
                                // string frameId = frame.Id?.ToString();
                                if (!string.IsNullOrEmpty(frameId))
                                {
                                    TabManager.SynchronizeTab0Content(frameId, "tab0", "remove");
                                }
                            }

                        }

                        // Refresh the display
                        var frameWindow = FindVisualParent<NonActivatingWindow>(wrapPanel);
                        if (frameWindow != null)
                        {
                            RefreshFrameContentSimple(frameWindow, frame, tabIndex);
                        }

                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Removed item '{itemFilename}' from tab {tabIndex}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error removing item from tab: {ex.Message}");
            }
        }

        // TABS FEATURE: Create custom button template with rounded top corners only
        private static ControlTemplate CreateTabButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            // Create the border with rounded top corners
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0)); // Top corners rounded
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            // Create the content presenter
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(Button.ContentProperty));

            border.AppendChild(contentPresenter);
            template.VisualTree = border;

            // Add hover trigger for better interaction feedback
            var trigger = new Trigger();
            trigger.Property = Button.IsMouseOverProperty;
            trigger.Value = true;
            trigger.Setters.Add(new Setter(Button.OpacityProperty, 0.8));

            template.Triggers.Add(trigger);

            return template;
        }

        // Helper event handlers to avoid lambda capture issues
        private static void TabButton_MouseEnter(object sender, RoutedEventArgs e) { }

        private static void TabButton_MouseLeave(object sender, RoutedEventArgs e) { }
    }
}
