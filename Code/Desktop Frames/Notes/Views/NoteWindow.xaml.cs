using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Desktop_Frames.Notes.Models;
using Desktop_Frames.Notes.Services;

namespace Desktop_Frames.Notes.Views
{
    /// <summary>
    /// Interaction logic for NoteWindow.xaml
    /// </summary>
    public partial class NoteWindow : Window
    {
        public NoteItem Item { get; }
        private bool _isInitializing = true;
        private bool _isDeleting;

        public event Action<NoteItem>? DataChanged;
        public event Action<NoteItem>? RequestDelete;

        public NoteWindow(NoteItem item)
        {
            InitializeComponent();
            Item = item ?? throw new ArgumentNullException(nameof(item));

            // Restore geometry
            Left = Item.X;
            Top = Item.Y;
            Width = Math.Max(180, Item.Width);
            Height = Math.Max(140, Item.Height);

            // Restore content
            ContentTextBox.Text = Item.Content ?? "";

            // Restore states
            ApplyAlwaysOnTop(Item.AlwaysOnTop);
            ApplyLock(Item.IsLocked);
            ApplyColor(Item.Color);
            ApplyFontSize(Item.FontSize > 0 ? Item.FontSize : 14.0);
            ApplyFontFamily(Item.FontFamily);

            LocationChanged += OnWindowLocationChanged;
            SizeChanged += OnWindowSizeChanged;

            _isInitializing = false;
        }

        public void ApplyColor(string colorKey)
        {
            Item.Color = colorKey;
            var palette = NoteColors.GetPalette(colorKey);

            MainBorder.BorderBrush = palette.BorderBrush;
            HeaderBorder.Background = palette.HeaderBrush;
            ContentTextBox.Background = palette.BodyBrush;
            Background = palette.BodyBrush;
            ContentTextBox.Foreground = palette.TextBrush;
        }

        public void ApplyAlwaysOnTop(bool alwaysOnTop)
        {
            Item.AlwaysOnTop = alwaysOnTop;
            Topmost = alwaysOnTop;
            PinButton.Opacity = alwaysOnTop ? 1.0 : 0.45;
            PinButton.ToolTip = alwaysOnTop ? "取消置頂" : "永遠置頂";
        }

        public void ApplyLock(bool isLocked)
        {
            Item.IsLocked = isLocked;
            ResizeMode = isLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        }

        public void ApplyFontSize(double size)
        {
            if (size <= 0) size = 14.0;
            Item.FontSize = size;
            ContentTextBox.FontSize = size;
            NotifyDataChanged();
        }

        public void ApplyFontFamily(string familyName)
        {
            // 空字串代表使用 Windows 系統預設字型；null 則是舊資料的安全 fallback。
            familyName ??= "Microsoft JhengHei";
            Item.FontFamily = familyName;
            try
            {
                ContentTextBox.FontFamily = string.IsNullOrEmpty(familyName)
                    ? SystemFonts.MessageFontFamily
                    : new System.Windows.Media.FontFamily($"{familyName}, Segoe UI, Microsoft JhengHei, PingFang TC");
            }
            catch
            {
                Item.FontFamily = "Microsoft JhengHei";
                ContentTextBox.FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei, Segoe UI, PingFang TC");
            }
            NotifyDataChanged();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && !Item.IsLocked)
            {
                DragMove();
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyAlwaysOnTop(!Item.AlwaysOnTop);
            NotifyDataChanged();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideNote();
        }

        public void HideNote()
        {
            Item.IsVisible = false;
            Hide();
            NotifyDataChanged();
        }

        public void ShowNote()
        {
            Item.IsVisible = true;
            Show();
            Activate();
            NotifyDataChanged();
        }

        public void CloseForDeletion()
        {
            _isDeleting = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!NoteManager.IsApplicationExiting && !_isDeleting)
            {
                // Never destroy window on simple close button, just hide it
                e.Cancel = true;
                HideNote();
            }
            base.OnClosing(e);
        }

        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;

