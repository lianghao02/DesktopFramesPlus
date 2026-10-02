using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FarmFenceSandbox
{
    public partial class MainWindow : Window
    {
        private IntPtr _desktopListViewHwnd = IntPtr.Zero;
        private List<DesktopIconInfo> _scannedIcons = new List<DesktopIconInfo>();
        private readonly Dictionary<int, (int OrigX, int OrigY)> _originalPositions = new Dictionary<int, (int, int)>();
        private readonly List<DesktopIconInfo> _managedIcons = new List<DesktopIconInfo>();

        private Point _lastWindowScreenPos;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 鉤住 WndProc 以實作特定區域的滑鼠點擊穿透
            var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            hwndSource.AddHook(WndProc);

            _lastWindowScreenPos = PointToScreen(new Point(0, 0));
            InitializeDesktop();
        }

        private void InitializeDesktop()
        {
            _desktopListViewHwnd = DesktopShellService.GetDesktopListViewHandle();
            if (_desktopListViewHwnd == IntPtr.Zero)
            {
                TxtLog.Text = "❌ 找不到桌面 SysListView32 控制代碼。請確認 Explorer 正在執行。";
                TxtStatusBadge.Text = " [錯誤]";
                return;
            }

            bool autoArrange = DesktopShellService.IsAutoArrangeEnabled(_desktopListViewHwnd);
            if (autoArrange)
            {
                TxtLog.Text = "⚠️ 注意：桌面開啟了「自動排列圖示」。請先在桌面按右鍵 > 檢視 > 取消勾選「自動排列圖示」。";
                TxtStatusBadge.Text = " [需關閉自動排列]";
            }
            else
            {
                TxtLog.Text = $"✅ 已連結桌面 SysListView32 (0x{_desktopListViewHwnd.ToInt64():X})，自動排列已關閉。";
                TxtStatusBadge.Text = " [就緒]";
            }
        }

        #region 命中測試與穿透處理 (WM_NCHITTEST)
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                // lParam 包含滑鼠在螢幕的實體座標 (x: low word, y: high word)
                int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                Point ptWindow = PointFromScreen(new Point(screenX, screenY));

                // 檢查是否落在標題列、控制列或邊框上
                Rect titleRect = GetElementRect(TitleBar);
                Rect controlRect = GetElementRect(ControlBar);

                if (titleRect.Contains(ptWindow) || controlRect.Contains(ptWindow))
                {
                    // 讓標題列與控制項正常響應點擊與拖曳
                    return IntPtr.Zero;
                }

                Rect clientRect = GetElementRect(ClientArea);
                if (clientRect.Contains(ptWindow))
                {
                    // 客戶區完全穿透！滑鼠事件直接下發給桌面原生圖示
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }
            }

            return IntPtr.Zero;
        }

        private Rect GetElementRect(FrameworkElement element)
        {
            if (element == null || !element.IsVisible) return Rect.Empty;
            var transform = element.TransformToAncestor(this);
            return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        #endregion

        #region 操作事件
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                _lastWindowScreenPos = PointToScreen(new Point(0, 0));
                
                // 拖曳移動視窗
                DragMove();

                // 拖曳結束後，計算整體偏移並同步所屬圖示座標
                Point newPos = PointToScreen(new Point(0, 0));
                int deltaX = (int)(newPos.X - _lastWindowScreenPos.X);
                int deltaY = (int)(newPos.Y - _lastWindowScreenPos.Y);

                if (_managedIcons.Count > 0 && (deltaX != 0 || deltaY != 0))
                {
                    SyncManagedIconPositions(deltaX, deltaY);
                }

                _lastWindowScreenPos = newPos;
            }
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (_desktopListViewHwnd == IntPtr.Zero) InitializeDesktop();
            if (_desktopListViewHwnd == IntPtr.Zero) return;

            _scannedIcons = DesktopShellService.GetAllIcons(_desktopListViewHwnd);
            TxtLog.Text = $"🔍 掃描完成：共偵測到 {_scannedIcons.Count} 個桌面項目（含文件、資料夾、捷徑）。";

            if (_scannedIcons.Count > 0)
            {
                var sample = _scannedIcons.Take(3).Select(i => $"{i.Name}({i.ItemType})");
                TxtLog.Text += $" 範例: {string.Join(", ", sample)}";
            }
        }

        private void BtnArrange_Click(object sender, RoutedEventArgs e)
        {
            if (_desktopListViewHwnd == IntPtr.Zero) InitializeDesktop();
            if (_desktopListViewHwnd == IntPtr.Zero) return;

            bool autoArrange = DesktopShellService.IsAutoArrangeEnabled(_desktopListViewHwnd);
            if (autoArrange)
            {
                MessageBox.Show(
                    "Windows 桌面目前開啟了「自動排列圖示」，這會強制將圖示鎖定在左側。\n\n" +
                    "請在桌面空白處按右鍵 >「檢視」> 取消勾選「自動排列圖示」後再試！",
                    "需先關閉桌面自動排列",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_scannedIcons.Count == 0)
            {
                _scannedIcons = DesktopShellService.GetAllIcons(_desktopListViewHwnd);
            }

            if (_scannedIcons.Count == 0)
            {
                TxtLog.Text = "❌ 桌面沒有任何圖示可供排列。";
                return;
            }

            // 挑選前 4 個圖示做為圍籬納管的測試項目
            int targetCount = Math.Min(_scannedIcons.Count, 4);
            var candidates = _scannedIcons.Take(targetCount).ToList();

            // 記錄初始座標（若尚未記錄）
            foreach (var icon in candidates)
            {
                if (!_originalPositions.ContainsKey(icon.Index))
                {
                    _originalPositions[icon.Index] = (icon.X, icon.Y);
                }
            }

            _managedIcons.Clear();
            _managedIcons.AddRange(candidates);

            // 計算外框內容客戶區在螢幕的實體像素座標
            Point clientScreenOrigin = ClientArea.PointToScreen(new Point(0, 0));
            int startX = (int)clientScreenOrigin.X + 20;
            int startY = (int)clientScreenOrigin.Y + 15;
            int colWidth = 90;
            int rowHeight = 90;

            int successCount = 0;
            for (int i = 0; i < _managedIcons.Count; i++)
            {
                var icon = _managedIcons[i];
                int col = i % 4;
                int row = i / 4;

                int targetX = startX + col * colWidth;
                int targetY = startY + row * rowHeight;

                bool ok = DesktopShellService.SetIconPosition(_desktopListViewHwnd, icon.Index, targetX, targetY);
                if (ok)
                {
                    icon.X = targetX;
                    icon.Y = targetY;
                    successCount++;
                }
            }

            TxtClientHint.Visibility = Visibility.Collapsed;
            TxtStatusBadge.Text = $" [已納管 {successCount} 個項目]";
            TxtLog.Text = $"🧲 成功將 {successCount} 個原生圖示排列入圍籬！原檔原路徑不變，直接於框內操作！";
        }

        private void SyncManagedIconPositions(int deltaX, int deltaY)
        {
            if (_desktopListViewHwnd == IntPtr.Zero || _managedIcons.Count == 0) return;

            int count = 0;
            foreach (var icon in _managedIcons)
            {
                int newX = icon.X + deltaX;
                int newY = icon.Y + deltaY;

                bool ok = DesktopShellService.SetIconPosition(_desktopListViewHwnd, icon.Index, newX, newY);
                if (ok)
                {
                    icon.X = newX;
                    icon.Y = newY;
                    count++;
                }
            }

            TxtLog.Text = $"📍 圍籬外框移動 (ΔX:{deltaX}, ΔY:{deltaY})，已同步更新 {count} 個原生圖示座標！";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            RestoreOriginalPositions();
        }

        private void RestoreOriginalPositions()
        {
            if (_desktopListViewHwnd == IntPtr.Zero || _originalPositions.Count == 0) return;

            int restored = 0;
            foreach (var kvp in _originalPositions)
            {
                int idx = kvp.Key;
                var (origX, origY) = kvp.Value;
                bool ok = DesktopShellService.SetIconPosition(_desktopListViewHwnd, idx, origX, origY);
                if (ok) restored++;
            }

            _managedIcons.Clear();
            TxtClientHint.Visibility = Visibility.Visible;
            TxtStatusBadge.Text = " [已還原]";
            TxtLog.Text = $"↺ 已還原 {restored} 個原生圖示至初始座標，解除圍籬納管。";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 退出時確保不殘留負座標，原桌面圖示完好可見
            TxtLog.Text = "沙盒外框關閉。原生圖示完好無損留在原處。";
        }
        #endregion
    }
}
