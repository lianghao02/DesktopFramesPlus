using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows;
using Desktop_Frames;
using Desktop_Frames.FarmFences;
using Newtonsoft.Json.Linq;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);

    [STAThread]
    private static void Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Run(); app.Shutdown(0); }
            catch (Exception ex) { Console.WriteLine(ex); app.Shutdown(1); }
        };
        app.Run();
    }

    private static async Task Run()
    {
        var host = typeof(FenceManager).Assembly.GetType("Desktop_Frames.FarmFences.FarmFenceHost", true)!;
        FenceManager? manager = null;
        host.GetProperty("ManagerFactory", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            (Func<string, FenceManager>)(path => manager = new FenceManager(path, arrangeDesktop: false)));
        ProfileManager.Initialize();
        FrameDataManager.Initialize();
        FrameDataManager.LoadFrameData(new TargetChecker(1000));
        var legacy = FrameDataManager.CreateNewFrame("已有捷徑", "Data");
        var legacyFields = (System.Collections.Generic.IDictionary<string, object>)legacy;
        legacyFields["Items"] = new JArray(new JObject { ["Filename"] = "保留此捷徑.lnk" });
        host.GetMethod("Adopt")!.Invoke(null, new object?[] { legacy, null, false, null });
        Require(manager == null && ((JArray)legacyFields["Items"]).Count == 1, "已有內容不得清除或轉換");
        Console.WriteLine("PASS：已有捷徑資料不轉換、不清除。");

        var panel = FrameDataManager.CreateNewFrame("原地面板", "Data");
        string id = panel.Id;
        var oldPanel = new Window { Title = "既有空面板", Left = SystemParameters.WorkArea.Left + 20, Top = SystemParameters.WorkArea.Top + 20, Width = 320, Height = 280, ShowInTaskbar = false };
        oldPanel.Show();
        Require(GetWindowRect(new WindowInteropHelper(oldPanel).Handle, out var originalBounds), "讀取舊面板位置");
        host.GetMethod("Adopt")!.Invoke(null, new object?[] { panel, oldPanel, false, null });
        for (int retry = 0; retry < 100 && (manager == null || manager.Fences.Count == 0); retry++) await Task.Delay(20);
        Require(manager != null, "原生核心必須啟動");
        await manager!.Ready;
        for (int retry = 0; retry < 100 && !File.Exists(Path.Combine(ProfileManager.CurrentProfileDir, "farm-fences.json")); retry++) await Task.Delay(20);
        Require(manager.Fences.Count == 1 && manager.Fences[0].FenceId == "panel:" + id, "同一面板 Id 只接管一次");
        for (int retry = 0; retry < 100 && oldPanel.IsVisible; retry++) await Task.Delay(20);
        var physical = manager.Fences[0].GetPhysicalBounds();
        Console.WriteLine($"原位比對：舊視窗可見={oldPanel.IsVisible}，原位置={originalBounds.Left},{originalBounds.Top},{originalBounds.Right-originalBounds.Left},{originalBounds.Bottom-originalBounds.Top}，接管後={physical}");
        Require(!oldPanel.IsVisible && physical.Left == originalBounds.Left && physical.Top == originalBounds.Top && physical.Width == originalBounds.Right - originalBounds.Left && physical.Height == originalBounds.Bottom - originalBounds.Top, "既有面板保持原位與尺寸，成功後才關閉舊視窗");
        Framemanager.CreateFrame(panel, new TargetChecker(1000));
        Require(Application.Current.Windows.Cast<Window>().Count() == 1, "不得再建立捷徑面板視窗");
        Require(!Framemanager.AddItemToDataFrame(panel, "不可建立副本.txt") && !Directory.Exists("Shortcuts"), "原地面板禁止走建立捷徑流程");
        manager.SaveState();
        Require(((JArray)((System.Collections.Generic.IDictionary<string, object>)panel)["Items"]).Count == 0, "Items 不得產生第二份入口");
        Console.WriteLine("PASS：既有面板原位與尺寸保留、同一 Id 單一面板、禁止建立捷徑、舊 Items 格式保持空清單。");

        host.GetMethod("Stop")!.Invoke(null, null);
        host.GetMethod("Start")!.Invoke(null, null);
        await manager!.Ready;
        Require(manager.Fences.Count == 1 && (bool)host.GetMethod("IsPanel")!.Invoke(null, new object[] { id })!, "重啟恢復面板綁定");
        var window = manager.Fences[0];
        typeof(FenceManager).GetMethod("CancelFence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, new object[] { window });
        window.Close();
        Require(!FrameDataManager.FrameData.Any(f => f.Id?.ToString() == id) && FrameDataManager.FrameData.Contains(legacy), "取消只移除指定面板紀錄，其他資料保留");
        host.GetMethod("Stop")!.Invoke(null, null);
        host.GetMethod("Start")!.Invoke(null, null);
        await manager!.Ready;
        Require(manager.Fences.Count == 0, "取消後重啟不得還原空面板");
        host.GetMethod("Stop")!.Invoke(null, null);
        Console.WriteLine("PASS：重啟接管、取消後回桌面歸屬、舊面板紀錄同步移除與其他面板保全。");
    }

    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
