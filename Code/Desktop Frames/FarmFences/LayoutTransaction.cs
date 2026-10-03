using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Desktop_Frames.FarmFences
{
    public static class LayoutTransaction
    {
        // 同步工作跑在背景執行緒；Stop 等它完成回復後才保存，避免退出途中保存半筆交易。
        public static bool Apply(IReadOnlyDictionary<string, Point> requested, IReadOnlyDictionary<string, Point> original,
            Func<string, Point, bool> move, CancellationToken cancellation, out bool restored, bool forceMoves = false, Func<bool>? verify = null)
        {
            restored = true;
            try
            {
                foreach (var pair in requested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!forceMoves && original.TryGetValue(pair.Key, out var old) && old == pair.Value) continue;
                    if (!move(pair.Key, pair.Value)) throw new InvalidOperationException("Shell 拒絕要求位置");
                }
                cancellation.ThrowIfCancellationRequested();
                if (verify?.Invoke() == false) throw new InvalidOperationException("整筆位置讀回不符");
                cancellation.ThrowIfCancellationRequested();
                return true;
            }
            catch (Exception)
            {
                // 跨框拖曳失敗時也回到按下滑鼠前，不能保留 Explorer 的半筆 Drop。
                foreach (var pair in original.Reverse())
                    try { restored &= move(pair.Key, pair.Value); } catch (Exception) { restored = false; }
                return false;
            }
        }
    }
}
