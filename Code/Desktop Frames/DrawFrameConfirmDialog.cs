using Desktop_Frames.Localization;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames
{
    public class DrawFrameConfirmDialog : Window
    {
        public bool Confirmed { get; private set; } = false;
        public string FrameTitle { get; private set; } = string.Empty;
        public string StoragePath { get; private set; } = string.Empty;

        private TextBox _txtTitle;
        private TextBox _txtPath;
        private bool _isCustomPath = false;

        public DrawFrameConfirmDialog(Rect rect, string defaultTitle)
        {
            Title = Strings.Get("DlgCreateStorageFenceTitle");
            Width = 480;
            Height = 444;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            Border mainBorder = new GameSkin
            {
                Background = GameTheme.Paper,
                BorderBrush = GameTheme.Outline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(8),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 2,
                    BlurRadius = 10,
                    Opacity = 0.15
                }
            };

            GameTheme.StyleDialog(mainBorder);
            FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            Foreground = GameTheme.Ink;
            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // --- HEADER ---
            Border header = new GameSkin
            {
                Skin = GameSkinKind.Header,
                Background = GameTheme.Accent,
                Padding = new Thickness(15),
                Height = 56,
                CornerRadius = new CornerRadius(12, 12, 0, 0)
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleBlock = new TextBlock
            {
                Text = Strings.Get("DlgCreateStorageFenceTitle"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(11, 35, 20),
                    BlurRadius = 2.5,
                    ShadowDepth = 1.2,
                    Direction = 270,
                    Opacity = 0.85
                }
            };

            Button closeBtn = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 30,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                FontSize = 16,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeBtn.Click += (s, e) => Close();

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            titleStack.Children.Add(GameTheme.StorageBadge());
            titleStack.Children.Add(titleBlock);
            headerGrid.Children.Add(titleStack);
            headerGrid.Children.Add(closeBtn);
            Grid.SetColumn(closeBtn, 1);
            header.Child = headerGrid;
            header.MouseLeftButtonDown += (s, e) => DragMove();

            // --- BODY ---
            Grid bodyGrid = new Grid { Margin = new Thickness(16) };
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name & Size
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Path
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Safe hint

            // 1. Name & Size
            StackPanel metaPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            TextBlock lblName = new TextBlock
            {
                Text = Strings.LblFrameName,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _txtTitle = new TextBox
            {
                Text = defaultTitle,
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                BorderBrush = GameTheme.Outline
            };
            _txtTitle.SelectAll();

            TextBlock lblSize = new TextBlock
            {
                Text = Strings.Get("LblFrameSize", (int)rect.Width, (int)rect.Height),
                FontSize = 12,
                Foreground = GameTheme.Ink,
                Margin = new Thickness(0, 3, 0, 0)
            };

            metaPanel.Children.Add(lblName);
            metaPanel.Children.Add(_txtTitle);
            metaPanel.Children.Add(lblSize);
            Grid.SetRow(metaPanel, 0);
            bodyGrid.Children.Add(metaPanel);

            // 2. Storage Path
            StackPanel pathPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            TextBlock lblPath = new TextBlock
            {
                Text = "實體收納資料夾：",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            Grid pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string initialStorageFolder = System.IO.Path.Combine(SettingsManager.DefaultPortalStorageRoot, defaultTitle);
            _txtPath = new TextBox
            {
                Text = initialStorageFolder,
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                BorderBrush = GameTheme.Outline
            };
            _txtPath.TextChanged += (s, e) => { _isCustomPath = true; };

            _txtTitle.TextChanged += (s, e) =>
            {
                if (!_isCustomPath)
                {
                    string safeName = string.Join("_", _txtTitle.Text.Trim().Split(Path.GetInvalidFileNameChars()));
                    if (string.IsNullOrEmpty(safeName)) safeName = defaultTitle;
                    _txtPath.Text = System.IO.Path.Combine(SettingsManager.DefaultPortalStorageRoot, safeName);
                }
            };

            Button btnBrowse = new Button
            {
                Content = "瀏覽...",
                Width = 65,
                Height = 30,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = GameTheme.Paper,
                BorderBrush = GameTheme.Outline
            };
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "選擇收納面板的資料夾";
                    dlg.SelectedPath = Directory.Exists(_txtPath.Text) ? _txtPath.Text : SettingsManager.DefaultPortalStorageRoot;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _isCustomPath = true;
                        _txtPath.Text = dlg.SelectedPath;
                    }
                }
            };

            pathGrid.Children.Add(_txtPath);
            pathGrid.Children.Add(btnBrowse);
            Grid.SetColumn(btnBrowse, 1);

            pathPanel.Children.Add(lblPath);
            pathPanel.Children.Add(pathGrid);
            Grid.SetRow(pathPanel, 1);
            bodyGrid.Children.Add(pathPanel);

            // 3. Safe hint
            TextBlock safeHint = new TextBlock
            {
                Text = "📥 拖入檔案會搬進這個收納資料夾（按住 Ctrl 複製、Alt 建立捷徑）\n🧹 拆除面板時，內容會移回個人桌面，不會覆蓋同名檔案",
                FontSize = 12,
                Foreground = GameTheme.Accent,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                LineHeight = 16
            };
            Grid.SetRow(safeHint, 2);
            bodyGrid.Children.Add(safeHint);

            // --- FOOTER ---
            Border footer = new Border
            {
                Padding = new Thickness(16),
                Background = GameTheme.Paper,
                BorderBrush = GameTheme.Outline,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            StackPanel footerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button btnCancel = new Button
            {
                Content = Strings.BtnCancel,
                IsCancel = true,
                Width = 85,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = GameTheme.Paper,
                BorderBrush = GameTheme.Outline,
                BorderThickness = new Thickness(1)
            };
            btnCancel.Click += (s, e) => Close();

            Button btnCreate = new Button
            {
                Content = Strings.BtnCreate,
                IsDefault = true,
                Width = 85,
                Height = 32,
                Background = GameTheme.Accent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnCreate.Click += (s, e) =>
            {
                string title = _txtTitle.Text.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgProfileNameInvalid, Strings.DlgError);
                    return;
                }

                string storagePath = _txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(storagePath))
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm("請指定有效的收納資料夾路徑。", Strings.DlgError);
                    return;
                }

                try
                {
                    if (!Directory.Exists(storagePath))
                    {
                        Directory.CreateDirectory(storagePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"建立收納資料夾失敗：{ex.Message}", Strings.DlgError);
                    return;
                }

                FrameTitle = title;
                StoragePath = storagePath;
                Confirmed = true;
                Close();
            };

            footerStack.Children.Add(btnCancel);
            footerStack.Children.Add(btnCreate);
            footer.Child = footerStack;

            // Assembly
            rootGrid.Children.Add(header);
            rootGrid.Children.Add(bodyGrid); Grid.SetRow(bodyGrid, 1);
            rootGrid.Children.Add(footer); Grid.SetRow(footer, 2);

            mainBorder.Child = rootGrid;
            Content = mainBorder;

            Loaded += (s, e) => _txtTitle.Focus();
        }

        private SolidColorBrush GetAccentBrush()
        {
            try { return new SolidColorBrush(Utility.GetColorFromName(SettingsManager.SelectedColor)); }
            catch { return new SolidColorBrush(Color.FromRgb(66, 133, 244)); }
        }
    }
}
