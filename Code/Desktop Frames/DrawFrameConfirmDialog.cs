using Desktop_Frames.Localization;
using System;
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
        private TextBox _txtTitle;

        public DrawFrameConfirmDialog(Rect rect, string defaultTitle)
        {
            Title = Strings.DlgCreateFrameTitle;
            Width = 460;
            Height = 300;
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

            // 4. Safe mode hint
            TextBlock safeHint = new TextBlock
            {
                Text = Strings.Get("NativePanelDragHint"),
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
