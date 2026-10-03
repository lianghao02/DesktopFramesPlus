using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FarmFenceSandbox
{
    public class FenceWindow : Window
    {
        #region Win32 Constants for Hit-Testing & Window Styles

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONUP = 0x00A2;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;

        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        #endregion

        public string FenceId { get; }
        public string FenceTitle { get; private set; }
        public bool IsUserMoving { get; private set; } = false;

        private readonly TextBlock _titleBlock;
        private readonly TextBlock _countBlock;
        private readonly Button _closeBtn;
        private readonly Border _headerBorder;

        private Point _lastPhysicalPos;
        private bool _isLoaded = false;
        private const int ResizeBorderThickness = 6;
        private const int HeaderHeight = 32;

        public event Action<FenceWindow>? FenceClosed;
        public event Action<FenceWindow, double, double>? FenceMoved;
        public event Action<FenceWindow>? MoveCompleted;

        public FenceWindow(string fenceId, string fenceTitle, double left, double top, double width, double height, Color accentColor)
        {
            FenceId = fenceId;
            FenceTitle = fenceTitle;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(40, 20, 25, 35));
            ShowInTaskbar = false;
            Topmost = false;

            Left = left;
            Top = top;
            Width = width;
            Height = height;

            // 主邊框與容器
            Border outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(30, 15, 20, 30))
            };

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 標題列
            _headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 25, 32, 45)),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Padding = new Thickness(10, 0, 8, 0)
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _titleBlock = new TextBlock
            {
                Text = FenceTitle,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_titleBlock, 0);

            _countBlock = new TextBlock
            {
                Text = " (0 項)",
                Foreground = Brushes.LightGray,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(_countBlock, 1);

            _closeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = Brushes.WhiteSmoke,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Width = 24,
                Height = 24,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "取消並解散此柵欄 (圖示安全保留在桌面原地)"
            };
            _closeBtn.Click += (s, e) =>
            {
                FenceClosed?.Invoke(this);
                this.Close();
            };
            Grid.SetColumn(_closeBtn, 2);

            headerGrid.Children.Add(_titleBlock);
            headerGrid.Children.Add(_countBlock);
            headerGrid.Children.Add(_closeBtn);
            _headerBorder.Child = headerGrid;

            Grid.SetRow(_headerBorder, 0);
            rootGrid.Children.Add(_headerBorder);

            // 柵欄內部工作區 (視覺指示，滑鼠完全穿透)
            Border canvasArea = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent
            };
            Grid.SetRow(canvasArea, 1);
            rootGrid.Children.Add(canvasArea);

            outerBorder.Child = rootGrid;
            Content = outerBorder;

            Loaded += OnLoaded;
            LocationChanged += OnLocationChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            try
            {
                _lastPhysicalPos = PointToScreen(new Point(0, 0));
            }
            catch
            {
                _lastPhysicalPos = new Point(this.Left, this.Top);
            }

            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                Point currentPhysical = PointToScreen(new Point(0, 0));
                double deltaX = currentPhysical.X - _lastPhysicalPos.X;
                double deltaY = currentPhysical.Y - _lastPhysicalPos.Y;

                if (Math.Abs(deltaX) > 0.01 || Math.Abs(deltaY) > 0.01)
                {
                    _lastPhysicalPos = currentPhysical;
                    FenceMoved?.Invoke(this, deltaX, deltaY);
                }
            }
            catch { }
        }

        public void UpdateItemCount(int count)
        {
            Dispatcher.Invoke(() =>
            {
                _countBlock.Text = $" ({count} 項)";
            });
        }

        /// <summary>
        /// 取得外框工作區在螢幕上的真實物理像素邊界 (Physical Pixels Bounds，扣除標題列高度，支援 DPI 縮放)
        /// </summary>
        public Rect GetPhysicalContentBounds()
        {
            if (!_isLoaded) return new Rect(this.Left, this.Top, this.ActualWidth, this.ActualHeight);

            try
            {
                Point topLeft = PointToScreen(new Point(0, 0));
                Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
                Point headerBottom = PointToScreen(new Point(0, HeaderHeight));

                double x = topLeft.X;
                double y = headerBottom.Y;
                double w = Math.Max(10, bottomRight.X - topLeft.X);
                double h = Math.Max(10, bottomRight.Y - headerBottom.Y);

                return new Rect(x, y, w, h);
            }
            catch
            {
                return new Rect(this.Left, this.Top + HeaderHeight, this.ActualWidth, Math.Max(10, this.ActualHeight - HeaderHeight));
            }
        }

        /// <summary>
        /// 核心命中測試 (WM_NCHITTEST) 與拖動狀態追蹤
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ENTERSIZEMOVE || (msg == WM_NCLBUTTONDOWN && (int)wParam == HTCAPTION))
            {
                IsUserMoving = true;
            }
            else if (msg == WM_EXITSIZEMOVE || msg == WM_NCLBUTTONUP)
            {
                if (IsUserMoving)
                {
                    IsUserMoving = false;
                    MoveCompleted?.Invoke(this);
                }
            }

            if (msg == WM_NCHITTEST)
            {
                short screenX = unchecked((short)(long)lParam);
                short screenY = unchecked((short)((long)lParam >> 16));
                Point mousePt = PointFromScreen(new Point(screenX, screenY));

                double w = ActualWidth;
                double h = ActualHeight;

                // 1. 關閉按鈕優先保留點擊
                Point closeBtnPt = _closeBtn.PointFromScreen(new Point(screenX, screenY));
                if (closeBtnPt.X >= 0 && closeBtnPt.X <= _closeBtn.ActualWidth &&
                    closeBtnPt.Y >= 0 && closeBtnPt.Y <= _closeBtn.ActualHeight)
                {
                    handled = true;
                    return (IntPtr)HTCLIENT;
                }

                // 2. 四周縮放邊緣判定
                bool isLeft = mousePt.X <= ResizeBorderThickness;
                bool isRight = mousePt.X >= w - ResizeBorderThickness;
                bool isTop = mousePt.Y <= ResizeBorderThickness;
                bool isBottom = mousePt.Y >= h - ResizeBorderThickness;

                if (isTop && isLeft) { handled = true; return (IntPtr)HTTOPLEFT; }
                if (isTop && isRight) { handled = true; return (IntPtr)HTTOPRIGHT; }
                if (isBottom && isLeft) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                if (isBottom && isRight) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
                if (isLeft) { handled = true; return (IntPtr)HTLEFT; }
                if (isRight) { handled = true; return (IntPtr)HTRIGHT; }
                if (isTop) { handled = true; return (IntPtr)HTTOP; }
                if (isBottom) { handled = true; return (IntPtr)HTBOTTOM; }

                // 3. 標題列區域 ➜ 可拖曳外框
                if (mousePt.Y <= HeaderHeight)
                {
                    handled = true;
                    return (IntPtr)HTCAPTION;
                }

                // 4. 柵欄內部工作區 ➜ 完全穿透給桌面原生圖示
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }

            return IntPtr.Zero;
        }
    }
}
