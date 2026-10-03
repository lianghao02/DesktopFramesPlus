using System;
using System.Windows.Media;

namespace Desktop_Frames.FarmFences
{
    public sealed class FenceAppearance
    {
        public static Func<string, FenceAppearance>? Resolve { get; set; }
        public Color BorderColor { get; init; } = Colors.Gray;
        public double BorderWidth { get; init; } = 2;
        public double CornerRadius { get; init; } = 6;
        public string FontFamily { get; init; } = "Microsoft JhengHei";
        public double TitleSize { get; init; } = 12;
        public bool Bold { get; init; }
        public Color TitleColor { get; init; } = Colors.White;
        public string MenuSymbol { get; init; } = "♥";
    }
}
