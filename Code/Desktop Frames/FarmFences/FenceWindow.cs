using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Desktop_Frames.FarmFences
{
    public sealed class FenceWindow : Window
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out DesktopInterop.RECT rectangle);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        private readonly TextBlock _title;
        private readonly Button _close;
        private IntPtr _handle;
        private bool _translation;
        private Rect _previousBounds;
        public string FenceId { get; }
        public string FenceTitle { get; private set; }
        public bool IsUserMoving { get; private set; }
        public event Action<FenceWindow>? FenceClosed, MoveCompleted, MoveStarted, TitleChanged;
        public event Action<FenceWindow, double, double>? FenceMoved;

        public FenceWindow(string id, string title, double left, double top, double width, double height, Color accent)
        {
            FenceId = id;
            FenceTitle = title;
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 160;
            MinHeight = 120;
            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
            Left = left;
            Top = top;
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new Grid { Background = new SolidColorBrush(Color.FromArgb(230, 25, 32, 45)) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _title = new TextBlock
            {
                Text = title, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = FenceText.Get("FarmRename")
            };
            _close = new Button
            {
                Content = "×", Width = 28, Height = 24, Margin = new Thickness(2),
                ToolTip = FenceText.Get("FarmCancel"), Cursor = Cursors.Hand
            };
            _close.Click += (_, _) => { FenceClosed?.Invoke(this); Close(); };
            Grid.SetColumn(_close, 1);
            header.Children.Add(_title);
            header.Children.Add(_close);
            grid.Children.Add(header);
            Content = new Border
            {
                BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(3),
                Background = Brushes.Transparent, Child = grid
            };
            SourceInitialized += (_, _) =>
            {
                _handle = new WindowInteropHelper(this).Handle;
                SetWindowLong(_handle, -20, GetWindowLong(_handle, -20) | 0x08000000);
                HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
            };
            Loaded += (_, _) =>
            {
                SetPhysicalBounds(new Rect(left, top, Math.Max(160, width), Math.Max(120, height)));
                _previousBounds = GetPhysicalBounds();
            };
            LocationChanged += (_, _) =>
            {
                if (_handle == IntPtr.Zero || !IsUserMoving || !_translation) return;
                var current = GetPhysicalBounds();
                FenceMoved?.Invoke(this, current.X - _previousBounds.X, current.Y - _previousBounds.Y);
                _previousBounds = current;
            };
        }

        public Rect GetPhysicalBounds()
        {
            if (_handle == IntPtr.Zero || !GetWindowRect(_handle, out var r)) return new Rect(Left, Top, Width, Height);
            return new Rect(r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        }

        public void SetPhysicalBounds(Rect r)
        {
            if (_handle != IntPtr.Zero)
                SetWindowPos(_handle, IntPtr.Zero, (int)Math.Round(r.X), (int)Math.Round(r.Y),
                    (int)Math.Round(r.Width), (int)Math.Round(r.Height), 0x14);
        }

        public Rect GetPhysicalContentBounds()
        {
            Point start = PointToScreen(new Point(3, 35));
            Point end = PointToScreen(new Point(Math.Max(3, ActualWidth - 3), Math.Max(35, ActualHeight - 3)));
            return new Rect(start, end);
        }

        public void Rename()
        {
            var input = new TextBox { Text = FenceTitle, Margin = new Thickness(12), MinWidth = 220 };
            var button = new Button { Content = FenceText.Get("FarmSave"), IsDefault = true, Margin = new Thickness(12), Padding = new Thickness(12, 4, 12, 4) };
            var panel = new StackPanel();
            panel.Children.Add(input);
            panel.Children.Add(button);
            var dialog = new Window
            {
                Title = FenceText.Get("FarmNameLabel"), Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            button.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; };
            dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
            if (dialog.ShowDialog() == true)
            {
                FenceTitle = input.Text.Trim();
                Title = _title.Text = FenceTitle;
                TitleChanged?.Invoke(this);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x21) { handled = true; return (IntPtr)3; } // 不搶桌面焦點
            if (message == 0xA3 && wParam == (IntPtr)2)
            {
                handled = true;
                Dispatcher.BeginInvoke(new Action(Rename));
                return IntPtr.Zero;
            }
            if (message == 0xA5 && wParam == (IntPtr)2)
            {
                var menu = new ContextMenu();
                var rename = new MenuItem { Header = FenceText.Get("FarmRename") };
                rename.Click += (_, _) => Rename();
                menu.Items.Add(rename);
                menu.IsOpen = true;
                handled = true;
            }
            if (message == 0xA1) _translation = wParam == (IntPtr)2;
            if (message == 0x231)
            {
                IsUserMoving = true;
                _previousBounds = GetPhysicalBounds();
                MoveStarted?.Invoke(this);
            }
            if (message == 0x232)
            {
                IsUserMoving = false;
                MoveCompleted?.Invoke(this);
            }
            if (message == 0x84)
            {
                short x = unchecked((short)(long)lParam);
                short y = unchecked((short)((long)lParam >> 16));
                Point p = PointFromScreen(new Point(x, y));
                Point closePoint = _close.PointFromScreen(new Point(x, y));
                handled = true;
                if (new Rect(0, 0, _close.ActualWidth, _close.ActualHeight).Contains(closePoint)) return (IntPtr)1;
                bool left = p.X < 6, right = p.X > ActualWidth - 6, top = p.Y < 6, bottom = p.Y > ActualHeight - 6;
                if (top && left) return (IntPtr)13;
                if (top && right) return (IntPtr)14;
                if (bottom && left) return (IntPtr)16;
                if (bottom && right) return (IntPtr)17;
                if (left) return (IntPtr)10;
                if (right) return (IntPtr)11;
                if (top) return (IntPtr)12;
                if (bottom) return (IntPtr)15;
                if (p.Y <= 35) return (IntPtr)2;
                // 真正 alpha=0 的內部已由 layered window 原生命中測試穿透。
                return (IntPtr)(-1);
            }
            return IntPtr.Zero;
        }
    }
}
