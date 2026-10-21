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
            return cborder;
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
    }
}
