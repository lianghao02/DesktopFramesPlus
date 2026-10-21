using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Desktop_Frames;
using Newtonsoft.Json.Linq;

/// <summary>
/// FrameManager 重構前的特徵化測試（Phase 2 資料層、Phase 3 視窗層、Phase 4 主視窗拖放）。
/// 目的不是證明設計正確，而是鎖住「目前行為」：日後搬移或抽取 CreateFrame／AddIcon 內的程式碼時，行為一變就失敗。
/// 選單結構以黃金檔（golden/*.txt，內嵌資源）比對；黃金檔變動代表選單變了，必須人工確認後才更新。
/// 全部使用虛構資料，只在隔離驗收目錄內讀寫。
/// </summary>
internal static class FrameManagerBehaviorChecks
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private const BindingFlags AnyStatic = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS：" + message);
    }

    public static async Task Run(TargetChecker checker)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "合成 行為驗證");
        Directory.CreateDirectory(root);
        // 隔離防護：本組測試不得在真實桌面的預設收納位置建立任何資料夾。
        string realStorageRoot = SettingsManager.DefaultPortalStorageRoot;
        bool realRootExisted = Directory.Exists(realStorageRoot);
        var realEntriesBefore = realRootExisted ? Directory.GetFileSystemEntries(realStorageRoot).OrderBy(x => x).ToArray() : Array.Empty<string>();
        UpdateFramePropertyChecks();
        await RuntimePropertyChecks(checker);
        MigrateLegacyJsonChecks(root);
        await RepositionChecks(checker);
        await MenuSnapshotChecks(checker, root);
        await LockRenameTabsIconChecks(checker, root);
        await TitleBarChecks(checker);
        await DropChecks(checker, root);
        await FitToContentChecks(checker, root);
        await ResetCustomizationChecks();
        var realEntriesAfter = Directory.Exists(realStorageRoot) ? Directory.GetFileSystemEntries(realStorageRoot).OrderBy(x => x).ToArray() : Array.Empty<string>();
        Check(Directory.Exists(realStorageRoot) == realRootExisted && realEntriesBefore.SequenceEqual(realEntriesAfter),
            "隔離：本組測試沒有在真實桌面的預設收納位置建立或改動任何資料夾");
    }

    // ---------- 共用工具 ----------
    private static JObject Saved(string id) =>
        (JObject)JArray.Parse(File.ReadAllText(FrameDataManager.JsonFilePath)).First(f => f["Id"]?.ToString() == id);

    // FrameData 內可能是 DynamicFrameData（IDictionary 包裝）或 JObject，一律以 dynamic 操作。
    private static dynamic Live(string id) => FrameDataManager.FindFrameById(id)!;

    private static NonActivatingWindow WindowOf(string id) =>
        Application.Current.Windows.OfType<NonActivatingWindow>().Single(w => w.Tag?.ToString() == id);

    // 部分操作（切換分頁、刷新圖示）會重建視窗，需等新視窗出現。
    private static async Task<NonActivatingWindow> WaitWindow(string id)
    {
        for (int i = 0; i < 60; i++)
        {
            var w = Application.Current.Windows.OfType<NonActivatingWindow>().FirstOrDefault(x => x.Tag?.ToString() == id && x.IsVisible);
            if (w != null) return w;
            await Task.Delay(50);
        }
        throw new InvalidOperationException($"等待面板視窗逾時：{id}");
    }

    private static async Task<NonActivatingWindow> Open(dynamic frame, TargetChecker checker)
    {
        Framemanager.CreateFrame(frame, checker);
        await Task.Delay(300);
        return WindowOf(frame.Id.ToString());
    }

    private static void Invoke(string name, params object?[] args)
    {
        var method = typeof(Framemanager).GetMethod(name, AnyStatic)
            ?? throw new InvalidOperationException($"找不到 Framemanager.{name}；若方法已改名或搬移，請同步更新特徵化測試");
        method.Invoke(null, args);
    }

    private static string TextOf(object? value) => value switch
    {
        null => "",
        string s => s,
        TextBlock tb => tb.Text,
        ContentControl cc => TextOf(cc.Content),
        Panel p => string.Join(" ", p.Children.Cast<object>().Select(TextOf).Where(t => t.Length > 0)),
        _ => value.ToString() ?? ""
    };

    private static string Snapshot(ItemsControl menu)
    {
        var sb = new StringBuilder();
        void Walk(ItemsControl parent, int depth)
        {
            foreach (var item in parent.Items)
            {
                string indent = new string(' ', depth * 2);
                if (item is Separator sep)
                {
                    sb.Append(indent).Append("---").Append(sep.Visibility == Visibility.Visible ? "" : " [隱藏]").Append('\n');
                    continue;
                }
                if (item is not MenuItem mi)
                {
                    sb.Append(indent).Append('<').Append(item.GetType().Name).Append(">\n");
                    continue;
                }
                var flags = new List<string>();
                if (mi.Visibility != Visibility.Visible) flags.Add("隱藏");
                if (!mi.IsEnabled) flags.Add("停用");
                if (mi.IsCheckable) flags.Add(mi.IsChecked ? "已勾選" : "未勾選");
                sb.Append(indent).Append("- ").Append(TextOf(mi.Header).Trim());
                if (flags.Count > 0) sb.Append(" [").Append(string.Join("、", flags)).Append(']');
                sb.Append('\n');
                if (mi.HasItems) Walk(mi, depth + 1);
            }
        }
        Walk(menu, 0);
        return sb.ToString();
    }

    private static void Golden(string name, string actual)
    {
        string normalized = actual.Replace("\r\n", "\n").Trim();
        string outFile = Path.Combine(AppContext.BaseDirectory, $"actual-{name}.txt");
        File.WriteAllText(outFile, normalized + "\n", Utf8);
        using var stream = typeof(FrameManagerBehaviorChecks).Assembly.GetManifestResourceStream($"golden.{name}.txt");
        if (stream == null && Environment.GetEnvironmentVariable("DF_GOLDEN_BOOTSTRAP") == "1")
        {
            Console.WriteLine($"BOOTSTRAP：黃金檔 {name} 尚未建立，已輸出實際結果 {outFile}");
            return;
        }
        if (stream == null)
            throw new InvalidOperationException($"缺少黃金檔 golden/{name}.txt；實際結果已輸出到 {outFile}，人工確認後複製到 tools/acceptance-tests/golden/");
        string expected = new StreamReader(stream, Encoding.UTF8).ReadToEnd().Replace("\r\n", "\n").Trim();
        if (expected != normalized)
            throw new InvalidOperationException($"選單結構與黃金檔不一致：{name}（實際結果見 {outFile}；若是刻意修改，確認後更新黃金檔）");
        Console.WriteLine($"PASS：選單結構與黃金檔一致：{name}");
    }

    private static void CloseDialogsSoon(DispatcherTimer timer)
    {
        timer.Interval = TimeSpan.FromMilliseconds(150);
        timer.Tick += (_, _) =>
        {
            foreach (var w in Application.Current.Windows.Cast<Window>().Where(w => w.GetType().Name.Contains("MessageBox")).ToList())
                w.Close();
        };
        timer.Start();
    }

    // ---------- Phase 2：資料層 ----------
    private static void UpdateFramePropertyChecks()
    {
        dynamic created = FrameDataManager.CreateNewFrame("屬性寫入驗證", "Data", 220, 180, width: 320, height: 220);
        string id = created.Id.ToString();
        dynamic live = Live(id);
        live["未來欄位"] = "保留";
        FrameDataManager.SaveFrameData();

        Framemanager.UpdateFrameProperty(live, "IsHidden", "True", "驗證");
        Check(Saved(id)["IsHidden"]?.Type == JTokenType.String && Saved(id)["IsHidden"]!.ToString() == "true",
            "特徵化：IsHidden 一律存成小寫字串 true／false");
        Framemanager.UpdateFrameProperty(live, "IsHidden", "abc", "驗證");
        Check(Saved(id)["IsHidden"]!.ToString() == "false", "特徵化：IsHidden 非 true 的值存成 false");

        Framemanager.UpdateFrameProperty(live, "FrameBorderThickness", "4", "驗證");
        Check(Saved(id)["FrameBorderThickness"]?.Type == JTokenType.Integer && (int)Saved(id)["FrameBorderThickness"]! == 4,
            "特徵化：外框粗細存成整數");
        Framemanager.UpdateFrameProperty(live, "FrameBorderThickness", "粗", "驗證");
        Check((int)Saved(id)["FrameBorderThickness"]! == 2, "特徵化：外框粗細無法解析時改用 2");

        Framemanager.UpdateFrameProperty(live, "TitleTextColor", "Red", "驗證");
        Check(Saved(id)["TitleTextColor"]!.ToString() == "Red" && Saved(id)["未來欄位"]?.ToString() == "保留",
            "相容：一般屬性照字串寫入，未知欄位不會遺失");

        var clone = JObject.FromObject((object)Live(id));
        Framemanager.UpdateFrameProperty(clone, "IconSize", "Large", "驗證");
        Check(clone["IconSize"]!.ToString() == "Large" && Live(id)["IconSize"]!.ToString() == "Large" && Saved(id)["IconSize"]!.ToString() == "Large",
            "特徵化：以副本呼叫時，副本、記憶體清單與檔案三者同步");

        byte[] before = File.ReadAllBytes(FrameDataManager.JsonFilePath);
        Framemanager.UpdateFrameProperty(new JObject { ["Id"] = "不存在的面板", ["Title"] = "幽靈" }, "IconSize", "Small", "驗證");
        Check(before.SequenceEqual(File.ReadAllBytes(FrameDataManager.JsonFilePath)), "特徵化：找不到面板時不寫檔");

        var transition = (HashSet<string>)typeof(Framemanager).GetField("_framesInTransition", AnyStatic)!.GetValue(null)!;
        transition.Add(id);
        try
        {
            Framemanager.UpdateFrameProperty(live, "CustomColor", "Blue", "驗證");
            Framemanager.UpdateFrameProperty(live, "UnrolledHeight", "333", "驗證");
            Check(Saved(id)["CustomColor"]?.ToString() != "Blue" && Saved(id)["UnrolledHeight"]?.ToString() == "333",
                "特徵化：收合動畫進行中只接受 IsRolled／UnrolledHeight，其他屬性略過");
        }
        finally { transition.Remove(id); }
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }

    private static async Task RuntimePropertyChecks(TargetChecker checker)
    {
        dynamic created = FrameDataManager.CreateNewFrame("執行期屬性驗證", "Data", 240, 200, width: 300, height: 200);
        string id = created.Id.ToString();
        NonActivatingWindow win = await Open(Live(id), checker);
        Framemanager.UpdateFrameProperty(Live(id), "IsHidden", "true", "驗證");
        Check(win.Visibility == Visibility.Hidden, "特徵化：IsHidden=true 立即隱藏視窗");
        Framemanager.UpdateFrameProperty(Live(id), "IsHidden", "false", "驗證");
        Check(win.Visibility == Visibility.Visible, "特徵化：IsHidden=false 立即顯示視窗");
        Framemanager.UpdateFrameProperty(Live(id), "AlwaysOnTop", "true", "驗證");
        Check(win.Topmost, "特徵化：AlwaysOnTop=true 立即置頂");
        Framemanager.UpdateFrameProperty(Live(id), "AlwaysOnTop", "false", "驗證");
        Check(!win.Topmost, "特徵化：AlwaysOnTop=false 取消置頂");
        win.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }

    private static void MigrateLegacyJsonChecks(string root)
    {
        string existingDir = Path.Combine(root, "舊版 資料夾");
        Directory.CreateDirectory(existingDir);
        var snapshot = FrameDataManager.FrameData.ToList(); // 保留原物件參照，結束後原樣放回
        try
        {
            FrameDataManager.FrameData.Clear();
            var legacy = new JObject
            {
                ["Title"] = "舊版面板", ["ItemsType"] = "Data", ["X"] = 10, ["Y"] = 10, ["Width"] = 300, ["Height"] = 222,
                ["IconSize"] = "Large", ["未來欄位"] = "保留",
                ["Items"] = new JArray(
                    new JObject { ["Filename"] = existingDir, ["DisplayName"] = "資料夾" },
                    new JObject { ["Filename"] = Path.Combine(root, "不存在.txt") }),
                ["Tabs"] = new JArray(new JObject { ["Items"] = "壞掉的資料" })
            };
            var portal = new JObject
            {
                ["Id"] = "舊版資料夾面板", ["Title"] = "舊版資料夾面板", ["ItemsType"] = "Portal", ["Path"] = existingDir,
                ["Items"] = new JArray(new JObject { ["Filename"] = "x" })
            };
            FrameDataManager.FrameData.Add(legacy);
            FrameDataManager.FrameData.Add(portal);
            Invoke("MigrateLegacyJson");

            var m = (JObject)FrameDataManager.FrameData[0];
            Check(Guid.TryParse(m["Id"]?.ToString(), out _), "遷移：缺少 Id 時補上 GUID");
            Check(m["IsLocked"]?.ToString() == "false" && m["IsRolled"]?.ToString() == "false" && m["AlwaysOnTop"]?.ToString() == "false"
                && m["IsHidden"]?.ToString() == "false" && m["TabsEnabled"]?.ToString() == "false",
                "遷移：布林屬性補成字串 false");
            Check((double)m["UnrolledHeight"]! == 222 && (int)m["IconSpacing"]! == 5 && (int)m["FrameBorderThickness"]! == 2
                && m["TitleTextSize"]?.ToString() == "Medium" && (int)m["CurrentTab"]! == 0,
                "遷移：數值預設值（展開高度沿用原高度、間距 5、外框 2）");
            Check(m["IconSize"]?.ToString() == "Large" && m["未來欄位"]?.ToString() == "保留" && m["Title"]?.ToString() == "舊版面板",
                "相容：既有設定與未知欄位不被覆蓋");
            var items = (JArray)m["Items"]!;
            Check((bool)items[0]["IsFolder"]! && !(bool)items[1]["IsFolder"]! && items.All(i => i["IsLink"]?.Type == JTokenType.Boolean
                && i["IsNetwork"] != null && i["AlwaysRunAsAdmin"] != null),
                "遷移：項目補上 IsFolder（依實際存在判斷）、IsLink、IsNetwork、AlwaysRunAsAdmin");
            var tab = (JObject)((JArray)m["Tabs"]!)[0];
            Check(tab["TabName"]?.ToString() == "Tab 1" && tab["Items"] is JArray { Count: 0 }, "遷移：損壞的分頁補上名稱與空清單");
            var p = (JObject)FrameDataManager.FrameData[1];
            Check(((JArray)p["Items"]!)[0]["IsLink"] == null, "遷移：舊版資料夾面板的項目不被改寫");
            Check(JArray.Parse(File.ReadAllText(FrameDataManager.JsonFilePath)).Any(f => f["Title"]?.ToString() == "舊版面板" && f["Id"] != null),
                "遷移：有變更時寫回 frames.json");
            byte[] once = File.ReadAllBytes(FrameDataManager.JsonFilePath);
            Invoke("MigrateLegacyJson");
            Check(once.SequenceEqual(File.ReadAllBytes(FrameDataManager.JsonFilePath)), "遷移：已遷移的資料再跑一次不會改動檔案");
        }
        finally
        {
            FrameDataManager.FrameData.Clear();
            foreach (var f in snapshot) FrameDataManager.FrameData.Add(f);
            FrameDataManager.SaveFrameData();
        }
    }

    private static async Task RepositionChecks(TargetChecker checker)
    {
        dynamic created = FrameDataManager.CreateNewFrame("螢幕外面板", "Data", 120, 150, width: 300, height: 200);
        string id = created.Id.ToString();
        NonActivatingWindow win = await Open(Live(id), checker);
        win.Left = -30000;
        win.Top = -30000;
        // 關閉自動調整位置，專測手動「拉回螢幕內」這條路（否則自動調整會掩蓋手動功能失效）。
        bool autoBefore = SettingsManager.AllowAutoReposition;
        SettingsManager.AllowAutoReposition = false;
        try { Framemanager.ForceRepositionallFrames(); }
        finally { SettingsManager.AllowAutoReposition = autoBefore; }
        await Task.Delay(100);
        var dpi = VisualTreeHelper.GetDpi(win);
        var point = new System.Drawing.Point((int)((win.Left + 5) * dpi.DpiScaleX), (int)((win.Top + 5) * dpi.DpiScaleY));
        Check(win.Left > -30000 && System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(point)),
            "特徵化：手動「拉回螢幕內」會把螢幕外的面板移入工作區");
        win.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }

    // ---------- Phase 3：視窗層 ----------
    private static async Task MenuSnapshotChecks(TargetChecker checker, string root)
    {
        string source = Path.Combine(root, "選單 來源");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "選單文件.txt");
        File.WriteAllText(file, "合成", Utf8);

        dynamic data = FrameDataManager.CreateNewFrame("選單快照 Data", "Data", 300, 200, width: 360, height: 260);
        string dataId = data.Id.ToString();
        Framemanager.AddItemToDataFrame(Live(dataId), file);
        Framemanager.AddItemToDataFrame(Live(dataId), source);
        FrameDataManager.SaveFrameData();
        NonActivatingWindow dataWin = await Open(Live(dataId), checker);
        var heart = GameThemeChecks.Children<TextBlock>(dataWin).First(t => t.Name == "FrameMenuIcon");
        Golden("data-heart-menu", Snapshot(heart.ContextMenu!));
        var panelMenu = dataWin.ContextMenu!;
        panelMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, panelMenu));
        Golden("data-panel-menu", Snapshot(panelMenu));
        var iconMenu = GameThemeChecks.Children<StackPanel>(dataWin).First(s => s.ContextMenu != null).ContextMenu!;
        iconMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, iconMenu));
        Golden("data-icon-menu", Snapshot(iconMenu));
        dataWin.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == dataId);

        string fenceDir = Path.Combine(root, "選單 收納夾");
        Directory.CreateDirectory(fenceDir);
        // 一律明確指定隔離目錄；未指定時程式會使用真實桌面的預設收納位置。
        dynamic fence = FrameDataManager.CreateNewFrame("選單快照 收納", "Portal", 320, 220, width: 360, height: 260,
            portalPath: fenceDir, isManagedStorage: true, storageRootPath: root);
        string fenceId = fence.Id.ToString();
        FrameDataManager.SaveFrameData();
        NonActivatingWindow fenceWin = await Open(Live(fenceId), checker);
        var fenceHeart = GameThemeChecks.Children<TextBlock>(fenceWin).First(t => t.Name == "FrameMenuIcon");
        Golden("fence-heart-menu", Snapshot(fenceHeart.ContextMenu!));
        var fenceMenu = fenceWin.ContextMenu!;
        fenceMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, fenceMenu));
        Golden("fence-panel-menu", Snapshot(fenceMenu));
        fenceWin.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == fenceId);
        FrameDataManager.SaveFrameData();
    }

    // 標題列與外框（CreateFrame 拆解第一段的保護）：外觀取自面板設定；改名進行中點 ♥ 或鎖頭會先保存名稱
    private static async Task TitleBarChecks(TargetChecker checker)
    {
        dynamic created = FrameDataManager.CreateNewFrame("標題列驗證", "Data", 280, 260, width: 380, height: 260);
        string id = created.Id.ToString();
        dynamic live = Live(id);
        live["TitleTextColor"] = "Red";
        live["TitleTextSize"] = "Large";
        live["BoldTitleText"] = "true";
        live["FrameBorderColor"] = "Blue";
        live["FrameBorderThickness"] = 3;
        FrameDataManager.SaveFrameData();
        NonActivatingWindow win = await Open(Live(id), checker);

        var label = GameThemeChecks.Children<Label>(win).First(l => l.Content?.ToString() == "標題列驗證");
        Check(label.Foreground is SolidColorBrush { } fg && fg.Color == Utility.GetColorFromName("Red")
            && label.FontSize == 16 && label.FontWeight == FontWeights.Bold,
            "特徵化：標題文字的顏色、字級（Large=16）與粗體取自面板設定");
        // 現狀：面板設定的外框顏色最後會被 Q 版主題的輪廓色蓋掉，且粗細為 0，外框顏色設定在畫面上沒有作用（另案評估）。
        Check(win.Content is GameSkin { Skin: GameSkinKind.Panel } panel && panel.BorderBrush == GameTheme.Outline
            && panel.BorderThickness == new Thickness(0),
            "特徵化：外框為 Q 版面板，筆刷為主題輪廓色、粗細 0（現狀：自訂外框顏色不生效）");

        var titleBox = ((Panel)VisualTreeHelper.GetParent(label)).Children.OfType<TextBox>().First();
        var heart = GameThemeChecks.Children<TextBlock>(win).First(t => t.Name == "FrameMenuIcon");
        var lockIcon = GameThemeChecks.Children<TextBlock>(win).First(t => t.Name == "FrameLockIcon");
        void Click(TextBlock target) => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = target });

        titleBox.Visibility = Visibility.Visible;
        titleBox.Text = "♥ 保存的名稱";
        Click(heart);
        await Task.Delay(50);
        Check(Saved(id)["Title"]?.ToString() == "♥ 保存的名稱" && label.Content?.ToString() == "♥ 保存的名稱"
            && titleBox.Visibility != Visibility.Visible && heart.ContextMenu?.IsOpen != true,
            "特徵化：改名進行中點 ♥ 會先保存名稱、不開選單");

        titleBox.Visibility = Visibility.Visible;
        titleBox.Text = "鎖頭保存的名稱";
        Click(lockIcon);
        await Task.Delay(50);
        Check(Saved(id)["Title"]?.ToString() == "鎖頭保存的名稱" && Saved(id)["IsLocked"]?.ToString() != "true",
            "特徵化：改名進行中點鎖頭會先保存名稱、不切換鎖定");

        Click(heart);
        await Task.Delay(50);
        Check(heart.ContextMenu?.IsOpen == true, "特徵化：一般狀態點 ♥ 會開啟 ♥ 選單");
        heart.ContextMenu!.IsOpen = false;

        win.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }

    private static async Task LockRenameTabsIconChecks(TargetChecker checker, string root)
    {
        string source = Path.Combine(root, "互動 來源");
        Directory.CreateDirectory(source);
        var files = Enumerable.Range(1, 3).Select(i => Path.Combine(source, $"互動文件{i}.txt")).ToList();
        foreach (var f in files) File.WriteAllText(f, "合成", Utf8);
        dynamic created = FrameDataManager.CreateNewFrame("互動驗證", "Data", 260, 240, width: 380, height: 260);
        string id = created.Id.ToString();
        foreach (var f in files) Framemanager.AddItemToDataFrame(Live(id), f);
        FrameDataManager.SaveFrameData();
        NonActivatingWindow win = await Open(Live(id), checker);

        // 鎖定切換
        var lockIcon = GameThemeChecks.Children<TextBlock>(win).First(t => t.Name == "FrameLockIcon");
        void ClickLock() => lockIcon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = lockIcon });
        ClickLock();
        await Task.Delay(50);
        Check(Saved(id)["IsLocked"]?.ToString() == "true" && lockIcon.Background is SolidColorBrush { Color.A: 255 },
            "特徵化：點鎖頭會鎖定並寫檔，鎖頭顯示不透明底色");
        ClickLock();
        await Task.Delay(50);
        Check(Saved(id)["IsLocked"]?.ToString() == "false" && (lockIcon.Background == null || lockIcon.Background is SolidColorBrush { Color.A: 0 }),
            "特徵化：再點一次解除鎖定並寫檔，鎖頭底色回到透明");

        // 重新命名（Enter 保存、Esc 取消）
        var label = GameThemeChecks.Children<Label>(win).First(l => l.Content?.ToString() == "互動驗證");
        var titleBox = ((Panel)VisualTreeHelper.GetParent(label)).Children.OfType<TextBox>().First();
        void Key(Key key) => titleBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(titleBox)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.KeyDownEvent });
        titleBox.Visibility = Visibility.Visible;
        titleBox.Text = "不該保存的名稱";
        Key(System.Windows.Input.Key.Escape);
        Check(Saved(id)["Title"]?.ToString() == "互動驗證" && titleBox.Visibility != Visibility.Visible, "特徵化：重新命名按 Esc 取消，不寫檔");
        titleBox.Visibility = Visibility.Visible;
        titleBox.Text = "改名後面板";
        Key(System.Windows.Input.Key.Enter);
        Check(Saved(id)["Title"]?.ToString() == "改名後面板" && win.Title == "改名後面板" && label.Content?.ToString() == "改名後面板",
            "特徵化：重新命名按 Enter 保存，視窗標題、標籤與檔案同步");

        // 分頁開關：開啟時主區項目移入第一個分頁，關閉時移回
        var names = ((JArray)Live(id)["Items"]!).Select(i => i["Filename"]!.ToString()).OrderBy(x => x).ToList();
        Invoke("ToggleFrameTabs", Live(id));
        var tabs = (JArray)Saved(id)["Tabs"]!;
        Check(Saved(id)["TabsEnabled"]?.ToString() == "true" && ((JArray)Saved(id)["Items"]!).Count == 0
            && ((JArray)tabs[0]["Items"]!).Select(i => i["Filename"]!.ToString()).OrderBy(x => x).SequenceEqual(names),
            "特徵化：開啟分頁時主區項目完整移入第一個分頁");
        Invoke("ToggleFrameTabs", Live(id));
        Check(Saved(id)["TabsEnabled"]?.ToString() == "false"
            && ((JArray)Saved(id)["Items"]!).Select(i => i["Filename"]!.ToString()).OrderBy(x => x).SequenceEqual(names),
            "特徵化：關閉分頁時目前分頁的項目完整移回主區、不重複");

        // 圖示大小
        win = await WaitWindow(id);
        Framemanager.SetFrameIconSizeLive(win, Live(id), "Small");
        await Task.Delay(150);
        double small = GameThemeChecks.Children<Image>(win).Where(i => i.ActualWidth > 0).Select(i => i.Width > 0 ? i.Width : i.ActualWidth).DefaultIfEmpty(0).Max();
        Framemanager.SetFrameIconSizeLive(win, Live(id), "Large");
        await Task.Delay(150);
        win = await WaitWindow(id);
        double large = GameThemeChecks.Children<Image>(win).Where(i => i.ActualWidth > 0).Select(i => i.Width > 0 ? i.Width : i.ActualWidth).DefaultIfEmpty(0).Max();
        Check(Saved(id)["IconSize"]?.ToString() == "Large" && small > 0 && large > small,
            $"特徵化：圖示大小即時套用並寫檔（小 {small}、大 {large}）");

        win.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }

    // ---------- Phase 4：主視窗拖放（無修飾鍵＝預設行為） ----------
    private static DragEventArgs Drop(Window win, params string[] paths)
    {
        var ctor = typeof(DragEventArgs).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) }, null)
            ?? throw new InvalidOperationException("無法建立 DragEventArgs（WPF 內部建構子已變更），拖放測試需改寫");
        var data = new DataObject(DataFormats.FileDrop, paths);
        var args = (DragEventArgs)ctor.Invoke(new object[] { data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, win, new Point(50, 80) });
        args.RoutedEvent = DragDrop.DropEvent;
        win.RaiseEvent(args);
        return args;
    }

    private static async Task DropChecks(TargetChecker checker, string root)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            throw new InvalidOperationException("執行拖放測試時偵測到按住的修飾鍵（Ctrl／Alt／Shift），請放開後重跑");
        string source = Path.Combine(root, "拖放 來源");
        Directory.CreateDirectory(source);

        // Data 面板：建立捷徑參照，來源不動
        string dataFile = Path.Combine(source, "拖入Data.txt");
        File.WriteAllText(dataFile, "Data 來源內容", Utf8);
        byte[] hash = SHA256.HashData(File.ReadAllBytes(dataFile));
        dynamic data = FrameDataManager.CreateNewFrame("拖放 Data", "Data", 280, 260, width: 360, height: 240);
        string dataId = data.Id.ToString();
        NonActivatingWindow dataWin = await Open(Live(dataId), checker);
        var accepted = Drop(dataWin, dataFile);
        await Task.Delay(200);
        // 對照組：成功拖入時效果不是 None，證明下方「舊版面板效果為 None」的檢查有意義。
        Check(accepted.Effects != DragDropEffects.None, "拖放：成功拖入時拖放效果不是 None（對照組）");
        var items = (JArray)Saved(dataId)["Items"]!;
        Check(items.Count == 1 && File.Exists(dataFile) && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(dataFile))),
            "拖放：拖入 Data 面板建立一筆項目，來源檔案不動、內容不變");
        WindowOf(dataId).Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == dataId);

        // 收納面板：預設是搬移
        string fenceDir = Path.Combine(root, "拖放 收納夾");
        Directory.CreateDirectory(fenceDir);
        dynamic fence = FrameDataManager.CreateNewFrame("拖放 收納", "Portal", 300, 280, width: 360, height: 240,
            portalPath: fenceDir, isManagedStorage: true, storageRootPath: root);
        string fenceId = fence.Id.ToString();
        FrameDataManager.SaveFrameData();
        string moveFile = Path.Combine(source, "拖入收納.txt");
        File.WriteAllText(moveFile, "收納來源內容", Utf8);
        NonActivatingWindow fenceWin = await Open(Live(fenceId), checker);
        var timer = new DispatcherTimer();
        CloseDialogsSoon(timer);
        Drop(fenceWin, moveFile);
        await Task.Delay(300);
        timer.Stop();
        string moved = Path.Combine(fenceDir, "拖入收納.txt");
        Check(!File.Exists(moveFile) && File.Exists(moved) && File.ReadAllText(moved, Encoding.UTF8) == "收納來源內容",
            "拖放：拖入收納面板預設為搬移，檔案完整移入收納夾");
        fenceWin.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == fenceId);

        // 舊版資料夾面板（非收納）：拒絕拖入
        string legacyDir = Path.Combine(root, "拖放 舊版資料夾");
        Directory.CreateDirectory(legacyDir);
        dynamic legacy = FrameDataManager.CreateNewFrame("拖放 舊版", "Portal", 320, 300, width: 360, height: 240,
            portalPath: legacyDir, isManagedStorage: false);
        string legacyId = legacy.Id.ToString();
        FrameDataManager.SaveFrameData();
        string keepFile = Path.Combine(source, "不該被收.txt");
        File.WriteAllText(keepFile, "保留", Utf8);
        NonActivatingWindow legacyWin = await Open(Live(legacyId), checker);
        var rejected = Drop(legacyWin, keepFile);
        await Task.Delay(200);
        Check(File.Exists(keepFile) && !File.Exists(Path.Combine(legacyDir, "不該被收.txt")),
            "拖放：舊版資料夾面板拒絕拖入，來源不動也不複製");
        // 介面層要明確回報「不接受」，不能只靠服務層的第二道防線。
        Check(rejected.Effects == DragDropEffects.None, "拖放：舊版資料夾面板在介面層就明確拒絕（拖放效果為 None）");
        legacyWin.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == legacyId);
        FrameDataManager.SaveFrameData();
    }

    // ---------- 怪現象 3：調整至最適大小須存檔（含剛從檔案讀入的面板） ----------
    private static async Task FitToContentChecks(TargetChecker checker, string root)
    {
        string source = Path.Combine(root, "最適大小 來源");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "最適大小.txt");
        File.WriteAllText(file, "合成", Utf8);
        dynamic created = FrameDataManager.CreateNewFrame("最適大小驗證", "Data", 360, 240, width: 360, height: 520);
        string id = created.Id.ToString();
        Framemanager.AddItemToDataFrame(Live(id), file);
        FrameDataManager.SaveFrameData();
        FrameDataManager.LoadFrameData(checker); // 重新讀檔：模擬使用者重開程式後的面板資料
        NonActivatingWindow win = await Open(Live(id), checker);
        var menu = win.ContextMenu!;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        var fit = menu.Items.OfType<MenuItem>().First(m => TextOf(m.Header).Trim() == "調整至最適大小");
        fit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, fit));
        await Task.Delay(500);
        double saved = (double)Saved(id)["Height"]!;
        Check(saved < 520 && Math.Abs(saved - win.Height) < 1,
            $"調整至最適大小會把新高度寫回 frames.json（剛讀檔的面板也一樣；存檔 {saved}、視窗 {win.Height}）");
        win.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }

    // ---------- 重設自訂外觀（會跳出完成訊息，由計時器自動關閉） ----------
    private static async Task ResetCustomizationChecks()
    {
        dynamic created = FrameDataManager.CreateNewFrame("重設驗證", "Data", 340, 320, width: 300, height: 200);
        string id = created.Id.ToString();
        dynamic live = Live(id);
        live["CustomColor"] = "Red";
        live["IconSize"] = "Large";
        live["TitleTextSize"] = "Large";
        live["GrayscaleIcons"] = "true";
        live["FrameBorderThickness"] = 5;
        live["未來欄位"] = "保留";
        FrameDataManager.SaveFrameData();
        var timer = new DispatcherTimer();
        CloseDialogsSoon(timer);
        Framemanager.ResetAllCustomizations();
        await Task.Delay(300);
        timer.Stop();
        var s = Saved(id);
        Check(s["CustomColor"]?.Type == JTokenType.Null && s["IconSize"]?.ToString() == "Medium" && s["TitleTextSize"]?.ToString() == "Medium"
            && s["GrayscaleIcons"]?.ToString() == "false" && (int)s["FrameBorderThickness"]! == 2 && (int)s["IconSpacing"]! == 5,
            "特徵化：重設自訂外觀把顏色、大小、灰階、外框、間距回到預設");
        Check(s["Title"]?.ToString() == "重設驗證" && s["未來欄位"]?.ToString() == "保留" && (double)s["X"]! == 340,
            "相容：重設外觀不動標題、位置與未知欄位");
        foreach (var w in Application.Current.Windows.OfType<NonActivatingWindow>().Where(w => w.Tag?.ToString() == id).ToList()) w.Close();
        FrameDataManager.FrameData.RemoveAll(f => f.Id?.ToString() == id);
        FrameDataManager.SaveFrameData();
    }
}
