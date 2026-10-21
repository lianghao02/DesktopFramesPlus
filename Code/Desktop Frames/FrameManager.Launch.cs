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
    // FrameManager 分檔：Launch（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        public static void UpdateOptionsAndClickEvents()
        {


            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Updating options, new singleClickToLaunch={SettingsManager.SingleClickToLaunch}");

            // Update _options
            _options = new
            {
                IsSnapEnabled = SettingsManager.IsSnapEnabled,
                ShowBackgroundImageOnPortalFences = SettingsManager.ShowBackgroundImageOnPortalFrames,
                Showintray = SettingsManager.ShowInTray,
                EnableSounds = SettingsManager.EnableSounds,
                TintValue = SettingsManager.TintValue,
                MenuTintValue = SettingsManager.MenuTintValue,
                MenuIcon = SettingsManager.MenuIcon,
                LockIcon = SettingsManager.LockIcon,
                SelectedColor = SettingsManager.SelectedColor,
                IsLogEnabled = SettingsManager.IsLogEnabled,
                singleClickToLaunch = SettingsManager.SingleClickToLaunch,
                LaunchEffect = SettingsManager.LaunchEffect,
                CheckNetworkPaths = false // Keep this as is
            };


            if (System.Windows.Application.Current != null)
            {

                // Force UI update on the main thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                int updatedItems = 0;
                foreach (var win in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
                {
                    var wpcont = ((win.Content as Border)?.Child as DockPanel)?.Children
                        .OfType<ScrollViewer>().FirstOrDefault()?.Content as WrapPanel;
                    if (wpcont != null)
                    {
                        foreach (var sp in wpcont.Children.OfType<StackPanel>())
                        {
                            string path = sp.Tag as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                bool isFolder = Directory.Exists(path) ||
                                    (System.IO.Path.GetExtension(path).ToLower() == ".lnk" &&
                                     Directory.Exists(Utility.GetShortcutTarget(path)));
                                string arguments = null;
                                if (System.IO.Path.GetExtension(path).ToLower() == ".lnk")
                                {
                                    WshShell shell = new WshShell();
                                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(path);
                                    arguments = shortcut.Arguments;
                                }
                                ClickEventAdder(sp, path, isFolder, arguments);
                                updatedItems++;
                            }
                        }
                    }
                }
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Updated click events for {updatedItems} items");
            });
            }
            else
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, "Application.Current is null, cannot update icon.");
            }
        }

        public static void ClickEventAdder(StackPanel sp, string path, bool isFolder, string arguments = null)
        {
            // Store only path, isFolder, and arguments in Tag
            sp.Tag = new { FilePath = path, IsFolder = isFolder, Arguments = arguments };

            // --- HANG FIX 1 (Startup): Removed synchronous Utility.GetShortcutTarget(path) from here. ---
            // The folder double-check is deferred until the exact moment of clicking to prevent startup freezing.
            bool isShortcut = System.IO.Path.GetExtension(path).ToLower() == ".lnk";

            // --- NAMED LOCAL FUNCTIONS FOR EVENTS ---
            System.Windows.Point? iconMouseDownPos = null;
            bool isIconDragging = false;

            void MouseDownHandler(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;

                // 1. Double click to launch / open
                if (e.ClickCount == 2)
                {
                    iconMouseDownPos = null;
                    isIconDragging = false;

                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                    string frameId = win?.Tag?.ToString();
                    dynamic frame = null;
                    if (!string.IsNullOrEmpty(frameId))
                        frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

                    if (frame != null && frame.ItemsType?.ToString() == "Portal")
                    {
                        // 僅在按住 Ctrl + 連點兩下時才執行「框內原地切換路徑」；
                        // 一般連點兩下直接由下方 LaunchItem 以檔案總管開啟資料夾（符合桌面直覺與詳細資料檢視行為）
                        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            string portalTargetFolder = isFolder ? path : null;
                            if (string.IsNullOrEmpty(portalTargetFolder) && isShortcut)
                            {
                                try
                                {
                                    string resolved = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                                    if (!string.IsNullOrEmpty(resolved) && System.IO.Directory.Exists(resolved))
                                        portalTargetFolder = resolved;
                                }
                                catch { }
                            }

                            if (!string.IsNullOrEmpty(portalTargetFolder) && System.IO.Directory.Exists(portalTargetFolder))
                            {
                                NavigatePortalFrame(frame, portalTargetFolder);
                                e.Handled = true;
                                return;
                            }
                        }
                    }

                    e.Handled = true; // Mark handled immediately so the UI thread is freed

                    // --- UX 優化：UI 執行緒立即播放點擊反饋動畫（0ms 零延遲手感，不等待背景解析） ---
                    TriggerLaunchEffect(sp, frame);

                    // --- HANG FIX 2 (Click): Move network resolution to an STA Background Thread ---
                    System.Threading.Thread launchThread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            bool isShortcutLocal = System.IO.Path.GetExtension(path).ToLower() == ".lnk";
                            bool targetExists;
                            string resolvedPath = path;
                            bool dynamicIsFolder = isFolder;

                            if (isShortcutLocal)
                            {
                                resolvedPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                                if (string.IsNullOrEmpty(resolvedPath))
                                {
                                    targetExists = false;
                                }
                                else
                                {
                                    targetExists = dynamicIsFolder ? System.IO.Directory.Exists(resolvedPath) : System.IO.File.Exists(resolvedPath);
                                    if (!dynamicIsFolder && System.IO.Directory.Exists(resolvedPath))
                                    {
                                        dynamicIsFolder = true;
                                        targetExists = true;
                                    }
                                }
                            }
                            else
                            {
                                targetExists = dynamicIsFolder ? System.IO.Directory.Exists(path) : System.IO.File.Exists(path);
                                if (!targetExists && !dynamicIsFolder && System.IO.Directory.Exists(path))
                                {
                                    dynamicIsFolder = true;
                                    targetExists = true;
                                }
                            }

                            bool isStoreApp = false;
                            string scrapedPath = null;

                            if (!targetExists && isShortcutLocal)
                            {
                                isStoreApp = Utility.IsStoreAppShortcut(path);
                                if (!isStoreApp)
                                {
                                    try { scrapedPath = GetScrapedNetworkPath(path); } catch { }
                                    if (!string.IsNullOrEmpty(scrapedPath))
                                    {
                                        targetExists = true;
                                    }
                                }
                            }

                            if (!targetExists && !isStoreApp) return;

                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { UpdateIcon(sp, path, dynamicIsFolder, resolvedPath); } catch { }
                                LaunchItem(sp, path, dynamicIsFolder, arguments, triggerEffect: false);
                            }));
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error checking target existence: {ex.Message}");
                        }
                    });

                    launchThread.SetApartmentState(System.Threading.ApartmentState.STA);
                    launchThread.IsBackground = true;
                    launchThread.Start();
                    return;
                }

                // 2. Single click: record position for drag detection
                if (e.ClickCount == 1)
                {
                    iconMouseDownPos = e.GetPosition(sp);
                    isIconDragging = false;
                }
            }

            void MouseMoveHandler(object sender, MouseEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed && iconMouseDownPos.HasValue && !isIconDragging && !IconDragDropManager.IsDragging)
                {
                    System.Windows.Point currentPos = e.GetPosition(sp);
                    double dragThresholdX = Math.Max(10.0, SystemParameters.MinimumHorizontalDragDistance * 2.5);
                    double dragThresholdY = Math.Max(10.0, SystemParameters.MinimumVerticalDragDistance * 2.5);
                    if (Math.Abs(currentPos.X - iconMouseDownPos.Value.X) >= dragThresholdX ||
                        Math.Abs(currentPos.Y - iconMouseDownPos.Value.Y) >= dragThresholdY)
                    {
                        NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                        string frameId = win?.Tag?.ToString();
                        dynamic frame = null;
                        if (!string.IsNullOrEmpty(frameId))
                            frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);

                        if (frame != null && Desktop_Frames.Services.StorageFenceService.IsManagedStorage(frame))
                        {
                            isIconDragging = true;
                            try
                            {
                                var data = new DataObject(DataFormats.FileDrop, new string[] { path });
                                System.IO.MemoryStream dropEffect = new System.IO.MemoryStream(new byte[] { 2, 0, 0, 0 });
                                data.SetData("Preferred DropEffect", dropEffect);
                                DragDrop.DoDragDrop(sp, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"專用收納圖示拖曳異常: {ex.Message}");
                            }
                            finally
                            {
                                isIconDragging = false;
                                iconMouseDownPos = null;
                                try
                                {
                                    var pEntry = _portalFrames.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == frameId);
                                    pEntry.Value?.TriggerSync(immediate: true);
                                    Desktop_Frames.Services.FenceInventoryManager.RefreshDesktopShell();
                                }
                                catch { }
                            }
                            return;
                        }

                        isIconDragging = true;
                        IconDragDropManager.StartIconDrag(sp, iconMouseDownPos.Value);
                    }
                }

                if (IconDragDropManager.IsDragging)
                {
                    try
                    {
                        System.Windows.Point screenPosition = sp.PointToScreen(e.GetPosition(sp));
                        IconDragDropManager.HandleDragMove(screenPosition);
                    }
                    catch { }
                }
            }

            void MouseUpHandler(object sender, MouseButtonEventArgs e)
            {
                if (IconDragDropManager.IsDragging)
                {
                    try
                    {
                        System.Windows.Point screenPosition = sp.PointToScreen(e.GetPosition(sp));
                        IconDragDropManager.CompleteDrag(screenPosition);
                        e.Handled = true;
                    }
                    catch
                    {
                        IconDragDropManager.CancelDrag();
                    }
                }
                else if (e.ChangedButton == MouseButton.Left && iconMouseDownPos.HasValue && !isIconDragging)
                {
                    // 單點選取：左鍵放開且非拖曳 → 高亮此圖示
                    SetSelectedIcon(sp);
                }
                iconMouseDownPos = null;
                isIconDragging = false;
            }

            void KeyUpHandler(object sender, KeyEventArgs e)
            {
                if (IconDragDropManager.IsDragging && e.Key == Key.Escape)
                {
                    IconDragDropManager.CancelDrag();
                    e.Handled = true;
                }
            }

            void KeyDownHandler(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Delete && _currentlySelectedIconPanel == sp)
                {
                    RemoveSelectedIconFromFrame(sp);
                    e.Handled = true;
                }
            }

            // --- SAFELY REMOVE PREVIOUS HANDLERS (WPF APPROACH) ---
            sp.RemoveHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(MouseDownHandler));
            sp.RemoveHandler(UIElement.MouseMoveEvent, new MouseEventHandler(MouseMoveHandler));
            sp.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(MouseUpHandler));
            sp.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUpHandler));
            sp.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(KeyDownHandler));

            // --- ATTACH FRESH HANDLERS ---
            sp.MouseLeftButtonDown += MouseDownHandler;
            sp.MouseMove += MouseMoveHandler;
            sp.MouseLeftButtonUp += MouseUpHandler;
            sp.KeyUp += KeyUpHandler;
            sp.PreviewKeyDown += KeyDownHandler;
        }

        public static void TriggerLaunchEffect(StackPanel sp, dynamic frame = null)
        {
            if (sp == null) return;
            try
            {
                if (frame == null)
                {
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                    string frameId = win?.Tag?.ToString();

                    if (!string.IsNullOrEmpty(frameId))
                    {
                        frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    }

                    // Fallback to Title only if ID lookup failed
                    if (frame == null)
                    {
                        frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Title == win?.Title);
                    }
                }

                // FIX: Read LIVE Global Setting directly from SettingsManager
                // This bypasses the stale _options cache.
                LaunchEffectsManager.LaunchEffect effect = SettingsManager.LaunchEffect;

                // Check for frame-Specific Override
                string customEffect = frame?.CustomLaunchEffect?.ToString();
                if (!string.IsNullOrEmpty(customEffect))
                {
                    try
                    {
                        effect = (LaunchEffectsManager.LaunchEffect)Enum.Parse(typeof(LaunchEffectsManager.LaunchEffect), customEffect, true);
                    }
                    catch { }
                }

                LaunchEffectsManager.ExecuteLaunchEffect(sp, effect);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Launch effect error: {ex.Message}");
            }
        }

        public static void LaunchItem(StackPanel sp, string path, bool isFolder, string arguments = null, bool triggerEffect = true)
        {
            try
            {
                // --- NEW: Ignore clicks on internal placeholders ---
                if (path != null && path.StartsWith("INTERNAL_BLANK_")) return;

                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                string frameId = win?.Tag?.ToString();
                dynamic frame = null;

                if (!string.IsNullOrEmpty(frameId))
                {
                    frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                }

                // Fallback to Title only if ID lookup failed
                if (frame == null)
                {
                    frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Title == win?.Title);
                }

                // 1. Visual Feedback
                if (triggerEffect)
                {
                    TriggerLaunchEffect(sp, frame);
                }

                // 2. Path Resolution
                string fullPath = path;
                try { fullPath = System.IO.Path.GetFullPath(path); } catch { }

                string extension = System.IO.Path.GetExtension(fullPath).ToLower();
                bool isUrlFile = extension == ".url";
                bool isLnk = extension == ".lnk";

                string targetPath = fullPath;
                string workingDirectory = "";
                string finalArguments = arguments ?? "";

                // 3. Resolve Target
                if (isUrlFile)
                {
                    targetPath = ExtractUrlFromUrlFile(fullPath);
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgInvalidUrlFile", path), Strings.DlgError);
                        return;
                    }
                }
                else if (isLnk)
                {
                    if (FilePathUtilities.TryResolveShortcutWithShellLinkW(fullPath, out string slTarget, out string slArgs, out string slWorkDir, out _, out _))
                    {
                        targetPath = slTarget;
                        if (!slTarget.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(finalArguments) && !string.IsNullOrEmpty(slArgs)) finalArguments = slArgs;
                            if (!string.IsNullOrEmpty(slWorkDir)) workingDirectory = slWorkDir;
                        }
                    }
                    else
                    {
                        string resolved = FilePathUtilities.GetShortcutTargetUnicodeSafe(fullPath);
                        if (string.IsNullOrEmpty(resolved)) resolved = GetScrapedNetworkPath(fullPath);

                        if (!string.IsNullOrEmpty(resolved))
                        {
                            targetPath = resolved;
                            try
                            {
                                WshShell shell = new WshShell();
                                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(fullPath);
                                string rawTarget = shortcut.TargetPath?.ToLower() ?? "";
                                if (!rawTarget.EndsWith("explorer.exe"))
                                {
                                    if (string.IsNullOrEmpty(finalArguments)) finalArguments = shortcut.Arguments;
                                    workingDirectory = shortcut.WorkingDirectory;
                                }
                            }
                            catch { }
                        }
                    }
                }

                // 4. Classification
                bool isWebUrl = IsWebUrl(targetPath);
                bool isStoreApp = isLnk && Utility.IsStoreAppShortcut(fullPath);
                bool isNetworkRoot = !string.IsNullOrEmpty(targetPath) && targetPath.StartsWith(@"\\") && !targetPath.Contains(@":\");
                bool isSpecialPath = IsSpecialWindowsPath(targetPath);

                bool isTargetFolder = false;
                bool targetExists = false;

                if (!isWebUrl && !isSpecialPath && !isNetworkRoot && !isStoreApp)
                {
                    isTargetFolder = System.IO.Directory.Exists(targetPath);
                    targetExists = isTargetFolder || System.IO.File.Exists(targetPath);
                }

                // 5. Validation Guard
                if (!targetExists && !isWebUrl && !isSpecialPath && !isStoreApp && !isNetworkRoot)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Target not found: {targetPath}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgTargetNotFound", targetPath), Strings.DlgLaunchError);
                    return;
                }

                // 6. Admin & Different User Logic
                bool alwaysRunAsAdmin = false;
                bool alwaysRunAsDifferentUser = false;
                try
                {
                    if (frame != null && frame.ItemsType?.ToString() == "Data")
                    {
                        var items = frame.Items as JArray ?? new JArray();
                        bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";

                        if (tabsEnabled)
                        {
                            var tabs = frame.Tabs as JArray ?? new JArray();
                            int currentTabIndex = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");
                            if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                            {
                                var currentTab = tabs[currentTabIndex] as JObject;
                                items = currentTab?["Items"] as JArray ?? items;
                            }
                        }

                        var matchingItem = items.FirstOrDefault(i => string.Equals(
                            System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                            fullPath, StringComparison.OrdinalIgnoreCase));

                        if (matchingItem != null)
                        {
                            alwaysRunAsAdmin = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                            alwaysRunAsDifferentUser = Convert.ToBoolean(matchingItem["AlwaysRunAsDifferentUser"] ?? false);
                        }
                    }
                }
                catch { }

                // 7. Execution
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = true;

                if (!isWebUrl && !isSpecialPath && !isTargetFolder && !isNetworkRoot)
                {
                    if (alwaysRunAsAdmin)
                        psi.Verb = "runas";
                    else if (alwaysRunAsDifferentUser)
                        psi.Verb = "runasuser";
                }

                if (isStoreApp)
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = $"\"{fullPath}\"";
                    psi.WorkingDirectory = "";
                }
                else if (isWebUrl)
                {
                    psi.FileName = targetPath;
                }
                else if (isTargetFolder || isNetworkRoot)
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = $"\"{targetPath}\"";
                }
                else
                {
                    psi.FileName = targetPath;
                    if (!string.IsNullOrEmpty(finalArguments)) psi.Arguments = finalArguments;

                    if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
                        psi.WorkingDirectory = workingDirectory;
                    else
                    {
                        string dir = System.IO.Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                            psi.WorkingDirectory = dir;
                    }
                }

                Task.Run(() =>
                {
                    try
                    {
                        Process.Start(psi);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Launched: {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("canceled"))
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Launch Error: {ex.Message}");
                            if (!SettingsManager.SuppressLaunchWarnings)
                            {
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgLaunchFailed", ex.Message), Strings.DlgLaunchError);
                                }));
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("canceled"))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Launch Error: {ex.Message}");
                    if (!SettingsManager.SuppressLaunchWarnings)
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgLaunchFailed", ex.Message), Strings.DlgLaunchError);
                }
            }
        }

        // Determines if a path is a web URL
        private static bool IsWebUrl(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult))
            {
                string scheme = uriResult.Scheme.ToLower();
                return scheme == Uri.UriSchemeHttp ||
                       scheme == Uri.UriSchemeHttps ||
                       scheme == Uri.UriSchemeFtp ||
                       scheme == Uri.UriSchemeMailto;
            }
            return false;
        }

        // Determines if a path is a special Windows path
        private static bool IsSpecialWindowsPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Support Custom App Protocols globally (steam://, spotify:, discord:, etc.)
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult))
            {
                string scheme = uriResult.Scheme.ToLower();
                if (scheme != Uri.UriSchemeFile &&
                    scheme != Uri.UriSchemeHttp &&
                    scheme != Uri.UriSchemeHttps &&
                    scheme != Uri.UriSchemeFtp &&
                    scheme != Uri.UriSchemeMailto)
                {
                    return true;
                }
            }

            return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("::") ||
                   IsControlPanelCommand(path);
        }

        // 控制台指令：control、control.exe，或其後接參數（例如 control /name Microsoft.Mouse）。
        // 以 control 開頭的一般檔名（例如 controller-說明.txt）不算，否則會跳過「目標是否存在」的檢查。
        private static bool IsControlPanelCommand(string path)
        {
            string trimmed = path.Trim();
            foreach (string command in new[] { "control.exe", "control" })
            {
                if (trimmed.Equals(command, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Extracts the URL from a .url file
        private static string ExtractUrlFromUrlFile(string urlFilePath)
        {
            try
            {
                if (!System.IO.File.Exists(urlFilePath))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"URL file not found: {urlFilePath}");
                    return null;
                }

                string[] lines = System.IO.File.ReadAllLines(urlFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = line.Substring(4).Trim(); // Remove "URL=" prefix
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Extracted URL from {urlFilePath}: {url}");
                        return url;
                    }
                }

                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"No URL= line found in .url file: {urlFilePath}");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error reading .url file {urlFilePath}: {ex.Message}");
                return null;
            }
        }

        // Helper method for network path detection
        // Helper method for network path detection
        public static bool IsNetworkPath(string filePath)
        {
            try
            {
                bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";

                if (isShortcut)
                {
                    // --- HANG FIX: Bypassed Utility.GetShortcutTarget(filePath) ---
                    // We use a fast binary scan to detect UNC paths without triggering Windows Network timeouts.
                    if (System.IO.File.Exists(filePath))
                    {
                        byte[] fileContent = System.IO.File.ReadAllBytes(filePath);
                        string asciiContent = System.Text.Encoding.ASCII.GetString(fileContent);
                        string unicodeContent = System.Text.Encoding.Unicode.GetString(fileContent);

                        bool isUncPath = asciiContent.Contains("\\\\") || unicodeContent.Contains("\\\\");

                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Shortcut {filePath} binary scan for UNC, IsUNC: {isUncPath}");
                        return isUncPath;
                    }
                }
                else
                {
                    // For direct paths, check if it's UNC
                    bool isUncPath = filePath.StartsWith("\\\\");
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Direct path {filePath}, IsUNC: {isUncPath}");
                    return isUncPath;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling, $"Error checking if {filePath} is network path: {ex.Message}");
            }
            return false;
        }
    }
}
