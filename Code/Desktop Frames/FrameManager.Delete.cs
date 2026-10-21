using Desktop_Frames.Localization;
using IWshRuntimeLibrary;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;
using Color = System.Drawing.Color;
using Desktop_Frames.Plugins; // --- NEW: Plugins Support ---

namespace Desktop_Frames
{
    // FrameManager 分檔：Delete（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        /// <summary>安全移除面板配置，保留既有資料防線。</summary>
        public static void DeleteFrameWithConfirmation(dynamic frame, Window parentWindow = null)
        {
            if (frame == null) return;

            if (Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
            {
                string frameTitle = frame.Title?.ToString() ?? "收納面板";
                string folderPath = frame.Path?.ToString() ?? string.Empty;
                int itemCount = 0;
                try
                {
                    if (Directory.Exists(folderPath))
                    {
                        itemCount = Directory.EnumerateFileSystemEntries(folderPath).Count();
                    }
                }
                catch { }

                string confirmMsg = $"要拆除收納面板「{frameTitle}」，並把裡面的 {itemCount} 個項目移回桌面嗎？\n\n（若桌面已有同名項目將自動加上編號，不會覆寫原檔；若有項目因開啟中或系統攔截無法移動，將安全保留柵欄與未完成檔案現場）";
                if (!MessageBoxesManager.ShowCustomYesNoMessageBox(confirmMsg, "拆除收納面板"))
                {
                    return;
                }

                Desktop_Frames.Services.DismantleReport report = Desktop_Frames.Services.StorageFenceService.DismantleToDesktop(frame);
                if (report.FolderDeleted)
                {
                    DeleteFrameConfiguration(frame, parentWindow);
                    Services.FenceInventoryManager.RefreshDesktopShell();
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"已拆除收納面板，{report.CompletedCount} 個項目已移回桌面。", "拆除完成");
                }
                else
                {
                    string frameIdStr = frame.Id?.ToString();
                    var portalEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameIdStr);
                    if (portalEntry.Value != null)
                    {
                        portalEntry.Value.TriggerSync(immediate: true);
                    }
                    Services.FenceInventoryManager.RefreshDesktopShell();

                    string errDetails = report.FolderDeletionReason;
                    if (report.FailedCount > 0 || report.ConflictCount > 0)
                    {
                        var issues = report.Results.Where(r => r.Status == Desktop_Frames.Services.DismantleItemStatus.Failed || r.Status == Desktop_Frames.Services.DismantleItemStatus.PartialBothExist).Take(5).Select(r => $"{Path.GetFileName(r.SourcePath)}: {r.ErrorMessage}");
                        errDetails += "\n" + string.Join("\n", issues);
                    }

                    string alertMsg = $"收納面板沒有完全清空，已停止拆除，原本的內容都保留：\n\n{errDetails}\n\n已移至桌面：{report.CompletedCount} 項\n未完成/需確認：{report.FailedCount + report.ConflictCount} 項\n\n請關閉可能開啟中的檔案或解除鎖定後，再執行一次拆除。";
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(alertMsg, "拆除未完成（現場已保留）");
                }
                return;
            }

            bool result = MessageBoxesManager.ShowCustomMessageBoxForm();
            if (result != true) return;

            DeleteFrameConfiguration(frame, parentWindow);
        }

        internal static void DeleteFrameConfiguration(dynamic frame, Window? parentWindow = null)
        {

            string frameIdStr = frame.Id?.ToString();
            string frameTitle = frame.Title?.ToString();

            // 刪除配置不匯出至桌面；使用者明確選取的匯出操作仍獨立保留。


            try
            {
                BackupManager.BackupDeletedFrame(frame);
            }
            catch { }

            // 徹底依據 ID 移除，防止 JObject 參照比對失敗導致殘留
            if (!string.IsNullOrEmpty(frameIdStr))
            {
                FrameDataManager.FrameData.RemoveAll(f =>
                {
                    try { return string.Equals(f.Id?.ToString(), frameIdStr, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                });
            }
            else
            {
                FrameDataManager.FrameData.Remove(frame);
            }

            // 清理 TextBlock 與 Portal 追蹤
            var heartEntry = _heartTextBlocks.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameIdStr);
            if (heartEntry.Key != null) _heartTextBlocks.Remove(heartEntry.Key);

            var targetPortal = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameIdStr);
            if (targetPortal.Value != null)
            {
                targetPortal.Value.Dispose();
                _portalFrames.Remove(targetPortal.Key);
            }

            // 立即落盤儲存 frames.json
            FrameDataManager.SaveFrameData();

            // 尋找並關閉對應視窗
            var windows = System.Windows.Application.Current?.Windows.OfType<NonActivatingWindow>();
            var targetWin = windows?.FirstOrDefault(w => string.Equals(w.Tag?.ToString(), frameIdStr, StringComparison.OrdinalIgnoreCase)) ?? (parentWindow as NonActivatingWindow);

            // 從系統列托盤的隱藏清單徹底清除
            TrayManager.RemoveHiddenFrame(targetWin, frameTitle);

            if (targetWin != null)
            {
                targetWin.Close();
            }

            UpdateAllHeartContextMenus();
        }
    }
}
