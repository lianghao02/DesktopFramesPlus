using System;
using System.Collections.Generic;
using System.Windows;

namespace Desktop_Frames
{
    public struct SnapResult
    {
        public double SnappedX;
        public double SnappedY;
        public bool HasSnapX;
        public bool HasSnapY;
        public double? GuideLineX; // X coordinate for vertical alignment line
        public double? GuideLineY; // Y coordinate for horizontal alignment line
    }

    /// <summary>
    /// Pure mathematical geometry calculator for smart frame edge-to-edge snapping,
    /// standard spacing gap, and screen working area alignment.
    /// Free of UI dependencies, file system, or settings storage.
    /// </summary>
    public static class FrameSnapCalculator
    {
        public const double SnapThreshold = 10.0;    // Trigger distance in WPF DIPs
        public const double ReleaseThreshold = 14.0; // Hysteresis release distance
        public const double StandardGap = 8.0;       // Standard gap between adjacent frames

        public static SnapResult Calculate(
            Rect movingRect,
            IEnumerable<Rect> otherRects,
            Rect screenWorkArea,
            double? activeSnapX = null,
            double? activeSnapY = null)
        {
            SnapResult result = new SnapResult
            {
                SnappedX = movingRect.X,
                SnappedY = movingRect.Y,
                HasSnapX = false,
                HasSnapY = false,
                GuideLineX = null,
                GuideLineY = null
            };

            double curLeft = movingRect.X;
            double curRight = curLeft + movingRect.Width;
            double curCenterX = curLeft + (movingRect.Width / 2.0);

            double curTop = movingRect.Y;
            double curBottom = curTop + movingRect.Height;
            double curCenterY = curTop + (movingRect.Height / 2.0);

            // --- HORIZONTAL SNAP CHECKS ---
            double bestDeltaX = double.MaxValue;
            double? chosenGuideX = null;

            // 1. Screen WorkArea Left / Right edges
            CheckEdge(curLeft, screenWorkArea.Left, screenWorkArea.Left, ref bestDeltaX, ref chosenGuideX, activeSnapX);
            CheckEdge(curRight, screenWorkArea.Right, screenWorkArea.Right, ref bestDeltaX, ref chosenGuideX, activeSnapX);

            // 2. Other Frames Left, Right, Adjacent Gaps, and Center
            foreach (var other in otherRects)
            {
                double oLeft = other.X;
                double oRight = oLeft + other.Width;
                double oCenterX = oLeft + (other.Width / 2.0);

                // Same edge alignment (Left-to-Left, Right-to-Right)
                CheckEdge(curLeft, oLeft, oLeft, ref bestDeltaX, ref chosenGuideX, activeSnapX);
                CheckEdge(curRight, oRight, oRight, ref bestDeltaX, ref chosenGuideX, activeSnapX);

                // Adjacent gap (Right to Left - 8px, Left to Right + 8px)
                CheckEdge(curRight, oLeft - StandardGap, oLeft, ref bestDeltaX, ref chosenGuideX, activeSnapX);
                CheckEdge(curLeft, oRight + StandardGap, oRight, ref bestDeltaX, ref chosenGuideX, activeSnapX);

                // Center alignment
                CheckEdge(curCenterX, oCenterX, oCenterX, ref bestDeltaX, ref chosenGuideX, activeSnapX);
            }

            if (Math.Abs(bestDeltaX) < double.MaxValue)
            {
                result.SnappedX = curLeft + bestDeltaX;
                result.HasSnapX = true;
                result.GuideLineX = chosenGuideX;
            }

            // --- VERTICAL SNAP CHECKS ---
            double bestDeltaY = double.MaxValue;
            double? chosenGuideY = null;

            // 1. Screen WorkArea Top / Bottom edges
            CheckEdge(curTop, screenWorkArea.Top, screenWorkArea.Top, ref bestDeltaY, ref chosenGuideY, activeSnapY);
            CheckEdge(curBottom, screenWorkArea.Bottom, screenWorkArea.Bottom, ref bestDeltaY, ref chosenGuideY, activeSnapY);

            // 2. Other Frames Top, Bottom, Adjacent Gaps, and Center
            foreach (var other in otherRects)
            {
                double oTop = other.Y;
                double oBottom = oTop + other.Height;
                double oCenterY = oTop + (other.Height / 2.0);

                // Same edge alignment (Top-to-Top, Bottom-to-Bottom)
                CheckEdge(curTop, oTop, oTop, ref bestDeltaY, ref chosenGuideY, activeSnapY);
                CheckEdge(curBottom, oBottom, oBottom, ref bestDeltaY, ref chosenGuideY, activeSnapY);

                // Adjacent gap (Bottom to Top - 8px, Top to Bottom + 8px)
                CheckEdge(curBottom, oTop - StandardGap, oTop, ref bestDeltaY, ref chosenGuideY, activeSnapY);
                CheckEdge(curTop, oBottom + StandardGap, oBottom, ref bestDeltaY, ref chosenGuideY, activeSnapY);

                // Center alignment
                CheckEdge(curCenterY, oCenterY, oCenterY, ref bestDeltaY, ref chosenGuideY, activeSnapY);
            }

            if (Math.Abs(bestDeltaY) < double.MaxValue)
            {
                result.SnappedY = curTop + bestDeltaY;
                result.HasSnapY = true;
                result.GuideLineY = chosenGuideY;
            }

            return result;
        }

        private static void CheckEdge(
            double currentPos,
            double targetPos,
            double guidePos,
            ref double bestDelta,
            ref double? chosenGuide,
            double? activeSnap)
        {
            double delta = targetPos - currentPos;
            double distance = Math.Abs(delta);

            // If we are already snapped to this exact target, use larger release threshold for damping
            double allowedThreshold = (activeSnap.HasValue && Math.Abs(activeSnap.Value - targetPos) < 1.0)
                ? ReleaseThreshold
                : SnapThreshold;

            if (distance <= allowedThreshold && distance < Math.Abs(bestDelta))
            {
                bestDelta = delta;
                chosenGuide = guidePos;
            }
        }
    }
}
