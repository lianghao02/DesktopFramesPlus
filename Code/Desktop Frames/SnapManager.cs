using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms; // For Screen.AllScreens
using System.Windows.Media; // For VisualTreeHelper

namespace Desktop_Frames
{
    public static class SnapManager
    {
        private const double SnapThreshold = 20; // Reduced slightly for tighter feel
        private const double MinGap = 10;        // Gap between snapped frames

        // Recursion guard to prevent the "fighting" loop
        private static bool _isSnapping = false;




        private static double? _activeSnapX = null;
        private static double? _activeSnapY = null;

        public static NonActivatingWindow ActiveDragWindow = null;

        public static void StartDrag(NonActivatingWindow win)
        {
            ActiveDragWindow = win;
            _activeSnapX = null;
            _activeSnapY = null;
        }

        public static void EndDrag(NonActivatingWindow win)
        {
            try
            {
                if (ActiveDragWindow != win) return;

                SnapGuideOverlay.Instance.HideGuides();
                _activeSnapX = null;
                _activeSnapY = null;

                string myId = GetFrameIdFromWindow(win);
                if (myId != null && FrameDataManager.DockingMap.TryGetValue(myId, out List<string> parentIds))
                {
                    AnimateSnapConfirmation(win);
                    FrameDataManager.UpdateDockedRelationships(myId, parentIds);
                }
                else if (myId != null)
                {
                    ShowSnapPreview(win, false);
                    FrameDataManager.UpdateDockedRelationships(myId, null);
                }
            }
            finally
            {
                ActiveDragWindow = null;
            }
        }

        public static void AddSnapping(NonActivatingWindow win, IDictionary<string, object> FrameData)
        {
            string myId = FrameData.ContainsKey("Id") ? FrameData["Id"].ToString() : null;

            win.PreviewMouseLeftButtonUp += (sender, e) =>
            {
                if (myId != null && FrameDataManager.DockingMap.TryGetValue(myId, out List<string> parentIds))
                {
                    AnimateSnapConfirmation(win);
                    FrameDataManager.UpdateDockedRelationships(myId, parentIds);
                }
                else if (myId != null)
                {
                    ShowSnapPreview(win, false);
                    FrameDataManager.UpdateDockedRelationships(myId, null);
                }
            };

            win.LocationChanged += (sender, e) =>
            {
                if (_isSnapping) return;
                if (ActiveDragWindow != win) return;

                _isSnapping = true;
                try
                {
                    var allFrames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                    var (newLeft, newTop) = CalculateSnapPosition(win, allFrames);

                    if (Math.Abs(win.Left - newLeft) > 0.1 || Math.Abs(win.Top - newTop) > 0.1)
                    {
                        win.Left = newLeft;
                        win.Top = newTop;
                        FrameData["X"] = newLeft;
                        FrameData["Y"] = newTop;
                        FrameDataManager.SaveFrameData();

                        if (myId != null)
                        {
                            // Find ALL co-parents sitting above this window that overlap horizontally by at least 20%
                            var parents = allFrames.Where(f =>
                            {
                                if (f == win) return false;
                                bool verticalMatch = Math.Abs(f.Top + f.Height + MinGap - newTop) < SnapThreshold;
                                double overlapWidth = Math.Min(f.Left + f.Width, newLeft + win.Width) - Math.Max(f.Left, newLeft);
                                return verticalMatch && overlapWidth > (Math.Min(f.Width, win.Width) * 0.2);
                            }).ToList();

                            var parentIds = parents.Select(p => GetFrameIdFromWindow(p)).Where(id => id != null).ToList();

                            if (parentIds.Count > 0)
                            {
                                FrameDataManager.DockingMap[myId] = parentIds;
                                ShowSnapPreview(win, true);
                            }
                            else
                            {
                                FrameDataManager.DockingMap.Remove(myId);
                                ShowSnapPreview(win, false);
                            }
                        }
                    }
                }
                finally
                {
                    _isSnapping = false;
                }
            };
        }



        private static (double, double) CalculateSnapPosition(NonActivatingWindow current, List<NonActivatingWindow> allFrames)
        {
            if (!SettingsManager.IsSnapEnabled)
            {
                SnapGuideOverlay.Instance.HideGuides();
                return (current.Left, current.Top);
            }

            double dpiScale = GetDpiScale(current);

            // 1. Calculate moving rect and geometric center in WPF DIPs
            Rect movingRect = new Rect(current.Left, current.Top, current.Width, current.Height);
            double centerX = movingRect.Left + (movingRect.Width / 2.0);
            double centerY = movingRect.Top + (movingRect.Height / 2.0);

            // 2. Identify the active screen based on window center point
            System.Drawing.Point centerPixel = new System.Drawing.Point(
                (int)Math.Round(centerX * dpiScale),
                (int)Math.Round(centerY * dpiScale));

            Screen activeScreen = Screen.FromPoint(centerPixel);
            if (activeScreen == null) activeScreen = Screen.PrimaryScreen;

            // Convert working area (excluding taskbar) from physical pixels to WPF DIPs
            Rect workArea = new Rect(
                activeScreen.WorkingArea.Left / dpiScale,
                activeScreen.WorkingArea.Top / dpiScale,
                activeScreen.WorkingArea.Width / dpiScale,
                activeScreen.WorkingArea.Height / dpiScale);

            Rect screenBounds = new Rect(
                activeScreen.Bounds.Left / dpiScale,
                activeScreen.Bounds.Top / dpiScale,
                activeScreen.Bounds.Width / dpiScale,
                activeScreen.Bounds.Height / dpiScale);

            // 3. Collect other frames as Rects
            var otherRects = allFrames
                .Where(f => f != current && f.IsVisible)
                .Select(f => new Rect(f.Left, f.Top, f.Width, f.Height))
                .ToList();

            // 4. Calculate smart snapping with FrameSnapCalculator
            var snapResult = FrameSnapCalculator.Calculate(
                movingRect,
                otherRects,
                workArea,
                _activeSnapX,
                _activeSnapY);

            // 5. Update active snap memory for hysteresis damping
            _activeSnapX = snapResult.HasSnapX ? snapResult.SnappedX : null;
            _activeSnapY = snapResult.HasSnapY ? snapResult.SnappedY : null;

            // 6. Display or hide alignment guidelines
            if (snapResult.HasSnapX || snapResult.HasSnapY)
            {
                SnapGuideOverlay.Instance.UpdateGuides(snapResult.GuideLineX, snapResult.GuideLineY, screenBounds);
            }
            else
            {
                SnapGuideOverlay.Instance.HideGuides();
            }

            return (snapResult.SnappedX, snapResult.SnappedY);
        }

