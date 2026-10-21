using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using Desktop_Frames;
using Newtonsoft.Json.Linq;

/// <summary>
/// FrameManager 重構前的特徵化測試（Phase 1：純函式）。
/// 以反射呼叫私有靜態方法，鎖住「目前的實際行為」，讓日後搬移或抽取程式碼時，行為一變就會被抓到。
/// 標為「現狀」的檢查是刻意記錄的既有行為（不代表理想），若要改變需另案評估並同步更新此處。
/// 全部使用虛構資料，只寫入隔離驗收目錄。
/// </summary>
internal static class FrameManagerHelperChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS：" + message);
    }

    private static T Call<T>(string name, params object?[] args)
    {
        var method = typeof(Framemanager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"找不到 Framemanager.{name}；若方法已改名或搬移，請同步更新特徵化測試");
        return (T)method.Invoke(null, args)!;
    }

    public static void Run()
    {
        WebUrlChecks();
        SpecialPathChecks();
        UncAndNetworkChecks();
        UrlFileChecks();
        DropDataChecks();
        DisplayOrderChecks();
    }

    private static void WebUrlChecks()
    {
        Check(Call<bool>("IsWebUrl", "https://example.test/a?b=1") && Call<bool>("IsWebUrl", "http://example.test")
            && Call<bool>("IsWebUrl", "ftp://example.test/f") && Call<bool>("IsWebUrl", "mailto:someone@example.test"),
            "特徵化：http、https、ftp、mailto 視為網址");
        Check(!Call<bool>("IsWebUrl", "steam://run/1") && !Call<bool>("IsWebUrl", @"C:\資料\a.txt")
            && !Call<bool>("IsWebUrl", "file:///C:/a.txt") && !Call<bool>("IsWebUrl", "") && !Call<bool>("IsWebUrl", (string?)null),
            "特徵化：應用程式協定、本機路徑、file 與空值不視為網址");

        Check(Call<bool>("IsValidWebUrl", "https://example.test") && Call<bool>("IsValidWebUrl", "steam://run/1")
            && Call<bool>("IsValidWebUrl", "spotify:track:abc"),
            "特徵化：拖入網址接受 http(s) 與應用程式協定（steam、spotify）");
        Check(!Call<bool>("IsValidWebUrl", "file:///C:/a.txt") && !Call<bool>("IsValidWebUrl", "不是網址")
            && !Call<bool>("IsValidWebUrl", "  ") && !Call<bool>("IsValidWebUrl", (string?)null),
            "特徵化：拖入網址拒絕 file、相對文字與空白");
    }

    private static void SpecialPathChecks()
    {
        Check(Call<bool>("IsSpecialWindowsPath", "shell:startup") && Call<bool>("IsSpecialWindowsPath", "ms-settings:display")
            && Call<bool>("IsSpecialWindowsPath", "steam://run/1") && Call<bool>("IsSpecialWindowsPath", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")
            && Call<bool>("IsSpecialWindowsPath", "control.exe"),
            "特徵化：shell:、ms-、應用程式協定、CLSID 與 control 視為特殊路徑");
        Check(!Call<bool>("IsSpecialWindowsPath", "https://example.test") && !Call<bool>("IsSpecialWindowsPath", "mailto:a@example.test")
            && !Call<bool>("IsSpecialWindowsPath", @"C:\資料\a.txt") && !Call<bool>("IsSpecialWindowsPath", ""),
            "特徵化：網址、一般本機路徑與空值不視為特殊路徑");
        // 怪現象 1 已修正：只有控制台指令本身（可接參數）才算特殊路徑，以 control 開頭的一般檔名不算。
        Check(Call<bool>("IsSpecialWindowsPath", "control") && Call<bool>("IsSpecialWindowsPath", "control.exe")
            && Call<bool>("IsSpecialWindowsPath", "control /name Microsoft.Mouse") && Call<bool>("IsSpecialWindowsPath", "CONTROL.EXE desk.cpl"),
            "控制台指令（含參數）視為特殊路徑");
        Check(!Call<bool>("IsSpecialWindowsPath", "controller-說明.txt") && !Call<bool>("IsSpecialWindowsPath", "controlpanel-備忘.docx"),
            "以 control 開頭的一般檔名不視為特殊路徑");
    }

    private static void UncAndNetworkChecks()
    {
        Check(Call<bool>("IsUncRoot", @"\\伺服器") && Call<bool>("IsUncRoot", @"\\伺服器\"),
            "特徵化：UNC 伺服器根目錄判斷");
        Check(!Call<bool>("IsUncRoot", @"\\伺服器\分享") && !Call<bool>("IsUncRoot", @"C:\資料")
            && !Call<bool>("IsUncRoot", @"\\伺服器\C:\x") && !Call<bool>("IsUncRoot", "") && !Call<bool>("IsUncRoot", (string?)null),
            "特徵化：分享路徑、本機路徑與空值不是 UNC 根目錄");

        Check(Framemanager.IsNetworkPath(@"\\伺服器\分享\a.txt") && !Framemanager.IsNetworkPath(@"C:\資料\a.txt"),
            "特徵化：直接路徑以 \\\\ 開頭才視為網路路徑");
        string dir = Path.Combine(AppContext.BaseDirectory, "合成 網路判斷");
        Directory.CreateDirectory(dir);
        string localLnk = Path.Combine(dir, "本機.lnk");
        string uncLnk = Path.Combine(dir, "網路.lnk");
        File.WriteAllBytes(localLnk, Encoding.Unicode.GetBytes(@"C:\資料\a.txt"));
        File.WriteAllBytes(uncLnk, Encoding.Unicode.GetBytes(@"\\伺服器\分享\a.txt"));
        Check(!Framemanager.IsNetworkPath(localLnk) && Framemanager.IsNetworkPath(uncLnk),
            "特徵化：捷徑以內容掃描判斷是否指向網路路徑（避免網路逾時卡住）");
        Check(!Framemanager.IsNetworkPath(Path.Combine(dir, "不存在.lnk")),
            "特徵化：不存在的捷徑不視為網路路徑");
    }

    private static void UrlFileChecks()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "合成 網址檔");
        Directory.CreateDirectory(dir);
        string ok = Path.Combine(dir, "範例.url");
        File.WriteAllText(ok, "[InternetShortcut]\r\nurl=https://example.test/頁面 \r\nIconIndex=0\r\n", new UTF8Encoding(false));
        Check(Call<string>("ExtractUrlFromUrlFile", ok) == "https://example.test/頁面",
            "特徵化：.url 檔讀出 URL= 內容（不分大小寫、去除空白）");
        string none = Path.Combine(dir, "沒有網址.url");
        File.WriteAllText(none, "[InternetShortcut]\r\nIconIndex=0\r\n", new UTF8Encoding(false));
        Check(Call<string?>("ExtractUrlFromUrlFile", none) == null && Call<string?>("ExtractUrlFromUrlFile", Path.Combine(dir, "不存在.url")) == null,
            "特徵化：沒有 URL= 或檔案不存在時回傳 null");
    }

    private static void DropDataChecks()
    {
        string? Extract(IDataObject data) => Call<string?>("ExtractUrlFromDropData", data);

        var moz = new DataObject();
        moz.SetData("text/x-moz-url", "https://example.test/firefox\n頁面標題");
        Check(Extract(moz) == "https://example.test/firefox", "特徵化：Firefox 拖曳格式取第一行網址");

        var html = new DataObject();
        html.SetData(DataFormats.Html, "<a href=\"https://example.test/html\">連結</a>");
        Check(Extract(html) == "https://example.test/html", "特徵化：HTML 拖曳格式取 href");

        var text = new DataObject();
        text.SetData(DataFormats.Text, "  https://example.test/text  ");
        Check(Extract(text) == "https://example.test/text", "特徵化：純文字網址去除空白後採用");

        var plain = new DataObject();
        plain.SetData(DataFormats.Text, "這只是一般文字");
        Check(Extract(plain) == null, "特徵化：一般文字不會被當成網址");

        var url = new DataObject();
        url.SetData("UniformResourceLocator", new MemoryStream(Encoding.ASCII.GetBytes("https://example.test/browser\0")));
        Check(Extract(url) == "https://example.test/browser", "特徵化：瀏覽器 UniformResourceLocator 格式優先採用");
    }

    private static void DisplayOrderChecks()
    {
        Check(Call<int>("GetNextDisplayOrder", JObject.Parse("{\"Items\":[{\"DisplayOrder\":2},{\"DisplayOrder\":7},{}]}")) == 8,
            "特徵化：下一個排序值為目前最大值加一（缺值以 0 計）");
        Check(Call<int>("GetNextDisplayOrder", JObject.Parse("{\"Items\":[]}")) == 0
            && Call<int>("GetNextDisplayOrder", JObject.Parse("{}")) == 0,
            "特徵化：沒有項目時排序從 0 開始");
    }
}
