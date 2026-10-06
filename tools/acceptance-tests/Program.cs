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
        string loadedDll = typeof(ProfileManager).Assembly.Location;
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadedDll))) == File.ReadAllText(Path.Combine(root, ".tested-dll-sha256")).Trim(), "載入 DLL 雜湊與指定成品一致");
        string profilesRoot = (string)typeof(ProfileManager).GetField("_profilesRootDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        Check(Path.GetFullPath(profilesRoot).Equals(Path.Combine(Path.GetFullPath(root), "Profiles"), StringComparison.OrdinalIgnoreCase), "初始化前確認 Profiles 根目錄位於隔離目錄");
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
        Check(loaded != null && loaded!.Title == "代表性分類面板" && ((JArray)loaded!.Items).Count == 2, "面板與項目保存後完整讀回");
        Framemanager.CreateFrame(loaded!, checker);
        await Task.Delay(300);
        var panel = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "代表性分類面板");
        Check(panel != null && panel.IsVisible && new WindowInteropHelper(panel).Handle != IntPtr.Zero, "正式 Data 面板建立原生視窗");
        string orderBefore = string.Join("|", ((JArray)loaded!.Items).Select(i => i["Filename"] + ":" + i["DisplayOrder"]));
        Check(panel!.Left == 120 && panel.Top == 150 && panel.Width == 400 && panel.Height == 300, "重建視窗位置與尺寸一致");
        panel!.Close();
        FrameDataManager.LoadFrameData(checker);
        loaded = FrameDataManager.FindFrameById(id);
        Framemanager.CreateFrame(loaded, checker);
        await Task.Delay(300);
        panel = Application.Current.Windows.Cast<Window>().Single(w => w.Tag?.ToString() == id);
        Check(panel.Left == 120 && panel.Top == 150 && panel.Width == 400 && panel.Height == 300 &&
            orderBefore == string.Join("|", ((JArray)loaded.Items).Select(i => i["Filename"] + ":" + i["DisplayOrder"])), "存讀並重建後兩筆項目歸屬與順序保持");
        panel.Close();
        await ExtendedChecks(checker, source, hash);

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

    private static async Task ExtendedChecks(TargetChecker checker, string source, byte[] hash)
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var move = typeof(Framemanager).Assembly.GetType("Desktop_Frames.FrameItemTransfer", true)!.GetMethod("Move", flags)!;
        foreach (bool tabs in new[] { false, true })
        {
            dynamic a = FrameDataManager.CreateNewFrame("來回 A", "Data", 120, 160, width: 400, height: 300);
            dynamic b = FrameDataManager.CreateNewFrame("來回 B", "Data");
            string aId = a.Id, bId = b.Id;
            Framemanager.AddItemToDataFrame(a, source);
            if (tabs)
            {
                a.TabsEnabled = "true"; a.CurrentTab = 0;
                b.TabsEnabled = "true"; b.CurrentTab = 0;
                a.Tabs = new JArray(new JObject { ["Title"] = "合成頁", ["Items"] = ((JArray)a.Items).DeepClone() }); a.Items = new JArray();
                b.Tabs = new JArray(new JObject { ["Title"] = "合成頁", ["Items"] = new JArray() });
            }
            for (int round = 0; round < 3; round++)
            {
                foreach (bool forward in new[] { true, false })
                {
                    a = FrameDataManager.FindFrameById(aId); b = FrameDataManager.FindFrameById(bId);
                    JArray aa = tabs ? (JArray)a.Tabs[0]["Items"] : (JArray)a.Items;
                    JArray bb = tabs ? (JArray)b.Tabs[0]["Items"] : (JArray)b.Items;
                    JArray from = forward ? aa : bb, to = forward ? bb : aa;
                    move.Invoke(null, new object[] { from, to, from[0], from[0]["Filename"]!.ToString(), 0 });
                    FrameDataManager.SaveFrameData(); FrameDataManager.LoadFrameData(checker);
                    a = FrameDataManager.FindFrameById(aId); b = FrameDataManager.FindFrameById(bId);
                    aa = tabs ? (JArray)a.Tabs[0]["Items"] : (JArray)a.Items;
                    bb = tabs ? (JArray)b.Tabs[0]["Items"] : (JArray)b.Items;
                    Check(aa.Count == (forward ? 0 : 1) && bb.Count == (forward ? 1 : 0), $"{(tabs ? "分頁" : "主區")}第 {round + 1} 輪{(forward ? "移出" : "移回")}存讀後單一歸屬");
                }
            }
            Check(Convert.ToDouble(a.X) == 120 && Convert.ToDouble(a.Y) == 160 && Convert.ToDouble(a.Width) == 400 && Convert.ToDouble(a.Height) == 300, "重新載入位置與尺寸保持");
            Framemanager.CreateFrame(a, checker);
            await Task.Delay(300);
            var window = Application.Current.Windows.Cast<Window>().Single(w => w.Tag?.ToString() == aId);
            string before = File.ReadAllText(FrameDataManager.JsonFilePath);
            SettingsManager.HideDesktopElementsOnAllFramesHide = false;
            Framemanager.ForceHideFrames(); await Task.Delay(400);
            Check(!window.IsVisible, "隱藏全部 Data 面板");
            Framemanager.WakeUpFrames(); await Task.Delay(400);
            Check(window.IsVisible && before == File.ReadAllText(FrameDataManager.JsonFilePath), "顯示全部後配置不變");
            // 移出最後一筆時，驗證來源的視覺樹與版面同步，不以存檔正確代替畫面驗收。
            var panelBorder = (System.Windows.Controls.Border)window.Content;
            var panelDock = (System.Windows.Controls.DockPanel)panelBorder.Child;
            var panelScroll = panelDock.Children.OfType<System.Windows.Controls.ScrollViewer>().First();
            var iconPanel = (System.Windows.Controls.WrapPanel)panelScroll.Content;
            Check(iconPanel.CacheMode == null, "Data 主區／分頁不保留整區位圖快取，單張圖示快取不受影響");
            Check(iconPanel.Children.OfType<System.Windows.Controls.StackPanel>().Count() == 1, "移出前來源畫面確有一個圖示");
            JArray sourceItems = tabs ? (JArray)a.Tabs[0]["Items"] : (JArray)a.Items;
            JArray targetItems = tabs ? (JArray)b.Tabs[0]["Items"] : (JArray)b.Items;
            move.Invoke(null, new object[] { sourceItems, targetItems, sourceItems[0], sourceItems[0]["Filename"]!.ToString(), 0 });
            Framemanager.RefreshFrameUsingFormApproach((NonActivatingWindow)window, a);
            Check(iconPanel.Children.Count == 0 && iconPanel.IsMeasureValid && iconPanel.IsArrangeValid,
                $"{(tabs ? "分頁" : "主區")}最後一筆移出後立即清空並完成版面更新");
            await Task.Delay(100);
            Check(iconPanel.Children.Count == 0 && sourceItems.Count == 0 && targetItems.Count == 1,
                "圖示延遲載入後來源仍空白且單一歸屬");
            window.Close();
        }
        dynamic portal = FrameDataManager.CreateNewFrame("離線合成 Portal", "Portal", portalPath: Path.Combine(AppContext.BaseDirectory, "不存在的資料夾"));
        string portalId = portal.Id;
        FrameDataManager.SaveFrameData(); FrameDataManager.LoadFrameData(checker);
        string savedPortal = File.ReadAllText(FrameDataManager.JsonFilePath);
        Framemanager.CreateFrame(FrameDataManager.FindFrameById(portalId), checker);
        Check(FrameDataManager.FindFrameById(portalId) != null && savedPortal == File.ReadAllText(FrameDataManager.JsonFilePath) &&
            !Application.Current.Windows.Cast<Window>().Any(w => w.Tag?.ToString() == portalId), "離線 Portal 保留配置、略過視窗且不存檔");
        string noticeMessage = "離線合成 Portal\n" + (string)portal.Path + "\n面板資料已保留";
        var notice = new PortalUnavailableWindow(noticeMessage);
        notice.Show();
        Check(notice.IsVisible && !notice.ShowActivated && notice.ShowInTaskbar,
            "離線提示 Show 立即返回且不搶焦點，工作列有入口");
        var noticeLayout = (System.Windows.Controls.Grid)notice.Content;
        var noticeText = noticeLayout.Children.OfType<System.Windows.Controls.TextBox>().Single();
        Check(noticeText.Text == noticeMessage && noticeText.IsReadOnly &&
            noticeText.VerticalScrollBarVisibility == System.Windows.Controls.ScrollBarVisibility.Auto,
            "離線提示完整保留名稱與路徑，可選取且可捲動");
        await Task.Delay(150);
        Check(notice.IsVisible && savedPortal == File.ReadAllText(FrameDataManager.JsonFilePath),
            "離線提示不自行關閉，也不修改面板配置");
        var noticeClose = noticeLayout.Children.OfType<System.Windows.Controls.Button>().Single();
        noticeClose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(!notice.IsVisible, "離線提示關閉按鈕正常");
        var incoming = JArray.Parse("[{ 'Filename':'Shortcuts/same.lnk','DisplayName':'來源','Arguments':'來源參數','Icon':'來源圖示' },{ 'Filename':'Shortcuts/same.lnk' }]");
        var target = JArray.Parse("[{ 'Filename':'Shortcuts/same.lnk','DisplayName':'目標','Arguments':'目標參數','Icon':'目標圖示' }]");
        move.Invoke(null, new object[] { incoming, target, incoming[0], "Shortcuts/same.lnk", 0 });
        Check(incoming.Count == 0 && target.Count == 1 && target[0]["DisplayName"]!.ToString() == "目標" && target[0]["Arguments"]!.ToString() == "目標參數" && target[0]["Icon"]!.ToString() == "目標圖示", "同路徑保留目標中繼資料、清除所有來源記錄（現行規則）");
        incoming = JArray.Parse("[{ 'Filename':'Shortcuts/same (1).lnk','DisplayName':'目標' }]");
        move.Invoke(null, new object[] { incoming, target, incoming[0], "Shortcuts/same (1).lnk", 1 });
        Check(target.Count == 2, "同顯示名稱、不同捷徑路徑不合併");
        dynamic removal = FrameDataManager.CreateNewFrame("合成移除", "Data");
        Framemanager.AddItemToDataFrame(removal, source);
        string copy = removal.Items[0]["Filename"].ToString();
        typeof(Framemanager).GetMethod("RemoveDataItem", flags)!.Invoke(null, new object[] { removal.Items, removal.Items[0] });
        Check(!File.Exists(copy) && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "移除記錄後清理捷徑，來源雜湊不變");
        Framemanager.AddItemToDataFrame(removal, source);
        copy = removal.Items[0]["Filename"].ToString();
        dynamic shared = FrameDataManager.CreateNewFrame("合成共用副本", "Data");
        shared.Items = new JArray(((JArray)removal.Items)[0].DeepClone()); FrameDataManager.SaveFrameData();
        typeof(Framemanager).GetMethod("RemoveDataItem", flags)!.Invoke(null, new object[] { removal.Items, removal.Items[0] });
        Check(File.Exists(copy), "副本仍由其他面板引用時不得清理");
        typeof(Framemanager).GetMethod("RemoveDataItem", flags)!.Invoke(null, new object[] { shared.Items, shared.Items[0] });
        Check(!File.Exists(copy), "最後一筆引用移除後才清理副本");
        string removalId = removal.Id;
        Framemanager.AddItemToDataFrame(removal, source);
        SettingsManager.ExportShortcutsOnFrameDeletion = true;
        typeof(Framemanager).GetMethod("DeleteFrameConfiguration", flags)!.Invoke(null, new object?[] { removal, null });
        FrameDataManager.LoadFrameData(checker);
        Check(FrameDataManager.FindFrameById(removalId) == null && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "刪除面板只移除配置，來源雜湊不變（含舊匯出設定）");
        dynamic visiblePortal = FrameDataManager.CreateNewFrame("可瀏覽合成 Portal", "Portal", portalPath: Path.GetDirectoryName(source)!);
        Framemanager.CreateFrame(visiblePortal, checker);
        await Task.Delay(500);
        var manager = Framemanager.GetPortalFrames().First(kv => kv.Key.Id == visiblePortal.Id).Value;
        var privateFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(PortalFramemanager).GetMethod("RenameItem", privateFlags)!.Invoke(manager, new object?[] { source, null });
        SettingsManager.UseRecycleBin = false;
        typeof(PortalFramemanager).GetMethod("DeleteItem", privateFlags)!.Invoke(manager, new object?[] { source, null });
        Check(File.Exists(source) && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Portal 改名與刪除入口不變更來源（含永久刪除舊設定）");
        var portalWindow = Application.Current.Windows.Cast<Window>().Single(w => w.Tag?.ToString() == (string)visiblePortal.Id);
        var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { source });
        // WPF 拖放事件沒有公開建構函式；僅在測試程序建立合成事件，不發送 OS 滑鼠輸入。
        var constructor = typeof(DragEventArgs).GetConstructors(privateFlags | System.Reflection.BindingFlags.Public).Single();
        var arguments = constructor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(IDataObject)) return (object)data;
            if (p.ParameterType == typeof(DragDropKeyStates)) return (object)DragDropKeyStates.None;
            if (p.ParameterType == typeof(DragDropEffects)) return (object)(DragDropEffects.Copy | DragDropEffects.Move);
            if (p.ParameterType == typeof(DependencyObject)) return (object)portalWindow;
            if (p.ParameterType == typeof(Point)) return (object)new Point(0, 0);
            throw new InvalidOperationException("未知 WPF 拖放建構參數：" + p.ParameterType);
        }).ToArray();
        var drop = (DragEventArgs)constructor.Invoke(arguments);
        drop.RoutedEvent = UIElement.DropEvent;
        portalWindow.RaiseEvent(drop);
        Check(drop.Handled && drop.Effects == DragDropEffects.None && File.Exists(source), "Portal 拒絕拖入，不搬移或複製來源");
        manager.Dispose(); portalWindow.Close();
    }
}
