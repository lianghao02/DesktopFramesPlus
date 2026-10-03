using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Desktop_Frames.FarmFences;

namespace FarmFenceSandbox
{
    internal static class LayoutRegressionTests
    {
        public static void Run(Action<string> log)
        {
            var work = new[] { new Rect(0, 0, 800, 600) };
            var fence = new LayoutFence("A", new Rect(120, 60, 240, 160), new Thickness(3, 35, 3, 3));
            var icons = Enumerable.Range(0, 8).Select(n => Icon("item" + n, 20 + n * 4, 20 + n * 3)).ToList();
            var assigned = icons.Take(7).ToDictionary(i => i.ResolvedPath, _ => "A", StringComparer.OrdinalIgnoreCase);
            var plan = FenceLayout.Plan(new[] { fence }, icons, assigned, work);
            Require(plan.Fences["A"].Width == 240 && plan.Fences["A"].Height > 160 && plan.Fences["A"].Top == 60, "增加列只向下增高");
            var enlarged = fence with { Bounds = plan.Fences["A"] };
            var rectangles = icons.Select(i => Positioned(i, plan.Positions[i.ResolvedPath])).ToArray();
            for (int n = 0; n < 7; n++) Require(enlarged.Content.Contains(rectangles[n]), "圖示與文字完整容納");
            Require(!FenceLayout.Overlaps(rectangles[7], enlarged.Bounds) && !assigned.ContainsKey("item7"), "框外圖示保持未分組");
            Require(rectangles.SelectMany((a, index) => rectangles.Skip(index + 1).Select(b => FenceLayout.Overlaps(a, b))).All(b => !b), "所有圖示無重疊");
            var smaller = FenceLayout.Plan(new[] { enlarged }, icons, new Dictionary<string, string> { ["item0"] = "A" }, work);
            Require(smaller.Fences["A"].Height == enlarged.Bounds.Height, "移出不自動縮小");
            log("PASS：框內排隊、標籤與 anchor 偏移、向下增高、不自動縮小、框外未分組與無重疊。");

            var obstacle = new LayoutFence("B", new Rect(120, 250, 240, 160), fence.Insets);
            Rejected(() => FenceLayout.Plan(new[] { fence, obstacle }, icons, assigned, work), "增高碰到其他柵欄取消");
            Rejected(() => FenceLayout.Plan(new[] { fence with { Bounds = new Rect(120, 450, 240, 150) } }, icons, assigned, work), "工作列／螢幕邊界取消");
            Rejected(() => FenceLayout.Plan(new[] { fence with { Bounds = new Rect(0, 0, 800, 600) } }, icons, new Dictionary<string, string>(), work), "框外空間不足取消");
            Require(assigned.Count == 7 && icons[0].ScreenPoint == new Point(20, 20), "規劃失敗不變更歸屬或原位置");
            Rejected(() => FenceLayout.Plan(new[] { fence }, icons, assigned, work, new HashSet<string>()), "未操作的柵欄不得被自動增高");
            Rejected(() => FenceLayout.Plan(new[] { fence, fence with { Id = "B" } }, icons, assigned, work), "既有重疊拒絕排列");
            log("PASS：增高碰撞、螢幕邊界、框外容量不足、既有重疊與預檢不修改原資料。");

            var start = new Rect(10, 10, 100, 100);
            var blocked = new Rect(200, 0, 80, 500);
            Require(FenceLayout.Constrain(start, new Rect(500, 10, 100, 100), new[] { blocked }, work).Right <= 200, "快速移動不能穿過其他柵欄");
            Require(FenceLayout.Constrain(start, new Rect(10, 10, 500, 100), new[] { blocked }, work).Right <= 200, "向右縮放碰到柵欄停止");
            Require(FenceLayout.Constrain(start, new Rect(10, 10, 100, 800), Array.Empty<Rect>(), work).Bottom <= 600, "向下縮放不碰工作列");
            var secondary = new[] { new Rect(-1000, -200, 1000, 700), work[0] };
            var negative = new LayoutFence("A", new Rect(-900, -100, 240, 160), fence.Insets);
            var multi = FenceLayout.Plan(new[] { negative }, icons, assigned, secondary);
            Require(secondary[0].Contains(multi.Fences["A"]), "負座標螢幕以實體工作區排列");
            log("PASS：移動／縮放掃過碰撞、工作區邊界、多螢幕負座標幾何。");

            var targets = new[] { fence, obstacle };
            Require(FenceLayout.DropOwner(new Rect(130, 110, 64, 64), targets, null) == "A", "手動拖入");
            Require(FenceLayout.DropOwner(new Rect(130, 300, 64, 64), targets, "A") == "B", "跨框優先");
            Require(FenceLayout.DropOwner(new Rect(350, 110, 64, 64), targets, "A") == "A", "靠邊碰到原框仍保留");
            Require(FenceLayout.DropOwner(new Rect(420, 110, 64, 64), targets, "A") == null, "完整拖出解除");
            log("PASS：拖入、跨框、靠邊保留、完整拖出；排列保持輸入歸屬。");

            var originals = new Dictionary<string, Point> { ["a"] = new(10, 10), ["b"] = new(20, 20), ["c"] = new(30, 30) };
            var requested = originals.ToDictionary(p => p.Key, p => new Point(p.Value.X + 50, p.Value.Y + 50));
            var actual = new Dictionary<string, Point>(originals);
            bool Move(string key, Point position) { actual[key] = position; return true; }
            Require(LayoutTransaction.Apply(requested, originals, Move, CancellationToken.None, out var restored) && restored && actual["a"] == requested["a"], "成功移動提交");
            actual = new Dictionary<string, Point>(originals);
            bool FailSecond(string key, Point position) { actual[key] = position; return !(key == "b" && position == requested["b"]); }
            Require(!LayoutTransaction.Apply(requested, originals, FailSecond, CancellationToken.None, out restored) && restored && originals.All(p => actual[p.Key] == p.Value), "第二筆拒絕回復全部");
            using var cancellation = new CancellationTokenSource();
            bool CancelAfterFirst(string key, Point position) { actual[key] = position; if (position == requested["a"]) cancellation.Cancel(); return true; }
            Require(!LayoutTransaction.Apply(requested, originals, CancelAfterFirst, cancellation.Token, out restored) && restored && originals.All(p => actual[p.Key] == p.Value), "退出途中取消仍回復全部");
            bool RejectRollback(string key, Point position) => false;
            Require(!LayoutTransaction.Apply(requested, originals, RejectRollback, CancellationToken.None, out restored) && !restored, "回復拒絕不可宣稱成功");
            Require(!LayoutTransaction.Apply(requested, originals, Move, CancellationToken.None, out restored, verify: () => false) && restored && originals.All(p => actual[p.Key] == p.Value), "整批讀回拒絕回復全部");
            log("PASS：位置交易成功、部分失敗整筆回復、退出取消回復、回復失敗可辨識。");
        }

        private static DesktopIconItem Icon(string key, double x, double y) => new() { ResolvedPath = key,
            ScreenPoint = new Point(x, y), BoundsOnScreen = new Rect(x + 6, y + 4, 64, 64), Spacing = new Size(80, 80) };
        private static Rect Positioned(DesktopIconItem icon, Point position)
        {
            var rect = icon.BoundsOnScreen;
            rect.Offset(position.X - icon.ScreenPoint.X, position.Y - icon.ScreenPoint.Y);
            return rect;
        }
        private static void Rejected(Action action, string reason)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, reason);
        }
        private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    }
}
