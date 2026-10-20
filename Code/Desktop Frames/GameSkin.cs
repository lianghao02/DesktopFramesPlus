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
            var brush = new ImageBrush(Art(new Int32Rect(512, 464, 512, 560)))
            {
                Stretch = Stretch.Fill, TileMode = TileMode.None
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
            double desired = kind switch { GameSkinKind.Panel => 22, GameSkinKind.Header => 12, GameSkinKind.Button => 10, _ => 9 };
            double vertical = kind is GameSkinKind.Button or GameSkinKind.Header ? 5 : desired;
            double x = Math.Min(desired, size.Width / 2), y = Math.Min(vertical, size.Height / 2);
            double[] xs = { 0, x, size.Width - x, size.Width }, ys = { 0, y, size.Height - y, size.Height };
            var pieces = GetSlices(kind);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    context.DrawImage(pieces[row * 3 + col], new Rect(xs[col], ys[row], Math.Max(0, xs[col + 1] - xs[col]), Math.Max(0, ys[row + 1] - ys[row])));
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            Draw(context, Skin, RenderSize);
            if (Skin == GameSkinKind.Paper)
                context.DrawRoundedRectangle(null, new Pen(GameTheme.Outline, 1), new Rect(RenderSize), 5, 5);
        }
    }
}
