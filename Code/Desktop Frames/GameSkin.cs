using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    public enum GameSkinKind { Panel, Header, Button, Slot, Paper }

    /// <summary>以九宮格繪製靜態遊戲素材，調整尺寸時保留角飾與邊框比例。</summary>
    public class GameSkin : Border
    {
        public static readonly DependencyProperty SkinProperty = DependencyProperty.Register(nameof(Skin),
            typeof(GameSkinKind), typeof(GameSkin), new FrameworkPropertyMetadata(GameSkinKind.Panel, FrameworkPropertyMetadataOptions.AffectsRender));
        public GameSkinKind Skin { get => (GameSkinKind)GetValue(SkinProperty); set => SetValue(SkinProperty, value); }

        private static readonly BitmapSource Atlas = LoadAtlas();
        private static readonly Dictionary<GameSkinKind, BitmapSource[]> Slices = new();
        private static BitmapSource LoadAtlas()
        {
            var image = new BitmapImage(new Uri("pack://application:,,,/Desktop Frames;component/Resources/GameArt/cozy-inventory-atlas.png"));
            image.Freeze();
            return image;
        }

        public static BitmapSource Art(Int32Rect rect)
        {
            var image = new CroppedBitmap(Atlas, rect);
            image.Freeze();
            return image;
        }

        public static ImageSource Chest { get; } = Art(new Int32Rect(1024, 464, 512, 560));
        public static Brush PaperTexture { get; } = CreatePaper();
        private static Brush CreatePaper()
        {
            // 羊皮紙紋理依原比例以鏡射方式平鋪（0.75 倍），避免寬或高的卡片把紋理拉成扁長斑塊。
            var brush = new ImageBrush(Art(new Int32Rect(512, 464, 512, 560)))
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.FlipXY, // 鏡射平鋪：接縫處紋理自然連續
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 384, 420)
            };
            brush.Freeze();
            return brush;
        }

        private static BitmapSource[] GetSlices(GameSkinKind kind)
        {
            if (Slices.TryGetValue(kind, out var cached)) return cached;
            Int32Rect rect = kind switch
            {
                GameSkinKind.Header => new Int32Rect(512, 84, 512, 304),
                GameSkinKind.Button => new Int32Rect(1024, 0, 512, 464),
                GameSkinKind.Slot => new Int32Rect(0, 464, 512, 560),
                _ => new Int32Rect(0, 0, 512, 464)
            };
            int inset = kind == GameSkinKind.Header ? 96 : 72;
            int[] xs = { rect.X, rect.X + inset, rect.X + rect.Width - inset, rect.X + rect.Width };
            int[] ys = { rect.Y, rect.Y + inset, rect.Y + rect.Height - inset, rect.Y + rect.Height };
            var slices = new BitmapSource[9];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    slices[row * 3 + col] = Art(new Int32Rect(xs[col], ys[row], xs[col + 1] - xs[col], ys[row + 1] - ys[row]));
            return Slices[kind] = slices;
        }

        private static readonly Dictionary<string, BitmapSource> PanelParts = new();

        private static BitmapSource PanelPart(string key, int x, int y, int width, int height)
        {
            if (!PanelParts.TryGetValue(key, out var part)) PanelParts[key] = part = Art(new Int32Rect(x, y, width, height));
            return part;
        }

        /// <summary>
        /// 木框專用繪製。原圖的黃銅角飾約 100 px，舊的九宮格只切 72 px，角飾被切半、
        /// 邊上的三葉草飾板又被橫向拉到數倍寬。這裡改為：
        /// 角飾整塊等比縮小；上下邊把飾板固定在中央（等比），只拉伸左右兩段單純木紋；左右邊以針腳為單位平鋪。
        /// </summary>
        private static void DrawPanel(DrawingContext context, Size size)
        {
            const int corner = 100;           // 原圖角飾邊長
            double c = Math.Min(Math.Round(corner * 0.34), Math.Min(size.Width, size.Height) / 2);
            double scale = c / corner;
            double w = size.Width, h = size.Height;

            // 中央羊皮紙
            context.DrawImage(PanelPart("center", 100, 100, 312, 264), new Rect(c, c, Math.Max(0, w - 2 * c), Math.Max(0, h - 2 * c)));

            // 上下邊
            double length = w - 2 * c;
            if (length > 0)
            {
                double ornament = 132 * scale;
                foreach (bool top in new[] { true, false })
                {
                    int sy = top ? 0 : 464 - corner;
                    double dy = top ? 0 : h - c;
                    string k = top ? "t" : "b";
                    if (length > ornament + 24)
                    {
                        double side = (length - ornament) / 2;
                        context.DrawImage(PanelPart(k + "L", 100, sy, 90, corner), new Rect(c, dy, side + 0.5, c));
                        context.DrawImage(PanelPart(k + "O", 190, sy, 132, corner), new Rect(c + side, dy, ornament, c));
                        context.DrawImage(PanelPart(k + "R", 322, sy, 90, corner), new Rect(c + side + ornament - 0.5, dy, side + 0.5, c));
                    }
                    else
                    {
                        context.DrawImage(PanelPart(k + "A", 100, sy, 312, corner), new Rect(c, dy, length, c));
                    }
                }
            }

            // 左右邊：以一個針腳週期為單位平鋪
            double span = h - 2 * c;
            if (span > 0)
            {
                double tileHeight = 52 * scale;
                foreach (bool left in new[] { true, false })
                {
                    double dx = left ? 0 : w - c;
                    var tile = PanelPart(left ? "l" : "r", left ? 0 : 512 - corner, 146, corner, 52);
                    context.PushClip(new RectangleGeometry(new Rect(dx, c, c, span)));
                    for (double y = c; y < h - c; y += tileHeight)
                        context.DrawImage(tile, new Rect(dx, y, c, tileHeight + 0.5));
                    context.Pop();
                }
            }

            // 四個角飾
            context.DrawImage(PanelPart("tl", 0, 0, corner, corner), new Rect(0, 0, c, c));
            context.DrawImage(PanelPart("tr", 512 - corner, 0, corner, corner), new Rect(w - c, 0, c, c));
            context.DrawImage(PanelPart("bl", 0, 464 - corner, corner, corner), new Rect(0, h - c, c, c));
            context.DrawImage(PanelPart("br", 512 - corner, 464 - corner, corner, corner), new Rect(w - c, h - c, c, c));
        }

        public static DrawingBrush TileBrush()
        {
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) Draw(context, GameSkinKind.Slot, new Size(80, 96));
            drawing.Freeze();
            var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }

        public static void Draw(DrawingContext context, GameSkinKind kind, Size size)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            if (kind == GameSkinKind.Paper)
            {
                context.DrawRectangle(PaperTexture, null, new Rect(size));
                return;
            }
            if (kind == GameSkinKind.Panel)
            {
                DrawPanel(context, size);
                return;
            }
            double desired = kind switch { GameSkinKind.Panel => 22, GameSkinKind.Header => 12, GameSkinKind.Button => 10, _ => 9 };
            double vertical = kind is GameSkinKind.Button or GameSkinKind.Header ? 5 : desired;
            double x = Math.Min(desired, size.Width / 2), y = Math.Min(vertical, size.Height / 2);
            double[] xs = { 0, x, size.Width - x, size.Width }, ys = { 0, y, size.Height - y, size.Height };
            var pieces = GetSlices(kind);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    context.DrawImage(pieces[row * 3 + col], new Rect(xs[col], ys[row], Math.Max(0, xs[col + 1] - xs[col]), Math.Max(0, ys[row + 1] - ys[row])));
        }

        // 面板自訂外框顏色：木框是點陣圖，原生 BorderBrush 看不到，改在木框內緣畫一圈描邊
        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush),
            typeof(Brush), typeof(GameSkin), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        public static readonly DependencyProperty AccentThicknessProperty = DependencyProperty.Register(nameof(AccentThickness),
            typeof(double), typeof(GameSkin), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double AccentThickness { get => (double)GetValue(AccentThicknessProperty); set => SetValue(AccentThicknessProperty, value); }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            Draw(context, Skin, RenderSize);
            if (Skin == GameSkinKind.Paper)
                context.DrawRoundedRectangle(null, new Pen(GameTheme.Outline, 1), new Rect(RenderSize), 5, 5);
            if (Skin == GameSkinKind.Panel && AccentBrush != null && AccentThickness > 0)
                DrawAccent(context, RenderSize, AccentBrush, AccentThickness);
        }

        // 描邊位置對齊 DrawPanel 的角飾尺寸：木紋內緣約在角飾邊長的 0.6 處
        private static void DrawAccent(DrawingContext context, Size size, Brush brush, double thickness)
        {
            double c = Math.Min(Math.Round(100 * 0.34), Math.Min(size.Width, size.Height) / 2);
            double inset = c * 0.6 - thickness / 2;
            var rect = new Rect(inset, inset, size.Width - 2 * inset, size.Height - 2 * inset);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            context.DrawRoundedRectangle(null, new Pen(brush, thickness), rect, c * 0.25, c * 0.25);
        }
    }
}