        // Helper to get DPI scaling
        private static double GetDpiScale(Visual visual)
        {
            try
            {
                var source = PresentationSource.FromVisual(visual);
                if (source != null && source.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            catch { }
            return 1.0; // Default if fails
        }
        // --- CONSTRAINT-BASED MULTI-PARENT STACK RESOLVER ---
        public static void CascadeStack(string parentId, double deltaY)
        {
            // Find all children that list this parentId in their co-parent list
            var childrenIds = FrameDataManager.DockingMap
                .Where(kvp => kvp.Value != null && kvp.Value.Contains(parentId))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var childId in childrenIds)
            {
                var win = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>()
                    .FirstOrDefault(w => GetFrameIdFromWindow(w) == childId);

                if (win != null && FrameDataManager.DockingMap.TryGetValue(childId, out List<string> parentIds))
                {
                    // Find all currently active window instances for all co-parents of this child
                    var activeParents = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>()
                        .Where(w => parentIds.Contains(GetFrameIdFromWindow(w)))
                        .ToList();

                    if (activeParents.Count > 0)
                    {
                        // The golden geometric rule: Anchor below the lowest unrolled bottom edge among all co-parents
                        double maxParentBottom = activeParents.Max(p => p.Top + p.Height);
                        double targetTop = maxParentBottom + 10.0; // Standard 10px snap gap

                        if (Math.Abs(win.Top - targetTop) > 0.5)
                        {
                            double actualDeltaY = targetTop - win.Top;
                            win.Top = targetTop;

                            // Recursively cascade downstream to any frames docked beneath this child
                            CascadeStack(childId, actualDeltaY);
                        }
                    }
                }
            }
        }
        public static string GetFrameIdFromWindow(NonActivatingWindow win)
        {
            return win?.Tag?.ToString();
        }
        // --- ACCORDION SNAP FEEDBACK ENGINE ---
        // Holds a vibrant border pulse for 350ms before smoothly fading out over 650ms (1000ms total)
        // --- INTERACTIVE SNAP FEEDBACK ENGINE ---
        private static readonly Dictionary<NonActivatingWindow, System.Windows.Media.Brush> _snapOrigBrushes = new Dictionary<NonActivatingWindow, System.Windows.Media.Brush>();
        private static readonly Dictionary<NonActivatingWindow, Thickness> _snapOrigThicknesses = new Dictionary<NonActivatingWindow, Thickness>();
        private static readonly HashSet<NonActivatingWindow> _inSnapPreview = new HashSet<NonActivatingWindow>();
        public static void ShowSnapPreview(NonActivatingWindow win, bool isSnapped)
        {
            try
            {
                if (win?.Content is not Border border) return;

                if (isSnapped)
                {
                    if (!_inSnapPreview.Contains(win))
                    {
                        _inSnapPreview.Add(win);
                        _snapOrigBrushes[win] = border.BorderBrush;
                        _snapOrigThicknesses[win] = border.BorderThickness;

                        border.BeginAnimation(Border.BorderBrushProperty, null);
                        border.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, 255));
                        border.BorderThickness = new Thickness(Math.Max(3, _snapOrigThicknesses[win].Top + 2));
                    }
                }
                else if (_inSnapPreview.Contains(win))
                {
                    _inSnapPreview.Remove(win);
                    border.BeginAnimation(Border.BorderBrushProperty, null);
                    if (_snapOrigBrushes.TryGetValue(win, out System.Windows.Media.Brush origB)) border.BorderBrush = origB;
                    if (_snapOrigThicknesses.TryGetValue(win, out Thickness origT)) border.BorderThickness = origT;
                }
            }
            catch { }
        }
        public static void AnimateSnapConfirmation(NonActivatingWindow win)
        {
            try
            {
                if (win?.Content is not Border border || !_inSnapPreview.Contains(win)) return;
                _inSnapPreview.Remove(win);

                System.Windows.Media.Brush origBrush = _snapOrigBrushes.ContainsKey(win) ? _snapOrigBrushes[win] : border.BorderBrush;
                Thickness origThick = _snapOrigThicknesses.ContainsKey(win) ? _snapOrigThicknesses[win] : border.BorderThickness;

                var pulseBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, 255));
                border.BorderBrush = pulseBrush;
                border.BorderThickness = origThick;

                var fadePulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                fadePulse.Completed += (s, e) =>
                {
                    pulseBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, null);
                    border.BorderBrush = origBrush;
                };

                pulseBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, fadePulse);
            }
            catch { }
        }
    }
}