            Item.Content = ContentTextBox.Text;
            NotifyDataChanged();
        }

        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            if (_isInitializing || WindowState != WindowState.Normal) return;

            Item.X = Left;
            Item.Y = Top;
            NotifyDataChanged();
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isInitializing || WindowState != WindowState.Normal) return;

            Item.Width = ActualWidth;
            Item.Height = ActualHeight;
            NotifyDataChanged();
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            // Lock MenuItem
            var lockItem = new MenuItem
            {
                Header = "鎖定位置與尺寸",
                IsCheckable = true,
                IsChecked = Item.IsLocked
            };
            lockItem.Click += (s, ev) =>
            {
                ApplyLock(!Item.IsLocked);
                NotifyDataChanged();
            };
            menu.Items.Add(lockItem);

            // Color MenuItem
            var colorMenu = new MenuItem { Header = "變更顏色" };
            foreach (var palette in NoteColors.AllPalettes)
            {
                var colorItem = new MenuItem
                {
                    Header = palette.DisplayName,
                    IsCheckable = true,
                    IsChecked = string.Equals(Item.Color, palette.Key, StringComparison.OrdinalIgnoreCase)
                };
                string key = palette.Key;
                colorItem.Click += (s, ev) =>
                {
                    ApplyColor(key);
                    NotifyDataChanged();
                };
                colorMenu.Items.Add(colorItem);
            }
            menu.Items.Add(colorMenu);

            // Font Size MenuItem
            var fontSizeMenu = new MenuItem { Header = "字體大小" };
            double[] sizes = { 12, 14, 16, 18 };
            string[] sizeLabels = { "小（12）", "標準（14）", "大（16）", "特大（18）" };
            for (int i = 0; i < sizes.Length; i++)
            {
                double sz = sizes[i];
                var szItem = new MenuItem
                {
                    Header = sizeLabels[i],
                    IsCheckable = true,
                    IsChecked = Math.Abs(Item.FontSize - sz) < 0.5
                };
                szItem.Click += (s, ev) => ApplyFontSize(sz);
                fontSizeMenu.Items.Add(szItem);
            }
            menu.Items.Add(fontSizeMenu);

            // Font Family MenuItem
            var fontMenu = new MenuItem { Header = "字型" };
            var fonts = new (string Name, string Value)[]
            {
                ("微軟正黑體", "Microsoft JhengHei"),
                ("標楷體", "DFKai-SB"),
                ("Segoe UI", "Segoe UI"),
                ("系統預設", "")
            };
            foreach (var f in fonts)
            {
                var fItem = new MenuItem
                {
                    Header = f.Name,
                    IsCheckable = true,
                    IsChecked = string.Equals(Item.FontFamily, f.Value, StringComparison.OrdinalIgnoreCase) ||
                                (string.IsNullOrEmpty(Item.FontFamily) && string.IsNullOrEmpty(f.Value))
                };
                string familyVal = f.Value;
                fItem.Click += (s, ev) => ApplyFontFamily(familyVal);
                fontMenu.Items.Add(fItem);
            }
            menu.Items.Add(fontMenu);

            menu.Items.Add(new Separator());

            // Options MenuItem (Open Settings dialog directly, tab 1 = Style & FX)
            var optionsItem = new MenuItem { Header = "便箋設定與選項..." };
            optionsItem.Click += (s, ev) =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OptionsFormManager.ShowOptionsForm(1);
                }));
            };
            menu.Items.Add(optionsItem);

            menu.Items.Add(new Separator());

            // Delete MenuItem
            var deleteItem = new MenuItem { Header = "刪除便箋" };
            deleteItem.Click += (s, ev) =>
            {
                RequestDelete?.Invoke(Item);
            };
            menu.Items.Add(deleteItem);

            menu.PlacementTarget = MoreButton;
            menu.IsOpen = true;
        }

        private void NotifyDataChanged()
        {
            if (!_isInitializing)
            {
                DataChanged?.Invoke(Item);
            }
        }
    }
}
