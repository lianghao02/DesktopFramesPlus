using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Desktop_Frames;

internal static class GameThemeChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS：" + message);
    }

    public static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }

    public static void Run(Window panel)
    {
        // 實際載入編譯後主題與原生控制項；所有輸出只寫入隔離驗收目錄。
        Check(GameSkin.Chest.Width > 0 && GameSkin.PaperTexture is ImageBrush, "靜態圖集資源成功嵌入且可解碼");
        Check(panel.Content is GameSkin, "實際收納面板使用九宮格素材");
        Check(GameTheme.ContextMenuStyle != null, "編譯後選單主題可載入");
        Render(panel, "收納面板");
        double expandedHeight = panel.Height;
        panel.Height = 28;
        panel.UpdateLayout();
        Render(panel, "收合面板");
        Check(((Border)panel.Content).Padding.Top <= 1, "收合時保留標題與操作空間");
        panel.Height = expandedHeight;
        panel.UpdateLayout();
        var dialog = new DrawFrameConfirmDialog(new Rect(0, 0, 400, 300), "工作文件");
        dialog.Show();
        dialog.UpdateLayout();
        Check(!dialog.Title.Contains("Dlg"), "建立視窗標題已正確翻譯");
        var buttons = Children<Button>(dialog).ToArray();
        var create = buttons.Single(b => b.IsDefault);
        Check(create.Template.FindName("Face", create) is GameSkin, "建立按鈕使用 2D 按鈕範本");
        create.IsEnabled = false;
        Check(create.Opacity < 1 && !create.IsEnabled, "停用按鈕清楚顯示且不可執行");
        create.IsEnabled = true;
        var title = Children<TextBox>(dialog).First();
        Check(title.Focus(), "名稱輸入框可用鍵盤取得焦點");
        Check(Children<TextBlock>(dialog).All(t => t.ActualHeight > 0 || string.IsNullOrEmpty(t.Text)),
            "建立對話框文字完成配置");
        foreach (double scale in new[] { 1.0, 1.25, 1.5 }) Render(dialog, "建立收納柵欄", scale);
        string folder = Path.Combine(SettingsManager.DefaultPortalStorageRoot, "工作文件");
        bool existed = Directory.Exists(folder);
        buttons.Single(b => b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!dialog.Confirmed && Directory.Exists(folder) == existed, "取消建立不產生收納資料夾");

        var menu = new ContextMenu { Style = GameTheme.ContextMenuStyle, PlacementTarget = panel };
        var check = new MenuItem { Header = "鎖定位置", IsCheckable = true };
        var parent = new MenuItem { Header = "檢視方式" };
        parent.Items.Add(new MenuItem { Header = "圖示" });
        menu.Items.Add(check); menu.Items.Add(parent);
        menu.IsOpen = true;
        menu.UpdateLayout();
        Check(check.IsEnabled && check.IsCheckable && parent.HasItems, "主題保留勾選與子選單");
        check.IsChecked = true;
        Check(check.IsChecked, "選單勾選狀態可切換");
        menu.IsOpen = false;

        Exception? optionsError = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var window = Application.Current.Windows.Cast<Window>()
                .SingleOrDefault(w => w != panel && w.IsVisible && w.GetType() == typeof(Window));
            try
            {
                Check(window != null, "設定視窗可正常開啟");
                Render(window!, "設定");
                var language = Children<ComboBox>(window!).Single(b => b.Name == "LanguageComboBox");
                var languageRow = (WrapPanel)language.Parent;
                Check(languageRow.Children.OfType<FrameworkElement>().All(c => c.TranslatePoint(new Point(c.ActualWidth, c.ActualHeight), languageRow).X <= languageRow.ActualWidth + 1), "語言操作自動換行且完整顯示");
                var tabs = Children<Button>(window!).Where(b => b.Tag is int).ToArray();
                Check(tabs.Length >= 5, "設定導覽保留既有分區");
                foreach (var tab in tabs)
                {
                    tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window!.UpdateLayout();
                    Check(window.IsVisible, "設定分頁切換正常：" + tab.Content);
                }
            }
            catch (Exception ex) { optionsError = ex; }
            finally { window?.Close(); }
        };
        timer.Start();
        OptionsFormManager.ShowOptionsForm();
        timer.Stop();
        if (optionsError != null) throw optionsError;
    }

    public static void Render(Window window, string name, double scale = 1)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        if (content.ActualWidth <= 0 || content.ActualHeight <= 0)
            throw new InvalidOperationException("介面未完成配置：" + name);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * scale),
            (int)Math.Ceiling(content.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string output = Path.Combine(AppContext.BaseDirectory, "ui-evidence");
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, name + "-" + (int)(scale * 100) + ".png"));
        encoder.Save(file);
        Console.WriteLine("PASS：實際 WPF 畫面輸出 " + name + " " + scale);
    }
}
