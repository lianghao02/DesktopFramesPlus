using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Desktop_Frames;
using Desktop_Frames.Notes.Services;
using Newtonsoft.Json.Linq;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        // 測試 EXE 不是產品入口；只在測試程序指定相同 DLL 的相對 WPF 資源來源。
        // WPF 公開 setter 在已有 EntryAssembly 時拒絕變更，故於建立視窗前設定宿主欄位。
        typeof(Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, typeof(ProfileManager).Assembly);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Run(args.Single()); app.Shutdown(0); }
            catch (Exception ex) { Console.WriteLine(ex); app.Shutdown(1); }
        };
        app.Run();
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS：" + message);
    }

    private static async Task Run(string expectedVersion)
    {
        string root = AppContext.BaseDirectory;
        // 只接受啟動器建立的隔離測試目錄，禁止在正式成品或個人 Profiles 執行。
        if (!File.Exists(Path.Combine(root, ".acceptance-session")))
            throw new InvalidOperationException("缺少隔離測試標記，停止執行。");
        Check(typeof(ProfileManager).Assembly.GetName().Version?.ToString() == expectedVersion, $"載入與專案相符的 {expectedVersion} DLL");
        ProfileManager.Initialize();
        Check(Path.GetFullPath(ProfileManager.CurrentProfileDir).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Profiles 只寫入隔離目錄");
        Directory.SetCurrentDirectory(ProfileManager.CurrentProfileDir);
        SettingsManager.LoadSettings();
        Framemanager.UpdateOptionsAndClickEvents();
        SettingsManager.Language = "zh-TW";
        SettingsManager.SaveSettings();
        SettingsManager.LoadSettings();
        typeof(ProfileManager).Assembly.GetType("Desktop_Frames.Localization.Strings", true)!
            .GetMethod("SetLanguage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object[] { SettingsManager.Language });
        Check(SettingsManager.Language == "zh-TW" && File.Exists("options.json"), "繁中設定保存與讀回");
        FrameDataManager.Initialize();
        File.WriteAllText(FrameDataManager.JsonFilePath, "[]", new System.Text.UTF8Encoding(false));
        var checker = new TargetChecker(1000);
        FrameDataManager.LoadFrameData(checker);
        dynamic frame = FrameDataManager.CreateNewFrame("代表性分類面板", "Data", 120, 150, width: 400, height: 300);
        string id = frame.Id;
        Check(frame.Width == 400 && frame.Height == 300 && frame.NonExistentProperty == null, "Data 面板尺寸與安全動態屬性");
        string inputDir = Path.Combine(root, "合成 來源");
        Directory.CreateDirectory(inputDir);
        string source = Path.Combine(inputDir, "合成文件.txt");
        File.WriteAllText(source, "純合成文件，來源不可變更。", new System.Text.UTF8Encoding(false));
        byte[] hash = SHA256.HashData(File.ReadAllBytes(source));
        Check(Framemanager.AddItemToDataFrame(frame, source) && Framemanager.AddItemToDataFrame(frame, inputDir)
            && ((JArray)frame.Items).Count == 2 && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "文件與資料夾建立參照，來源內容保留");
        FrameDataManager.SaveFrameData();
        FrameDataManager.LoadFrameData(checker);
        dynamic loaded = FrameDataManager.FindFrameById(id);
        Check(loaded != null && loaded.Title == "代表性分類面板" && ((JArray)loaded.Items).Count == 2, "面板與項目保存後完整讀回");
        Framemanager.CreateFrame(loaded!, checker);
        await Task.Delay(300);
        var panel = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "代表性分類面板");
        Check(panel != null && panel.IsVisible && new WindowInteropHelper(panel).Handle != IntPtr.Zero, "正式 Data 面板建立原生視窗");
        panel!.Close();

        NoteManager.Initialize();
        var note = NoteManager.Instance.CreateNewNote();
        note.Item.Content = "代表性便箋\n繁體中文與第二行";
        note.ApplyColor("green");
        note.ApplyFontSize(18);
        NoteManager.Instance.HideAllNotes();
        Check(!note.IsVisible, "便箋隱藏");
        NoteManager.Instance.ShowAllNotes();
        Check(note.IsVisible, "便箋顯示");
        NoteManager.Instance.FlushAndCloseAll();
        var storage = new NoteStorageService();
        var notes = storage.LoadNotes();
        Check(notes.Count == 1 && notes[0].Content == "代表性便箋\n繁體中文與第二行"
            && notes[0].FontSize == 18 && notes[0].Color == "green", "便箋內容、樣式與正常結束保存");
        string notesPath = storage.GetNotesFilePath()!;
        File.WriteAllText(notesPath, "{故意損毀的合成資料", new System.Text.UTF8Encoding(false));
        byte[] corrupt = File.ReadAllBytes(notesPath);
        Check(storage.LoadNotes().Count == 0, "損毀合成便箋安全返回");
        storage.SaveImmediate(new List<Desktop_Frames.Notes.Models.NoteItem>());
        Check(corrupt.SequenceEqual(File.ReadAllBytes(notesPath)) && Directory.GetFiles(Path.GetDirectoryName(notesPath)!, "notes.json.corrupt.*").Length > 0, "損毀便箋備份且不被空清單覆寫");
        Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "驗收完成後來源雜湊不變");
    }
}
