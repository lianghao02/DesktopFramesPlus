using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Desktop_Frames.FarmFences;

namespace FarmFenceSandbox
{
    // 僅沙盒 CLI 測試載入；正式程式不包含建立／清除測試資產的程式碼。
    internal static class NativeFixtureTests
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string first, IntPtr second);
        public static void Run(Action<string> log)
        {
            string desktop = DesktopInterop.GetActualDesktopDirectories().First();
            string prefix = "DFP_柵欄驗證_" + Guid.NewGuid().ToString("N");
            string stem = Path.Combine(desktop, prefix);
            string folder = stem + "_資料夾";
            var files = new[] { stem + ".txt", stem + ".lnk", stem + ".url" };
            var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "測試標記.txt"), "僅供農場柵欄原生驗證", new UTF8Encoding(false));
                File.WriteAllText(files[0], "繁體中文文件測試。原檔不得搬移或改寫。", new UTF8Encoding(false));
                File.WriteAllText(files[2], "[InternetShortcut]\r\nURL=https://example.com/\r\n", new UTF8Encoding(false));
                object shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                object? shortcutObject = null;
                try
                {
                    dynamic shell = shellObject;
                    shortcutObject = shell.CreateShortcut(files[1]);
                    dynamic shortcut = shortcutObject;
                    shortcut.TargetPath = files[0];
                    shortcut.Arguments = "";
                    shortcut.WorkingDirectory = desktop;
                    shortcut.Save();
                }
                finally
                {
                    if (shortcutObject != null) Marshal.ReleaseComObject(shortcutObject);
                    Marshal.ReleaseComObject(shellObject);
                }
                var hashes = files.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
                var attributes = files.ToDictionary(p => p, File.GetAttributes);
                var paths = files.Append(folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
                SHChangeNotify(0x8000000, 0x1005, desktop, IntPtr.Zero);
                DesktopIconItem[] items = Array.Empty<DesktopIconItem>();
                for (int retry = 0; retry < 30; retry++)
                {
                    Thread.Sleep(200);
                    var snapshot = DesktopInterop.GetAllDesktopIcons();
                    items = snapshot.Where(i => paths.Contains(i.ResolvedPath)).ToArray();
                    if (retry == 0 || retry == 29) log($"測試資產路徑={stem}，Shell 項目={snapshot.Count}，匹配={items.Length}");
                    if (items.Length == 4) break;
                }
                if (items.Length != 4) throw new InvalidOperationException("Shell 未完整列出四種測試項目");
                if (items.Any(i => i.BoundsOnScreen.IsEmpty)) throw new InvalidOperationException("測試項目缺少原生範圍");
                foreach (var item in items) positions.Add(item.ResolvedPath, item.ScreenPoint);
                foreach (var item in items)
                {
                    var target = new Point(item.ScreenPoint.X + item.BoundsOnScreen.Width, item.ScreenPoint.Y + 124);
                    bool accepted = DesktopInterop.MoveShellItem(item.ResolvedPath, target);
                    var current = DesktopInterop.GetAllDesktopIcons().Single(i => string.Equals(i.ResolvedPath, item.ResolvedPath, StringComparison.OrdinalIgnoreCase));
                    log($"原生移動 {Path.GetExtension(item.ResolvedPath)}: accepted={accepted}, before={item.ScreenPoint}, after={current.ScreenPoint}, bounds={current.BoundsOnScreen}");
                    if (!accepted) throw new InvalidOperationException("Shell 拒絕要求座標，需驗證自動排列／對齊格線限制");
                    if (!DesktopInterop.MoveShellItem(item.ResolvedPath, item.ScreenPoint)) throw new InvalidOperationException("測試圖示位置復原失敗");
                    if (hashes.Any(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))) != p.Value) ||
                        attributes.Any(p => File.GetAttributes(p.Key) != p.Value)) throw new InvalidOperationException("測試原檔內容或屬性發生變更");
                }
                // 在沒有使用者圖示的空白區驗證同一共用排列核心，不重排使用者桌面。
                var all = DesktopInterop.GetAllDesktopIcons();
                var others = all.Where(i => !paths.Contains(i.ResolvedPath)).ToArray();
                Rect? region = null;
                foreach (var area in DesktopInterop.GetWorkAreas())
                {
                    for (double x = area.Right - 640; x >= area.Left && region == null; x -= 160)
                        for (double y = area.Top; y + 600 <= area.Bottom; y += 120)
                        {
                            var candidate = new Rect(x, y, 640, 600);
                            if (others.Any(i => !i.BoundsOnScreen.IsEmpty && FenceLayout.Overlaps(candidate, i.BoundsOnScreen))) continue;
                            region = candidate; break;
                        }
                    if (region != null) break;
                }
                if (region == null) throw new InvalidOperationException("沒有足夠的空白區執行原生排列測試，未移動使用者圖示");
                var emptyArea = region.Value;
                var fence = new LayoutFence("fixture", new Rect(emptyArea.Left + 150, emptyArea.Top + 20, 360, 260), new Thickness(3, 35, 3, 3));
                var currentItems = all.Where(i => paths.Contains(i.ResolvedPath)).ToList();
                var owners = currentItems.Take(2).ToDictionary(i => i.ResolvedPath, _ => fence.Id, StringComparer.OrdinalIgnoreCase);
                var plan = FenceLayout.Plan(new[] { fence }, currentItems, owners, new[] { emptyArea });
                bool Verify()
                {
                    Thread.Sleep(100);
                    var actual = DesktopInterop.GetAllDesktopIcons();
                    return currentItems.All(i => actual.Count(a => a.ResolvedPath == i.ResolvedPath) == 1 &&
                        (actual.Single(a => a.ResolvedPath == i.ResolvedPath).ScreenPoint - plan.Positions[i.ResolvedPath]).Length <= 2) &&
                        others.All(i => actual.Single(a => a.ResolvedPath == i.ResolvedPath).ScreenPoint == i.ScreenPoint);
                }
                if (!LayoutTransaction.Apply(plan.Positions, positions, DesktopInterop.MoveShellItem, CancellationToken.None, out bool restored, verify: Verify))
                    throw new InvalidOperationException($"共用排列核心原生交易失敗，回復={restored}");
                var arranged = DesktopInterop.GetAllDesktopIcons().Where(i => paths.Contains(i.ResolvedPath)).ToArray();
                var plannedFence = fence with { Bounds = plan.Fences[fence.Id] };
                if (arranged.Any(i => owners.ContainsKey(i.ResolvedPath) ? !plannedFence.Content.Contains(i.BoundsOnScreen) :
                    FenceLayout.Overlaps(plannedFence.Bounds, i.BoundsOnScreen)) ||
                    hashes.Any(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))) != p.Value) ||
                    attributes.Any(p => File.GetAttributes(p.Key) != p.Value)) throw new InvalidOperationException("原生排列或原檔保全驗證失敗");
                log("PASS：共用核心框內／框外原生排列、整批位置讀回、零第二份入口、使用者圖示位置未變。");
                log("PASS：中文文件／資料夾／lnk／url 原生身分與座標讀回、原檔雜湊及屬性未變。");
            }
            finally
            {
                foreach (var pair in positions)
                {
                    try { DesktopInterop.MoveShellItem(pair.Key, pair.Value); }
                    catch (Exception ex) { log($"測試座標復原：{ex.Message}"); }
                }
                // 僅清除本次新建的精確路徑；不枚舉、不遞迴、不碰使用者原檔。
                foreach (var path in files) if (File.Exists(path)) File.Delete(path);
                string marker = Path.Combine(folder, "測試標記.txt");
                if (File.Exists(marker)) File.Delete(marker);
                if (Directory.Exists(folder)) Directory.Delete(folder, false);
                SHChangeNotify(0x8000000, 0x1005, desktop, IntPtr.Zero);
            }
        }
    }
}
