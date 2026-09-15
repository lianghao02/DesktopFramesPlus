using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Desktop_Frames
{
    /// <summary>
    /// Lightweight click-through overlay window for rendering magnetic snap guidelines during frame dragging.
    /// Transparent to user input and completely hidden when not snapping.
    /// </summary>
    public class SnapGuideOverlay : Window
    {
        private readonly Canvas _canvas;
        private readonly Line _verticalLine;
        private readonly Line _horizontalLine;

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static SnapGuideOverlay _instance;
        public static SnapGuideOverlay Instance => _instance ??= new SnapGuideOverlay();

        public SnapGuideOverlay()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Focusable = false;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _canvas = new Canvas();
            Content = _canvas;

            var guideBrush = new SolidColorBrush(Color.FromArgb(200, 0, 191, 255)); // DeepSkyBlue with 78% opacity
            guideBrush.Freeze();

            _verticalLine = new Line
            {
                Stroke = guideBrush,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Visibility = Visibility.Collapsed
            };

            _horizontalLine = new Line
            {
                Stroke = guideBrush,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Visibility = Visibility.Collapsed
            };

            _canvas.Children.Add(_verticalLine);
            _canvas.Children.Add(_horizontalLine);

            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            // Set extended window style to WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW so mouse clicks pass through unconditionally
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        public void UpdateGuides(double? guideX, double? guideY, Rect screenBounds)
        {
            bool hasAny = false;

            if (guideX.HasValue)
            {
                _verticalLine.X1 = guideX.Value - Left;
                _verticalLine.X2 = guideX.Value - Left;
                _verticalLine.Y1 = screenBounds.Top - Top;
                _verticalLine.Y2 = screenBounds.Bottom - Top;
                _verticalLine.Visibility = Visibility.Visible;
                hasAny = true;
            }
            else
            {
                _verticalLine.Visibility = Visibility.Collapsed;
            }

            if (guideY.HasValue)
            {
                _horizontalLine.X1 = screenBounds.Left - Left;
                _horizontalLine.X2 = screenBounds.Right - Left;
                _horizontalLine.Y1 = guideY.Value - Top;
                _horizontalLine.Y2 = guideY.Value - Top;
                _horizontalLine.Visibility = Visibility.Visible;
                hasAny = true;
            }
            else
            {
                _horizontalLine.Visibility = Visibility.Collapsed;
            }

            if (hasAny)
            {
                if (!IsVisible) Show();
            }
            else
            {
                HideGuides();
            }
        }

        public void HideGuides()
        {
            _verticalLine.Visibility = Visibility.Collapsed;
            _horizontalLine.Visibility = Visibility.Collapsed;
            if (IsVisible) Hide();
        }
    }
}
