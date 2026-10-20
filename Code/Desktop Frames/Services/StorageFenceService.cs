using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using IWshRuntimeLibrary;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames.Services
{
    public enum StorageDropAction
    {
        Move,
        Copy,
        Link
    }

    public enum IngestResultStatus
    {
        Completed,
        SkippedSameFolder,
        SourceIsAncestorOrSame,
        PartialBothExist,
        Failed
    }

    public class IngestResult
    {
        public IngestResultStatus Status { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public enum DismantleItemStatus
    {
        Completed,
        RenamedAndMoved,
        PartialBothExist,
        Failed
    }

    public class DismantleItemResult
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public DismantleItemStatus Status { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class DismantleReport
    {
        public bool Success => FailedCount == 0 && ConflictCount == 0 && FolderDeleted;
        public int TotalItems { get; set; }
        public int CompletedCount { get; set; }
        public int ConflictCount { get; set; }
        public int FailedCount { get; set; }
        public bool FolderDeleted { get; set; }
        public string FolderDeletionReason { get; set; } = string.Empty;
        public List<DismantleItemResult> Results { get; } = new List<DismantleItemResult>();
    }

    /// <summary>
    /// 專用收納柵欄（Managed Storage Fence）專用檔案服務。
    /// 負責受控專用目錄驗證、拖入搬移／複製／建捷徑、拆除並移至桌面與三態安全防禦。
    /// </summary>
    public static class StorageFenceService
    {
        #region Win32 Recycle Bin Interop
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation([In] ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        #endregion

        public static bool IsManagedStorage(dynamic? frame)
        {
            if (frame == null) return false;
            try
            {
                if (frame is JObject jObj)
                {
                    return string.Equals(jObj["IsManagedStorage"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                }
                if (frame is IDictionary<string, object> dict)
                {
                    return dict.ContainsKey("IsManagedStorage") &&
                           string.Equals(dict["IsManagedStorage"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                }
                var prop = frame.GetType().GetProperty("IsManagedStorage");
                if (prop != null)
                {
                    var val = prop.GetValue(frame);
                    return string.Equals(val?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        public static bool IsSafeManagedDirectory(dynamic? frame, out string folderPath, out string reason)
        {
            folderPath = string.Empty;
            reason = string.Empty;

            if (frame == null)
            {
                reason = "面板物件不存在。";
                return false;
            }

            if (!IsManagedStorage(frame))
            {
                reason = "這個面板不是收納面板。";
                return false;
            }

            string rawPath = string.Empty;
            try
            {
                if (frame is JObject jObj) rawPath = jObj["Path"]?.ToString() ?? string.Empty;
                else if (frame is IDictionary<string, object> dict && dict.ContainsKey("Path")) rawPath = dict["Path"]?.ToString() ?? string.Empty;
                else rawPath = frame.Path?.ToString() ?? string.Empty;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                reason = "專用收納夾路徑為空。";
                return false;
            }

            try
            {
                folderPath = Path.GetFullPath(rawPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                reason = $"路徑無效：{ex.Message}";
                return false;
            }

            if (!Directory.Exists(folderPath))
            {
                reason = $"專用收納資料夾不存在：{folderPath}";
                return false;
            }

            // 根目錄防護（例如 C:\、D:\）
            string root = Path.GetPathRoot(folderPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
            if (string.Equals(folderPath, root, StringComparison.OrdinalIgnoreCase))
            {
                reason = "專用收納夾不得指向磁碟根目錄。";
                return false;
            }

            // 系統目錄防護
            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            };

            try
            {
                string profileDir = ProfileManager.CurrentProfileDir;
                if (!string.IsNullOrEmpty(profileDir))
                {
                    protectedPaths.Add(Path.GetFullPath(profileDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
            catch { }

            foreach (var p in protectedPaths)
            {
                if (!string.IsNullOrEmpty(p) && string.Equals(folderPath, p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"收納夾不得為系統或桌面根層級保護目錄：{folderPath}";
                    return false;
                }
            }
            return true;
        }

        public static IngestResult IngestItem(dynamic frame, string sourcePath, StorageDropAction action)
        {
            if (!IsSafeManagedDirectory(frame, out string targetFolder, out string reason))
            {
                return new IngestResult
                {
                    SourcePath = sourcePath,
                    Status = IngestResultStatus.Failed,
                    ErrorMessage = reason
                };
            }

            return IngestItem(sourcePath, targetFolder, action);
        }

        public static IngestResult IngestItem(string sourcePath, string targetFolder, StorageDropAction action)
        {
            var result = new IngestResult { SourcePath = sourcePath };

            if (string.IsNullOrWhiteSpace(sourcePath) || (!System.IO.File.Exists(sourcePath) && !Directory.Exists(sourcePath)))
            {
                result.Status = IngestResultStatus.Failed;
                result.ErrorMessage = $"來源項目不存在：{sourcePath}";
                return result;
            }

            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                result.Status = IngestResultStatus.Failed;
                result.ErrorMessage = $"目標收納資料夾不存在：{targetFolder}";
                return result;
            }

            string fullSource = Path.GetFullPath(sourcePath);
            string fullTarget = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            bool isSourceDir = Directory.Exists(fullSource);
            string sourceParent = Path.GetDirectoryName(fullSource)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;

            // 同目錄防呆（同框內拖曳不產生副本）
            if (string.Equals(sourceParent, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = IngestResultStatus.SkippedSameFolder;
                result.DestinationPath = fullSource;
                return result;
            }

            // 子母目錄防呆（禁止將父資料夾拖入自身子資料夾）
            if (isSourceDir && (string.Equals(fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullTarget, StringComparison.OrdinalIgnoreCase) ||
                                fullTarget.StartsWith(fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                result.Status = IngestResultStatus.SourceIsAncestorOrSame;
                result.ErrorMessage = "無法將資料夾移入自身或其子資料夾。";
                return result;
            }

            string itemName = isSourceDir ? Path.GetFileName(fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(fullSource);
            if (string.IsNullOrEmpty(itemName)) itemName = "Item";

            try
            {
                if (action == StorageDropAction.Link)
                {
                    string lnkName = Path.ChangeExtension(itemName, ".lnk");
                    string destLnk = GetUniqueDestinationPath(fullTarget, lnkName, isDirectory: false);
                    result.DestinationPath = destLnk;

                    WshShell shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(destLnk);
                    shortcut.TargetPath = fullSource;
                    if (isSourceDir) shortcut.WorkingDirectory = fullSource;
                    shortcut.Save();

                    result.Status = IngestResultStatus.Completed;
                    return result;
                }

                // Move 或 Copy
                string destPath = Path.Combine(fullTarget, itemName);

                // 檢查同名項目是否為上次中斷殘留（同大小與同修改時間）
                if (System.IO.File.Exists(destPath) && System.IO.File.Exists(fullSource))
                {
                    try
                    {
                        var fiSrc = new FileInfo(fullSource);
                        var fiDst = new FileInfo(destPath);
                        if (fiSrc.Length == fiDst.Length && fiSrc.LastWriteTimeUtc == fiDst.LastWriteTimeUtc)
                        {
                            result.Status = IngestResultStatus.PartialBothExist;
                            result.DestinationPath = destPath;
                            result.ErrorMessage = "目標收納夾已存在同名且屬性相符之項目，來源亦存在，需人工確認。";
                            return result;
                        }
                    }
                    catch { }
                }

                if (System.IO.File.Exists(destPath) || Directory.Exists(destPath))
                {
                    destPath = GetUniqueDestinationPath(fullTarget, itemName, isSourceDir);
                }
                result.DestinationPath = destPath;

                if (action == StorageDropAction.Move)
                {
                    if (isSourceDir)
                    {
                        Directory.Move(fullSource, destPath);
                    }
                    else
                    {
                        System.IO.File.Move(fullSource, destPath);
                    }

                    bool srcExists = isSourceDir ? Directory.Exists(fullSource) : System.IO.File.Exists(fullSource);
                    bool dstExists = isSourceDir ? Directory.Exists(destPath) : System.IO.File.Exists(destPath);

                    if (!srcExists && dstExists)
                    {
                        result.Status = IngestResultStatus.Completed;
                    }
                    else if (srcExists && dstExists)
                    {
                        result.Status = IngestResultStatus.PartialBothExist;
                        result.ErrorMessage = "移動後來源與目標皆存在，來源未被清除。";
                    }
                    else
                    {
                        result.Status = IngestResultStatus.Failed;
                        result.ErrorMessage = "移動操作未產出目標檔案。";
                    }
                }
                else // Copy
                {
                    if (isSourceDir)
                    {
                        CopyDirectoryRecursive(fullSource, destPath);
                    }
                    else
                    {
                        System.IO.File.Copy(fullSource, destPath, overwrite: false);
                    }
                    result.Status = IngestResultStatus.Completed;
                }
            }
            catch (Exception ex)
            {
                result.Status = IngestResultStatus.Failed;
                result.ErrorMessage = ex.Message;
            }

            var logLevel = result.Status == IngestResultStatus.Failed || result.Status == IngestResultStatus.PartialBothExist
                ? LogManager.LogLevel.Warn
                : LogManager.LogLevel.Info;
            LogManager.Log(logLevel, LogManager.LogCategory.IconHandling,
                $"[StorageFence.Ingest] Action={action} | Status={result.Status} | Src='{result.SourcePath}' | Dst='{result.DestinationPath}'{(string.IsNullOrEmpty(result.ErrorMessage) ? "" : $" | Msg={result.ErrorMessage}")}");

            return result;
        }

        public static DismantleReport DismantleToDesktop(dynamic frame, string? targetDesktopOverride = null)
        {
            var report = new DismantleReport();

            if (!IsSafeManagedDirectory(frame, out string folderPath, out string reason))
            {
                report.FolderDeleted = false;
                report.FolderDeletionReason = reason;
                return report;
            }

            string desktopDir = !string.IsNullOrWhiteSpace(targetDesktopOverride) && Directory.Exists(targetDesktopOverride)
                ? Path.GetFullPath(targetDesktopOverride)
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            if (!Directory.Exists(desktopDir))
            {
                report.FolderDeleted = false;
                report.FolderDeletionReason = $"目標桌面目錄不存在：{desktopDir}";
                return report;
            }

            List<FileSystemInfo> topItems = new List<FileSystemInfo>();
            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                topItems = dirInfo.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex)
            {
                report.FolderDeleted = false;
                report.FolderDeletionReason = $"無法列舉收納夾檔案：{ex.Message}";
                return report;
            }

            report.TotalItems = topItems.Count;

            foreach (var item in topItems)
            {
                var itemResult = new DismantleItemResult { SourcePath = item.FullName };
                bool isDir = (item.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                try
                {
                    string destPath = Path.Combine(desktopDir, item.Name);
                    bool renamed = false;

                    // 檢查是否兩端皆存在且為同一殘留項目
                    if (!isDir && System.IO.File.Exists(destPath) && System.IO.File.Exists(item.FullName))
                    {
                        try
                        {
                            var fiSrc = new FileInfo(item.FullName);
                            var fiDst = new FileInfo(destPath);
                            if (fiSrc.Length == fiDst.Length && fiSrc.LastWriteTimeUtc == fiDst.LastWriteTimeUtc)
                            {
                                itemResult.Status = DismantleItemStatus.PartialBothExist;
                                itemResult.DestinationPath = destPath;
                                itemResult.ErrorMessage = "桌面已存在完全相符之同名項目，收納夾原檔未移除，保留現場以供確認。";
                                report.ConflictCount++;
                                report.Results.Add(itemResult);
                                continue;
                            }
                        }
                        catch { }
                    }

                    if (System.IO.File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        destPath = GetUniqueDestinationPath(desktopDir, item.Name, isDir);
                        renamed = true;
                    }

                    itemResult.DestinationPath = destPath;

                    if (isDir)
                    {
                        Directory.Move(item.FullName, destPath);
                    }
                    else
                    {
                        System.IO.File.Move(item.FullName, destPath);
                    }

                    bool srcExists = isDir ? Directory.Exists(item.FullName) : System.IO.File.Exists(item.FullName);
                    bool dstExists = isDir ? Directory.Exists(destPath) : System.IO.File.Exists(destPath);

                    if (!srcExists && dstExists)
                    {
                        itemResult.Status = renamed ? DismantleItemStatus.RenamedAndMoved : DismantleItemStatus.Completed;
                        report.CompletedCount++;
                    }
                    else if (srcExists && dstExists)
                    {
                        itemResult.Status = DismantleItemStatus.PartialBothExist;
                        itemResult.ErrorMessage = "目標已移至桌面，但收納夾來源未成功清除。";
                        report.ConflictCount++;
                    }
                    else
                    {
                        itemResult.Status = DismantleItemStatus.Failed;
                        itemResult.ErrorMessage = "移動完成驗證失敗。";
                        report.FailedCount++;
                    }
                }
                catch (Exception ex)
                {
                    itemResult.Status = DismantleItemStatus.Failed;
                    itemResult.ErrorMessage = ex.Message;
                    report.FailedCount++;
                }

                report.Results.Add(itemResult);
            }

            // 四道硬性刪除門檻
            if (report.FailedCount > 0 || report.ConflictCount > 0)
            {
                report.FolderDeleted = false;
                report.FolderDeletionReason = "尚有未完成或需確認項目，依安全準則保留專用收納夾。";
                return report;
            }

            try
            {
                // 檢查目錄底層是否 100% 完全為空（無過濾器，含隱藏檔、系統檔、~$ 鎖定檔）
                bool hasRemainingEntries = Directory.EnumerateFileSystemEntries(folderPath).Any();
                if (hasRemainingEntries)
                {
                    report.FolderDeleted = false;
                    report.FolderDeletionReason = "收納夾內仍有未顯示之隱藏檔、系統檔或暫存鎖定檔，依安全準則保留資料夾。";
                    return report;
                }

                // 嚴格非遞迴刪除
                Directory.Delete(folderPath, recursive: false);
                report.FolderDeleted = true;
                report.FolderDeletionReason = "專用收納夾已全空並安全刪除。";
            }
            catch (Exception ex)
            {
                report.FolderDeleted = false;
                report.FolderDeletionReason = $"刪除空收納夾時作業系統拒絕：{ex.Message}";
            }

            LogManager.Log(report.Success ? LogManager.LogLevel.Info : LogManager.LogLevel.Warn,
                LogManager.LogCategory.FrameUpdate,
                $"[StorageFence.Dismantle] Folder='{folderPath}' | Total={report.TotalItems}, Completed={report.CompletedCount}, Conflict={report.ConflictCount}, Failed={report.FailedCount}, FolderDeleted={report.FolderDeleted} | {report.FolderDeletionReason}");

            return report;
        }

        public static bool DeleteToRecycleBin(string path, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || (!System.IO.File.Exists(path) && !Directory.Exists(path)))
            {
                error = "項目不存在。";
                return false;
            }

            try
            {
                SHFILEOPSTRUCT shf = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION,
                    pFrom = path + '\0' + '\0'
                };

                int res = SHFileOperation(ref shf);
                if (res != 0)
                {
                    error = $"SHFileOperation 失敗，代碼：{res}";
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling, $"[StorageFence.RecycleBin] Failed '{path}': {error}");
                    return false;
                }
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling, $"[StorageFence.RecycleBin] Moved to Recycle Bin: '{path}'");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling, $"[StorageFence.RecycleBin] Error '{path}': {error}");
                return false;
            }
        }

        public static string GetSuggestedNewFolderName(string parentDir, string baseName = "新增資料夾")
        {
            string cleanBase = SanitizeFolderName(baseName);
            if (string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir))
                return cleanBase;

            string uniquePath = GetUniqueDestinationPath(parentDir, cleanBase, isDirectory: true);
            return Path.GetFileName(uniquePath);
        }

        public static string CreateNewFolder(string parentDir, string desiredName = "新增資料夾")
        {
            if (string.IsNullOrWhiteSpace(parentDir))
                throw new ArgumentException("目標父資料夾路徑不可為空。", nameof(parentDir));

            if (!Directory.Exists(parentDir))
                Directory.CreateDirectory(parentDir);

            string cleanName = SanitizeFolderName(desiredName);
            string targetPath = GetUniqueDestinationPath(parentDir, cleanName, isDirectory: true);
            Directory.CreateDirectory(targetPath);
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling, $"[StorageFence.CreateFolder] Created '{targetPath}'");
            return targetPath;
        }

        public static string? PromptAndCreateNewFolder(string parentDir, System.Windows.Window? owner = null)
        {
            if (string.IsNullOrWhiteSpace(parentDir)) return null;
            try
            {
                if (!Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);
            }
            catch { return null; }

            string defaultName = GetSuggestedNewFolderName(parentDir, "新增資料夾");

            var dlg = new System.Windows.Window
            {
                Title = "新增資料夾",
                Width = 340,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
            };

            var outerBorder = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 130, 230)),
                BorderThickness = new System.Windows.Thickness(1.5),
                CornerRadius = new System.Windows.CornerRadius(8),
                Padding = new System.Windows.Thickness(16, 14, 16, 14),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    BlurRadius = 14,
                    ShadowDepth = 3,
                    Opacity = 0.65
                }
            };

            var rootStack = new System.Windows.Controls.StackPanel();

            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = "📁 在收納框內新增資料夾",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13.5,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            rootStack.Children.Add(titleBlock);

            var nameBox = new System.Windows.Controls.TextBox
            {
                Text = defaultName,
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.White,
                CaretBrush = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85)),
                BorderThickness = new System.Windows.Thickness(1),
                Padding = new System.Windows.Thickness(8, 6, 8, 6),
                Margin = new System.Windows.Thickness(0, 0, 0, 12)
            };
            rootStack.Children.Add(nameBox);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 68,
                Height = 28,
                IsCancel = true,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 210, 210)),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 55, 55)),
                BorderThickness = new System.Windows.Thickness(0),
                Margin = new System.Windows.Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnCancel.Click += (s, e) => { dlg.DialogResult = false; dlg.Close(); };

            var btnCreate = new System.Windows.Controls.Button
            {
                Content = "建立",
                Width = 74,
                Height = 28,
                IsDefault = true,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 120, 220)),
                BorderThickness = new System.Windows.Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnCreate.Click += (s, e) => { dlg.DialogResult = true; dlg.Close(); };

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnCreate);
            rootStack.Children.Add(btnPanel);

            outerBorder.Child = rootStack;
            dlg.Content = outerBorder;

            outerBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                {
                    try { dlg.DragMove(); } catch { }
                }
            };

            dlg.Loaded += (s, e) =>
            {
                nameBox.Focus();
                nameBox.SelectAll();
            };

            if (dlg.ShowDialog() == true)
            {
                string chosen = string.IsNullOrWhiteSpace(nameBox.Text) ? defaultName : nameBox.Text.Trim();
                return CreateNewFolder(parentDir, chosen);
            }

            return null;
        }

        private static string SanitizeFolderName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "新增資料夾";
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(rawName.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim('.');
            return string.IsNullOrWhiteSpace(cleaned) ? "新增資料夾" : cleaned;
        }

        private static string GetUniqueDestinationPath(string dir, string fileNameOrDirName, bool isDirectory)
        {
            string ext = isDirectory ? string.Empty : Path.GetExtension(fileNameOrDirName);
            string baseName = isDirectory ? fileNameOrDirName : Path.GetFileNameWithoutExtension(fileNameOrDirName);

            string candidate = Path.Combine(dir, fileNameOrDirName);
            int counter = 1;

            while (System.IO.File.Exists(candidate) || Directory.Exists(candidate))
            {
                string newName = isDirectory ? $"{baseName} ({counter++})" : $"{baseName} ({counter++}){ext}";
                candidate = Path.Combine(dir, newName);
            }

            return candidate;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, overwrite: false);
            }
            foreach (var sub in Directory.GetDirectories(sourceDir))
            {
                string destSub = Path.Combine(targetDir, Path.GetFileName(sub));
                CopyDirectoryRecursive(sub, destSub);
            }
        }
    }
}
