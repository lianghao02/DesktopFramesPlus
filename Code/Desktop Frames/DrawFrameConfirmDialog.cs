using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames
{
    public class DrawFrameConfirmDialog : Window
    {
        public bool Confirmed { get; private set; } = false;
        public string FrameTitle { get; private set; }
        public List<string> SelectedItemPaths { get; private set; } = new List<string>();

        private TextBox _txtTitle;
        private CheckBox _chkCollectIcons;
        private Border _iconsContainer;
        private StackPanel _iconsListPanel;
        private List<CheckBox> _iconCheckBoxes = new List<CheckBox>();

        public DrawFrameConfirmDialog(Rect rect, string defaultTitle)
        {
            Title = Strings.DlgCreateFrameTitle;
            Width = 460;
            Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            Border mainBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
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

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // --- HEADER ---
            Border header = new Border
            {
                Background = GetAccentBrush(),
                Padding = new Thickness(15),
                Height = 50
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleBlock = new TextBlock
            {
                Text = Strings.DlgCreateFrameTitle,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button closeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                FontSize = 16,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeBtn.Click += (s, e) => Close();

            headerGrid.Children.Add(titleBlock);
            headerGrid.Children.Add(closeBtn);
            Grid.SetColumn(closeBtn, 1);
            header.Child = headerGrid;
            header.MouseLeftButtonDown += (s, e) => DragMove();

            // --- BODY ---
            Grid bodyGrid = new Grid { Margin = new Thickness(16) };
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name & Size
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Checkbox
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List container
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Safe hint

            // 1. Name & Size
            StackPanel metaPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            TextBlock lblName = new TextBlock
            {
                Text = Strings.LblFrameName,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _txtTitle = new TextBox
            {
                Text = defaultTitle,
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224))
            };
            _txtTitle.SelectAll();

            TextBlock lblSize = new TextBlock
            {
                Text = Strings.Get("LblFrameSize", (int)rect.Width, (int)rect.Height),
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            };

            metaPanel.Children.Add(lblName);
            metaPanel.Children.Add(_txtTitle);
            metaPanel.Children.Add(lblSize);
            bodyGrid.Children.Add(metaPanel);

            // 2. Checkbox to collect icons
            _chkCollectIcons = new CheckBox
            {
                Content = Strings.OptCollectDesktopIcons,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsChecked = false
            };
            Grid.SetRow(_chkCollectIcons, 1);
            bodyGrid.Children.Add(_chkCollectIcons);

            // 3. Icons List Container
            _iconsContainer = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
                Padding = new Thickness(8),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 8)
            };

            Grid listLayout = new Grid();
            listLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
            listLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // ScrollViewer

            StackPanel toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            Button btnSelectAll = new Button
            {
                Content = Strings.BtnSelectAll,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnSelectAll.Click += (s, e) =>
            {
                foreach (var cb in _iconCheckBoxes) cb.IsChecked = true;
            };

            Button btnDeselectAll = new Button
            {
                Content = Strings.BtnDeselectAll,
                Padding = new Thickness(8, 2, 8, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnDeselectAll.Click += (s, e) =>
            {
                foreach (var cb in _iconCheckBoxes) cb.IsChecked = false;
            };

            toolbar.Children.Add(btnSelectAll);
            toolbar.Children.Add(btnDeselectAll);
            listLayout.Children.Add(toolbar);

            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _iconsListPanel = new StackPanel();
            scrollViewer.Content = _iconsListPanel;
            Grid.SetRow(scrollViewer, 1);
            listLayout.Children.Add(scrollViewer);

            _iconsContainer.Child = listLayout;
            Grid.SetRow(_iconsContainer, 2);
            bodyGrid.Children.Add(_iconsContainer);

            // Populate Desktop Icons
            PopulateDesktopIcons();

            _chkCollectIcons.Checked += (s, e) => _iconsContainer.Visibility = Visibility.Visible;
            _chkCollectIcons.Unchecked += (s, e) => _iconsContainer.Visibility = Visibility.Collapsed;

            // 4. Safe mode hint
            TextBlock safeHint = new TextBlock
            {
                Text = Strings.LblSafeModeHint,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(34, 139, 34)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(safeHint, 3);
            bodyGrid.Children.Add(safeHint);

            // --- FOOTER ---
            Border footer = new Border
            {
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
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
                Width = 85,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1)
            };
            btnCancel.Click += (s, e) => Close();

            Button btnCreate = new Button
            {
                Content = Strings.BtnCreate,
                Width = 85,
                Height = 32,
                Background = GetAccentBrush(),
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

                FrameTitle = title;
                if (_chkCollectIcons.IsChecked == true)
                {
                    SelectedItemPaths = _iconCheckBoxes
                        .Where(cb => cb.IsChecked == true && cb.Tag is string)
                        .Select(cb => (string)cb.Tag)
                        .ToList();
                }

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

        private void PopulateDesktopIcons()
        {
            try
            {
                var desktopPaths = new List<string>();
                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

                if (Directory.Exists(userDesktop)) desktopPaths.AddRange(Directory.GetFiles(userDesktop));
                if (Directory.Exists(commonDesktop)) desktopPaths.AddRange(Directory.GetFiles(commonDesktop));

                var filteredFiles = desktopPaths
                    .Where(p => !string.Equals(Path.GetFileName(p), "desktop.ini", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => Path.GetFileNameWithoutExtension(p))
                    .ToList();

                if (filteredFiles.Count == 0)
                {
                    _iconsListPanel.Children.Add(new TextBlock
                    {
                        Text = Strings.LblNoDesktopIcons,
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(4)
                    });
                    return;
                }

                foreach (string filePath in filteredFiles)
                {
                    string displayName = Path.GetFileNameWithoutExtension(filePath);
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = Path.GetFileName(filePath);

                    var cb = new CheckBox
                    {
                        Content = displayName,
                        Tag = filePath,
                        Margin = new Thickness(2, 3, 2, 3),
                        IsChecked = true, // Default to checked if user enables icon collection
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = filePath
                    };

                    _iconCheckBoxes.Add(cb);
                    _iconsListPanel.Children.Add(cb);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Failed to scan desktop icons: {ex.Message}");
            }
        }

        private SolidColorBrush GetAccentBrush()
        {
            try { return new SolidColorBrush(Utility.GetColorFromName(SettingsManager.SelectedColor)); }
            catch { return new SolidColorBrush(Color.FromRgb(66, 133, 244)); }
        }
    }
}
