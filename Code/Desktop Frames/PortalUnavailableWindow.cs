using Desktop_Frames.Localization;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Desktop_Frames
{
    // 非阻塞提示保留到使用者關閉，不依賴 Windows 托盤通知是否顯示。
    public sealed class PortalUnavailableWindow : Window
    {
        public PortalUnavailableWindow(string message)
        {
            Title = Strings.Get("DlgPortalUnavailable");
            Width = Math.Min(560, SystemParameters.WorkArea.Width);
            MaxHeight = SystemParameters.WorkArea.Height * 0.8;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            ShowActivated = false;
            ShowInTaskbar = true;
            Topmost = true;

            var layout = new Grid { Margin = new Thickness(20) };
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBox
            {
                Text = message,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                FontSize = 14,
                MaxHeight = Math.Max(60, MaxHeight - 130)
            };
            layout.Children.Add(text);
            var close = new Button
            {
                Content = Strings.Get("BtnClose"),
                MinWidth = 90,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            close.Click += (_, _) => Close();
            Grid.SetRow(close, 1);
            layout.Children.Add(close);
            Content = layout;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            };
        }
    }
}
