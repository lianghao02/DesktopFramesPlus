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
                string frameTitle = frame.Title?.ToString() ?? Strings.Get("DefaultFrameTitle");
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

                string confirmMsg = Strings.Get("MsgDismantleStorageConfirm", frameTitle, itemCount);
                if (!MessageBoxesManager.ShowCustomYesNoMessageBox(confirmMsg, Strings.Get("DlgDismantleStorageTitle")))
                {
                    return;
                }

                Desktop_Frames.Services.DismantleReport report = Desktop_Frames.Services.StorageFenceService.DismantleToDesktop(frame);
                if (report.FolderDeleted)
                {
                    DeleteFrameConfiguration(frame, parentWindow);
                    Services.FenceInventoryManager.RefreshDesktopShell();
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgDismantleDone", report.CompletedCount), Strings.Get("DlgDismantleDoneTitle"));
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

                    string alertMsg = Strings.Get("MsgDismantleIncomplete", errDetails, report.CompletedCount, report.FailedCount + report.ConflictCount);
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(alertMsg, Strings.Get("DlgDismantleIncompleteTitle"));
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

            if (!string.IsNullOrEmpty(frameIdStr))
            {
                if (_autoRollTimers.TryGetValue(frameIdStr, out var autoRollTimer))
                {
                    autoRollTimer?.Stop();
                    _autoRollTimers.Remove(frameIdStr);
                }
                _autoRolledFrames.Remove(frameIdStr);
                _framesInTransition.Remove(frameIdStr);

                if (_activePlugins.TryGetValue(frameIdStr, out var plugin))
                {
                    try { plugin?.Cleanup(); } catch { }
                    _activePlugins.Remove(frameIdStr);
                }
            }

            // 立即落盤儲存 frames.json
            FrameDataManager.SaveFrameData();

            // 尋找並關閉對應視窗
            var windows = System.Windows.Application.Current?.Windows.OfType<NonActivatingWindow>();
            var targetWin = windows?.FirstOrDefault(w => string.Equals(w.Tag?.ToString(), frameIdStr, StringComparison.OrdinalIgnoreCase)) ?? (parentWindow as NonActivatingWindow);

            // 從系統列托盤的隱藏清單徹底清除
            TrayManager.RemoveHiddenFrame(targetWin, frameIdStr);

            if (targetWin != null)
            {
                targetWin.Close();
            }

            UpdateAllHeartContextMenus();
        }
    }
}
