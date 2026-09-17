using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// 在目前使用中的分區清單之間移動圖示記錄；不處理實體檔案。
    /// </summary>
    internal static class FrameItemTransfer
    {
        internal static (int SourceRemoved, int TargetDuplicatesRemoved, bool AddedToTarget) Move(
            JArray source, JArray target, JToken selected, string filename, int insertIndex)
        {
            if (source == null || target == null || selected == null ||
                ReferenceEquals(source, target) || !ReferenceEquals(selected.Parent, source) ||
                string.IsNullOrWhiteSpace(filename) ||
                !string.Equals(selected["Filename"]?.ToString(), filename, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("無法確認目前使用中的來源與目標圖示清單。");
            }

            // 先保留選取項目的內容，移除來源端同一路徑的歷史殘留記錄。
            JToken itemToInsert = selected.DeepClone();
            int sourceRemoved = 0;
            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (SameFilename(source[i], filename))
                {
                    source.RemoveAt(i);
                    sourceRemoved++;
                }
            }

            // 目標已存在時保留既有項目；僅清除同一路徑的多餘記錄。
            var targetMatches = target.Where(item => SameFilename(item, filename)).ToList();
            int targetDuplicatesRemoved = 0;
            for (int i = 1; i < targetMatches.Count; i++)
            {
                target.Remove(targetMatches[i]);
                targetDuplicatesRemoved++;
            }

            bool addedToTarget = targetMatches.Count == 0;
            if (addedToTarget)
            {
                target.Insert(Math.Max(0, Math.Min(insertIndex, target.Count)), itemToInsert);
            }

            for (int i = 0; i < source.Count; i++) source[i]["DisplayOrder"] = i;
            for (int i = 0; i < target.Count; i++) target[i]["DisplayOrder"] = i;

            return (sourceRemoved, targetDuplicatesRemoved, addedToTarget);
        }

        private static bool SameFilename(JToken item, string filename) =>
            string.Equals(item?["Filename"]?.ToString(), filename, StringComparison.OrdinalIgnoreCase);
    }
}
