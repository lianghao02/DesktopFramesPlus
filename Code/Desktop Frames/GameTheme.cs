using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames
{
    /// <summary>共用 Q 版 2D 遊戲收納視覺，嚴格維持高對比可讀性與純淨道具槽表格質感。</summary>
    public static class GameTheme
    {
        public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached("IsSelected", typeof(bool), typeof(GameTheme), new PropertyMetadata(false));
        public static bool GetIsSelected(DependencyObject item) => (bool)item.GetValue(IsSelectedProperty);
        public static void SetIsSelected(DependencyObject item, bool value) => item.SetValue(IsSelectedProperty, value);

        // 核心色彩：高對比深墨炭黑、溫潤象牙羊皮、深木棕框線與典雅森林墨綠
        public static readonly Brush Paper = Brush("#FAF1DE");
        public static readonly Brush Ink = Brush("#1A120B");
        // 次要說明文字：羊皮紙卡片上對比 ≥ 7:1，取代原本約 3.7:1 的系統灰。
        public static readonly Brush InkSoft = Brush("#5B4A3A");
        public static readonly Brush Outline = Brush("#6E4F32");
        public static readonly Brush Accent = Brush("#2E5338");
        public static readonly Brush Honey = Brush("#E5B24E");
        public static readonly Brush ItemHover = Brush("#2EFDB850");
        public static readonly Brush ItemSelected = Brush("#45386644");

        private static Brush? _slotNormal;
        private static Brush? _slotHover;
        private static Brush? _slotSelected;
        private static ResourceDictionary? _resources;

        public static readonly DependencyProperty ItemBackgroundProperty = DependencyProperty.RegisterAttached(
            "ItemBackground", typeof(Brush), typeof(GameTheme), new PropertyMetadata(Brushes.Transparent));
        public static Brush GetItemBackground(DependencyObject item) => (Brush)item.GetValue(ItemBackgroundProperty);

        public static ResourceDictionary Resources => _resources ??= new ResourceDictionary
        {
            Source = new Uri("/Desktop Frames;component/Themes/CozyGame.xaml", UriKind.Relative)
        };
        public static Style ContextMenuStyle => (Style)Resources["GameContextMenu"];

        private static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public static Brush FramePaper(Color color, double opacity)
        {
            // 羊皮紙底板採 100% 實心不透明純色，徹底消滅 WPF 透明視窗縮放／位移時的半透明歷史殘影累積
            var mixed = Color.FromRgb(
                (byte)((color.R + 3 * 253) / 4),
                (byte)((color.G + 3 * 243) / 4),
                (byte)((color.B + 3 * 224) / 4));
            var brush = new SolidColorBrush(mixed);
            brush.Freeze();
            return brush;
        }

        public static Brush LabelBacking(Brush foreground)
        {
            // 文字底板維持乾淨自然，不疊加生硬矩形黑塊
            return Brushes.Transparent;
        }

        /// <summary>2D Q 版遊戲道具收納槽（平時常態）：純淨淡米凹槽、細膩暖金米棕圓角框線與微鉚釘裝飾</summary>
        public static Brush ItemTile() => _slotNormal ??= CreateSlotBrush(
            background: Brush("#F4E8D1"),
            innerTop: Color.FromArgb(42, 165, 125, 82),
            border: Brush("#DAC19A"),
            borderThickness: 1.0,
            accentDot: Brush("#C6AC87"));

        /// <summary>2D Q 版遊戲道具收納槽（懸停焦點）：明亮暖金高亮光澤與溫潤金邊</summary>
        public static Brush ItemHoverBrush => _slotHover ??= CreateSlotBrush(
            background: Brush("#FFF7E7"),
            innerTop: Color.FromArgb(50, 245, 185, 75),
            border: Brush("#DCA842"),
            borderThickness: 1.5,
            accentDot: Brush("#E2B755"));

        /// <summary>2D Q 版遊戲道具收納槽（選中鎖定）：典雅清爽淺草綠金底色與沉穩森林墨綠金邊</summary>
        public static Brush ItemSelectedBrush => _slotSelected ??= CreateSlotBrush(
            background: Brush("#EBF4EA"),
            innerTop: Color.FromArgb(60, 60, 120, 80),
            border: Brush("#386644"),
            borderThickness: 1.8,
            accentDot: Brush("#DFC17A"));

        private static DrawingBrush CreateSlotBrush(Brush background, Color innerTop, Brush border, double borderThickness, Brush accentDot)
        {
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                var rect = new Rect(0.5, 0.5, 79, 95);
                // 1. 底色與圓角外邊框（8px 圓角）
                var pen = new Pen(border, borderThickness);
                pen.Freeze();
                dc.DrawRoundedRectangle(background, pen, rect, 8, 8);

                // 2. 頂部微漸層（2D Q 版道具欄內凹微陰影感，不搶圖示風采）
                var innerShadowRect = new Rect(1.5, 1.5, 77, 14);
                var gradBrush = new LinearGradientBrush(
                    innerTop,
                    Color.FromArgb(0, 0, 0, 0),
                    new Point(0.5, 0),
                    new Point(0.5, 1));
                gradBrush.Freeze();
                dc.DrawRoundedRectangle(gradBrush, null, innerShadowRect, 6, 6);

                // 3. 四角微金屬鉚釘裝飾（Q 版冒險道具背包特色）
                double dotOffset = 5.0;
                double dotRadius = 1.2;
                Point[] dots =
                {
                    new Point(rect.Left + dotOffset, rect.Top + dotOffset),
                    new Point(rect.Right - dotOffset, rect.Top + dotOffset),
                    new Point(rect.Left + dotOffset, rect.Bottom - dotOffset),
                    new Point(rect.Right - dotOffset, rect.Bottom - dotOffset)
                };
                foreach (var pt in dots)
                {
                    dc.DrawEllipse(accentDot, null, pt, dotRadius, dotRadius);
                }
            }
            drawing.Freeze();
            var brush = new DrawingBrush(drawing)
            {
                Stretch = Stretch.Fill
            };
            brush.Freeze();
            return brush;
        }

        public static Image StorageBadge() => new Image
        {
            Source = GameSkin.Chest, Width = 40, Height = 34,
            Margin = new Thickness(0, 0, 8, 0), IsHitTestVisible = false
        };

        public static void StyleDialog(Border border)
        {
            border.Background = GameSkin.PaperTexture;
            border.BorderBrush = Outline;
            border.BorderThickness = new Thickness(1);
            border.Padding = new Thickness(14);
            border.CornerRadius = new CornerRadius(16);
        }
    }
}
