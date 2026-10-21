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
    // FrameManager 分檔：CreateFrame 拆出的建構步驟－拖放（自 FrameManager.FrameBuild.cs 原樣搬移；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        // 拖放：拖入時外框提示與手勢判斷、離開還原、放下時依面板類型搬移／複製／建立捷徑或網址
        private static void BindDragDrop(FrameBuildContext ctx, Border cborder, NonActivatingWindow win, WrapPanel wpcont)
        {
            dynamic frame = ctx.Frame;
            TargetChecker targetChecker = ctx.TargetChecker;
            System.Windows.Media.Brush defaultFrameBorderBrush = cborder.BorderBrush;
            win.DragOver += (sender, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                if (frame.ItemsType?.ToString() == "Portal")
                {
                    if (!Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
                    {
                        e.Effects = DragDropEffects.None;
                        cborder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 220, 80, 80));
                        e.Handled = true;
                        return;
                    }

                    // --- 專用收納柵欄 (Managed Storage Fence) ---
                    string[] portalDragFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
                    string commonDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                    bool isAllCommonDesktop = portalDragFiles != null && portalDragFiles.Length > 0 &&
                        portalDragFiles.All(f => !string.IsNullOrEmpty(commonDesktopDir) &&
                            System.IO.Path.GetFullPath(f).StartsWith(commonDesktopDir, StringComparison.OrdinalIgnoreCase));

                    string gestureHint = "";
                    if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                    {
                        e.Effects = DragDropEffects.Link;
                        gestureHint = "在收納面板建立捷徑";
                    }
                    else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        e.Effects = DragDropEffects.Copy;
                        gestureHint = "複製到收納面板";
                    }
                    else if (isAllCommonDesktop)
                    {
                        e.Effects = DragDropEffects.Copy;
                        gestureHint = "從公用桌面複製捷徑";
                    }
                    else
                    {
                        e.Effects = DragDropEffects.Move;
                        gestureHint = "搬進收納面板";
                    }

                    cborder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 0, 150, 255));
                    win.ToolTip = gestureHint;
                    e.Handled = true;
                    return;
                }

                string[] dragFiles = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool isDesktopShortcutMove = false;
                if (dragFiles != null && dragFiles.Length > 0)
                {
                    string userDesk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    isDesktopShortcutMove = dragFiles.All(f =>
                    {
                        string ext = System.IO.Path.GetExtension(f);
                        string parent = System.IO.Path.GetDirectoryName(f);
                        return (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase)) &&
                               string.Equals(parent, userDesk, StringComparison.OrdinalIgnoreCase);
                    });
                }

                e.Effects = isDesktopShortcutMove ? DragDropEffects.Move : DragDropEffects.Link;
                cborder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 110, 185, 255));
                e.Handled = true;
            };

            win.DragLeave += (sender, e) =>
            {
                cborder.BorderBrush = defaultFrameBorderBrush;
                win.ToolTip = null;
            };

            win.Drop += (sender, e) =>
            {
                cborder.BorderBrush = defaultFrameBorderBrush;
                win.ToolTip = null;
                e.Handled = true;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] droppedFiles = null;
                    try
                    {
                        droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                            $"Drop handler received {droppedFiles?.Length ?? 0} files");
                    }
                    catch (Exception dataEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                            $"Error getting drop data: {dataEx.Message}");
                        return;
                    }
                    if (droppedFiles == null) return;
                    if (frame.ItemsType?.ToString() == "Portal")
                    {
                        if (!Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
                        {
                            e.Effects = DragDropEffects.None;
                            e.Handled = true;
                            TrayManager.Instance?.ShowFarmFenceWarning(Strings.Get("MsgPortalReadOnly"));
                            return;
                        }

                        // --- 專用收納柵欄 Drop 執行 ---
                        var gesture = Desktop_Frames.Services.StorageDropAction.Move;
                        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                        {
                            gesture = Desktop_Frames.Services.StorageDropAction.Link;
                        }
                        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            gesture = Desktop_Frames.Services.StorageDropAction.Copy;
                        }

                        int successCount = 0;
                        int noopCount = 0;
                        var failedItems = new System.Collections.Generic.List<string>();

                        foreach (string item in droppedFiles)
                        {
                            Desktop_Frames.Services.IngestResult res = Desktop_Frames.Services.StorageFenceService.IngestItem(frame, item, gesture);
                            if (res.Status == Desktop_Frames.Services.IngestResultStatus.Completed)
                            {
                                successCount++;
                            }
                            else if (res.Status == Desktop_Frames.Services.IngestResultStatus.SkippedSameFolder)
                            {
                                noopCount++;
                            }
                            else
                            {
                                failedItems.Add($"{System.IO.Path.GetFileName(item)}: {res.ErrorMessage}");
                            }
                        }

                        // 觸發該 Portal 的即時刷新與桌面 Shell 刷新
                        string fId = frame.Id?.ToString();
                        var portalEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == fId);
                        portalEntry.Value?.TriggerSync(immediate: true);
                        Desktop_Frames.Services.FenceInventoryManager.RefreshDesktopShell();

                        if (failedItems.Count > 0)
                        {
                            string alert = $"部分項目收納未完成：\n\n" + string.Join("\n", failedItems.Take(5));
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm(alert, "收納未完成");
                        }
                        return;
                    }

                    int portalMovedCount = 0;  // 追蹤成功移動的項目數量（收納）
                    int portalCopiedCount = 0; // 追蹤成功複製的項目數量

                    foreach (string droppedFile in droppedFiles)
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                            $"Processing dropped item: '{droppedFile}'");
                        try
                        {
                            // Path Validation (Existence / Folder Check)
                            bool fileExists = false;
                            bool directoryExists = false;
                            bool isFolderFlag = false;

                            try
                            {
                                FileAttributes attrs = System.IO.File.GetAttributes(droppedFile);
                                isFolderFlag = attrs.HasFlag(FileAttributes.Directory);
                                directoryExists = isFolderFlag;
                                fileExists = !isFolderFlag;
                            }
                            catch (Exception pathEx)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation, $"Path validation error: {pathEx.Message}");
                                continue;
                            }

                            if (!fileExists && !directoryExists)
                            {
                                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgInvalidFileOrFolder", droppedFile), Strings.DlgError);
                                continue;
                            }

                     
                            // --- DATA FRAME LOGIC ---
                            if (frame.ItemsType?.ToString() == "Data")
                            {
                                string targetFrameId = frame.Id?.ToString() ?? string.Empty;
                                string userDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                                string fullDroppedPath = System.IO.Path.GetFullPath(droppedFile);
                                bool isFromUserDesktop = !string.IsNullOrEmpty(userDesktopDir) &&
                                                         fullDroppedPath.StartsWith(userDesktopDir, StringComparison.OrdinalIgnoreCase);

                                if (!System.IO.Directory.Exists("Shortcuts")) System.IO.Directory.CreateDirectory("Shortcuts");
                                string baseShortcutName = System.IO.Path.Combine("Shortcuts", System.IO.Path.GetFileName(droppedFile));
                                string shortcutName = baseShortcutName;
                                int counter = 1;

                                bool isDroppedShortcut = System.IO.Path.GetExtension(droppedFile).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                                bool isDroppedUrlFile = System.IO.Path.GetExtension(droppedFile).Equals(".url", StringComparison.OrdinalIgnoreCase);
                                bool isUserDesktopShortcut = isFromUserDesktop && (isDroppedShortcut || isDroppedUrlFile);

                                // FIX: Trust the extension. If it ends in .url, it IS a link.
                                // This bypasses content checks that fail on files with custom headers.
                                bool isWebLink = isDroppedUrlFile || CoreUtilities.IsWebLinkShortcut(droppedFile);

                                string targetPath;
                                bool isFolder = false;
                                string webUrl = null;

                                if (isWebLink)
                                {
                                    // Try to extract clean URL, but if it fails (weird header), fallback to file path
                                    try { webUrl = CoreUtilities.ExtractWebUrlFromFile(droppedFile); } catch { }

                                    targetPath = !string.IsNullOrEmpty(webUrl) ? webUrl : droppedFile;
                                    isFolder = false;
                                }
                                else
                                {
                                    if (isDroppedShortcut)
                                    {
                                        targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(droppedFile);
                                        if (string.IsNullOrEmpty(targetPath))
                                        {
                                            isFolder = false;
                                        }
                                        else
                                        {
                                            isFolder = System.IO.Directory.Exists(targetPath);
                                            if (!isFolder && string.IsNullOrEmpty(System.IO.Path.GetExtension(targetPath)))
                                            {
                                                isFolder = System.IO.Directory.Exists(targetPath);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        targetPath = droppedFile;
                                        isFolder = System.IO.Directory.Exists(targetPath);
                                    }
                                }

                                if (!isDroppedShortcut && !isDroppedUrlFile)
                                {
                                    // CASE A: Creating new shortcut from raw file/folder
                                    string rawFileName = isFolder ? System.IO.Path.GetFileName(droppedFile.TrimEnd('\\', '/')) : System.IO.Path.GetFileName(droppedFile);
                                    if (string.IsNullOrEmpty(rawFileName)) rawFileName = "Item";
                                    shortcutName = System.IO.Path.Combine("Shortcuts", rawFileName + ".lnk");
                                    while (System.IO.File.Exists(shortcutName))
                                    {
                                        shortcutName = System.IO.Path.Combine("Shortcuts", $"{rawFileName} ({counter++}).lnk");
                                    }

                                    try
                                    {
                                        WshShell shell = new WshShell();
                                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutName);
                                        shortcut.TargetPath = droppedFile;
                                        if (isFolder) shortcut.WorkingDirectory = droppedFile;
                                        shortcut.Save();
                                    }
                                    catch { continue; }
                                }
                                else
                                {
                                    // CASE B: Copying or moving existing shortcut (LNK or URL)
                                    string ext = isWebLink ? ".url" : ".lnk";

                                    // Ensure base name has correct extension
                                    if (!baseShortcutName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                                        baseShortcutName = System.IO.Path.ChangeExtension(baseShortcutName, ext);

                                    shortcutName = baseShortcutName;

                                    // Handle Duplicates while preserving extension
                                    while (System.IO.File.Exists(shortcutName))
                                    {
                                        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                        shortcutName = System.IO.Path.Combine("Shortcuts", $"{nameNoExt} ({counter++}){ext}");
                                    }

                                    System.IO.File.Copy(droppedFile, shortcutName, true);
                                }

                                dynamic newItem = new System.Dynamic.ExpandoObject();
                                IDictionary<string, object> newItemDict = newItem;

                                newItemDict["Filename"] = shortcutName;
                                newItemDict["IsFolder"] = isFolder;
                                newItemDict["IsLink"] = isWebLink;
                                newItemDict["IsNetwork"] = IsNetworkPath(shortcutName);

                                // --- 顯示名稱智能處理：實體檔案保留副檔名便於辨識，捷徑/目錄則維持不顯示副檔名 ---
                                string displayFileName;
                                if (isDroppedShortcut || isDroppedUrlFile || isFolder)
                                {
                                    displayFileName = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                }
                                else
                                {
                                    // 實體檔案保留副檔名（如 .csv, .svg, .pdf, .zip 等），徹底解決同主檔名無法區分的盲區
                                    displayFileName = System.IO.Path.GetFileName(droppedFile);
                                }

                                // If Path.GetFileName fails (happens for UNC roots like \\Server\Share or C:\)
                                if (string.IsNullOrWhiteSpace(displayFileName))
                                {
                                    displayFileName = droppedFile.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                                                 .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                                                 .LastOrDefault();

                                    if (string.IsNullOrWhiteSpace(displayFileName)) displayFileName = droppedFile;
                                }

                                newItemDict["DisplayName"] = displayFileName;
                                newItemDict["AlwaysRunAsAdmin"] = false;

                                // TABS FEATURE: Get fresh framne data
                                string frameId = frame.Id?.ToString();
                                var freshFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                                JArray items = null;
                                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";
                                bool addedToTab0 = false;

                                if (tabsEnabled && freshFrame != null)
                                {
                                    int currentTab = Convert.ToInt32(freshFrame.CurrentTab?.ToString() ?? "0");
                                    var tabs = freshFrame.Tabs as JArray ?? new JArray();
                                    if (currentTab >= 0 && currentTab < tabs.Count)
                                    {
                                        var activeTab = tabs[currentTab] as JObject;
                                        if (activeTab != null)
                                        {
                                            items = activeTab["Items"] as JArray ?? new JArray();
                                            addedToTab0 = (currentTab == 0);
                                        }
                                    }
                                    if (items == null) items = freshFrame.Items as JArray ?? new JArray();
                                }
                                else
                                {
                                    items = (freshFrame ?? frame).Items as JArray ?? new JArray();
                                }

                                int nextDisplayOrder = items.Count;
                                newItemDict["DisplayOrder"] = nextDisplayOrder;
                                items.Add(JObject.FromObject(newItem));

                                if (!string.IsNullOrEmpty(frameId))
                                {
                                    if (addedToTab0) TabManager.SynchronizeTab0Content(frameId, "tab0", "add");
                                    else if (!tabsEnabled) TabManager.SynchronizeTab0Content(frameId, "main", "add");
                                }

                                if (freshFrame != null)
                                {
                                    int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                                    if (frameIndex >= 0)
                                    {
                                        FrameDataManager.FrameData[frameIndex] = freshFrame;
                                        FrameDataManager.SaveFrameData();
                                    }
                                }

                                AddIcon(newItem, wpcont);
                                StackPanel sp = wpcont.Children[wpcont.Children.Count - 1] as StackPanel;
                                if (sp != null)
                                {
                                    // FIX: Extract arguments for the newly created shortcut
                                    string args = Utility.GetShortcutArguments(shortcutName);

                                    ClickEventAdder(sp, shortcutName, isFolder, args);

                                    // [TARGET VALIDATION ENGINE - DROP CONTENT]
                                    bool isRealNetwork = IsNetworkPath(shortcutName);
                                    bool allowNetworkChecking = _options.CheckNetworkPaths ?? false;
                                    bool isDroppedShortcutFile = shortcutName != null && shortcutName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

                                    if (!isWebLink && (!isRealNetwork || allowNetworkChecking))
                                    {
                                        string resolvedTarget = shortcutName;
                                        bool excludeFromCheck = false;

                                        if (isDroppedShortcutFile)
                                        {
                                            resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(shortcutName) ?? shortcutName;

                                            if (resolvedTarget.StartsWith(@"\\") ||
                                                resolvedTarget.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
                                                Utility.IsStoreAppShortcut(shortcutName))
                                            {
                                                excludeFromCheck = true;
                                            }
                                        }

                                        if (!excludeFromCheck)
                                        {
                                            targetChecker.AddCheckAction(shortcutName, () => UpdateIcon(sp, shortcutName, isFolder, resolvedTarget), isFolder);
                                            if (isRealNetwork) LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Monitoring network path: {shortcutName}");
                                        }
                                        else
                                        {
                                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Skipped continuous background check for newly dropped shortcut/link: {shortcutName}");
                                        }
                                    }

                                    // Attach Context Menu
                                    AttachIconContextMenu(sp, newItem, frame, win);

                                    // --- TWO-PHASE TRANSACTIONAL ADOPTION: 個人桌面捷徑收納 ---
                                    if (isUserDesktopShortcut && System.IO.File.Exists(shortcutName))
                                    {
                                        try
                                        {
                                            if (System.IO.File.Exists(droppedFile))
                                            {
                                                System.IO.File.Delete(droppedFile);
                                                Services.FenceInventoryManager.RefreshDesktopShell();
                                            }
                                        }
                                        catch (Exception delEx)
                                        {
                                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                                                $"Two-phase cleanup: Failed to delete desktop shortcut '{droppedFile}': {delEx.Message}");
                                        }
                                    }
                                }
                            }


                            // --- PORTAL FRAME LOGIC (RESTORED) ---
                            else if (frame.ItemsType?.ToString() == "Portal")
                            {
                                IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict ? dict : ((JObject)frame).ToObject<IDictionary<string, object>>();
                                string destinationFolder = frameDict.ContainsKey("Path") ? frameDict["Path"]?.ToString() : null;

                                if (string.IsNullOrEmpty(destinationFolder))
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgNoPortalDestination, Strings.DlgError);
                                    continue;
                                }
                                if (!System.IO.Directory.Exists(destinationFolder))
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgDestinationFolderGone", destinationFolder), Strings.DlgError);
                                    continue;
                                }

                                string destinationPath = System.IO.Path.Combine(destinationFolder, System.IO.Path.GetFileName(droppedFile));
                                int counter = 1;
                                string baseName = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                string extension = System.IO.Path.GetExtension(droppedFile);

                                while (System.IO.File.Exists(destinationPath) || System.IO.Directory.Exists(destinationPath))
                                {
                                    destinationPath = System.IO.Path.Combine(destinationFolder, $"{baseName} ({counter++}){extension}");
                                }

                                bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                                bool shouldMove = !isCtrlPressed; // 預設拖曳為收納搬移（Move），按住 Ctrl 則維持複製（Copy）

                                if (System.IO.File.Exists(droppedFile))
                                {
                                    if (shouldMove)
                                    {
                                        try
                                        {
                                            FileSystem.MoveFile(droppedFile, destinationPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
                                            portalMovedCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Shell MoveFile fallback: {ex.Message}");
                                            System.IO.File.Copy(droppedFile, destinationPath, true);
                                            portalCopiedCount++;
                                        }
                                    }
                                    else
                                    {
                                        FileSystem.CopyFile(droppedFile, destinationPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
                                        portalCopiedCount++;
                                    }
                                }
                                else if (System.IO.Directory.Exists(droppedFile))
                                {
                                    if (shouldMove)
                                    {
                                        try
                                        {
                                            FileSystem.MoveDirectory(droppedFile, destinationPath, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
                                            portalMovedCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Shell MoveDirectory fallback: {ex.Message}");
                                            BackupManager.CopyDirectory(droppedFile, destinationPath);
                                            portalCopiedCount++;
                                        }
                                    }
                                    else
                                    {
                                        BackupManager.CopyDirectory(droppedFile, destinationPath);
                                        portalCopiedCount++;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgAddItemFailed", droppedFile, ex.Message), Strings.DlgError);
                        }
                    }

                    // --- NEW: Visual Feedback for Portal Frames ---
                    if (frame.ItemsType?.ToString() == "Portal")
                    {
                        string frameTitle = frame.Title?.ToString() ?? "收納面板";
                        if (portalMovedCount > 0)
                        {
                            ShowPortalToast(win, $"已搬進 [{frameTitle}]：{portalMovedCount} 個項目");
                            Services.FenceInventoryManager.RefreshDesktopShell();
                        }
                        else if (portalCopiedCount > 0)
                        {
                            ShowPortalToast(win, $"已複製到 [{frameTitle}]：{portalCopiedCount} 個項目");
                        }
                    }

                    FrameDataManager.SaveFrameData();
                }
                // --- URL DROP LOGIC (RESTORED) ----
                else if (e.Data.GetDataPresent(DataFormats.Text) ||
                         e.Data.GetDataPresent(DataFormats.Html) ||
                         e.Data.GetDataPresent("UniformResourceLocator"))
                {
                    try
                    {
                        string droppedUrl = ExtractUrlFromDropData(e.Data);
                        if (!string.IsNullOrEmpty(droppedUrl) && IsValidWebUrl(droppedUrl))
                        {
                            if (frame.ItemsType?.ToString() == "Data")
                            {
                                AddUrlShortcutToFrame(droppedUrl, frame, wpcont);
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation, "URL drops not supported for Portal frames");
                            }
                        }
                    }
                    catch (Exception urlEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation, $"Error processing URL drop: {urlEx.Message}");
                    }
                }
            };
        }
    }
}
