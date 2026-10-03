using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Desktop_Frames.Localization;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames.FarmFences
{
    internal static class FarmFenceHost
    {
        private const string PanelPrefix = "panel:";
        private static FenceManager? _manager;
        internal static Func<string, FenceManager> ManagerFactory { get; set; } = path => new FenceManager(path);
        private static DateTime _lastWarning = DateTime.MinValue;
        private static string? _lastWarningMessage;
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out DesktopInterop.RECT bounds);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        private static string StatePath => Path.Combine(ProfileManager.CurrentProfileDir, "farm-fences.json");
        internal static string PanelId(string id) => PanelPrefix + id;

        public static bool IsPanel(string id)
        {
            if (_manager?.Fences.Any(f => f.FenceId == PanelId(id)) == true) return true;
            if (!File.Exists(StatePath)) return false;
            try { return JsonSerializer.Deserialize<FenceStateData[]>(File.ReadAllText(StatePath, System.Text.Encoding.UTF8))?.Any(f => f.Id == PanelId(id)) == true; }
            catch { return false; } // 原設定讀取失敗仍由共用核心提示與保護，不接管舊面板。
        }

        public static void Start()
        {
            if (_manager != null) return;
            FenceText.Resource = key => Strings.Get(key);
            FenceAppearance.Resolve = Appearance;
            _manager = ManagerFactory(StatePath);
            _manager.LogMessage += message => LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, message);
            _manager.Warning += Warn;
            _manager.StateSaved += states =>
            {
                bool changed = false;
                double scale = Math.Max(1, GetDpiForSystem()) / 96.0;
                foreach (var state in states.Where(s => s.Id.StartsWith(PanelPrefix, StringComparison.Ordinal)))
                {
                    object? model = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == state.Id.Substring(PanelPrefix.Length));
                    if (model == null) continue;
                    void Set(string key, object value)
                    {
                        if (model is JObject json) json[key] = JToken.FromObject(value);
                        else if (model is IDictionary<string, object> fields) fields[key] = value;
                    }
                    // 保留舊欄位與 DIP 語意；歸屬只存在原生核心資料，Items 不建立影子入口。
                    Set("Title", state.Title); Set("X", state.Left / scale); Set("Y", state.Top / scale);
                    Set("Width", state.Width / scale); Set("Height", state.Height / scale);
                    changed = true;
                }
                if (changed) FrameDataManager.SaveFrameData();
            };
            _manager.FenceCancelled += id =>
            {
                if (!id.StartsWith(PanelPrefix, StringComparison.Ordinal)) return;
                var model = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == id.Substring(PanelPrefix.Length));
                if (model != null) { FrameDataManager.FrameData.Remove(model); FrameDataManager.SaveFrameData(); }
            };
            _manager.Initialize();
        }

        private static void Warn(string message)
        {
            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, message);
            if (_lastWarningMessage == message && (DateTime.UtcNow - _lastWarning).TotalSeconds < 30) return;
            _lastWarning = DateTime.UtcNow; _lastWarningMessage = message;
            TrayManager.Instance?.ShowFarmFenceWarning(message);
        }

        public static bool CanAdopt(JObject model) => model["ItemsType"]?.ToString() == "Data" &&
            (model["Items"] == null || model["Items"] is JArray { Count: 0 }) &&
            (model["Tabs"] == null || model["Tabs"] is JArray { Count: 0 }) &&
            !string.Equals(model["TabsEnabled"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

        public static async void Adopt(object frame, Window? oldWindow = null, bool newlyCreated = false, Rect? drawnBounds = null)
        {
            bool success = false;
            try
            {
                var model = JObject.FromObject(frame);
                if (!CanAdopt(model)) { Warn(Strings.Get("NativePanelNeedsEmpty")); return; }
                Start();
                var manager = _manager!;
                await manager.Ready;
                Rect? bounds = null;
                if (oldWindow != null)
                {
                    if (!GetWindowRect(new WindowInteropHelper(oldWindow).Handle, out var rectangle))
                        throw new InvalidOperationException("無法取得原面板位置，已保留原面板。");
                    bounds = new Rect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
                }
                else if (drawnBounds is Rect drawn)
                {
                    double scale = Math.Max(1, GetDpiForSystem()) / 96.0;
                    bounds = new Rect(drawn.X * scale, drawn.Y * scale, drawn.Width * scale, drawn.Height * scale);
                }
                string id = PanelId(model["Id"]!.ToString());
                string title = model["Title"]!.ToString();
                if (bounds is Rect physical)
                    success = await manager.BindPanelAsync(id, title, physical);
                else
                {
                    await manager.CreateNewFenceAsync(title, id);
                    success = manager.Fences.Any(f => f.FenceId == id);
                }
                if (success) oldWindow?.Close();
            }
            catch (Exception ex) { Warn(ex.Message); }
            finally
            {
                if (!success && newlyCreated)
                {
                    FrameDataManager.FrameData.Remove(frame);
                    FrameDataManager.SaveFrameData();
                }
            }
        }

        private static FenceAppearance Appearance(string id)
        {
            var frame = id.StartsWith(PanelPrefix, StringComparison.Ordinal) ? FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == id.Substring(PanelPrefix.Length)) : null;
            var model = frame == null ? new JObject() : JObject.FromObject(frame);
            Color ReadColor(string key, Color fallback)
            {
                var name = model[key]?.ToString();
                try { return string.IsNullOrWhiteSpace(name) ? fallback : Utility.GetColorFromName(name); } catch { return fallback; }
            }
            return new FenceAppearance {
                BorderColor = ReadColor("FrameBorderColor", Colors.Gray), BorderWidth = Math.Clamp((double?)model["FrameBorderThickness"] ?? 2, 1, 6),
                CornerRadius = SettingsManager.FramesWithNoRoundCorners ? 0 : 6, FontFamily = SettingsManager.GlobalFontFamily,
                TitleSize = model["TitleTextSize"]?.ToString() switch { "Small" => 10, "Large" => 16, _ => 12 },
                Bold = string.Equals(model["BoldTitleText"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
                TitleColor = ReadColor("TitleTextColor", Colors.White),
                MenuSymbol = SettingsManager.MenuIcon switch { 1 => "☰", 2 => "≣", 3 => "𓃑", _ => "♥" }
            };
        }

        public static void Create() => Framemanager.CreateNativePanel();
        public static void Stop() { _manager?.Stop(); _manager = null; FenceAppearance.Resolve = null; }
    }
}
