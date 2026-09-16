using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages drag and drop operations for icon reordering within Data frames.
    /// Updated to be TAB-AWARE (supports reordering inside specific tabs).
    /// </summary>
    public static class IconDragDropManager
    {
        #region Win32 API for cursor position
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        #endregion

        #region Private Fields
        // Drag and drop state management for icon reordering
        private static bool _isDragging = false;
        private static StackPanel _draggedIcon = null;
        private static System.Windows.Point _dragStartPoint;

        private static dynamic _draggedItem = null;
        private static dynamic _sourceFrame = null;
        private static JArray _sourceItemsList = null; // FIX: The specific list we are editing (Main or Tab)

        private static WrapPanel _sourceWrapPanel = null;
        private static WrapPanel _currentHoverWrapPanel = null;
        private static Window _dragPreviewWindow = null;
        private static System.Windows.Point _lastDropIndicatorPosition = new System.Windows.Point(-1, -1);
        private static int _lastDropIndicatorIndex = -1;
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets whether a drag operation is currently in progress
        /// </summary>
        public static bool IsDragging => _isDragging;
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts a drag operation for icon reordering
        /// </summary>
        /// <param name="iconStackPanel">The icon being dragged</param>
        /// <param name="startPoint">The starting point of the drag</param>
        public static void StartIconDrag(StackPanel iconStackPanel, System.Windows.Point startPoint)
        {
            try
            {
                // Only allow dragging in Data frames, not Portal frames
                NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(iconStackPanel);
                if (parentWindow == null) return;

                string frameId = parentWindow.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var FrameData = Framemanager.GetFrameData();
                dynamic frame = FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (frame == null || frame.ItemsType?.ToString() != "Data") return;

                // Find the WrapPanel containing the icons
                WrapPanel wrapPanel = FindWrapPanel(parentWindow);
                if (wrapPanel == null) return;

                // Get the dragged item data from the icon's Tag
                var tagData = iconStackPanel.Tag;
                if (tagData == null) return;

                string filePath = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                if (string.IsNullOrEmpty(filePath)) return;

                // --- FIX: TAB-AWARE LIST SELECTION ---
                // Determine which JArray we are modifying (Main Items vs Active Tab Items)
                JArray targetList = null;
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                if (tabsEnabled)
                {
                    var tabs = frame.Tabs as JArray;
                    int currentTabIndex = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");

                    if (tabs != null && currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                    {
                        var activeTab = tabs[currentTabIndex] as JObject;
                        targetList = activeTab?["Items"] as JArray;
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Drag started in Tab {currentTabIndex}");
                    }
                }

                // Fallback to Main Items if tabs disabled or invalid
                if (targetList == null)
                {
                    targetList = frame.Items as JArray;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Drag started in Main Items");
                }

                if (targetList == null) return;

                // Find the specific item in the specific list
                dynamic draggedItem = null;
                foreach (var item in targetList)
                {
                    if (item["Filename"]?.ToString() == filePath)
                    {
                        draggedItem = item;
                        break;
                    }
                }

                if (draggedItem == null)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Cannot start drag: Item {filePath} not found in the active list.");
                    return;
                }

                // Set drag state
                _isDragging = true;
                _draggedIcon = iconStackPanel;
                _dragStartPoint = startPoint;
                _draggedItem = draggedItem;
                _sourceFrame = frame;
                _sourceItemsList = targetList; // Store the specific list reference!
                _sourceWrapPanel = wrapPanel;

                // Capture mouse
                iconStackPanel.CaptureMouse();

                // Focus parent for Key events (Escape)
                if (parentWindow.Focusable) parentWindow.Focus();

                // Create visual drag preview
                CreateDragPreview(iconStackPanel);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Started drag for {filePath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error starting drag: {ex.Message}");
                CancelDrag();
            }
        }

        /// <summary>
        /// Cancels the current drag operation
        /// </summary>
        public static void CancelDrag()
        {
            try
            {
                if (_isDragging)
                {
                    if (_draggedIcon != null) _draggedIcon.ReleaseMouseCapture();

                    if (_dragPreviewWindow != null)
                    {
                        _dragPreviewWindow.Close();
                        _dragPreviewWindow = null;
                    }

                    if (_sourceWrapPanel != null) RemoveDropZoneIndicators(_sourceWrapPanel);
                    if (_currentHoverWrapPanel != null && _currentHoverWrapPanel != _sourceWrapPanel) RemoveDropZoneIndicators(_currentHoverWrapPanel);
                    _currentHoverWrapPanel = null;

                    _isDragging = false;
                    _draggedIcon = null;
                    _draggedItem = null;
                    _sourceFrame = null;
                    _sourceItemsList = null;
                    _sourceWrapPanel = null;
                    _lastDropIndicatorPosition = new System.Windows.Point(-1, -1);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error cancelling drag: {ex.Message}");
                // Force reset state
                _isDragging = false;
            }
        }

        /// <summary>
        /// 依據物理螢幕座標精確查找游標所在的 NonActivatingWindow（支援多螢幕與任意 DPI 縮放，絕無偏差）
        /// </summary>
        private static NonActivatingWindow FindTargetWindowAtScreenPoint(System.Windows.Point screenPosition)
        {
            // 方案 1: Win32 WindowFromPoint 獲取游標正下方的頂層視窗 Handle（最精準，免疫 DPI 縮放問題）
            try
            {
                POINT pt = new POINT { X = (int)screenPosition.X, Y = (int)screenPosition.Y };
                IntPtr hwnd = WindowFromPoint(pt);
                IntPtr previewHwnd = _dragPreviewWindow != null ? new System.Windows.Interop.WindowInteropHelper(_dragPreviewWindow).Handle : IntPtr.Zero;

                if (hwnd != IntPtr.Zero && hwnd != previewHwnd)
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        if (win is NonActivatingWindow nw && nw.IsVisible)
                        {
                            IntPtr nwHwnd = new System.Windows.Interop.WindowInteropHelper(nw).Handle;
                            if (nwHwnd == hwnd) return nw;
                        }
                    }
                }
            }
            catch { }

            // 方案 2: 使用 WPF PointFromScreen（自動經由 WPF 坐標矩陣還原 DPI）
            foreach (Window win in Application.Current.Windows)
            {
                if (win is NonActivatingWindow nw && nw.IsVisible)
                {
                    try
                    {
                        System.Windows.Point localPoint = nw.PointFromScreen(screenPosition);
                        double w = nw.ActualWidth > 0 ? nw.ActualWidth : nw.Width;
                        double h = nw.ActualHeight > 0 ? nw.ActualHeight : nw.Height;
                        if (localPoint.X >= 0 && localPoint.X <= w && localPoint.Y >= 0 && localPoint.Y <= h)
                        {
                            return nw;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        /// <summary>
        /// Handles mouse move during drag operation
        /// </summary>
        public static void HandleDragMove(System.Windows.Point screenPosition)
        {
            if (!_isDragging || _draggedIcon == null) return;

            try
            {
                UpdateDragPreviewPosition(screenPosition);

                NonActivatingWindow targetWin = FindTargetWindowAtScreenPoint(screenPosition);
                WrapPanel targetPanel = targetWin != null ? FindWrapPanel(targetWin) : _sourceWrapPanel;

                if (targetPanel == null) targetPanel = _sourceWrapPanel;

                if (_currentHoverWrapPanel != targetPanel)
                {
                    if (_currentHoverWrapPanel != null) RemoveDropZoneIndicators(_currentHoverWrapPanel);
                    _currentHoverWrapPanel = targetPanel;
                }

                if (_currentHoverWrapPanel != null)
                {
                    System.Windows.Point wrapPanelPosition = _currentHoverWrapPanel.PointFromScreen(screenPosition);
                    ShowDropZoneIndicators(_currentHoverWrapPanel, wrapPanelPosition);
                }
            }
            catch { }
        }

        /// <summary>
        /// Completes the drag operation and performs reordering or cross-frame move
        /// </summary>
        public static void CompleteDrag(System.Windows.Point screenPosition)
        {
            if (!_isDragging || _draggedIcon == null || _sourceWrapPanel == null) return;

            try
            {
                NonActivatingWindow sourceWindow = FindVisualParent<NonActivatingWindow>(_sourceWrapPanel);
                NonActivatingWindow targetWindow = FindTargetWindowAtScreenPoint(screenPosition);

                if (targetWindow == null || targetWindow == sourceWindow)
                {
                    System.Windows.Point wrapPanelPosition = _sourceWrapPanel.PointFromScreen(screenPosition);
                    int dropPosition = CalculateDropPosition(_sourceWrapPanel, wrapPanelPosition);
                    ReorderframeItems(dropPosition);
                }
                else
                {
                    MoveItemToTargetFrame(targetWindow, screenPosition);
                }

                CancelDrag();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error completing drag: {ex.Message}");
                CancelDrag();
            }
        }

        public static void CompleteDrag()
        {
            GetCursorPos(out POINT pt);
            CompleteDrag(new System.Windows.Point(pt.X, pt.Y));
        }

        private static void MoveItemToTargetFrame(NonActivatingWindow targetWindow, System.Windows.Point screenPosition)
        {
            if (targetWindow == null || _draggedItem == null || _sourceItemsList == null || _sourceFrame == null) return;

            string targetFrameId = targetWindow.Tag?.ToString();
            if (string.IsNullOrEmpty(targetFrameId)) return;

            var FrameData = Framemanager.GetFrameData();
            dynamic targetFrame = FrameData.FirstOrDefault(f => f.Id?.ToString() == targetFrameId);
            if (targetFrame == null || targetFrame.ItemsType?.ToString() != "Data")
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, "Cannot drop into target frame (not a Data frame or not found).");
                return;
            }

            // Target JArray
            JArray targetList = null;
            bool tabsEnabled = targetFrame.TabsEnabled?.ToString().ToLower() == "true";
            if (tabsEnabled)
            {
                var tabs = targetFrame.Tabs as JArray;
                int currentTabIndex = Convert.ToInt32(targetFrame.CurrentTab?.ToString() ?? "0");
                if (tabs != null && currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                {
                    var activeTab = tabs[currentTabIndex] as JObject;
                    targetList = activeTab?["Items"] as JArray;
                }
            }

            if (targetList == null)
            {
                targetList = targetFrame.Items as JArray;
                if (targetList == null)
                {
                    targetList = new JArray();
                    if (targetFrame is JObject jObj) jObj["Items"] = targetList;
                    else targetFrame.Items = targetList;
                }
            }

            WrapPanel targetWrapPanel = FindWrapPanel(targetWindow);
            int insertIndex = targetList.Count;
            if (targetWrapPanel != null)
            {
                try
                {
                    System.Windows.Point targetPanelPoint = targetWrapPanel.PointFromScreen(screenPosition);
                    insertIndex = CalculateDropPositionForPanel(targetWrapPanel, targetPanelPoint, targetList);
                }
                catch { }
            }

            // 1. Remove from source list
            int currentPosition = -1;
            for (int i = 0; i < _sourceItemsList.Count; i++)
            {
                if (string.Equals(_sourceItemsList[i]["Filename"]?.ToString(), _draggedItem["Filename"]?.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    currentPosition = i;
                    break;
                }
            }

            if (currentPosition >= 0)
            {
                _sourceItemsList.RemoveAt(currentPosition);
            }
            else if (_draggedItem is JToken draggedToken && draggedToken.Parent != null)
            {
                draggedToken.Remove();
            }

            for (int i = 0; i < _sourceItemsList.Count; i++)
            {
                _sourceItemsList[i]["DisplayOrder"] = i;
            }

            // 2. Clone and Insert into target list
            JToken itemToInsert = _draggedItem is JToken jt ? jt.DeepClone() : JToken.FromObject(_draggedItem);
            insertIndex = Math.Max(0, Math.Min(insertIndex, targetList.Count));
            targetList.Insert(insertIndex, itemToInsert);
            for (int i = 0; i < targetList.Count; i++)
            {
                targetList[i]["DisplayOrder"] = i;
            }

            // 3. Save
            FrameDataManager.SaveFrameData();

            // 4. Refresh both frames
            Application.Current.Dispatcher.Invoke(() =>
            {
                NonActivatingWindow sourceWindow = FindVisualParent<NonActivatingWindow>(_sourceWrapPanel);
                if (sourceWindow != null)
                {
                    Framemanager.RefreshFrameUsingFormApproach(sourceWindow, _sourceFrame);
                }
                Framemanager.RefreshFrameUsingFormApproach(targetWindow, targetFrame);
            });

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                $"Transferred item {_draggedItem["Filename"]} to Target Frame {targetFrameId}");
        }

        private static int CalculateDropPositionForPanel(WrapPanel wrapPanel, System.Windows.Point mousePosition, JArray targetList)
        {
            try
            {
                if (wrapPanel == null) return 0;
                var iconPanels = wrapPanel.Children.OfType<StackPanel>().Where(sp => sp != _draggedIcon).ToList();
                if (iconPanels.Count == 0) return 0;

                double closestDistance = double.MaxValue;
                int bestInsertIndex = 0;

                for (int i = 0; i < iconPanels.Count; i++)
                {
                    var iconPanel = iconPanels[i];
                    try
                    {
                        var iconPosition = iconPanel.TranslatePoint(new System.Windows.Point(0, 0), wrapPanel);
                        var iconCenter = new System.Windows.Point(
                            iconPosition.X + iconPanel.ActualWidth / 2,
                            iconPosition.Y + iconPanel.ActualHeight / 2
                        );

                        double distance = Math.Sqrt(Math.Pow(mousePosition.X - iconCenter.X, 2) + Math.Pow(mousePosition.Y - iconCenter.Y, 2));
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            bool insertBefore = mousePosition.X < iconCenter.X;

                            var tagData = iconPanel.Tag;
                            string filePath = tagData?.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();

                            int dataIndex = -1;
                            if (targetList != null && !string.IsNullOrEmpty(filePath))
                            {
                                for (int k = 0; k < targetList.Count; k++)
                                {
                                    if (string.Equals(targetList[k]["Filename"]?.ToString(), filePath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        dataIndex = k;
                                        break;
                                    }
                                }
                            }

                            bestInsertIndex = dataIndex != -1 ? (insertBefore ? dataIndex : dataIndex + 1) : (insertBefore ? i : i + 1);
                        }
                    }
                    catch { }
                }

                int maxCount = targetList?.Count ?? 0;
                return Math.Max(0, Math.Min(bestInsertIndex, maxCount));
            }
            catch
            {
                return 0;
            }
        }
        #endregion

        #region Reordering Logic (The Core Fix)

        private static int CalculateDropPosition(WrapPanel wrapPanel, System.Windows.Point mousePosition)
        {
            try
            {
                if (wrapPanel == null || _sourceItemsList == null || _sourceItemsList.Count == 0) return 0;

                var iconPanels = wrapPanel.Children.OfType<StackPanel>().ToList();
                if (iconPanels.Count == 0) return 0;

                double closestDistance = double.MaxValue;
                int closestIndex = 0;
                bool insertBefore = true;

                for (int i = 0; i < iconPanels.Count; i++)
                {
                    var iconPanel = iconPanels[i];
                    try
                    {
                        var iconPosition = iconPanel.TranslatePoint(new System.Windows.Point(0, 0), wrapPanel);
                        var iconCenter = new System.Windows.Point(
                            iconPosition.X + iconPanel.ActualWidth / 2,
                            iconPosition.Y + iconPanel.ActualHeight / 2
                        );

                        double distance = Math.Sqrt(Math.Pow(mousePosition.X - iconCenter.X, 2) + Math.Pow(mousePosition.Y - iconCenter.Y, 2));

                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestIndex = i;
                            insertBefore = mousePosition.X < iconCenter.X;
                        }
                    }
                    catch { }
                }

                // 找到 closestIndex 對應到 _sourceItemsList 的 dataIndex
                int targetDataIndex = closestIndex;
                if (closestIndex >= 0 && closestIndex < iconPanels.Count)
                {
                    var tagData = iconPanels[closestIndex].Tag;
                    string filePath = tagData?.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        for (int k = 0; k < _sourceItemsList.Count; k++)
                        {
                            if (_sourceItemsList[k]["Filename"]?.ToString() == filePath)
                            {
                                targetDataIndex = k;
                                break;
                            }
                        }
                    }
                }

                return insertBefore ? targetDataIndex : targetDataIndex + 1;
            }
            catch
            {
                return 0;
            }
        }

        private static void ReorderframeItems(int newPosition)
        {
            try
            {
                if (_sourceItemsList == null || _draggedItem == null) return;

                int currentPosition = -1;
                for (int i = 0; i < _sourceItemsList.Count; i++)
                {
                    if (_sourceItemsList[i]["Filename"]?.ToString() == _draggedItem["Filename"]?.ToString())
                    {
                        currentPosition = i;
                        break;
                    }
                }

                if (currentPosition == -1) return;

                // 若目標位置在當前位置之後，因為移除當前項目後索引會往前縮 1，因此目標索引減 1
                int finalTargetIndex = newPosition;
                if (currentPosition < newPosition)
                {
                    finalTargetIndex = newPosition - 1;
                }

                finalTargetIndex = Math.Max(0, Math.Min(finalTargetIndex, _sourceItemsList.Count - 1));

                // 只有在目標索引不同時才執行重新排序
                if (finalTargetIndex != currentPosition)
                {
                    var itemToMove = _sourceItemsList[currentPosition];
                    _sourceItemsList.RemoveAt(currentPosition);
                    _sourceItemsList.Insert(finalTargetIndex, itemToMove);

                    // 更新所有項目的 DisplayOrder
                    for (int i = 0; i < _sourceItemsList.Count; i++)
                    {
                        _sourceItemsList[i]["DisplayOrder"] = i;
                    }

                    FrameDataManager.SaveFrameData();

                    // 即時刷新 UI
                    RefreshFrameUI();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Reordered item {_draggedItem["Filename"]} from {currentPosition} to {finalTargetIndex}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error reordering items: {ex.Message}");
            }
        }

        private static void RefreshFrameUI()
        {
            try
            {
                if (_sourceFrame == null || _sourceWrapPanel == null) return;

                NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(_sourceWrapPanel);
                if (parentWindow == null) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // FIX: Delegate to Framemanager's robust refresh logic
                    // This handles Tabs vs Main logic automatically
                    Framemanager.RefreshFrameUsingFormApproach(parentWindow, _sourceFrame);
                });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing frame UI: {ex.Message}");
            }
        }

        #endregion

        #region Private Helper Methods (Visuals & Utils)

        // ... (Standard FindVisualParent, FindWrapPanel, GetCursorPos, DragPreview logic remains same) ...
        // Included for completeness to ensure the file compiles without missing refs

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private static WrapPanel FindWrapPanel(DependencyObject parent, int depth = 0, int maxDepth = 10)
        {
            if (parent == null || depth > maxDepth) return null;
            if (parent is WrapPanel wrapPanel) return wrapPanel;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindWrapPanel(VisualTreeHelper.GetChild(parent, i), depth + 1, maxDepth);
                if (result != null) return result;
            }
            return null;
        }

        private static System.Windows.Point GetCursorPosition()
        {
            POINT point;
            GetCursorPos(out point);
            return new System.Windows.Point(point.X, point.Y);
        }

        private static double GetDpiScaleFactor(Window window)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)window.Left, (int)window.Top));
            using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                return graphics.DpiX / 96.0;
            }
        }

        private static void CreateDragPreview(StackPanel originalIcon)
        {
            try
            {
                if (_dragPreviewWindow != null)
                {
                    _dragPreviewWindow.Close();
                    _dragPreviewWindow = null;
                }

                NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(originalIcon);
                double dpiScale = parentWindow != null ? GetDpiScaleFactor(parentWindow) : 1.0;

                _dragPreviewWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = true,
                    Width = originalIcon.ActualWidth > 0 ? originalIcon.ActualWidth : 60,
                    Height = originalIcon.ActualHeight > 0 ? originalIcon.ActualHeight : 80,
                    IsHitTestVisible = false,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                // Clone visual content for preview
                StackPanel previewContent = new StackPanel { Width = originalIcon.Width, Margin = originalIcon.Margin, Opacity = 0.7 };

                // Copy Image/Grid
                var originalGrid = originalIcon.Children.OfType<Grid>().FirstOrDefault();
                var originalImage = originalIcon.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();

                if (originalGrid != null)
                {
                    // Clone Grid (Network Icon)
                    Grid previewGrid = new Grid { Width = originalGrid.Width, Height = originalGrid.Height, Margin = originalGrid.Margin };
                    var gridImage = originalGrid.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                    if (gridImage != null) previewGrid.Children.Add(new System.Windows.Controls.Image { Source = gridImage.Source, Width = gridImage.Width, Height = gridImage.Height, Margin = gridImage.Margin });

                    var netInd = originalGrid.Children.OfType<TextBlock>().FirstOrDefault();
                    if (netInd != null) previewGrid.Children.Add(new TextBlock { Text = netInd.Text, FontSize = netInd.FontSize, Foreground = netInd.Foreground, Margin = netInd.Margin });

                    previewContent.Children.Add(previewGrid);
                }
                else if (originalImage != null)
                {
                    previewContent.Children.Add(new System.Windows.Controls.Image { Source = originalImage.Source, Width = originalImage.Width, Height = originalImage.Height, Margin = originalImage.Margin });
                }

                // Copy Label
                var originalLabel = originalIcon.Children.OfType<TextBlock>().FirstOrDefault();
                if (originalLabel != null)
                {
                    previewContent.Children.Add(new TextBlock
                    {
                        Text = originalLabel.Text,
                        TextWrapping = originalLabel.TextWrapping,
                        TextAlignment = originalLabel.TextAlignment,
                        Foreground = originalLabel.Foreground,
                        Width = originalLabel.Width
                    });
                }

                _dragPreviewWindow.Content = previewContent;

                System.Windows.Point cursorPos = GetCursorPosition();
                _dragPreviewWindow.Left = (cursorPos.X / dpiScale) + 10;
                _dragPreviewWindow.Top = (cursorPos.Y / dpiScale) - 10;
                _dragPreviewWindow.Show();
            }
            catch { }
        }

        private static void UpdateDragPreviewPosition(System.Windows.Point screenPosition)
        {
            if (_dragPreviewWindow != null)
            {
                double dpiScale = 1.0; // Simplified for speed, usually sufficient
                _dragPreviewWindow.Left = (screenPosition.X / dpiScale) + 10;
                _dragPreviewWindow.Top = (screenPosition.Y / dpiScale) - 10;
            }
        }

        private static void ShowDropZoneIndicators(WrapPanel wrapPanel, System.Windows.Point mousePosition)
        {
            // Simple optimization
            if ((mousePosition - _lastDropIndicatorPosition).Length < 15) return;
            _lastDropIndicatorPosition = mousePosition;

            RemoveDropZoneIndicators(wrapPanel);

            var iconPanels = wrapPanel.Children.OfType<StackPanel>().Where(sp => sp != _draggedIcon).ToList();
            if (iconPanels.Count == 0) return;

            StackPanel closestIcon = null;
            double closestDist = double.MaxValue;
            bool insertBefore = true;

            foreach (var panel in iconPanels)
            {
                var pos = panel.TranslatePoint(new System.Windows.Point(0, 0), wrapPanel);
                var center = new System.Windows.Point(pos.X + panel.ActualWidth / 2, pos.Y + panel.ActualHeight / 2);
                double dist = (mousePosition - center).Length;

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIcon = panel;
                    insertBefore = mousePosition.X < center.X;
                }
            }

            if (closestIcon != null)
            {
                var indicator = new Border
                {
                    Width = 3,
                    Height = closestIcon.ActualHeight,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 150, 255)),
                    CornerRadius = new CornerRadius(1.5),
                    Tag = "DropIndicator",
                    Margin = new Thickness(2, 5, 2, 5),
                    Effect = new DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(0, 150, 255), BlurRadius = 8, ShadowDepth = 0 }
                };

                int idx = wrapPanel.Children.IndexOf(closestIcon);
                if (insertBefore) wrapPanel.Children.Insert(idx, indicator);
                else wrapPanel.Children.Insert(idx + 1, indicator);
            }
        }

        private static void RemoveDropZoneIndicators(WrapPanel wrapPanel)
        {
            if (wrapPanel == null) return;
            var toRemove = wrapPanel.Children.OfType<Border>().Where(b => "DropIndicator".Equals(b.Tag?.ToString())).ToList();
            foreach (var item in toRemove) wrapPanel.Children.Remove(item);
        }

        #endregion
    }
}