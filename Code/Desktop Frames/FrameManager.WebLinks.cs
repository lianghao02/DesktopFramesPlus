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
    // FrameManager 分檔：WebLinks（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        private static void CreateWebLinkShortcut(string targetUrl, string shortcutPath, string displayName)
        {
            try
            {
                // For web links, create a .url file instead of .lnk file
                // Change the extension to .url if it's not already
                string urlFilePath = shortcutPath;
                if (System.IO.Path.GetExtension(shortcutPath).ToLower() == ".lnk")
                {
                    urlFilePath = System.IO.Path.ChangeExtension(shortcutPath, ".url");
                }

                // Create a .url file content
                string urlFileContent = $"[InternetShortcut]\r\nURL={targetUrl}\r\nIconIndex=0\r\n";

                // Write the .url file
                System.IO.File.WriteAllText(urlFilePath, urlFileContent, System.Text.Encoding.ASCII);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Created web link URL file: {urlFilePath} -> {targetUrl}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling, $"Error creating web link shortcut {shortcutPath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Exports all icons from a Data frames to the desktop
        /// </summary>
        /// <param name="frame">The frame to export icons from</param>
        /// <param name="showConfirmation">If true, shows a message box on completion. False for silent automation.</param>
        private static void ExportAllIconsToDesktop(dynamic frame, bool showConfirmation = true)
        {
            try
            {
                // Verify this is a Data frame
                if (frame.ItemsType?.ToString() != "Data")
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"Export all icons attempted on non-Data frame: {frame.ItemsType}");
                    return;
                }

                int exportedCount = 0;
                int totalCount = 0;

                // Check if frame has tabs
                bool hasTabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                if (hasTabsEnabled && frame.Tabs != null)
                {
                    // Handle tabbed frame - export from all tabs
                    var tabs = frame.Tabs as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();

                    foreach (var tab in tabs)
                    {
                        var tabObj = tab as Newtonsoft.Json.Linq.JObject;
                        var tabItems = tabObj?["Items"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();

                        foreach (var item in tabItems)
                        {
                            totalCount++;
                            try
                            {
                                CopyPasteManager.SendToDesktop(item);
                                exportedCount++;
                            }
                            catch (Exception itemEx)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                                    $"Error exporting item to desktop: {itemEx.Message}");
                            }
                        }
                    }
                }
                else
                {
                    // Handle regular frame (no tabs)
                    var items = frame.Items as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();

                    foreach (var item in items)
                    {
                        totalCount++;
                        try
                        {
                            CopyPasteManager.SendToDesktop(item);
                            exportedCount++;
                        }
                        catch (Exception itemEx)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                                $"Error exporting item to desktop: {itemEx.Message}");
                        }
                    }
                }

                // Show result message after a brief delay to allow desktop refresh
                string resultMessage = $"Exported {exportedCount} of {totalCount} icons to desktop.";
                if (exportedCount != totalCount)
                {
                    resultMessage += $" {totalCount - exportedCount} items failed to export.";
                }

                // Use DispatcherTimer for UI thread delay
                var delayTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1700) // 1.7 second delay
                };
                delayTimer.Tick += (s, e) =>
                {
                    delayTimer.Stop();

                    // FIX: Check flag before showing message
                    if (showConfirmation)
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(resultMessage, "Export Complete");
                    }
                };
                delayTimer.Start();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Export all icons completed for frame '{frame.Title}': {exportedCount}/{totalCount} successful");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in ExportAllIconsToDesktop: {ex.Message}");

                // Always show errors, even in silent mode, so user knows why it failed
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgExportIconsFailed", ex.Message), Strings.DlgExportError);
            }
        }

        /// <summary>
        /// Extracts URL from different browser drop data formats
        /// </summary>
        private static string ExtractUrlFromDropData(IDataObject dataObject)
        {
            try
            {
                // Try UniformResourceLocator format first (most browsers)
                if (dataObject.GetDataPresent("UniformResourceLocator"))
                {
                    object urlData = dataObject.GetData("UniformResourceLocator");
                    if (urlData is System.IO.MemoryStream stream)
                    {
                        byte[] bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, (int)stream.Length);
                        string url = System.Text.Encoding.ASCII.GetString(bytes).Trim('\0');
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                            $"Extracted URL from UniformResourceLocator: {url}");
                        return url;
                    }
                }

                // Try Firefox format
                if (dataObject.GetDataPresent("text/x-moz-url"))
                {
                    string mozUrl = dataObject.GetData("text/x-moz-url") as string;
                    if (!string.IsNullOrEmpty(mozUrl))
                    {
                        string[] parts = mozUrl.Split('\n');
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                                $"Extracted URL from text/x-moz-url: {parts[0]}");
                            return parts[0].Trim();
                        }
                    }
                }

                // Try HTML format  
                if (dataObject.GetDataPresent(DataFormats.Html))
                {
                    string html = dataObject.GetData(DataFormats.Html) as string;
                    if (!string.IsNullOrEmpty(html))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(html, @"href=['""]([^'""]+)['""]");
                        if (match.Success)
                        {
                            string url = match.Groups[1].Value;
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                                $"Extracted URL from HTML: {url}");
                            return url;
                        }
                    }
                }

                // Try plain text format (last resort)
                if (dataObject.GetDataPresent(DataFormats.Text))
                {
                    string text = dataObject.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrEmpty(text) && IsValidWebUrl(text.Trim()))
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                            $"Extracted URL from text: {text.Trim()}");
                        return text.Trim();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error extracting URL from drop data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates if a string is a valid web URL
        /// </summary>
        private static bool IsValidWebUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // Accept ANY valid URI scheme except local files when dropping. 
            // This allows spotify:, steam://, etc., to be successfully created as shortcuts.
            return Uri.TryCreate(url, UriKind.Absolute, out Uri validUri) &&
                   validUri.Scheme != Uri.UriSchemeFile;
        }

        /// <summary>
        /// Adds a URL shortcut to the frame from dropped URL
        /// </summary>
        private static void AddUrlShortcutToFrame(string url, dynamic frame, WrapPanel wrapPanel)
        {
            try
            {
                // Create shortcuts directory if it doesn't exist
                if (!System.IO.Directory.Exists("Shortcuts"))
                {
                    System.IO.Directory.CreateDirectory("Shortcuts");
                }

                // Generate shortcut name from URL
                Uri uri = new Uri(url);
                string displayName = uri.Host.Replace("www.", "");
                string baseShortcutName = System.IO.Path.Combine("Shortcuts", $"{displayName}.url");
                string shortcutPath = baseShortcutName;

                // Ensure unique filename
                int counter = 1;
                while (System.IO.File.Exists(shortcutPath))
                {
                    shortcutPath = System.IO.Path.Combine("Shortcuts", $"{displayName} ({counter++}).url");
                }

                // Create the URL shortcut file  
                CreateWebLinkShortcut(url, shortcutPath, displayName);

                // Create item data for the frame
                dynamic newItem = new System.Dynamic.ExpandoObject();
                IDictionary<string, object> newItemDict = newItem;
                newItemDict["Filename"] = shortcutPath;
                newItemDict["IsFolder"] = false;
                newItemDict["IsLink"] = true;
                newItemDict["IsNetwork"] = false;
                newItemDict["DisplayName"] = displayName;
                newItemDict["AlwaysRunAsAdmin"] = false;

                // Find the frame in FrameDataManager.FrameData and update it properly
                string frameId = frame.Id?.ToString();
                int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);

                if (frameIndex >= 0)
                {
                    // Get the actual frame from FrameDataManager.FrameData
                    dynamic actualFrame = FrameDataManager.FrameData[frameIndex];
                    var actualItems = actualFrame.Items as JArray ?? new JArray();

                    // Handle tabs if enabled
                    bool tabsEnabled = actualFrame.TabsEnabled?.ToString().ToLower() == "true";
                    JArray targetList = actualItems;

                    if (tabsEnabled)
                    {
                        var tabs = actualFrame.Tabs as JArray ?? new JArray();
                        int currentTab = Convert.ToInt32(actualFrame.CurrentTab?.ToString() ?? "0");

                        if (currentTab >= 0 && currentTab < tabs.Count)
                        {
                            var activeTab = tabs[currentTab] as JObject;
                            if (activeTab != null)
                            {
                                var tabItems = activeTab["Items"] as JArray ?? new JArray();
                                tabItems.Add(JObject.FromObject(newItem));
                                targetList = tabItems; // Mark as target for ordering
                            }
                        }
                    }
                    else
                    {
                        actualItems.Add(JObject.FromObject(newItem));
                    }

                    // Set DisplayOrder
                    newItemDict["DisplayOrder"] = targetList.Count - 1;

                    // Save frame data BEFORE creating the icon
                    FrameDataManager.SaveFrameData();

                    // Add icon to UI using Framemanager.AddIcon for proper sizing
                    Framemanager.AddIcon(newItem, wrapPanel);
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                        $"Could not find frame with ID {frameId} in FrameDataManager.FrameData");
                }

                // Add event handlers to the new icon
                StackPanel sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                if (sp != null)
                {
                    ClickEventAdder(sp, shortcutPath, false);

                    // FIX: Use the FULL Context Menu (Edit, Move, Copy, etc.)
                    // We need to find the parent window to pass it to the menu builder
                    NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(wrapPanel);
                    if (parentWindow != null)
                    {
                        AttachIconContextMenu(sp, newItem, frame, parentWindow);
                    }
                    else
                    {
                        // Fallback (rare) - Should not happen if UI is loaded
                        CreateBasicContextMenu(sp, newItem, shortcutPath);
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Successfully added URL shortcut: {displayName} -> {url}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error adding URL shortcut to frame: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the next display order for new items
        /// </summary>
        private static int GetNextDisplayOrder(dynamic frame)
        {
            try
            {
                var items = frame.Items as JArray ?? new JArray();
                int maxOrder = items.Count > 0 ? items.Max(i => i["DisplayOrder"]?.Value<int>() ?? 0) : -1;
                return maxOrder + 1;
            }
            catch { return 0; }
        }
    }
}
