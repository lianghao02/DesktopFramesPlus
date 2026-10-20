using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Desktop_Frames;

internal static class RoundTwoChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS：" + message);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    public static void Run(TargetChecker checker)
    {
        var requested = new Rect(107, 53, 393, 247);
        var left = ResizeSnapCalculator.Calculate(requested, ResizeEdge.Left);
        var right = ResizeSnapCalculator.Calculate(requested, ResizeEdge.Right);
        Check(left.Width == 390 && left.Right == requested.Right && left.Height == requested.Height, "左邊尺寸吸附固定右邊，且不改變高度");
        Check(right.Width == 390 && right.Left == requested.Left, "右邊尺寸吸附固定左邊");
        var corner = ResizeSnapCalculator.Calculate(requested, ResizeEdge.TopLeft);
        Check(corner.Right == requested.Right && corner.Bottom == requested.Bottom && corner.Height == 250, "左上角吸附固定右下角");
        var negative = ResizeSnapCalculator.Calculate(new Rect(-907, -403, 393, 247), ResizeEdge.Left);
        Check(negative.Right == -514 && negative.Width == 390, "負座標螢幕的左邊吸附正確");
        var minimum = ResizeSnapCalculator.Calculate(new Rect(0, 0, 101, 200), ResizeEdge.Left, 105);
        Check(minimum.Width == 105 && minimum.Right == 101, "最小尺寸不被吸附突破");
        Check(ResizeSnapCalculator.Calculate(requested, ResizeEdge.None) == requested, "單純移動不會誤調整尺寸");
        Check(ResizeSnapCalculator.HitTest(new Point(2, 100), new Size(400, 300)) == 10 &&
              ResizeSnapCalculator.HitTest(new Point(398, 100), new Size(400, 300)) == 11, "左右兩邊都提供原生縮放命中區");
        Check(ResizeSnapCalculator.HitTest(new Point(100, 100), new Size(400, 300)) == 0, "內容區不會被縮放命中區攔截");

        dynamic data = FrameDataManager.CreateNewFrame("雙向尺寸吸附驗證", "Data", 107, 53, width: 393, height: 247);
        string id = data.Id.ToString();
        Framemanager.CreateFrame(data, checker);
        var window = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().Single(w => w.Tag?.ToString() == id);
        bool enabled = SettingsManager.EnableDimensionSnap;
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            SettingsManager.EnableDimensionSnap = true;
            var hook = typeof(NonActivatingWindow).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            foreach (var edge in new[] { ResizeEdge.Left, ResizeEdge.Right })
            {
                var native = new NativeRect { Left = (int)Math.Round(107 * dpi.DpiScaleX), Top = 53,
                    Right = (int)Math.Round(500 * dpi.DpiScaleX), Bottom = 300 };
                Marshal.StructureToPtr(native, memory, false);
                object[] args = { new WindowInteropHelper(window).Handle, 0x0214, new IntPtr((int)edge), memory, false };
                hook.Invoke(window, args);
                var result = Marshal.PtrToStructure<NativeRect>(memory);
                Check((bool)args[4] && (edge == ResizeEdge.Left ? result.Right == native.Right : result.Left == native.Left),
                    "WM_SIZING 實際訊息保留固定邊：" + edge);
            }
            window.Left = 107; window.Width = 393;
            Framemanager.OnResizingEnded(window);
            var saved = FrameDataManager.FindFrameById(id);
            Check((double)saved.Width == 390 && (double)saved.X == 107, "尺寸吸附最終座標依 ID 正確保存");

            SettingsManager.EnableDimensionSnap = false;
            var unchanged = new NativeRect { Left = 107, Top = 53, Right = 500, Bottom = 300 };
            Marshal.StructureToPtr(unchanged, memory, false);
            object[] off = { new WindowInteropHelper(window).Handle, 0x0214, new IntPtr(1), memory, false };
            hook.Invoke(window, off);
            Check(!(bool)off[4] && Marshal.PtrToStructure<NativeRect>(memory).Left == 107, "關閉尺寸吸附時不更動原生拖曳矩形");
            window.ResizeMode = ResizeMode.NoResize;
            SettingsManager.EnableDimensionSnap = true;
            hook.Invoke(window, off);
            Check(!(bool)off[4], "鎖定視窗不執行尺寸吸附");
        }
        finally
        {
            SettingsManager.EnableDimensionSnap = enabled;
            Marshal.FreeHGlobal(memory);
            window.Close();
            FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
            FrameDataManager.SaveFrameData();
        }

        // 僅建立本次 GUID 專用機碼，不操作正式 Run 機碼。
        string keyPath = @"Software\DesktopFramesPlus\Regression-" + Guid.NewGuid().ToString("N");
        string exe = Environment.ProcessPath!;
        Check(WindowsStartupRegistration.RunKey == string.Join((char)92, new[] { "Software", "Microsoft", "Windows", "CurrentVersion", "Run" }), "正式啟動機碼使用 Windows 標準 Run 位置");
        try
        {
            Check(!WindowsStartupRegistration.IsEnabled(exe, keyPath), "缺少啟動機碼時正確回報未啟用");
            WindowsStartupRegistration.SetEnabled(true, exe, keyPath);
            Check(WindowsStartupRegistration.IsEnabled(exe, keyPath), "啟動註冊會建立機碼且讀回一致");
            WindowsStartupRegistration.SetEnabled(true, exe, keyPath);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true)!)
            {
                Check(key.GetValueNames().Length == 1 && (string)key.GetValue(WindowsStartupRegistration.ValueName)! == ((char)34 + exe + (char)34),
                    "含中文與空白的執行路徑有引號，重複啟用維持單一項目");
                key.SetValue("其他程式", "保留");
                key.SetValue(WindowsStartupRegistration.ValueName, "不存在的舊路徑.exe");
            }
            Check(!WindowsStartupRegistration.IsEnabled(exe, keyPath), "舊路徑或錯誤命令不會誤判為啟用");
            WindowsStartupRegistration.SetEnabled(true, exe, keyPath);
            bool rejected = false;
            try { WindowsStartupRegistration.SetEnabled(true, Path.Combine(AppContext.BaseDirectory, "missing.exe"), keyPath); }
            catch (FileNotFoundException) { rejected = true; }
            Check(rejected && WindowsStartupRegistration.IsEnabled(exe, keyPath), "不存在的 EXE 被拒絕，且保留有效註冊");
            WindowsStartupRegistration.SetEnabled(false, exe, keyPath);
            using var final = Registry.CurrentUser.OpenSubKey(keyPath)!;
            Check(final.GetValue(WindowsStartupRegistration.ValueName) == null && (string)final.GetValue("其他程式")! == "保留",
                "停用只移除本程式，不影響其他項目");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
    }
}
