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
    }
}
