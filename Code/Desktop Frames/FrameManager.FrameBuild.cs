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
    // FrameManager 分檔：CreateFrame 拆出的建構步驟（第二階段；using 區塊必須與 FrameManager.cs 一致）
    public static partial class Framemanager
    {
        // CreateFrame 各建構步驟共用的資料。原本是 CreateFrame 的區域變數，被多個事件處理器捕捉；
        // 改放在同一個物件上，事件觸發時讀到的仍是同一份最新值，行為與捕捉區域變數相同。
        private sealed class FrameBuildContext
        {
            public dynamic Frame;
            public TargetChecker TargetChecker;
            public TextBox TitleTextBox;
            // 重新命名的提交動作：在 CreateFrame 後段才指定，♥、鎖頭與標題的點擊事件在觸發當下讀取
            public Action CommitRename;
            // 標題文字：在 CreateFrame 中段才建立，面板選單「重新命名」等事件在觸發當下讀取
            public Label TitleLabel;
        }

        // 外框：邊框顏色與粗細、Q 版面板底、尺寸變化時的內距、雙擊開啟搜尋
        private static Border BuildFrameBorder(FrameBuildContext ctx, DockPanel dp)
        {
            dynamic frame = ctx.Frame;
            // Get frame border color and thickness from frame data
            System.Windows.Media.Brush borderBrush = null;
            double borderThickness = 0;
            try
            {
                string borderColorName = frame.FrameBorderColor?.ToString();
                int customThickness = Convert.ToInt32(frame.FrameBorderThickness?.ToString() ?? "2");
                if (!string.IsNullOrEmpty(borderColorName))
                {
                    var borderColor = Utility.GetColorFromName(borderColorName);
                    borderBrush = new SolidColorBrush(borderColor);
                    borderThickness = customThickness; // Use custom thickness
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Applied border color '{borderColorName}' with thickness {borderThickness} to frame '{frame.Title}'");
                }
                else if (customThickness > 0)
                {
                    // Even without color, apply thickness with default color
                    borderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                    borderThickness = customThickness;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Applied default border with thickness {borderThickness} to frame '{frame.Title}'");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Error applying border: {ex.Message}");
                borderBrush = null;
                borderThickness = 0;
            }
            Border cborder = new GameSkin
            {
                Skin = GameSkinKind.Panel,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0)),
                // --- 現代磨砂 Dock 風格：預設 14px 柔和圓角；直角模式維持 0px ---
                CornerRadius = SettingsManager.FramesWithNoRoundCorners ? new CornerRadius(0) : new CornerRadius(14),
                BorderBrush = borderBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), // 晶透微高光透光邊框
                BorderThickness = new Thickness(borderThickness > 0 ? borderThickness : 1), // 預設 1px 細膩邊界
                Child = dp
            };
            cborder.BorderBrush = string.IsNullOrEmpty(GetSafeProperty(frame, "FrameBorderColor")) ? GameTheme.Outline : borderBrush;
            cborder.BorderThickness = new Thickness(0);
            cborder.Padding = new Thickness(16, 8, 16, 12);
            cborder.ClipToBounds = true;
            cborder.SizeChanged += (sender, args) =>
            {
                cborder.Padding = cborder.ActualHeight < 60 ? new Thickness(8, 1, 8, 0) : new Thickness(16, 8, 16, 12);
                cborder.InvalidateVisual();
            };
            cborder.SnapsToDevicePixels = true;
            // Add Double Click Handler
            cborder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SearchFormManager.ToggleSearch();
                    e.Handled = true;
                }
            };
            ApplyFrameBorderAccent(cborder, (object)frame);
            return cborder;
        }

        // 自訂外框顏色與粗細：Q 版木框以點陣圖繪製，原生邊框維持 0，改由 GameSkin 在木框內緣畫描邊。
        // 未設定顏色或粗細為 0 時不畫（外觀與預設相同）。建立面板與「自訂外觀」即時修改共用。
        internal static void ApplyFrameBorderAccent(Border frameBorder, object frame)
        {
            if (frameBorder is not GameSkin skin) return;
            string colorName = GetSafeProperty(frame, "FrameBorderColor");
            if (!int.TryParse(GetSafeProperty(frame, "FrameBorderThickness"), out int thickness)) thickness = 2;
            skin.BorderThickness = new Thickness(0);
            skin.AccentThickness = Math.Max(0, thickness);
            skin.AccentBrush = !string.IsNullOrEmpty(colorName) && thickness > 0
                ? new SolidColorBrush(Utility.GetColorFromName(colorName))
                : null;
        }

        // 標題列左側的選單圖示（♥）：外觀、滑鼠移入移出淡入淡出、點擊開啟 ♥ 選單（按住 Ctrl 為延伸選單）
        private static TextBlock BuildMenuIcon(FrameBuildContext ctx)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            //  Add heart symbol in top-left corner
            string MenuSymbol = "♥";

            if (SettingsManager.MenuIcon == 0)
            {
                MenuSymbol = "♥";
              
            }
            else if (SettingsManager.MenuIcon == 1)
            {
                MenuSymbol = "☰";
            }
            else if (SettingsManager.MenuIcon == 2)
            {
                MenuSymbol = "≣";
            }
            else if (SettingsManager.MenuIcon == 3)
            {
                MenuSymbol = "𓃑";
            }

            TextBlock heart = new TextBlock
            {
                // Text = "♥",

                Name = "FrameMenuIcon", // New! Name
                Text = MenuSymbol,
                FontSize = 18,
                Foreground = System.Windows.Media.Brushes.White, // Match title and icon text color
                // 透明底加內距：圖示字形本身很小，擴大可點擊範圍（左右 6、上下 3 px）
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(2, 0, 0, 0), // Position neatly inside banner left column
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Opacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(11, 35, 20),
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.75
                }
            };
  
            _heartTextBlocks[frame] = heart;


            heart.MouseEnter += (s, e) =>
            {
                // Remove previous animation 
                heart.BeginAnimation(UIElement.OpacityProperty, null);

                heart.Opacity = 1.0;
            };

            heart.MouseLeave += (s, e) =>
            {
                double targetOpacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100);

                DoubleAnimation fadeBack = new DoubleAnimation
                {
                    From = 1.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromMilliseconds(800)
                };

                heart.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            };

            // Store heart TextBlock reference for this frame
            _heartTextBlocks[frame] = heart;
            // Create and assign heart ContextMenu using centralized builder
      
            heart.ContextMenu = BuildHeartContextMenu(frame, false); // Normal menu by default
                                                                     //// Handle left-click to open heart context menu
                                                                     //heart.MouseLeftButtonDown += (s, e) =>
                                                                     //{
                                                                     // if (e.ChangedButton == MouseButton.Left && heart.ContextMenu != null)
                                                                     // {
                                                                     // heart.ContextMenu.IsOpen = true;
                                                                     // e.Handled = true;
                                                                     // }
                                                                     //};
                                                                     // Handle left-click to open heart context menu
            heart.MouseLeftButtonDown += (s, e) =>
            {


                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    ctx.CommitRename?.Invoke();
                    return;
                }


                if (e.ChangedButton == MouseButton.Left && heart.ContextMenu != null)
                {
                    // Check if Ctrl is pressed for extended menu
                    bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    // DEBUG: Log the Ctrl state
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Heart clicked - Ctrl pressed: {isCtrlPressed}");
                    // Rebuild context menu based on Ctrl state
                    heart.ContextMenu = BuildHeartContextMenu(frame, isCtrlPressed);
                    heart.ContextMenu.IsOpen = true;
                    e.Handled = true;
                }
            };
            return heart;
        }

        // 標題列右側的鎖頭：外觀、滑鼠移入移出淡入淡出、點擊切換鎖定並寫入設定
        private static TextBlock BuildLockIcon(FrameBuildContext ctx)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            // Add a protection symbol in top-right corner


            string LockSymbol = "🛡️";

            if (SettingsManager.LockIcon == 0)
            {
                LockSymbol = "🛡️";
            }
            else if (SettingsManager.LockIcon == 1)
            {
                LockSymbol = "🔑";
            }
            else if (SettingsManager.LockIcon == 2)
            {
                LockSymbol = "🔐";
            }
            else if (SettingsManager.LockIcon == 3)
            {
                LockSymbol = "🔒";
            }

            //MessageBox.Show(SettingsManager.LockIcon +" " + LockSymbol.ToString());


            TextBlock lockIcon = new TextBlock
           
            {

                //     Text = "🔐",
                //     Text = "🔑",
                //     Text = "🔒",
                //     Text = "🔓",
                Name = "FrameLockIcon", // New! Name
                Text = LockSymbol,//"🛡️",
                FontSize = 14,
                Foreground = frame.IsLocked?.ToString().ToLower() == "true" ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 8, 0), // Adjusted for top-right positioning neatly inside banner
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = frame.IsLocked?.ToString().ToLower() == "true" ? "Frame is locked (click to unlock)" : "Frame is unlocked (click to lock)",
                Opacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(11, 35, 20),
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.75
                }
            };

  
            GameTheme.ApplyLockIconStyle(lockIcon, frame.IsLocked?.ToString().ToLower() == "true");

            lockIcon.MouseEnter += (s, e) =>
            {
                // Remove previous animation
                lockIcon.BeginAnimation(UIElement.OpacityProperty, null);

                lockIcon.Opacity = 1.0;
            };

            lockIcon.MouseLeave += (s, e) =>
            {
                double targetOpacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100);

                DoubleAnimation fadeBack = new DoubleAnimation
                {
                    From = 1.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromMilliseconds(800)
                };

                lockIcon.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            };


            // Set initial state without saving to JSON
            UpdateLockState(lockIcon, frame, null, saveToJson: false);
            // Lock icon click handler
            lockIcon.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {

                    // FIX: Directly call CommitRename logic
                    if (titletb.IsVisible)
                    {
                        ctx.CommitRename?.Invoke();
                        return;
                    }

                    // Get the NonActivatingWindow to find the frame by Id
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(lockIcon);
                    if (win == null)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Could not find NonActivatingWindow for lock icon click in frame '{frame.Title}'");
                        return;
                    }
                    // Find the frame in FrameDataManager.FrameData using the window's Tag (Id)
                    string frameId = win.Tag?.ToString();
                    if (string.IsNullOrEmpty(frameId))
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Frame Id is missing for window '{win.Title}'");
                        return;
                    }
                    dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    if (currentFrame == null)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameUpdate, $"Frame with Id '{frameId}' not found in FrameDataManager.FrameData");
                        return;
                    }
                    // Toggle the lock state
                    bool currentState = currentFrame.IsLocked?.ToString().ToLower() == "true";
                    bool newState = !currentState;
                    // Update UI and JSON on the main thread
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateLockState(lockIcon, currentFrame, newState, saveToJson: true);
                    });
                }
            };
            return lockIcon;
        }

        // 標題列底板：四欄（選單圖示、標題、篩選、鎖頭），先放入選單圖示
        private static Grid BuildTitleGrid(TextBlock heart)
        {
            // Create a Grid for the titlebar - move here to ensure it is created before mouse handler
            Grid titleGrid = new Grid
            {
                Background = GameTheme.Accent
            };
            var headerSkin = new GameSkin { Skin = GameSkinKind.Header, IsHitTestVisible = false };
            Grid.SetColumnSpan(headerSkin, 4);
            titleGrid.Children.Add(headerSkin);
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Pixel) }); // Col 0: Menu Icon
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Title
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // Col 2: Filter Icon (Auto width)
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Pixel) }); // Col 3: Lock Icon
            Grid.SetColumn(heart, 0);
            titleGrid.Children.Add(heart);
            return titleGrid;
        }

        // 標題文字：粗體、顏色、字級依面板設定，放在標題列第 2 欄
        private static Label BuildTitleLabel(FrameBuildContext ctx, Grid titleGrid)
        {
            dynamic frame = ctx.Frame;
            bool isBoldTitle = false;
            try
            {
                isBoldTitle = frame.BoldTitleText?.ToString().ToLower() == "true";
            }
            catch { /* Safe fallback */ }
            // Get title text color or use default white
            System.Windows.Media.Brush titleTextBrush = System.Windows.Media.Brushes.White; // Default
            try
            {
                string titleColorName = frame.TitleTextColor?.ToString();
                if (!string.IsNullOrEmpty(titleColorName))
                {
                    var titleColor = Utility.GetColorFromName(titleColorName);
                    titleTextBrush = new SolidColorBrush(titleColor);
                }
            }
            catch
            {
                titleTextBrush = System.Windows.Media.Brushes.White; // Fallback
            }
            // Get title text size from frame data
            double titleFontSize = 12; // Default Medium size
            try
            {
                string titleSizeValue = frame.TitleTextSize?.ToString() ?? "Medium";
                switch (titleSizeValue)
                {
                    case "Small":
                        titleFontSize = 10;
                        break;
                    case "Large":
                        titleFontSize = 16;
                        break;
                    default: // Medium
                        titleFontSize = 12;
                        break;
                }
            }
            catch
            {
                titleFontSize = 12; // Fallback to Medium
            }
            Label titlelabel = new Label
            {
                Content = frame.Title.ToString(),
                Foreground = titleTextBrush, // Changed from hardcoded White
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.SizeAll,
                FontWeight = isBoldTitle ? FontWeights.Bold : FontWeights.SemiBold,
                FontSize = titleFontSize, // Apply custom title text size
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft JhengHei UI"),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(11, 35, 20),
                    BlurRadius = 2.5,
                    ShadowDepth = 1.2,
                    Direction = 270,
                    Opacity = 0.85
                }
            };
            Grid.SetColumn(titlelabel, 1);
            titleGrid.Children.Add(titlelabel);
            return titlelabel;
        }

        // 資料夾面板的篩選列：標題列篩選圖示、下拉選單與套用按鈕；非資料夾面板回傳 null
        private static Grid BuildFilterBar(FrameBuildContext ctx, Grid titleGrid, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            // --- FILTER UI START ---
            // Only add Filter UI for Portal Frames
            Grid filterBar = null;
            if (frame.ItemsType?.ToString() == "Portal")
            {
                // 1. The Filter Icon (Funnel)
                // 1. The Filter Icon (Funnel)
                string initialFilter = GetSafeProperty(frame, "FilterString");
                bool hasInitialFilter = !string.IsNullOrWhiteSpace(initialFilter);

                TextBlock filterIcon = new TextBlock
                {
                    Name = "FrameFilterIcon", // New! Name
                    Text = "❖",
                    FontSize = 18,
                    Foreground = hasInitialFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = Strings.TooltipFilterFiles,
                    Opacity = hasInitialFilter ? 1.0 : Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100)
                };

                filterIcon.MouseEnter += (s, e) =>
                {
                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = 1.0;
                };

                filterIcon.MouseLeave += (s, e) =>
                {
                    // Skip fade out if a filter is actively applied
                    if (filterIcon.Foreground != System.Windows.Media.Brushes.Orange)
                    {
                        double targetOpacity = Math.Max(0.85, (double)SettingsManager.MenuTintValue / 100);
                        DoubleAnimation fadeBack = new DoubleAnimation
                        {
                            From = 1.0,
                            To = targetOpacity,
                            Duration = TimeSpan.FromMilliseconds(300),
                            BeginTime = TimeSpan.FromMilliseconds(800)
                        };
                        filterIcon.BeginAnimation(UIElement.OpacityProperty, fadeBack);
                    }
                };

                Grid.SetColumn(filterIcon, 2);
                titleGrid.Children.Add(filterIcon);


                // 2. The Filter Bar (Stable Version)
                filterBar = new Grid
                {
                    Height = 26,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                ComboBox cmbFilter = new ComboBox
                {
                    IsEditable = true,
                    Height = 24,
                    StaysOpenOnEdit = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(5, 0, 5, 0),
                    Text = GetSafeProperty(frame, "FilterString")
                };

                // Helper: Only Repopulate when needed (prevents freeze loop)
                Action repopulateDropdown = () =>
                {
                    cmbFilter.Items.Clear();

                    // A. History
                    try
                    {
                        string currentId = frame.Id?.ToString();
                        var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == currentId);
                        if (liveFrame != null)
                        {
                            Newtonsoft.Json.Linq.JArray history = null;
                            if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
                                history = jFrame["FilterHistory"] as Newtonsoft.Json.Linq.JArray;
                            else
                                history = liveFrame.GetType().GetProperty("FilterHistory")?.GetValue(liveFrame) as Newtonsoft.Json.Linq.JArray;

                            if (history != null)
                            {
                                foreach (var item in history)
                                {
                                    cmbFilter.Items.Add(new ComboBoxItem
                                    {
                                        Content = item.ToString(),
                                        Tag = item.ToString(),
                                        FontWeight = FontWeights.Bold
                                    });
                                }
                            }
                        }
                    }
                    catch { }

                    // B. Separator (if needed)
                    if (cmbFilter.Items.Count > 0) cmbFilter.Items.Add(new Separator());

                    // C. Standard Presets (From static dictionary)
                    foreach (var kvp in _standardPresets)
                    {
                        cmbFilter.Items.Add(new ComboBoxItem { Content = kvp.Key, Tag = kvp.Value });
                    }
                };

                // FIX 1: Only populate on OPEN, never during selection/typing
                cmbFilter.DropDownOpened += (s, e) => repopulateDropdown();

                // Clear Button
                Button btnClearFilter = new Button
                {
                    Content = "✕",
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Width = 20,
                    ToolTip = Strings.TooltipClearFilter
                };

                filterBar.Children.Add(cmbFilter);
                filterBar.Children.Add(btnClearFilter);
                Grid.SetColumn(btnClearFilter, 1);

                // --- LOGIC SETUP ---

                // Helper to execute/save
                Action<string> commitFilter = (text) =>
                {
                    bool hasFilter = !string.IsNullOrWhiteSpace(text);
                    filterIcon.Foreground = hasFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White;

                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = hasFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100);

                    if (_portalFrames.ContainsKey(frame))
                        _portalFrames[frame].ApplyFilter(text);

                    UpdateFrameProperty(frame, "FilterString", text, "Updated filter");

                    // Update history (logic now handles ignoring presets)
                    UpdateFilterHistory(frame, text);
                };

                // Typing Timer
                System.Windows.Threading.DispatcherTimer debounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                debounceTimer.Tick += (s, e) =>
                {
                    debounceTimer.Stop();
                    // Visual update only while typing
                    if (_portalFrames.ContainsKey(frame)) _portalFrames[frame].ApplyFilter(cmbFilter.Text);

                    bool hasFilter = !string.IsNullOrWhiteSpace(cmbFilter.Text);
                    filterIcon.Foreground = hasFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White;

                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = hasFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100);
                };

                // 1. STYLE FIX (Internal TextBox)
                cmbFilter.Loaded += (s, e) =>
                {
                    var textBox = (TextBox)cmbFilter.Template.FindName("PART_EditableTextBox", cmbFilter);
                    if (textBox != null)
                    {
                        textBox.Background = System.Windows.Media.Brushes.Transparent;
                        textBox.Foreground = System.Windows.Media.Brushes.White;
                        textBox.CaretBrush = System.Windows.Media.Brushes.White;
                        textBox.BorderThickness = new Thickness(0);
                    }
                };

                // 2. TYPING LOGIC
                cmbFilter.KeyUp += (s, e) =>
                {
                    if (e.Key == Key.Up || e.Key == Key.Down) return;

                    if (e.Key == Key.Enter)
                    {
                        commitFilter(cmbFilter.Text); // Save history on Enter
                        cmbFilter.IsDropDownOpen = false;
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                        e.Handled = true;
                        return;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                        e.Handled = true;
                        return;
                    }

                    debounceTimer.Stop();
                    debounceTimer.Start();
                };

                // 3. SELECTION LOGIC (FIXED)
                // We do NOT repopulate here. We just apply the value.
                cmbFilter.SelectionChanged += (s, e) =>
                {
                    if (cmbFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
                    {
                        string selectedFilter = item.Tag.ToString();

                        // Break the event loop by running this later
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            cmbFilter.IsDropDownOpen = false;

                            // Important: Don't set SelectedItem = null here, it causes flickering.
                            // Just force the text and run logic.
                            cmbFilter.Text = selectedFilter;

                            debounceTimer.Stop();
                            commitFilter(selectedFilter);
                        }));
                    }
                };

                // 4. CLEAR LOGIC
                btnClearFilter.Click += (s, e) =>
                {
                    cmbFilter.Text = "";
                    if (_portalFrames.ContainsKey(frame)) _portalFrames[frame].ApplyFilter("");
                    UpdateFrameProperty(frame, "FilterString", "", "Cleared filter");
                    filterIcon.Foreground = System.Windows.Media.Brushes.White;
                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = (double)SettingsManager.MenuTintValue / 100;
                    filterBar.Visibility = Visibility.Collapsed;
                    win.EndKeyboardInteractiveEdit();
                };

                // 5. TOGGLE BAR
                filterIcon.MouseLeftButtonDown += (s, e) =>
                {
                    if (filterBar.Visibility == Visibility.Visible)
                    {
                        if (!string.IsNullOrWhiteSpace(cmbFilter.Text)) commitFilter(cmbFilter.Text); // Save on close
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                    }
                    else
                    {
                        filterBar.Visibility = Visibility.Visible;
                        win.BeginKeyboardInteractiveEdit(cmbFilter);
                    }
                    e.Handled = true;
                };

                // Initial Apply
                if (!string.IsNullOrEmpty(cmbFilter.Text))
                {
                    filterIcon.Foreground = System.Windows.Media.Brushes.Orange;
                    filterIcon.Opacity = 1.0; // Ensure it starts fully visible if active

                    win.Loaded += (s, e) =>
                    {
                        if (_portalFrames.ContainsKey(frame))
                            _portalFrames[frame].ApplyFilter(cmbFilter.Text);
                    };
                }
            }
            // --- FILTER UI END ---
            return filterBar;
        }

        // 重新命名：定義提交動作（寫入面板資料與視窗標題），輸入框 Enter 保存、Esc 取消、失去焦點保存
        private static void BindRenameEvents(FrameBuildContext ctx, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            // --- STEP 4 FIX: Centralized Rename Logic ---

            // 1. Define the Logic ONCE
            ctx.CommitRename = () =>
            {
                // Only run if actually editing
                if (titletb.Visibility != Visibility.Visible) return;

                string originalTitle = frame.Title.ToString();
                string newTitle = titletb.Text;
                string finalTitle = InterCore.ProcessTitleChange(frame, newTitle, originalTitle);

                // Update Data
                string id = frame.Id?.ToString();
                var liveFrame = GetFrameData().FirstOrDefault(f => f.Id?.ToString() == id);

                if (liveFrame != null)
                {
                    if (liveFrame is Newtonsoft.Json.Linq.JObject jFrame)
                        jFrame["Title"] = finalTitle;
                    else
                        liveFrame.Title = finalTitle;
                    frame.Title = finalTitle;
                }

                // Update UI
                ctx.TitleLabel.Content = finalTitle;
                win.Title = finalTitle;
                titletb.Visibility = Visibility.Collapsed;
                ctx.TitleLabel.Visibility = Visibility.Visible;

                // Save
                FrameDataManager.SaveFrameData();
                win.EndKeyboardInteractiveEdit();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename committed: {finalTitle}");
            };

            // 2. Wire up Events to use the central logic
            titletb.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ctx.CommitRename(); // Call shared logic
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Cancel Logic
                    titletb.Text = frame.Title.ToString();
                    titletb.Visibility = Visibility.Collapsed;
                    ctx.TitleLabel.Visibility = Visibility.Visible;
                    win.EndKeyboardInteractiveEdit();
                    e.Handled = true;
                }
            };

            titletb.LostFocus += (sender, e) =>
            {
                ctx.CommitRename(); // Call shared logic
            };
        }

        // 標題列滑鼠：雙擊捲起／展開、單擊放開進入改名、拖曳移動面板（鎖定時不移動）
        private static void BindTitleMouseEvents(FrameBuildContext ctx, Grid titleGrid, NonActivatingWindow win)
        {
            dynamic frame = ctx.Frame;
            TextBox titletb = ctx.TitleTextBox;
            System.Windows.Point? titleMouseDownPoint = null;
            bool titleIsDraggingWindow = false;

            ctx.TitleLabel.MouseDown += (sender, e) =>
            {
                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    ctx.CommitRename?.Invoke();
                    return;
                }

                if (e.ClickCount == 2)
                {
                    titleMouseDownPoint = null;
                    titleIsDraggingWindow = false;
                    // Roll-up/roll-down logic (swapped from Ctrl+Click)
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(ctx.TitleLabel);
                    string frameId = win?.Tag?.ToString();
                    // DebugLog("IMMEDIATE", frameId ?? "UNKNOWN", $"Ctrl+Click FIRST LINE - win.Height:{win?.Height ?? -1:F1}");
                    if (string.IsNullOrEmpty(frameId) || win == null)
                    {
                        // DebugLog("ERROR", frameId ?? "UNKNOWN", "Missing window or frameId in Ctrl+Click");
                        return;
                    }
              
                    dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    if (currentFrame == null)
                    {
                        // DebugLog("ERROR", frameId, "Frame not found in FrameDataManager.FrameData for Ctrl+Click");
                        return;
                    }
                    bool isRolled = currentFrame.IsRolled?.ToString().ToLower() == "true";
                    // Get frame data height (always accurate from SizeChanged handler)
                    double frameHeight = Convert.ToDouble(currentFrame.Height?.ToString() ?? "130");
                    double windowHeight = win.Height;
                    // DebugLog("SYNC_CHECK", frameId, $"Before sync - frameHeight:{frameHeight:F1} | WindowHeight:{windowHeight:F1} | IsRolled:{isRolled}");
                    if (!isRolled)
                    {
                        // ROLLUP: Use frame data height (always current)
                        // DebugLog("ACTION", frameId, "Starting ROLLUP");
                        _framesInTransition.Add(frameId);
                        // DebugLog("TRANSITION", frameId, "Added to transition state");
                        // FINAL FIX: Use frame data height which is always accurate from SizeChanged handler
                        double currentHeight = frameHeight;

                        // --- BUG FIX: The "Stuck at 28" Safeguard ---
                        // If currentHeight is suspiciously small (like the title bar height), it means the 
                        // WPF layout data is stale. We reject it and use the last known good UnrolledHeight.
                        if (currentHeight <= 35)
                        {
                            currentHeight = Convert.ToDouble(currentFrame.UnrolledHeight?.ToString() ?? "130");
                            if (currentHeight <= 35) currentHeight = 130; // Ultimate fallback
                        }

                        // DebugLog("ROLLUP_HEIGHT_SOURCE", frameId, $"Using frame.Height:{frameHeight:F1} (win.Height was stale:{win.Height:F1})");
                        // Save current frame height as UnrolledHeight
                        IDictionary<string, object> frameDict = currentFrame as IDictionary<string, object> ?? ((JObject)currentFrame).ToObject<IDictionary<string, object>>();
                        frameDict["UnrolledHeight"] = currentHeight;
                        frameDict["IsRolled"] = "true";
                        int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                        if (frameIndex >= 0)
                        {
                            FrameDataManager.FrameData[frameIndex] = JObject.FromObject(frameDict);
                        }
                        FrameDataManager.SaveFrameData();
                        // DebugLog("SAVE", frameId, $"Saved ROLLUP state | UnrolledHeight:{currentHeight:F1} | IsRolled:true");
                        // Roll up animation - starts from current height
                        double targetHeight = 28;   //rolled height
                        var heightAnimation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            // DebugLog("ANIMATION", frameId, "ROLLUP animation completed");
                            // Update WrapPanel visibility
                            var border = win.Content as Border;
                            if (border != null)
                            {
                                var dockPanel = border.Child as DockPanel;
                                if (dockPanel != null)
                                {
                                    var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                                    if (scrollViewer != null)
                                    {
                                        var wpcont = scrollViewer.Content as WrapPanel;
                                        if (wpcont != null)
                                        {
                                            wpcont.Visibility = Visibility.Collapsed;
                                            // DebugLog("UI", frameId, "Set WrapPanel visibility to Collapsed");
                                        }
                                    }

                                    // --- NEW: Plugin Pause & Hide ---
                                    if (_activePlugins.TryGetValue(frameId, out var plugin))
                                    {
                                        plugin.Pause();
                                        foreach (UIElement child in dockPanel.Children)
                                        {
                                            if (child is FrameworkElement fe && fe.Name == "FrameMenuIcon") continue;
                                            if (DockPanel.GetDock(child) == Dock.Top) continue;
                                            child.Visibility = Visibility.Collapsed;
                                        }
                                    }
                                }
                            }
                            _framesInTransition.Remove(frameId);

                            // FIX: Release animation lock so manual resizes track correctly later
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = targetHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                        // DebugLog("ANIMATION", frameId, $"Started ROLLUP animation from {currentHeight:F1} to height {targetHeight:F1}");
                    }
                    else
                    {
                        // ROLLDOWN: Roll down to UnrolledHeight
                        double unrolledHeight = Convert.ToDouble(currentFrame.UnrolledHeight?.ToString() ?? "130");
                        // // DebugLog("ACTION", frameId, $"Starting ROLLDOWN to {unrolledHeight:F1}");
                        _framesInTransition.Add(frameId);
                        // DebugLog("TRANSITION", frameId, "Added to transition state");
                        IDictionary<string, object> frameDict = currentFrame as IDictionary<string, object> ?? ((JObject)currentFrame).ToObject<IDictionary<string, object>>();
                        frameDict["IsRolled"] = "false";
                        int frameIndex = FrameDataManager.FrameData.FindIndex(f => f.Id?.ToString() == frameId);
                        if (frameIndex >= 0)
                        {
                            FrameDataManager.FrameData[frameIndex] = JObject.FromObject(frameDict);
                        }
                        FrameDataManager.SaveFrameData();
                        // DebugLog("SAVE", frameId, $"Saved ROLLDOWN state | IsRolled:false | TargetHeight:{unrolledHeight:F1}");
                        var heightAnimation = new DoubleAnimation(win.Height, unrolledHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            // DebugLog("ANIMATION", frameId, "ROLLDOWN animation completed");
                            // Update WrapPanel visibility
                            var border = win.Content as Border;
                            if (border != null)
                            {
                                var dockPanel = border.Child as DockPanel;
                                if (dockPanel != null)
                                {
                                    var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                                    if (scrollViewer != null)
                                    {
                                        var wpcont = scrollViewer.Content as WrapPanel;
                                        if (wpcont != null)
                                        {
                                            wpcont.Visibility = Visibility.Visible;

                                            // --- MULTI-MONITOR INVISIBLE ICON FIX ---
                                            wpcont.UpdateLayout();
                                            wpcont.InvalidateVisual();
                                            wpcont.Opacity = 0.99;
                                            wpcont.Dispatcher.BeginInvoke(new Action(() => { wpcont.Opacity = 1.0; }), System.Windows.Threading.DispatcherPriority.Render);
                                            // DebugLog("UI", frameId, "Set WrapPanel visibility to Visible");
                                        }
                                    }

                                    // --- NEW: Plugin Resume & Show ---
                                    if (_activePlugins.TryGetValue(frameId, out var plugin))
                                    {
                                        foreach (UIElement child in dockPanel.Children)
                                        {
                                            if (child is FrameworkElement fe && fe.Name == "FrameMenuIcon") continue;
                                            if (DockPanel.GetDock(child) == Dock.Top) continue;
                                            child.Visibility = Visibility.Visible;
                                        }
                                        plugin.Resume();
                                    }
                                }
                            }
                            _framesInTransition.Remove(frameId);
                            // FIX: Release animation lock and force real height so SizeChanged updates the JSON properly
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = unrolledHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                        // DebugLog("ANIMATION", frameId, $"Started ROLLDOWN animation to height {unrolledHeight:F1}");
                    }
                    e.Handled = true;
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        titleMouseDownPoint = null;
                        titleIsDraggingWindow = false;
                        titletb.Text = ctx.TitleLabel.Content.ToString();
                        ctx.TitleLabel.Visibility = Visibility.Collapsed;
                        titletb.Visibility = Visibility.Visible;
                        win.BeginKeyboardInteractiveEdit(titletb);
                        titletb.SelectAll();
                        titletb.Focus();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation, $"Focus set to title textbox for frame: {frame.Title}");
                        e.Handled = true;
                    }
                    else
                    {
                        titleMouseDownPoint = e.GetPosition(win);
                        titleIsDraggingWindow = false;
                    }
                }
            };

            ctx.TitleLabel.MouseMove += (sender, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && titleMouseDownPoint.HasValue && !titleIsDraggingWindow)
                {
                    System.Windows.Point curPoint = e.GetPosition(win);
                    if (Math.Abs(curPoint.X - titleMouseDownPoint.Value.X) > 4 || Math.Abs(curPoint.Y - titleMouseDownPoint.Value.Y) > 4)
                    {
                        titleIsDraggingWindow = true;
                        string frameId = win.Tag?.ToString();
                        dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                        bool isLockedFrame = currentFrame?.IsLocked?.ToString().ToLower() == "true";
                        if (!isLockedFrame)
                        {
                            SnapManager.StartDrag(win);
                            try
                            {
                                win.DragMove();
                            }
                            finally
                            {
                                SnapManager.EndDrag(win);
                            }
                        }
                    }
                }
            };

            ctx.TitleLabel.MouseLeftButtonUp += (sender, e) =>
            {
                if (titleMouseDownPoint.HasValue && !titleIsDraggingWindow && !titletb.IsVisible)
                {
                    titletb.Text = ctx.TitleLabel.Content.ToString();
                    ctx.TitleLabel.Visibility = Visibility.Collapsed;
                    titletb.Visibility = Visibility.Visible;
                    win.BeginKeyboardInteractiveEdit(titletb);
                    titletb.SelectAll();
                    titletb.Focus();
                    e.Handled = true;
                }
                titleMouseDownPoint = null;
                titleIsDraggingWindow = false;
            };

            titleGrid.MouseDown += (sender, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && (e.OriginalSource == titleGrid || e.OriginalSource is Border))
                {
                    string frameId = win.Tag?.ToString();
                    dynamic currentFrame = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
                    bool isLockedFrame = currentFrame?.IsLocked?.ToString().ToLower() == "true";
                    if (!isLockedFrame)
                    {
                        SnapManager.StartDrag(win);
                        try
                        {
                            win.DragMove();
                        }
                        finally
                        {
                            SnapManager.EndDrag(win);
                        }
                    }
                }
            };
        }
    }
}
