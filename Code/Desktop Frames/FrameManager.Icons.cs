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
    // FrameManager 分檔：Icons（自 FrameManager.cs 原樣搬移，未修改方法內容；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        /// <summary>
        /// 自適應圖示網格排版：當收納框縮小或調整寬度時，平滑壓縮與均分每列圖示槽位寬度與左右間距，
        /// 避免傳統固定寬度 WrapPanel 提早換行造成右側大片留白或文字裁切。
        /// </summary>
        public static void UpdateAdaptiveIconGrid(WrapPanel wpcont)
        {
            if (wpcont == null || wpcont.Children.Count == 0) return;
            try
            {
                int iconSpacing = 5;
                double iconWidth = 40;

                dynamic settings = null;
                if (wpcont.Tag != null)
                {
                    string fId = wpcont.Tag.ToString();
                    settings = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == fId);
                }

                if (settings != null)
                {
                    try { iconSpacing = Convert.ToInt32(settings.IconSpacing?.ToString() ?? "5"); } catch { }
                    string sizeVal = settings.IconSize?.ToString() ?? "Medium";
                    switch (sizeVal)
                    {
                        case "Tiny": iconWidth = 24; break;
                        case "Small": iconWidth = 32; break;
                        case "Large": iconWidth = 48; break;
                        case "Huge": iconWidth = 64; break;
                        default: iconWidth = 40; break;
                    }
                }

                double baseCellWidth = Math.Max(62.0, iconWidth + 22.0);
                double baseMarginX = Math.Max(2.0, iconSpacing);
                double baseMarginY = Math.Max(2.0, iconSpacing - 1);
                double idealSlotWidth = baseCellWidth + (baseMarginX * 2.0);

                double minCellWidth = Math.Max(48.0, iconWidth + 10.0);
                double minMarginX = Math.Max(2.0, Math.Floor(baseMarginX * 0.5));
                double minSlotWidth = minCellWidth + (minMarginX * 2.0);

                double availWidth = wpcont.ActualWidth;
                if (availWidth <= 10)
                {
                    var sv = wpcont.Parent as ScrollViewer;
                    if (sv != null && sv.ActualWidth > 20)
                    {
                        availWidth = sv.ActualWidth - wpcont.Margin.Left - wpcont.Margin.Right - 4;
                    }
                    else
                    {
                        var win = FindVisualParent<NonActivatingWindow>(wpcont);
                        if (win != null)
                        {
                            double w = win.ActualWidth > 20 ? win.ActualWidth : win.Width;
                            if (w > 24) availWidth = w - 24;
                        }
                    }
                }

                var visiblePanels = wpcont.Children.OfType<StackPanel>()
                    .Where(p => p.Visibility != Visibility.Collapsed)
                    .ToList();

                if (visiblePanels.Count == 0) return;

                double targetCellWidth = baseCellWidth;
                double targetMarginX = baseMarginX;

                if (availWidth >= minSlotWidth)
                {
                    int idealCols = Math.Max(1, (int)Math.Floor((availWidth + 0.5) / idealSlotWidth));
                    int targetCols = idealCols;

                    // 若框稍微縮小但以緊湊間距仍可容納多 1 欄，優先維持原欄數並平滑收斂間距，避免突兀換行
                    if (visiblePanels.Count > idealCols && (idealCols + 1) * minSlotWidth <= availWidth + 0.5)
                    {
                        targetCols = idealCols + 1;
                    }

                    bool singleRowFitsIdeally = visiblePanels.Count <= targetCols &&
                                                (visiblePanels.Count * idealSlotWidth) <= (availWidth + 1.0);

                    if (!singleRowFitsIdeally)
                    {
                        int activeCols = Math.Max(1, Math.Min(visiblePanels.Count, targetCols));
                        double rawSlotWidth = Math.Floor((availWidth - 1.0) / activeCols);
                        double clampedSlotWidth = Math.Clamp(rawSlotWidth, minSlotWidth, idealSlotWidth + 14.0);

                        double span = Math.Max(1.0, idealSlotWidth - minSlotWidth);
                        double ratio = Math.Clamp((clampedSlotWidth - minSlotWidth) / span, 0.0, 1.25);

                        targetMarginX = Math.Round(Math.Clamp(minMarginX + (baseMarginX - minMarginX) * ratio, 2.0, baseMarginX + 3.0));
                        targetCellWidth = Math.Max(minCellWidth, clampedSlotWidth - (targetMarginX * 2.0));
                    }
                }

                foreach (var sp in visiblePanels)
                {
                    if (Math.Abs(sp.Width - targetCellWidth) > 0.5)
                        sp.Width = targetCellWidth;

                    var m = sp.Margin;
                    if (Math.Abs(m.Left - targetMarginX) > 0.5 || Math.Abs(m.Top - baseMarginY) > 0.5)
                        sp.Margin = new Thickness(targetMarginX, baseMarginY, targetMarginX, baseMarginY);

                    var lbl = sp.Children.OfType<TextBlock>().LastOrDefault();
                    if (lbl != null)
                    {
                        double lblMax = Math.Max(44.0, targetCellWidth - 2.0);
                        if (Math.Abs(lbl.MaxWidth - lblMax) > 0.5)
                            lbl.MaxWidth = lblMax;
                    }
                }
            }
            catch { }
        }

        public static void AddIcon(dynamic icon, WrapPanel wpcont, dynamic frameContext = null)
        {
            // 1. EXTRACT DATA
            IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                ? dict
                : ((JObject)icon).ToObject<IDictionary<string, object>>();

            string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
            bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];
            bool isLink = iconDict.ContainsKey("IsLink") && (bool)iconDict["IsLink"];

            // Fail-Safe Network Detection
            bool isNetwork = iconDict.ContainsKey("IsNetwork") && (bool)iconDict["IsNetwork"];
            if (!isNetwork && !string.IsNullOrEmpty(filePath))
            {
                isNetwork = IsNetworkPath(filePath);
            }

            // --- STEP 1: Determine Settings Context (Always prefer live FrameData by Id) ---
            dynamic settings = null;
            try
            {
                string frameId = frameContext?.Id?.ToString() ?? wpcont.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId))
                {
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(wpcont);
                    frameId = win?.Tag?.ToString();
                }
                if (!string.IsNullOrEmpty(frameId))
                {
                    settings = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == frameId);
                }
            }
            catch { }
            if (settings == null) settings = frameContext;

            // --- NEW: Identify frame Type (Portal vs Data) ---
            bool isPortal = false;
            if (settings != null)
            {
                try
                {
                    // Handle both JObject (JSON) and ExpandoObject (Runtime)
                    if (settings is JObject jObj)
                        isPortal = jObj["ItemsType"]?.ToString() == "Portal";
                    else
                        isPortal = settings.ItemsType?.ToString() == "Portal";
                }
                catch { }
            }

            // --- STEP 2: Read Customization Settings ---
            int iconSpacing = 5;
            double iconWidth = 40, iconHeight = 40;
            bool grayscale = false;
            string textColorName = null;
            bool disableShadow = false;

            if (settings != null)
            {
                try { iconSpacing = Convert.ToInt32(settings.IconSpacing?.ToString() ?? "5"); } catch { }

                string sizeVal = settings.IconSize?.ToString() ?? "Medium";
                switch (sizeVal)
                {
                    case "Tiny": iconWidth = iconHeight = 24; break;
                    case "Small": iconWidth = iconHeight = 32; break;
                    case "Large": iconWidth = iconHeight = 48; break;
                    case "Huge": iconWidth = iconHeight = 64; break;
                    default: iconWidth = iconHeight = 40; break;
                }

                try { grayscale = settings.GrayscaleIcons?.ToString().ToLower() == "true"; } catch { }
                try { textColorName = settings.TextColor?.ToString(); } catch { }
                try { disableShadow = settings.DisableTextShadow?.ToString().ToLower() == "true"; } catch { }
            }

            // --- STEP 3: Create UI Elements ---
            double baseCellWidth = Math.Max(62.0, iconWidth + 22.0);
            double baseMarginY = Math.Max(2.0, iconSpacing - 1);
            StackPanel sp = new StackPanel
            {
                Margin = new Thickness(iconSpacing, baseMarginY, iconSpacing, baseMarginY),
                Width = baseCellWidth
            };

            System.Windows.Controls.Image ico = new System.Windows.Controls.Image
            {
                Width = iconWidth,
                Height = iconHeight,
                Margin = new Thickness(4, 4, 4, 2),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(ico, BitmapScalingMode.HighQuality);
            if (SettingsManager.IconVisibilityEffect != IconVisibilityEffect.None)
            {
                ico.Effect = Utility.CreateIconEffect(SettingsManager.IconVisibilityEffect);
            }
            else
            {
                // 環境微陰影：保護淺色與細線條圖示在羊皮紙底板上的清晰輪廓，避免邊界融底
                ico.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.22
                };
            }
            // --- NEW: Intercept Internal Blank Spacer ---
            if (filePath != null && filePath.StartsWith("INTERNAL_BLANK_"))
            {
                string resImage = filePath.StartsWith("INTERNAL_BLANK_DOT") ? "dot.png" : "empty.png";
                ico.Source = new BitmapImage(new Uri($"pack://application:,,,/Resources/{resImage}", UriKind.Absolute));
                sp.Children.Add(ico);

                // Add empty label to maintain exact grid alignment
                TextBlock blankLbl = new TextBlock { Text = " " };
                sp.Children.Add(blankLbl);

                // CRITICAL: Must be transparent to catch mouse clicks/drags
                sp.Background = System.Windows.Media.Brushes.Transparent;

                sp.Tag = new { FilePath = filePath, IsFolder = false, Arguments = (string)null };
                sp.ToolTip = new ToolTip { Content = Strings.TooltipBlankSpacer };
                wpcont.Children.Add(sp);
                UpdateAdaptiveIconGrid(wpcont);
                return; // Skip standard extraction
            }


            // --- LAZY LOAD PREPARATION ---
            bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
            bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";

            // 1. Apply base visual effects immediately so the UI draws fast
            double targetIconOpacity = 1.0;
            if (SettingsManager.ApplyTintToIcons)
            {
                targetIconOpacity = Math.Max(0.2, SettingsManager.TintValue / 100.0);
                ico.Opacity = targetIconOpacity;
            }

            if (grayscale)
            {
                ico.Opacity = 0.6;
                ico.Effect = new DropShadowEffect { Color = Colors.Gray, BlurRadius = 0, ShadowDepth = 0 };
            }

            //// 2. Set an instant placeholder to prevent blank spaces during load
            //ico.Source = isFolder
            //    ? new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"))
            //    : new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));

            // 2. Set an instant placeholder to prevent layout collapsing during load
            // OPTION A: We use your existing transparent 'empty.png' (or null).
            // This reserves the physical space for the grid and catches mouse clicks, 
            // but remains completely invisible until the real icon seamlessly fades in.
            ico.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/placeholder.png", UriKind.Absolute));


            // 3. Assemble Network Overlay
            if (isNetwork)
            {
                Grid iconGrid = new Grid { Width = iconWidth + 8, Height = iconHeight + 8 };
                iconGrid.Children.Add(ico);

                TextBlock networkIndicator = new TextBlock
                {
                    Text = "🔗",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 135, 225)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 2, 0, 0),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, Opacity = 0.7 }
                };
                iconGrid.Children.Add(networkIndicator);
                sp.Children.Add(iconGrid);
            }
            else
            {
                sp.Children.Add(ico);
            }

            // 4. Assemble Text Label
            string displayName = (!iconDict.ContainsKey("DisplayName") || iconDict["DisplayName"] == null)
                ? System.IO.Path.GetFileNameWithoutExtension(filePath)
                : (string)iconDict["DisplayName"];

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(filePath))
            {
                displayName = filePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                      .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                                      .LastOrDefault();
                if (string.IsNullOrWhiteSpace(displayName)) displayName = "Unknown";
            }

            string originalDisplayName = displayName;
            if (displayName.Length > SettingsManager.MaxDisplayNameLength)
                displayName = displayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "...";

            // 預設字體顏色：淺色羊皮紙底板採用深墨炭黑 Ink，確保高對比銳利度與可讀性；透明透光底則維持白字
            System.Windows.Media.Brush textBrush = SettingsManager.TintValue > 0 ? GameTheme.Ink : System.Windows.Media.Brushes.White;
            if (!string.IsNullOrEmpty(textColorName) && textColorName != "Default")
            {
                try { textBrush = new SolidColorBrush(Utility.GetColorFromName(textColorName)); } catch { }
            }

            TextBlock lbl = new TextBlock
            {
                Text = displayName,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = textBrush,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft JhengHei UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                MaxWidth = baseCellWidth - 4.0,
                MaxHeight = 34, // 精確限制最多 2 行（行高 16px * 2 + 2px 容差），超過自動在第 2 行末端顯示省略號（...）
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(3, 2, 3, 4),
                Padding = new Thickness(2, 0, 2, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Opacity = 1.0 // 文字永遠維持 100% 實心，絕不打折以確保最高銳利度與可讀性
            };

            // 保持文字底板乾淨透明，絕不使用覆蓋圖示的矩形遮罩塊
            if (SettingsManager.TintValue == 0 && !disableShadow)
            {
                lbl.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.65 };
            }

            sp.Children.Add(lbl);
            var itemTile = GameTheme.ItemTile();
            sp.SetValue(GameTheme.ItemBackgroundProperty, itemTile);
            sp.Background = itemTile;
            sp.MouseEnter += (s, e) =>
            {
                if (_currentlySelectedIconPanel != sp)
                    sp.Background = GameTheme.ItemHoverBrush;
            };
            sp.MouseLeave += (s, e) =>
            {
                if (_currentlySelectedIconPanel != sp)
                    sp.Background = itemTile;
            };
            sp.Tag = new { FilePath = filePath, IsFolder = isFolder, Arguments = (string)(iconDict.ContainsKey("Arguments") ? iconDict["Arguments"] : null) };
            sp.ToolTip = new ToolTip { Content = originalDisplayName + "\nLoading target..." };

            wpcont.Children.Add(sp); // ADD TO UI INSTANTLY!
            UpdateAdaptiveIconGrid(wpcont);

            // --- 5. BACKGROUND RESOLVER TASK ---
            // This offloads the 30-second WshShell and Directory.Exists network hangs!
            Action backgroundLoader = () =>
            {
                ImageSource shortcutIcon = null;
                string targetPath = null;
                bool targetIsUncRoot = false;

                // Helper to safely create unfrozen background images for the UI
                BitmapImage CreateSafeBitmap(string uriPath)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(uriPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze(); // CRITICAL FIX: Allows passing to UI thread
                    return bmp;
                }

                if (isShortcut || isLink || isUrlFile)
                {
                    if (isUrlFile || isLink)
                    {
                        var urlIcon = GetUrlCustomIcon(filePath);
                        if (urlIcon.Path != null) shortcutIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                    }

                    if (shortcutIcon == null && isShortcut)
                    {
                        // 優先使用 Win32 IShellLinkW (Unicode-safe) 解析目標與自訂圖示，避免 WshShell 將 ● 等字元轉為 ?
                        if (FilePathUtilities.TryResolveShortcutWithShellLinkW(filePath, out string slTarget, out _, out _, out string slIconPath, out int slIconIdx))
                        {
                            targetPath = slTarget;
                            if (!string.IsNullOrEmpty(slIconPath) && System.IO.File.Exists(slIconPath))
                            {
                                shortcutIcon = IconManager.ExtractIconFromFile(slIconPath, slIconIdx);
                            }
                        }

                        if (shortcutIcon == null && string.IsNullOrEmpty(targetPath))
                        {
                            try
                            {
                                WshShell shell = new WshShell();
                                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(System.IO.Path.GetFullPath(filePath));
                                if (!string.IsNullOrEmpty(shortcut.TargetPath) && !shortcut.TargetPath.Contains("?"))
                                    targetPath = shortcut.TargetPath;

                                if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                                {
                                    string[] iconParts = shortcut.IconLocation.Split(',');
                                    string iconPath = iconParts[0];
                                    int iconIndex = 0;
                                    if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex)) iconIndex = parsedIndex;
                                    if (System.IO.File.Exists(iconPath)) shortcutIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (shortcutIcon == null)
                {
                    if (isLink || isUrlFile)
                    {
                        string urlTarget = targetPath;
                        if (string.IsNullOrEmpty(urlTarget))
                        {
                            try
                            {
                                var lines = System.IO.File.ReadAllLines(filePath);
                                var urlLine = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                                if (urlLine != null) urlTarget = urlLine.Substring(4).Trim();
                            }
                            catch { }
                        }

                        if (!string.IsNullOrEmpty(urlTarget) && !urlTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !urlTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            shortcutIcon = IconManager.GetIconForFile(targetPath, filePath, isFolder, isLink, isShortcut, iconDict);
                        }

                        if (shortcutIcon == null) shortcutIcon = CreateSafeBitmap("pack://application:,,,/Resources/link-White.png");
                    }
                    else if (isShortcut)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(targetPath) || targetPath.Contains("?"))
                                targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                            if (IsUncRoot(targetPath)) { targetIsUncRoot = true; isFolder = true; isNetwork = true; }
                        }
                        catch { }

                        bool targetIsFolder = false;
                        bool targetExists = false;
                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            if (targetIsUncRoot || targetPath.StartsWith(@"\\"))
                            {
                                targetIsFolder = true;
                                targetExists = true;
                            }
                            else
                            {
                                targetIsFolder = System.IO.Directory.Exists(targetPath);
                                targetExists = targetIsFolder || System.IO.File.Exists(targetPath);
                            }
                        }

                        if (!targetExists && ScrapeLnkForNetworkRoot(filePath))
                        {
                            targetIsFolder = true; isNetwork = true; isFolder = true; targetIsUncRoot = true;
                        }

                        if (targetExists) shortcutIcon = Utility.GetShellIcon(targetPath, targetIsFolder);
                        else shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
                    }
                    else
                    {
                        shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
                    }
                }

                if (shortcutIcon == null)
                {
                    if (isFolder)
                    {
                        bool isUncRoot = targetIsUncRoot || IsUncRoot(filePath);
                        bool valid = isUncRoot || FilePathUtilities.DoesFolderExist(filePath, isFolder);
                        if (!valid && isShortcut && !string.IsNullOrEmpty(targetPath)) valid = IsUncRoot(targetPath) || System.IO.Directory.Exists(targetPath);
                        shortcutIcon = valid ? CreateSafeBitmap("pack://application:,,,/Resources/folder-White.png") : CreateSafeBitmap("pack://application:,,,/Resources/folder-WhiteX.png");
                    }
                    else shortcutIcon = CreateSafeBitmap("pack://application:,,,/Resources/file-WhiteX.png");
                }

                // CRITICAL FIX: Freeze dynamically extracted shell icons as well
                if (shortcutIcon != null && shortcutIcon.CanFreeze && !shortcutIcon.IsFrozen)
                {
                    shortcutIcon.Freeze();
                }

                // [Tooltip Parsing]
                string toolTipText = originalDisplayName;
                string resolvedTarget = filePath;

                if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk") resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                else if (System.IO.Path.GetExtension(filePath).ToLower() == ".url")
                {
                    try { string content = System.IO.File.ReadAllText(filePath); var match = System.Text.RegularExpressions.Regex.Match(content, @"URL=([^\r\n]+)"); if (match.Success) resolvedTarget = match.Groups[1].Value.Trim(); } catch { }
                }
                if (string.IsNullOrEmpty(resolvedTarget)) resolvedTarget = filePath;

                string displayTarget = resolvedTarget;
                if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
                {
                    string packageId = displayTarget.Split('!')[0]; int hashIndex = packageId.IndexOf('_'); if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex); displayTarget = $"Windows App ({packageId})";
                }
                if (resolvedTarget != filePath) toolTipText += $"\nTarget: {displayTarget}"; else toolTipText += $"\nLocation: {displayTarget}";

                // 6. PUSH TO UI
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (shortcutIcon != null)
                    {
                        if (grayscale && shortcutIcon is BitmapSource bmp)
                        {
                            var formatBmp = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, BitmapPalettes.Gray256, 0);
                            formatBmp.Freeze(); // CRITICAL FIX
                            ico.Source = formatBmp;
                        }
                        else ico.Source = shortcutIcon;
                    }
                    sp.ToolTip = new ToolTip { Content = toolTipText };
                }), System.Windows.Threading.DispatcherPriority.Background);
            };


            // --- SAFE EXECUTION WRAPPER ---
            Action safeBackgroundExecution = () =>
            {
                try { backgroundLoader(); }
                catch { /* Catch all unexpected errors so they NEVER crash the app */ }
            };

            if (isNetwork)
            {
                // CRITICAL FIX: COM Objects (WshShell) MUST run on an STA thread. Task.Run is MTA!
                System.Threading.Thread staThread = new System.Threading.Thread(new System.Threading.ThreadStart(safeBackgroundExecution));
                staThread.SetApartmentState(System.Threading.ApartmentState.STA); // Set to Single Threaded Apartment
                staThread.IsBackground = true;
                staThread.Start();
            }
            else
            {
                // Local files are fast, run immediately to avoid visual "pop in" delays
                safeBackgroundExecution();
            }
        }

        /// <summary>
        /// HELPER: Extracts a Network Root path (e.g. \\Server) from a .lnk file by reading raw content.
        /// Used when Windows APIs fail to resolve the target.
        /// </summary>
        private static string GetScrapedNetworkPath(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath) || !System.IO.File.Exists(lnkPath)) return null;

                string fileContent;
                // Read with Share.ReadWrite to avoid locking issues
                using (var fs = new System.IO.FileStream(lnkPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.Default))
                {
                    fileContent = sr.ReadToEnd();
                }

                // Regex to capture \\Servername or \\1.2.3.4
                // Matches start of line or null-preceded, followed by \\, then valid host chars, ending with null or newline
                var regex = new System.Text.RegularExpressions.Regex(@"(^|\0)\\\\([a-zA-Z0-9\.\-_]+)(\x00|$)", System.Text.RegularExpressions.RegexOptions.Multiline);
                var match = regex.Match(fileContent);

                if (match.Success)
                {
                    // Clean up the match (remove leading nulls if any) to get just "\\Server"
                    string raw = match.Value.Trim('\0', '\r', '\n');
                    if (raw.StartsWith(@"\\")) return raw;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Scrape path failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// SAFE FALLBACK: Scans the raw binary content of a .lnk file for Network Root patterns (e.g. \\192.168.1.10).
        /// This bypasses Windows API validation failures for server roots or offline locations.
        /// </summary>
        private static bool ScrapeLnkForNetworkRoot(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath) || !System.IO.File.Exists(lnkPath)) return false;

                // 1. Read file safely with a timeout mechanism (handled by standard stream opening)
                // Use default encoding to capture ANSI/ASCII strings embedded in the binary
                string fileContent;
                using (var fs = new System.IO.FileStream(lnkPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.Default))
                {
                    fileContent = sr.ReadToEnd();
                }

                // 2. Define Regex for UNC Root: Matches \\Server or \\192.168.1.1
                // Explanation:
                // \\\\       -> Literal "\\"
                // [^\\]+     -> Any character that is NOT a backslash (The server name/IP)
                // (\x00|$)   -> Ends with a null byte or end of string (typical in binary formats)
                var networkRootRegex = new System.Text.RegularExpressions.Regex(@"^\\\\([a-zA-Z0-9\.\-_]+)(\x00|$)", System.Text.RegularExpressions.RegexOptions.Multiline);

                // 3. Scan the content
                // We scan for specific "unc" prefixes often found in LNK structures or just the raw path
                if (networkRootRegex.IsMatch(fileContent)) return true;

                // Fallback simple string check for the user's specific case (\\IP) if regex is too strict on binary noise
                // We look for "\\" followed by a digit (IP) that appears in the file
                int index = fileContent.IndexOf(@"\\");
                if (index >= 0 && index + 3 < fileContent.Length)
                {
                    char nextChar = fileContent[index + 2];
                    // If it looks like \\1... or \\a... it's likely a path. 
                    // To be safe, we check if it DOESN'T look like a file path (no ":\")
                    bool hasColon = fileContent.IndexOf(@":\", index) == index + 2; // e.g. C:\ check
                    if (!hasColon) return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // SANITIZATION: Never let a scraping error break the UI loading
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Lnk scraping suppressed error: {ex.Message}");
                return false;
            }
        }

        // --- HELPER: Manual Icon Extraction for .url Files ---
        private static (string Path, int Index) GetUrlCustomIcon(string urlPath)
        {
            try
            {
                if (!System.IO.File.Exists(urlPath)) return (null, 0);
                var lines = System.IO.File.ReadAllLines(urlPath);
                string iconFile = null;
                int iconIndex = 0;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconFile = trimmed.Substring(9).Trim();
                    else if (trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(trimmed.Substring(10).Trim(), out iconIndex);
                }

                if (!string.IsNullOrEmpty(iconFile)) return (iconFile, iconIndex);
            }
            catch { }
            return (null, 0);
        }

        public static void EditItem(dynamic icon, dynamic frame, NonActivatingWindow win)
        {
            IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();
            string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
            string displayName = iconDict.ContainsKey("DisplayName") ? (string)iconDict["DisplayName"] : System.IO.Path.GetFileNameWithoutExtension(filePath);

            // --- BUG FIX: Handle empty display names for UNC roots in editor ---
            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(filePath))
            {
                displayName = filePath.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault();
                if (string.IsNullOrWhiteSpace(displayName)) displayName = "Unknown";
            }

            // Allow .lnk and .url
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            bool isEditable = ext == ".lnk" || ext == ".url";

            if (!isEditable)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgEditNotAvailable, Strings.DlgInfo);
                return;
            }

            var editWindow = new EditShortcutWindow(filePath, displayName);

            // Note: The EditWindow calls 'UpdateFrameDataForIcon' internally when Save is clicked.
            // We don't need to duplicate the saving logic here.
            if (editWindow.ShowDialog() == true)
            {
                string newDisplayName = editWindow.NewDisplayName;

                // Update local memory reference immediately for responsiveness
                iconDict["DisplayName"] = newDisplayName;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"EditItem: Local display name updated to {newDisplayName}");

                // UI Refresh Logic
                if (win == null) return;

                WrapPanel wpcont = null;
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer != null) wpcont = scrollViewer.Content as WrapPanel;

                if (wpcont == null) wpcont = FrameUtilities.FindWrapPanel(win);

                if (wpcont != null)
                {
                    var sp = wpcont.Children.OfType<StackPanel>()
                        .FirstOrDefault(s => s.Tag != null && s.Tag.GetType().GetProperty("FilePath")?.GetValue(s.Tag)?.ToString() == filePath);

                    if (sp != null)
                    {
                        RefreshSingleIconComplete(sp, filePath, newDisplayName, win);
                    }
                    else
                    {
                        // Fallback: Rebuild if not found (e.g. Tab switch might have hidden it)
                        // In most cases RefreshSingleIconComplete handles it, or a full frame refresh will catch it later.
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, "StackPanel not found for immediate refresh, data is saved though.");
                    }
                }
            }
        }

        private static void BackupOrRestoreShortcut(string filePath, bool targetExists, bool isFolder)
        {
            // FIX: Use ProfileManager to get the path relative to the active profile
            string tempShortcutsDir = ProfileManager.GetProfileFilePath("Temp Shortcuts");

            string backupFileName = System.IO.Path.GetFileName(filePath);
            string backupPath = System.IO.Path.Combine(tempShortcutsDir, backupFileName);

            try
            {
                // Ensure TempShortcuts directory exists
                if (!Directory.Exists(tempShortcutsDir))
                {
                    Directory.CreateDirectory(tempShortcutsDir);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Created TempShortcuts directory: {tempShortcutsDir}");
                }

                if (!targetExists)
                {
                    // Backup shortcut if target is missing and not already backed up
                    if (System.IO.File.Exists(filePath) && !System.IO.File.Exists(backupPath))
                    {
                        System.IO.File.Copy(filePath, backupPath, true);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Backed up shortcut {filePath} to {backupPath}");
                    }
                }
                else
                {
                    // Restore shortcut from backup if target exists
                    if (System.IO.File.Exists(backupPath))
                    {
                        // Verify the backup has a custom icon before restoring
                        WshShell shell = new WshShell();
                        IWshShortcut backupShortcut = (IWshShortcut)shell.CreateShortcut(backupPath);
                        if (!string.IsNullOrEmpty(backupShortcut.IconLocation))
                        {
                            System.IO.File.Copy(backupPath, filePath, true);
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Restored shortcut {filePath} from {backupPath} with custom icon");
                        }
                        // Delete backup
                        System.IO.File.Delete(backupPath);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Deleted backup {backupPath} after restoration");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error in BackupOrRestoreShortcut for {filePath}: {ex.Message}");
            }
        }

        /// Refresh frame using the same approach as CustomizeFrameForm
        /// This ensures consistent sizing and behavior
        /// </summary>
        public static void RefreshFrameUsingFormApproach(NonActivatingWindow win, dynamic frame)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Refreshing frame using form approach for '{frame.Title}'");

                // Find the WrapPanel
                var wrapPanel = FindWrapPanel(win);
                if (wrapPanel == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Cannot find WrapPanel for frame refresh");
                    return;
                }

                // Clear existing icons
                wrapPanel.Children.Clear();

                // TABS FEATURE: Check if tabs are enabled and load from appropriate source
                bool tabsEnabled = frame.TabsEnabled?.ToString().ToLower() == "true";
                JArray items = null;

                if (tabsEnabled)
                {
                    // Load from current tab
                    var tabs = frame.Tabs as JArray ?? new JArray();
                    int currentTab = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");

                    if (currentTab >= 0 && currentTab < tabs.Count)
                    {
                        var activeTab = tabs[currentTab] as JObject;
                        if (activeTab != null)
                        {
                            items = activeTab["Items"] as JArray ?? new JArray();
                            string tabName = activeTab["TabName"]?.ToString() ?? $"Tab {currentTab}";
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                                $"Refreshing icons from tab '{tabName}' for frame '{frame.Title}'");
                        }
                    }
                }
                else
                {
                    // Load from main Items array (existing behavior)
                    items = frame.Items as JArray;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        $"Refreshing icons from main Items for frame '{frame.Title}'");
                }

                if (items != null)
                {
                    // Sort items by DisplayOrder and add them (SAME AS FORM)
                    var sortedItems = items
                        .OfType<JObject>()
                        .OrderBy(item => item["DisplayOrder"]?.Value<int>() ?? 0)
                        .ToList();

                    foreach (dynamic item in sortedItems)
                    {
                        // Use the SAME method as the working form
                        AddIcon(item, wrapPanel);

                        // Add basic event handlers (SAME AS FORM)
                        if (wrapPanel.Children.Count > 0)
                        {
                            var sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                string filePath = item.Filename?.ToString() ?? "Unknown";
                                bool isFolder = item.IsFolder?.ToString().ToLower() == "true";
                                ClickEventAdder(sp, filePath, isFolder);

                                // FIX: Attach the missing Context Menu
                                AttachIconContextMenu(sp, item, frame, win);
                            }
                        }

                    }

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Successfully refreshed {sortedItems.Count} icons for frame '{frame.Title}' using form approach");
                }

                // 刷新後完成版面更新；整區位圖快取的取捨由建立面板時處理。
                win.UpdateLayout();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing frame using form approach: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to find WrapPanel in frame window
        /// </summary>
        private static WrapPanel FindWrapPanel(NonActivatingWindow win)
        {
            try
            {
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                return scrollViewer?.Content as WrapPanel;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error finding WrapPanel: {ex.Message}");
                return null;
            }
        }

        // [TARGET VALIDATION ENGINE - START]
        private static void UpdateIcon(StackPanel sp, string filePath, bool isFolder, string resolvedTargetPath = null)
        {
            if (System.Windows.Application.Current == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (filePath != null && filePath.StartsWith("INTERNAL_BLANK_")) return;

                // 1. OPTIMIZATION: Cache Check (Ensures local proxy file exists)
                bool fileExists = System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath);
                if (!fileExists) return;

                DateTime currentLastWrite;
                try { currentLastWrite = System.IO.File.GetLastWriteTime(filePath); }
                catch { return; }

                bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
                bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";
                bool targetValid = true;

                // 2. TARGET VALIDATION LOGIC (Native File Polling, No COM)
                if (isShortcut && !Utility.IsStoreAppShortcut(filePath))
                {
                    string tPath = resolvedTargetPath ?? FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                    targetValid = !string.IsNullOrEmpty(tPath) &&
                                 (System.IO.File.Exists(tPath) || System.IO.Directory.Exists(tPath));
                }
                else if (!isShortcut)
                {
                    targetValid = System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath);
                }
                // [TARGET VALIDATION ENGINE - END]

                bool isNowBroken = !targetValid;

                if (_iconStates.TryGetValue(filePath, out var lastState))
                {
                    if (lastState.LastWrite == currentLastWrite && lastState.IsBroken == isNowBroken)
                        return; // Cache hit
                }
                _iconStates[filePath] = (currentLastWrite, isNowBroken);

                // 2. Identify Web Link (Do NOT return early anymore)
                bool isWebLink = isUrlFile;
                if (!isWebLink)
                {
                    try
                    {
                        foreach (var frame in FrameDataManager.FrameData)
                        {
                            if (frame.ItemsType?.ToString() == "Data")
                            {
                                bool CheckList(JArray list)
                                {
                                    if (list == null) return false;
                                    foreach (var item in list)
                                    {
                                        string itemPath = item["Filename"]?.ToString();
                                        if (!string.IsNullOrEmpty(itemPath) && string.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase))
                                            return item["IsLink"]?.ToObject<bool>() ?? false;
                                    }
                                    return false;
                                }
                                if (CheckList(frame.Items as JArray)) { isWebLink = true; break; }
                                var tabs = frame.Tabs as JArray;
                                if (tabs != null)
                                {
                                    foreach (var tab in tabs) if (CheckList(tab["Items"] as JArray)) { isWebLink = true; goto FoundLink; }
                                }
                            }
                        }
                    FoundLink:;
                    }
                    catch { }
                }

                System.Windows.Controls.Image ico = sp.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (ico == null) return;

                // 3. MAIN ICON LOGIC
                ImageSource newIcon = null;

                // [TARGET VALIDATION ENGINE - SECONDARY PATCH]
                string targetPath = isShortcut ? (resolvedTargetPath ?? FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath)) : filePath;

                bool targetExists = System.IO.File.Exists(targetPath) || System.IO.Directory.Exists(targetPath);
                bool isTargetFolder = System.IO.Directory.Exists(targetPath);
                if (isShortcut && isTargetFolder) isFolder = true;

                if (isShortcut) BackupOrRestoreShortcut(filePath, targetExists, isFolder);

                
                // --- PRIORITY LOGIC START ---
                // CASE A: WEB LINKS
                if (isWebLink)
                {
                    // 1. Try Custom Icon first
                    var urlIcon = GetUrlCustomIcon(filePath); // <--- USE NEW HELPER
                    if (urlIcon.Path != null)
                    {
                        newIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                    }
                    else
                    {
                        // Try WshShell only if manual failed (legacy fallback)
                        try { /* existing WshShell logic */ } catch { }
                    }

                    // 2. Fallback to Theme or Custom Protocol
                    if (newIcon == null)
                    {
                        // Recheck the actual target inside the file
                        string fileText = "";
                        try { fileText = System.IO.File.ReadAllText(filePath).ToLower(); } catch { }

                        if (fileText.Contains("spotify:") || fileText.Contains("spotify.com"))
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/spotify-White.png"));
                        }
                        else if (fileText.Contains("steam://"))
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/steam-White.png"));
                        }
                        else
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));
                        }
                    }
                }
                // CASE B: FOLDERS
                else if (isFolder)
                {
                    // 1. Broken Target -> White X
                    if (!FilePathUtilities.DoesFolderExist(filePath, isFolder))
                    {
                        newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                    }

                    // 2. Custom Icon
                    if (newIcon == null && isShortcut)
                    {
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                            if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                            {
                                string[] iconParts = shortcut.IconLocation.Split(',');
                                string iconPath = iconParts[0];
                                int iconIndex = 0;
                                if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex))
                                    iconIndex = parsedIndex;

                                if (System.IO.File.Exists(iconPath))
                                    newIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                            }
                        }
                        catch { }
                    }

                    // 3. Theme Fallback
                    if (newIcon == null)
                    {
                        NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                        string frameId = win?.Tag?.ToString();
                        var frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                        bool isPortal = frame != null && frame.ItemsType?.ToString() == "Portal";

                        newIcon = Utility.GetShellIcon(filePath, true);
                        if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                    }
                }
                // CASE C: SHORTCUT FILES (.lnk) - 優先解析，即使目標暫時不可達亦保留捷徑原始圖示
                else if (isShortcut)
                {
                    // 1. 嘗試由捷徑指定的 IconLocation 提取自訂圖示
                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                        if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                        {
                            string[] iconParts = shortcut.IconLocation.Split(',');
                            string iconPath = iconParts[0];
                            int iconIndex = 0;
                            if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex)) iconIndex = parsedIndex;

                            if (System.IO.File.Exists(iconPath))
                                newIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                        }
                    }
                    catch { }

                    // 2. 目標存在時，提取目標之高解析圖示
                    if (newIcon == null && targetPath != null && targetExists)
                    {
                        newIcon = Utility.GetShellIcon(targetPath, isTargetFolder);
                    }

                    // 3. 若目標暫時未就緒（網路磁碟機、NAS 待機、或權限路徑），從 .lnk 自身快取提取真實圖示，絕不隨意塗成紅 X
                    if (newIcon == null)
                    {
                        if (Utility.IsStoreAppShortcut(filePath))
                        {
                            try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                            catch { newIcon = Utility.GetShellIcon(filePath, isFolder); }
                        }
                        else
                        {
                            try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                            catch { }

                            if (newIcon == null)
                            {
                                newIcon = Utility.GetShellIcon(filePath, false);
                            }
                        }
                    }

                    // 4. 僅在捷徑本體完全損毀且目標確實不存在時，才顯示損毀警告圖示
                    if (newIcon == null && !targetExists)
                    {
                        newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                    }
                }
                // CASE D: BROKEN FILES (非捷徑且檔案不存在之一般檔案)
                else if (!targetExists)
                {
                    newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                }
                // CASE E: STANDARD FILES
                //else
                //{
                //    try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                //    catch { newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png")); }
                //}
                // CASE E: STANDARD FILES
                else
                {
                    // 優先使用系統 Shell 高清圖示（Jumbo 256x256 或 ExtraLarge 48x48）
                    newIcon = Utility.GetShellIcon(filePath, false);
                    if (newIcon == null)
                    {
                        try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                        catch
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                        }
                    }
                }


                // Apply
                if (ico.Source != newIcon && newIcon != null)
                {
                    ico.Source = newIcon;
                    if (IconManager.IconCache.ContainsKey(filePath))
                    {
                        IconManager.IconCache[filePath] = newIcon;
                    }
                }
            });
        }

        // Public method to refresh an icon's click handlers after shortcut editing
        // Called by EditShortcutWindow to ensure immediate argument updates
        public static void RefreshIconClickHandlers(string shortcutPath, string newDisplayName)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Refreshing click handlers for edited shortcut: {shortcutPath}");

                // Find all frame windows and locate the icon
                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                bool iconFound = false;

                foreach (var window in windows)
                {
                    // Find the WrapPanel containing icons
                    var wrapPanel = FrameUtilities.FindWrapPanel(window);

                    if (wrapPanel == null) continue;

                    // Find the specific icon StackPanel
                    foreach (StackPanel iconPanel in wrapPanel.Children.OfType<StackPanel>())
                    {
                        var tagData = iconPanel.Tag;
                        if (tagData != null)
                        {
                            string filePath = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                            if (!string.IsNullOrEmpty(filePath) &&
                                string.Equals(System.IO.Path.GetFullPath(filePath), System.IO.Path.GetFullPath(shortcutPath), StringComparison.OrdinalIgnoreCase))
                            {
                                // Found it! Refresh this icon completely
                                RefreshSingleIconComplete(iconPanel, shortcutPath, newDisplayName, window);
                                iconFound = true;
                                break;
                            }
                        }
                    }
                    if (iconFound) break;
                }

                if (!iconFound)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"Could not find icon to refresh for: {shortcutPath}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error refreshing icon click handlers: {ex.Message}");
            }
        }

        // Completely refreshes a single icon with fresh data from the .lnk fil
        private static void RefreshSingleIconComplete(StackPanel iconPanel, string shortcutPath, string newDisplayName, NonActivatingWindow parentWindow)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Performing complete refresh of icon: {shortcutPath}");

                // 1. Read fresh data
                string freshTargetPath = shortcutPath;
                string freshArguments = "";
                bool isFolder = false;
                string workingDirectory = "";

                // Identify Type
                bool isUrl = System.IO.Path.GetExtension(shortcutPath).ToLower() == ".url";
                bool isLnk = System.IO.Path.GetExtension(shortcutPath).ToLower() == ".lnk";

                if (isLnk)
                {
                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                        freshTargetPath = shortcut.TargetPath ?? shortcutPath;
                        freshArguments = shortcut.Arguments ?? "";
                        workingDirectory = shortcut.WorkingDirectory ?? "";
                        isFolder = System.IO.Directory.Exists(freshTargetPath);
                    }
                    catch { }
                }
                else if (isUrl)
                {
                    // For URLs, we can try to extract the clean URL, but IsFolder is always false
                    string url = CoreUtilities.ExtractWebUrlFromFile(shortcutPath);
                    if (!string.IsNullOrEmpty(url)) freshTargetPath = url;
                }

                // 2. Update Tag
                iconPanel.Tag = new
                {
                    FilePath = shortcutPath,
                    IsFolder = isFolder,
                    Arguments = freshArguments
                };

                // 3. Update Text
                if (!string.IsNullOrEmpty(newDisplayName))
                {
                    var textBlock = iconPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null)
                    {
                        string displayText = newDisplayName.Length > SettingsManager.MaxDisplayNameLength
                            ? newDisplayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "..."
                            : newDisplayName;
                        textBlock.Text = displayText;
                    }
                }

                // 4. Update ToolTip
                string displayTarget = freshTargetPath;

                // Clean up MS Store App targets for the tooltip
                if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
                {
                    string packageId = displayTarget.Split('!')[0];
                    int hashIndex = packageId.IndexOf('_');
                    if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex);
                    displayTarget = $"Windows App ({packageId})";
                }

                string toolTipText = $"{newDisplayName ?? System.IO.Path.GetFileNameWithoutExtension(shortcutPath)}\nTarget: {displayTarget}";
                if (!string.IsNullOrEmpty(freshArguments)) toolTipText += $"\nArguments: {freshArguments}";
                iconPanel.ToolTip = new ToolTip { Content = toolTipText };

                // 5. UPDATE ICON IMAGE (The Critical Fix)
                var ico = iconPanel.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (ico != null)
                {
                    ImageSource newIcon = null;

                    // A. Try Manual Extraction for .url (Fixes Runtime Update)
                    if (isUrl)
                    {
                        var urlIcon = GetUrlCustomIcon(shortcutPath);
                        if (urlIcon.Path != null)
                        {
                            newIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                        }
                    }
                    // B. Try WshShell for .lnk
                    else if (isLnk)
                    {
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                            if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                            {
                                string[] parts = shortcut.IconLocation.Split(',');
                                if (System.IO.File.Exists(parts[0]))
                                {
                                    int idx = 0;
                                    if (parts.Length > 1) int.TryParse(parts[1], out idx);
                                    newIcon = IconManager.ExtractIconFromFile(parts[0], idx);
                                }
                            }
                        }
                        catch { }
                    }

                    // C. Fallback to Theme/Shell Icon
                    if (newIcon == null)
                    {
                        if (isUrl)
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));
                        }
                        else if (isFolder)
                        {
                            // Check for broken target
                            if (!FilePathUtilities.DoesFolderExist(shortcutPath, true))
                                newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                            else
                            {
                                newIcon = Utility.GetShellIcon(freshTargetPath ?? shortcutPath, true);
                                if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                            }
                        }
                        else
                        {
                            // Standard file fallback
                            newIcon = Utility.GetShellIcon(freshTargetPath, false);
                            if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                        }
                    }

                    // Apply and Update Cache
                    if (newIcon != null)
                    {
                        ico.Source = newIcon;
                        IconManager.IconCache[shortcutPath] = newIcon; // Force update cache
                    }
                }

                // 6. Re-attach Events and Update Data
                ClickEventAdder(iconPanel, shortcutPath, isFolder, freshArguments);
                UpdateFrameDataForIcon(shortcutPath, newDisplayName, parentWindow);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Complete icon refresh successful for: {shortcutPath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error in complete icon refresh: {ex.Message}");
            }
        }

        // Updates frame JSON data after icon refresh
        private static void UpdateFrameDataForIcon(string shortcutPath, string newDisplayName, NonActivatingWindow parentWindow)
        {
            try
            {
                if (string.IsNullOrEmpty(newDisplayName)) return;

                string frameId = parentWindow.Tag?.ToString();
                if (string.IsNullOrEmpty(frameId)) return;

                var frame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                if (frame == null) return;

                // FIX: Search for the item in BOTH Main Items and ALL Tabs
                bool found = false;

                // Helper function to search and update a list
                bool SearchList(JArray list)
                {
                    if (list == null) return false;
                    foreach (var item in list)
                    {
                        string itemFilename = item["Filename"]?.ToString();
                        if (!string.IsNullOrEmpty(itemFilename) &&
                            string.Equals(System.IO.Path.GetFullPath(itemFilename), System.IO.Path.GetFullPath(shortcutPath), StringComparison.OrdinalIgnoreCase))
                        {
                            item["DisplayName"] = newDisplayName;
                            return true; // Found and updated
                        }
                    }
                    return false;
                }

                // 1. Check Main Items
                if (SearchList(frame.Items as JArray)) found = true;

                // 2. Check Tabs (Always check tabs to keep Tab 0 and Main Items completely synced)
                var tabs = frame.Tabs as JArray;
                if (tabs != null)
                {
                    foreach (var tab in tabs)
                    {
                        if (SearchList(tab["Items"] as JArray))
                        {
                            found = true;
                            // We purposefully do NOT break here to ensure all instances across all tabs are updated
                        }
                    }
                }

                if (found)
                {
                    FrameDataManager.SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Updated JSON DisplayName for: {shortcutPath}");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Could not find item in JSON to update name: {shortcutPath}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error updating frame JSON: {ex.Message}");
            }
        }
    }
}
