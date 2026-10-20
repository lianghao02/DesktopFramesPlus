using System;
using System.Windows;

namespace Desktop_Frames
{
    public enum ResizeEdge { None, Left, Right, Top, TopLeft, TopRight, Bottom, BottomLeft, BottomRight }

    public static class ResizeSnapCalculator
    {
        public const double Step = 10;
        public static Rect Calculate(Rect proposed, ResizeEdge edge, double minWidth = 1, double minHeight = 1,
            double maxWidth = double.PositiveInfinity, double maxHeight = double.PositiveInfinity)
        {
            bool left = edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft;
            bool right = edge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight;
            bool top = edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight;
            bool bottom = edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight;
            double width = left || right ? Math.Clamp(Math.Round(proposed.Width / Step) * Step, Math.Max(1, minWidth), maxWidth) : proposed.Width;
            double height = top || bottom ? Math.Clamp(Math.Round(proposed.Height / Step) * Step, Math.Max(1, minHeight), maxHeight) : proposed.Height;
            return new Rect(left ? proposed.Right - width : proposed.Left, top ? proposed.Bottom - height : proposed.Top, width, height);
        }

        public static int HitTest(Point point, Size size, double border = 7)
        {
            bool left = point.X >= 0 && point.X < border, right = point.X <= size.Width && point.X > size.Width - border;
            bool top = point.Y >= 0 && point.Y < border, bottom = point.Y <= size.Height && point.Y > size.Height - border;
            if (left && top) return 13;
            if (right && top) return 14;
            if (left && bottom) return 16;
            if (right && bottom) return 17;
            if (left) return 10;
            if (right) return 11;
            if (top) return 12;
            if (bottom) return 15;
            return 0;
        }
    }
}
