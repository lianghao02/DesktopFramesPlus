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
    // FrameManager 分檔：Selection（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
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
    }
}
