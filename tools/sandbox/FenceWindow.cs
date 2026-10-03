using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FarmFenceSandbox
{
    public class FenceWindow : Window
    {
        #region Win32 Constants for Hit-Testing & Window Styles

        private const int WM_NCHITTEST = 0x0084;
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
        private readonly TextBlock _titleBlock;
        private readonly TextBlock _countBlock;
        private readonly Button _closeBtn;
        private readonly Border _headerBorder;

        private Point _lastWindowPos;
        private bool _isLoaded = false;
        private const int ResizeBorderThickness = 6;
        private const int HeaderHeight = 32;

        public event Action<FenceWindow>? FenceClosed;
        public event Action<FenceWindow, double, double>? FenceMoved;

        public FenceWindow(string fenceId, string fenceTitle, double left, double top, double width, double height, Color accentColor)
        {
            FenceId = fenceId;
            FenceTitle = fenceTitle;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(40, 20, 25, 35));
            ShowInTaskbar = false;
            Topmost = false; // 不蓋在一般應用程式之上

            Left = left;
            Top = top;
            Width = width;
            Height = height;

            _lastWindowPos = new Point(left, top);

            // 主邊框與容器
            Border outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(35, 10, 20, 30))
            };

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 標題列
            _headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 25, 30, 40)),
                CornerRadius = new CornerRadius(3, 3, 0, 0),
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
                VerticalAlignment = VerticalAlignment.Center
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

            // 柵欄中央工作區（視覺上半透明指示）
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
            _lastWindowPos = new Point(this.Left, this.Top);

            var helper = new WindowInteropHelper(this);
            // 啟用 WS_EX_NOACTIVATE，點擊柵欄不搶走前台焦點
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

            // 掛鉤 WndProc 實現點擊穿透與邊框拖曳
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            if (!_isLoaded) return;

            double deltaX = this.Left - _lastWindowPos.X;
            double deltaY = this.Top - _lastWindowPos.Y;

            if (Math.Abs(deltaX) > 0.01 || Math.Abs(deltaY) > 0.01)
            {
                _lastWindowPos = new Point(this.Left, this.Top);
                FenceMoved?.Invoke(this, deltaX, deltaY);
            }
        }

        public void UpdateItemCount(int count)
        {
            Dispatcher.Invoke(() =>
            {
                _countBlock.Text = $" ({count} 項)";
            });
        }

        public Rect GetFenceBounds()
        {
            return new Rect(this.Left, this.Top, this.ActualWidth, this.ActualHeight);
        }

        /// <summary>
        /// 核心命中測試 (WM_NCHITTEST)：
        /// 1. 四邊與四角 ➜ 返回 Resize 代碼 (可自由縮放)
        /// 2. 標題列關閉按鈕 ➜ HTCLIENT (可點擊關閉)
        /// 3. 標題列其餘區域 ➜ HTCAPTION (滑鼠按住可拖動整個柵欄)
        /// 4. 柵欄內部圖示區 ➜ HTTRANSPARENT (滑鼠穿透到底層桌面 SysListView32，原生操作毫不受阻！)
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
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

                // 3. 標題列區域判定 ➜ 可拖動視窗
                if (mousePt.Y <= HeaderHeight)
                {
                    handled = true;
                    return (IntPtr)HTCAPTION;
                }

                // 4. 內部工作區 ➜ 完全穿透給桌面原生圖示
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }

            return IntPtr.Zero;
        }
    }
}
