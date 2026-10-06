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
        string root = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(root, ".acceptance-session"))) throw new InvalidOperationException("缺少隔離驗收標記。");
        string profilesRoot = (string)typeof(ProfileManager).GetField("_profilesRootDir", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Require(Path.GetFullPath(profilesRoot).Equals(Path.Combine(Path.GetFullPath(root), "Profiles"), StringComparison.OrdinalIgnoreCase), "初始化前確認隔離 Profiles");
        // 本輪正式產品為 Data；保留下方原生沙盒歷史斷言，但不得由總驗收啟動 Explorer 接管。
        if (!Environment.GetCommandLineArgs().Contains("--legacy-native"))
        {
            typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, typeof(ProfileManager).Assembly);
            ProfileManager.Initialize();
            Directory.SetCurrentDirectory(ProfileManager.CurrentProfileDir);
            FrameDataManager.Initialize();
            File.WriteAllText(FrameDataManager.JsonFilePath, "[]");
            FrameDataManager.LoadFrameData(new TargetChecker(1000));
            var data = FrameDataManager.CreateNewFrame("合成 Data 框選", "Data", 120, 150, width: 400, height: 300);
            Require(data.Width == 400 && data.Height == 300 && data.IsLocked == "false" && data.NonExistentProperty == null, "Data 框選尺寸與安全動態屬性");
            Framemanager.CreateFrame(data, new TargetChecker(1000));
            var created = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "合成 Data 框選");
            Require(created.IsVisible, "Data 視窗建立成功");
            created.Close();
            Console.WriteLine("PASS：正式 Data 面板回歸；原生沙盒測試保留但不屬本輪產品驗收。");
            return;
        }
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
        host.GetMethod("Stop")!.Invoke(null, null);
        Console.WriteLine("PASS：重啟接管、取消後回桌面歸屬、舊面板紀錄同步移除與其他面板保全。");

        // 驗證框選新增框架流程（模擬 CreateFrameFromDraw 邏輯，驗證 IsLocked、未定義屬性與視窗建立不崩潰）
        var drawnRect = new Rect(120, 150, 400, 300);
        string drawTitle = "框選自訂框架";
        var drawnFrame = FrameDataManager.CreateNewFrame(drawTitle, "Data", drawnRect.X, drawnRect.Y, width: drawnRect.Width, height: drawnRect.Height);
        Require(drawnFrame.Title == drawTitle && drawnFrame.Width == 400 && drawnFrame.Height == 300, "框選尺寸與自訂標題必須正確保存");
        Require(drawnFrame.IsLocked == "false", "預設 IsLocked 必須安全可讀取且為 false");
        Require(drawnFrame.NonExistentProperty == null, "未定義動態屬性必須安全回傳 null 不得拋出例外");
        Framemanager.CreateFrame(drawnFrame, new TargetChecker(1000));
        var createdWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == drawTitle);
        Require(createdWindow != null, "必須成功建立對應視窗不崩潰");
        createdWindow!.Close();
        Console.WriteLine("PASS：框選新增框架邏輯、安全動態屬性存取與視窗建立完整驗證通過。");

        // 驗證資料夾收納柵欄（Portal Frame）初始化、預設路徑與自動建立目錄邏輯
        string testStorageRoot = Path.Combine(Path.GetTempPath(), "DFP_Test_Storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testStorageRoot);
        try
        {
            SettingsManager.DefaultPortalStorageRoot = testStorageRoot;
            var portalFrame = FrameDataManager.CreateNewFrame("待辦公文", "Portal");
            string portalPath = portalFrame.Path;
            Require(!string.IsNullOrEmpty(portalPath) && Directory.Exists(portalPath), "Portal Frame 建立時應自動在預設總目錄建立真實資料夾");
            Require(portalPath.StartsWith(testStorageRoot, StringComparison.OrdinalIgnoreCase), "自動建立之目錄應位於 DefaultPortalStorageRoot 之下");

            // 驗證實體收納檔案屬性（絕對不得為 Hidden）
            string sampleFile = Path.Combine(portalPath, "公文測試.docx");
            File.WriteAllText(sampleFile, "測試內容");
            var attrs = File.GetAttributes(sampleFile);
            Require(!attrs.HasFlag(FileAttributes.Hidden), "收納於資料夾之檔案絕對不可具有 Hidden 屬性");
            Console.WriteLine("PASS：資料夾收納柵欄自動綁定、預設路徑建立與非隱藏檔案安全驗證通過。");
        }
        finally
        {
            if (Directory.Exists(testStorageRoot)) Directory.Delete(testStorageRoot, true);
        }
    }

    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
