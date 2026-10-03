using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Desktop_Frames.FarmFences
{
    // 僅計算實體像素；不操作 Shell、檔案或歸屬。正式程式與沙盒共用。
    public sealed record LayoutFence(string Id, Rect Bounds, Thickness Insets)
    {
        public Rect Content => new(Bounds.X + Insets.Left, Bounds.Y + Insets.Top,
            Math.Max(0, Bounds.Width - Insets.Left - Insets.Right),
            Math.Max(0, Bounds.Height - Insets.Top - Insets.Bottom));
    }

    public sealed class LayoutPlan
    {
        public Dictionary<string, Rect> Fences { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Point> Positions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class FenceLayout
    {
        private const double Gap = 8;
        public static bool Overlaps(Rect a, Rect b) => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

        public static bool HasOverlap(IReadOnlyList<LayoutFence> fences) => fences.SelectMany((f, index) =>
            fences.Skip(index + 1).Select(g => Overlaps(f.Bounds, g.Bounds))).Any(b => b);

        public static string? DropOwner(Rect item, IReadOnlyList<LayoutFence> fences, string? previous)
        {
            if (HasOverlap(fences)) throw new InvalidOperationException("FarmOverlap");
            var center = new Point(item.Left + item.Width / 2, item.Top + item.Height / 2);
            var target = fences.SingleOrDefault(f => f.Content.Contains(center));
            if (target != null) return target.Id;
            return fences.Any(f => f.Id == previous && Overlaps(f.Bounds, item)) ? previous : null;
        }

        // 線性掃過外框四邊，找出首次碰撞；即使滑鼠跳過整個障礙也不能穿越。
        public static Rect Constrain(Rect start, Rect requested, IReadOnlyList<Rect> obstacles, IReadOnlyList<Rect> workAreas)
        {
            var area = workAreas.FirstOrDefault(a => a.Contains(start));
            if (area.IsEmpty || !area.Contains(start)) return start;
            double limit = 1;
            void Boundary(double initial, double final)
            {
                if (final < 0 && final < initial) limit = Math.Min(limit, initial / (initial - final));
            }
            Boundary(start.Left - area.Left, requested.Left - area.Left);
            Boundary(area.Right - start.Right, area.Right - requested.Right);
            Boundary(start.Top - area.Top, requested.Top - area.Top);
            Boundary(area.Bottom - start.Bottom, area.Bottom - requested.Bottom);
            foreach (var obstacle in obstacles)
            {
                if (Overlaps(start, obstacle)) return start;
                double entry = 0, exit = 1;
                bool possible = true;
                void Inside(double initial, double final)
                {
                    double velocity = final - initial;
                    if (velocity == 0) { if (initial <= 0) possible = false; return; }
                    double crossing = -initial / velocity;
                    if (velocity > 0) entry = Math.Max(entry, crossing);
                    else exit = Math.Min(exit, crossing);
                }
                Inside(obstacle.Right - start.Left, obstacle.Right - requested.Left);
                Inside(start.Right - obstacle.Left, requested.Right - obstacle.Left);
                Inside(obstacle.Bottom - start.Top, obstacle.Bottom - requested.Top);
                Inside(start.Bottom - obstacle.Top, requested.Bottom - obstacle.Top);
                if (possible && entry < exit && exit > 0 && entry < 1) limit = Math.Min(limit, Math.Max(0, entry));
            }
            // 沿路徑向原點取整，避免 Win32 四捨五入後跨過碰撞邊界。
            double distance = new[] { Math.Abs(requested.Left - start.Left), Math.Abs(requested.Top - start.Top),
                Math.Abs(requested.Right - start.Right), Math.Abs(requested.Bottom - start.Bottom) }.Max();
            if (distance > 0 && limit < 1) limit = Math.Max(0, Math.Floor(limit * distance) / distance);
            return new Rect(start.X + (requested.X - start.X) * limit, start.Y + (requested.Y - start.Y) * limit,
                start.Width + (requested.Width - start.Width) * limit, start.Height + (requested.Height - start.Height) * limit);
        }

        public static LayoutPlan Plan(IReadOnlyList<LayoutFence> fences, IReadOnlyList<DesktopIconItem> icons,
            IReadOnlyDictionary<string, string> assigned, IReadOnlyList<Rect> workAreas, IReadOnlySet<string>? growable = null)
        {
            if (workAreas.Count == 0 || HasOverlap(fences)) throw new InvalidOperationException("FarmOverlap");
            if (icons.Any(i => i.BoundsOnScreen.IsEmpty || i.BoundsOnScreen.Width <= 0 || i.BoundsOnScreen.Height <= 0))
                throw new InvalidOperationException("FarmOperationFailed");
            if (assigned.Values.Any(id => !fences.Any(f => f.Id == id))) throw new InvalidOperationException("FarmOperationFailed");
            var result = new LayoutPlan();
            var planned = fences.ToList();
            for (int index = 0; index < planned.Count; index++)
            {
                var fence = planned[index];
                var members = icons.Where(i => assigned.TryGetValue(i.ResolvedPath, out var id) && id == fence.Id)
                    .OrderBy(i => i.ScreenPoint.Y).ThenBy(i => i.ScreenPoint.X).ThenBy(i => i.ResolvedPath, StringComparer.OrdinalIgnoreCase).ToArray();
                var size = CellSize(members);
                double cellWidth = size.Width, cellHeight = size.Height;
                int columns = (int)Math.Floor(fence.Content.Width / cellWidth);
                if (members.Length > 0 && columns < 1) throw new InvalidOperationException("FarmNoSpace");
                int rows = members.Length == 0 ? 0 : (members.Length + columns - 1) / columns;
                double height = Math.Max(fence.Bounds.Height, rows * cellHeight + fence.Insets.Top + fence.Insets.Bottom);
                if (height > fence.Bounds.Height && growable != null && !growable.Contains(fence.Id)) throw new InvalidOperationException("FarmNoSpace");
                fence = fence with { Bounds = new Rect(fence.Bounds.X, fence.Bounds.Y, fence.Bounds.Width, height) };
                if (!workAreas.Any(a => a.Contains(fence.Bounds)) || planned.Where((_, j) => j != index).Any(f => Overlaps(f.Bounds, fence.Bounds)))
                    throw new InvalidOperationException("FarmNoSpace");
                planned[index] = fence;
                result.Fences.Add(fence.Id, fence.Bounds);
                for (int n = 0; n < members.Length; n++)
                    Place(result, members[n], fence.Content.Left + (n % columns) * cellWidth,
                        fence.Content.Top + (n / columns) * cellHeight);
            }
            // 未分組項目只排列於框外；絕不依新位置推導歸屬。
            var outside = icons.Where(i => !assigned.ContainsKey(i.ResolvedPath)).OrderBy(i => i.ScreenPoint.X)
                .ThenBy(i => i.ScreenPoint.Y).ThenBy(i => i.ResolvedPath, StringComparer.OrdinalIgnoreCase).ToArray();
            var outsideSize = CellSize(outside);
            int next = 0;
            foreach (var area in workAreas.OrderBy(a => a.Left).ThenBy(a => a.Top))
            {
                for (double x = area.Left; x + outsideSize.Width <= area.Right && next < outside.Length; x += outsideSize.Width)
                    for (double y = area.Top; y + outsideSize.Height <= area.Bottom && next < outside.Length; y += outsideSize.Height)
                    {
                        var cell = new Rect(x, y, outsideSize.Width, outsideSize.Height);
                        if (planned.Any(f => Overlaps(f.Bounds, cell))) continue;
                        Place(result, outside[next++], x, y);
                    }
            }
            if (next != outside.Length) throw new InvalidOperationException("FarmNoSpace");
            return result;
        }

        private static void Place(LayoutPlan plan, DesktopIconItem icon, double left, double top)
        {
            // Shell anchor 不是圖示／文字範圍的左上角；保留真實偏移。
            plan.Positions.Add(icon.ResolvedPath, new Point(Math.Round(left - icon.BoundsOnScreen.Left + icon.ScreenPoint.X),
                Math.Round(top - icon.BoundsOnScreen.Top + icon.ScreenPoint.Y)));
        }

        private static Size CellSize(IReadOnlyList<DesktopIconItem> icons) => icons.Count == 0 ? new Size(1, 1) : new Size(
            Math.Ceiling(icons.Max(i => Math.Max(i.BoundsOnScreen.Width + Gap, i.Spacing.Width))),
            Math.Ceiling(icons.Max(i => Math.Max(i.BoundsOnScreen.Height + Gap, i.Spacing.Height))));
    }
}
