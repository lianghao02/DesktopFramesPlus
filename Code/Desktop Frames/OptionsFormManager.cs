using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    public static class OptionsFormManager
    {
        private static int _lastSelectedTabIndex = 0;
        private static TabControl _tabControl;
        private static Window _optionsWindow;
        private static Color _userAccentColor;
        private static bool _isMaximized = false;
        private static Rect _normalBounds;
        private static Button _maxButton;

        // Colors for tabs
        private static readonly Color ColorStyle = Color.FromRgb(128, 0, 128); // Purple
        private static readonly Color ColorTools = Color.FromRgb(34, 139, 34); // Green
        private static readonly Color LookDeeper = Color.FromRgb(220, 53, 69); // Red
        private static readonly Color ColorLookDeeper = Color.FromRgb(220, 53, 69); // Red
        private static readonly Color ColorProfiles = Color.FromRgb(255, 20, 147); // Deep Pink
        private static readonly Color ColorHotkeys = Color.FromRgb(139, 69, 19); // SaddleBrown
        private static readonly Color ColorSmartDesktop = Color.FromRgb(41, 74, 122); // Semi Dark Blue

        private static double GetScreenDpiScale(IntPtr hwnd)
        {
            try
            {
                using (var graphics = System.Drawing.Graphics.FromHwnd(hwnd))
                {
                    return graphics.DpiX / 96.0;
                }
            }
            catch
            {
                return 1.0;
            }
        }

        private static void ToggleMaximize(double screenLeftDiu, double screenTopDiu, double screenWidthDiu, double screenHeightDiu)
        {
            if (_optionsWindow == null) return;
            if (!_isMaximized)
            {
                _normalBounds = new Rect(_optionsWindow.Left, _optionsWindow.Top, _optionsWindow.Width, _optionsWindow.Height);
                _optionsWindow.Left = screenLeftDiu;
                _optionsWindow.Top = screenTopDiu;
                _optionsWindow.Width = screenWidthDiu;
                _optionsWindow.Height = screenHeightDiu;
                _isMaximized = true;
                if (_maxButton != null) _maxButton.Content = "🗗";
            }
            else
            {
                _optionsWindow.Left = _normalBounds.Left;
                _optionsWindow.Top = _normalBounds.Top;
                _optionsWindow.Width = _normalBounds.Width;
                _optionsWindow.Height = _normalBounds.Height;
                _isMaximized = false;
                if (_maxButton != null) _maxButton.Content = "🗖";
            }
        }

        public static void ShowOptionsForm(int targetTabIndex = 0)
        {
            try
            {
                if (targetTabIndex >= 0 && targetTabIndex <= 6)
                {
                    _lastSelectedTabIndex = targetTabIndex;
                }
                _userAccentColor = Utility.GetColorFromName(SettingsManager.SelectedColor);

                // --- 螢幕與工作區自適應計算 (Screen-Adaptive Dimensions & Bounds) ---
                double dpiScale = 1.0;
                double screenLeftDiu = SystemParameters.WorkArea.Left;
                double screenTopDiu = SystemParameters.WorkArea.Top;
                double screenWidthDiu = SystemParameters.WorkArea.Width;
                double screenHeightDiu = SystemParameters.WorkArea.Height;

                try
                {
                    var mousePos = System.Windows.Forms.Cursor.Position;
                    var currentScreen = System.Windows.Forms.Screen.FromPoint(mousePos) ?? System.Windows.Forms.Screen.PrimaryScreen;
                    if (currentScreen != null)
                    {
                        dpiScale = GetScreenDpiScale(IntPtr.Zero);
                        if (dpiScale <= 0) dpiScale = 1.0;

                        screenLeftDiu = currentScreen.WorkingArea.Left / dpiScale;
                        screenTopDiu = currentScreen.WorkingArea.Top / dpiScale;
                        screenWidthDiu = currentScreen.WorkingArea.Width / dpiScale;
                        screenHeightDiu = currentScreen.WorkingArea.Height / dpiScale;
                    }
                }
                catch { }

                // 自適應目標尺寸：不超出工作區範圍，小螢幕自動縮減，大螢幕保持舒適比例
                double targetWidth = Math.Min(800, Math.Max(580, screenWidthDiu * 0.94));
                double targetHeight = Math.Min(780, Math.Max(460, screenHeightDiu * 0.90));

                double left = screenLeftDiu + (screenWidthDiu - targetWidth) / 2;
                double top = screenTopDiu + (screenHeightDiu - targetHeight) / 2;
                if (left < screenLeftDiu) left = screenLeftDiu;
                if (top < screenTopDiu) top = screenTopDiu;

                _isMaximized = false;
                _optionsWindow = new Window
                {
                    Title = Strings.OptionsTitle,
                    Width = targetWidth,
                    Height = targetHeight,
                    Left = left,
                    Top = top,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    MaxWidth = screenWidthDiu,
                    MaxHeight = screenHeightDiu,
                    MinWidth = Math.Min(580, screenWidthDiu * 0.85),
                    MinHeight = Math.Min(450, screenHeightDiu * 0.75),
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                    WindowStyle = WindowStyle.None,
                    Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    AllowsTransparency = true
                };

                try
                {
                    _optionsWindow.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName).Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                catch { }

                Border mainBorder = new Border
                {
                    Background = Brushes.White,
                    Margin = new Thickness(8),
                    Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 10, Opacity = 0.2 }
                };

                Grid mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); // Header
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) }); // Footer

                // Header
                Border headerBorder = new Border { Background = new SolidColorBrush(_userAccentColor), Height = 40 };
                Grid.SetRow(headerBorder, 0);

                Grid headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); // Maximize/Restore
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); // Close

                TextBlock titleBlock = new TextBlock
                {
                    Text = Strings.OptionsHeading,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20, 0, 0, 0)
                };

                Button maxButton = new Button
                {
                    Content = "🗖",
                    Width = 32,
                    Height = 32,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 13,
                    ToolTip = "最大化 / 還原"
                };
                _maxButton = maxButton;
                maxButton.Click += (s, e) => ToggleMaximize(screenLeftDiu, screenTopDiu, screenWidthDiu, screenHeightDiu);

                Button closeButton = new Button
                {
                    Content = "✕",
                    Width = 32,
                    Height = 32,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                closeButton.Click += (s, e) => _optionsWindow.Close();

                Grid.SetColumn(titleBlock, 0); headerGrid.Children.Add(titleBlock);
                Grid.SetColumn(maxButton, 1); headerGrid.Children.Add(maxButton);
                Grid.SetColumn(closeButton, 2); headerGrid.Children.Add(closeButton);
                headerBorder.Child = headerGrid;

                headerBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        ToggleMaximize(screenLeftDiu, screenTopDiu, screenWidthDiu, screenHeightDiu);
                    }
                    else if (e.ButtonState == MouseButtonState.Pressed)
                    {
                        if (_isMaximized)
                        {
                            ToggleMaximize(screenLeftDiu, screenTopDiu, screenWidthDiu, screenHeightDiu);
                        }
                        _optionsWindow.DragMove();
                    }
                };

                CreateTabContent(mainGrid);
                CreateFooter(mainGrid);
                mainGrid.Children.Add(headerBorder);
                mainBorder.Child = mainGrid;
                _optionsWindow.Content = mainBorder;
                _optionsWindow.KeyDown += (s, e) => { if (e.Key == Key.Enter) SaveOptions(); else if (e.Key == Key.Escape) _optionsWindow.Close(); };
                _optionsWindow.Closed += (s, e) => { _isMaximized = false; };
                // Pause Here:
                AutoOrganizeManager.Pause();

                _optionsWindow.ShowDialog();

                // Resume Here:
                AutoOrganizeManager.Resume();

            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error showing Options: {ex.Message}");
            }
        }

        private static void CreateTabContent(Grid mainGrid)
        {
            Grid contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(contentGrid, 1);

            StackPanel tabPanel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)) };
            ScrollViewer tabScrollViewer = new ScrollViewer
            {
                Content = tabPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Margin = new Thickness(0, 20, 0, 0)
            };
            Border contentBorder = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(20), Margin = new Thickness(0, 20, 0, 0) };

            _tabControl = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            var template = new ControlTemplate(typeof(TabControl));
            template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            ((FrameworkElementFactory)template.VisualTree).SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
            _tabControl.Template = template;

            CreateGeneralTab();
            CreateStyleTab();
            CreateToolsTab();
            CreateProfilesTab();
            CreateHotkeysTab();
            CreateSmartDesktopTab();
            CreateLookDeeperTab();

            _tabControl.SelectedIndex = _lastSelectedTabIndex;
            CreateTabButton(tabPanel, Strings.TabGeneral, 0, _lastSelectedTabIndex == 0);
            CreateTabButton(tabPanel, Strings.TabStyleFx, 1, _lastSelectedTabIndex == 1);
            CreateTabButton(tabPanel, Strings.TabTools, 2, _lastSelectedTabIndex == 2);
            CreateTabButton(tabPanel, Strings.TabProfiles, 3, _lastSelectedTabIndex == 3);
            CreateTabButton(tabPanel, Strings.TabHotkeys, 4, _lastSelectedTabIndex == 4);
            CreateTabButton(tabPanel, Strings.TabSmartDesktop, 5, _lastSelectedTabIndex == 5);
            CreateTabButton(tabPanel, Strings.TabLookDeeper, 6, _lastSelectedTabIndex == 6);

            contentBorder.Child = _tabControl;
            Grid.SetColumn(tabScrollViewer, 0); contentGrid.Children.Add(tabScrollViewer);
            Grid.SetColumn(contentBorder, 1); contentGrid.Children.Add(contentBorder);
            mainGrid.Children.Add(contentGrid);
        }

        private static void CreateTabButton(StackPanel parent, string title, int tabIndex, bool isSelected)
        {
            Button tabButton = new Button
            {
                Content = title,
                Height = 40,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(20, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, 2)
            };
            SetTabButtonColors(tabButton, tabIndex, isSelected);
            tabButton.Click += (s, e) => SelectTab(tabIndex, tabButton);
            tabButton.MouseEnter += (s, e) => { if (_tabControl.SelectedIndex != tabIndex) SetTabButtonColors(tabButton, tabIndex, false, true); };
            tabButton.MouseLeave += (s, e) => { if (_tabControl.SelectedIndex != tabIndex) SetTabButtonColors(tabButton, tabIndex, false, false); };
            parent.Children.Add(tabButton);
        }

        private static void SetTabButtonColors(Button button, int tabIndex, bool isSelected, bool isHover = false)
        {
            // Keyed on the tab index, not on its label. The labels are translated
            // now, and a switch needs compile-time constants anyway.
            Color activeColor = tabIndex switch { 1 => ColorStyle, 2 => ColorTools, 3 => ColorProfiles, 4 => ColorHotkeys, 5 => ColorSmartDesktop, 6 => ColorLookDeeper, _ => _userAccentColor };
            if (isSelected) { button.Background = new SolidColorBrush(activeColor); button.Foreground = Brushes.White; }
            else if (isHover) { button.Background = new SolidColorBrush(Color.FromRgb((byte)(activeColor.R + 40), (byte)(activeColor.G + 40), (byte)(activeColor.B + 40))); button.Foreground = Brushes.White; }
            else { button.Background = new SolidColorBrush(Color.FromRgb(200, 200, 200)); button.Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)); }
        }

        private static void SelectTab(int tabIndex, Button selectedButton)
        {
            _lastSelectedTabIndex = tabIndex;
            _tabControl.SelectedIndex = tabIndex;
            StackPanel tabPanel = (StackPanel)selectedButton.Parent;
            for (int i = 0; i < tabPanel.Children.Count; i++) if (tabPanel.Children[i] is Button btn) SetTabButtonColors(btn, i, i == tabIndex);
        }

        // --- Tabs ---

        /// <summary>
        /// Language picker plus the two buttons that make hand-installed packs
        /// usable: one to import a .resx, one to open the folder they live in.
        /// The combo stores the culture name in the item's Tag — the label is
        /// what the language calls itself, and that must never be saved.
        /// </summary>
        /// <summary>
        /// The language selector, remembered when the row is built.
        ///
        /// Saving used to look for it by name among the direct children of each Grid on the
        /// tab, the way the other dropdowns are found. This one sits inside a StackPanel
        /// inside that Grid, so the search never reached it: the chosen language was applied
        /// on the spot but never written to options.json, and came back as it was on the next
        /// start. Holding the reference cannot go wrong when the layout changes.
        /// </summary>
        private static ComboBox _languageCombo;

        /// <summary>Set when the saved options carry a different language than before.</summary>
        private static bool _languageChanged;

        private static ComboBox _noteDefaultFontCombo = null!;
        private static ComboBox _noteDefaultSizeCombo = null!;
        private static ComboBox _noteDefaultColorCombo = null!;

        /// <summary>
        /// Offers to restart once the settings are on disk, when the language changed.
        ///
        /// Every window, menu and label reads its text while being built, so a language chosen
        /// now only shows up on the next start. Saying so in a note was not enough: the choice
        /// is saved and nothing visibly happens, which reads like a failure. The program
        /// already restarts itself after a factory reset and after restoring settings, so it
        /// does the same here — asked, not imposed.
        /// </summary>
        private static void OfferRestartAfterLanguageChange()
        {
            if (!_languageChanged) return;
            _languageChanged = false;

            if (!MessageBoxesManager.ShowCustomYesNoMessageBox(Strings.MsgRestartForLanguage, Strings.DlgRestartRequired))
                return;

            try
            {
                string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c ping 127.0.0.1 -n 3 > nul & start \"\" \"{appPath}\"",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings,
                    $"Could not restart to apply the language: {ex.Message}");
            }
        }

        private static void CreateLanguageRow(StackPanel parent)
        {
            Grid g = new Grid { Margin = new Thickness(15, 8, 15, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            g.Children.Add(new TextBlock
            {
                Text = Strings.LblLanguage,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(right, 1);

            ComboBox cb = new ComboBox
            {
                Name = "LanguageComboBox",
                Width = 220,
                Height = 25,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Items.Add(new ComboBoxItem { Content = Strings.LangAutomatic, Tag = "" });
            foreach (var ci in Desktop_Frames.Localization.Strings.AvailableLanguages())
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = ci.NativeName + "  (" + ci.Name + ")",
                    Tag = ci.Name
                });
            }
            cb.SelectedItem = cb.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, SettingsManager.Language,
                                                   StringComparison.OrdinalIgnoreCase))
                ?? cb.Items.OfType<ComboBoxItem>().FirstOrDefault();
            _languageCombo = cb;
            right.Children.Add(cb);

            Button importa = new Button
            {
                Content = Strings.BtnImportLanguagePack,
                MinWidth = 150,
                Height = 25,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(10, 0, 0, 0),
                Cursor = Cursors.Hand,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12
            };
            importa.Click += (s, e) => ImportLanguagePack();
            right.Children.Add(importa);

            Button apri = new Button
            {
                Content = Strings.BtnOpenLanguagesFolder,
                MinWidth = 150,
                Height = 25,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12
            };
            apri.Click += (s, e) =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Desktop_Frames.Localization.Strings.PacksFolder);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Desktop_Frames.Localization.Strings.PacksFolder,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                        $"Cannot open languages folder: {ex.Message}");
                }
            };
            right.Children.Add(apri);

            g.Children.Add(right);
            parent.Children.Add(g);
        }

        /// <summary>
        /// Copies a .resx into the Languages folder. The file name is the
        /// culture code, so it is checked before copying: a pack called
        /// "italiano.resx" would sit there forever without ever being read.
        /// </summary>
        private static void ImportLanguagePack()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = Strings.LanguagePackFilter,
                    CheckFileExists = true
                };
                if (dlg.ShowDialog() != true) return;

                string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                try { _ = new System.Globalization.CultureInfo(name); }
                catch (System.Globalization.CultureNotFoundException)
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxFormStatic(Strings.LanguagePackBadName, Strings.SecLanguage);
                    return;
                }

                System.IO.Directory.CreateDirectory(Desktop_Frames.Localization.Strings.PacksFolder);
                System.IO.File.Copy(dlg.FileName,
                    System.IO.Path.Combine(Desktop_Frames.Localization.Strings.PacksFolder, name + ".resx"), true);
                Desktop_Frames.Localization.Strings.ReloadPacks();
                MessageBoxesManager.ShowOKOnlyMessageBoxFormStatic(Strings.Get("LanguagePackImported", name), Strings.SecLanguage);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Cannot import language pack: {ex.Message}");
            }
        }

        private static StackPanel CreateGroupCard(StackPanel parent, string title, Color themeColor, string subtitle = null)
        {
            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(225, 228, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(8, 4, 8, 14),
                Padding = new Thickness(14, 10, 14, 12)
            };

            StackPanel cardContent = new StackPanel();

            Grid titleGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border accentBar = new Border
            {
                Background = new SolidColorBrush(themeColor),
                CornerRadius = new CornerRadius(2),
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(accentBar, 0);
            titleGrid.Children.Add(accentBar);

            StackPanel titleTextPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock titleText = new TextBlock
            {
                Text = title,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 37, 41))
            };
            titleTextPanel.Children.Add(titleText);

            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock subtitleText = new TextBlock
                {
                    Text = subtitle,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                titleTextPanel.Children.Add(subtitleText);
            }

            Grid.SetColumn(titleTextPanel, 2);
            titleGrid.Children.Add(titleTextPanel);

            cardContent.Children.Add(titleGrid);
            card.Child = cardContent;
            parent.Children.Add(card);

            return cardContent;
        }

        private static List<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            var list = new List<T>();
            if (parent == null) return list;

            if (parent is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    if (child is T typed) list.Add(typed);
                    list.AddRange(FindDescendants<T>(child));
                }
            }
            else if (parent is ContentControl cc && cc.Content is DependencyObject content)
            {
                if (content is T typed) list.Add(typed);
                list.AddRange(FindDescendants<T>(content));
            }
            else if (parent is Border border && border.Child != null)
            {
                if (border.Child is T typed) list.Add(typed);
                list.AddRange(FindDescendants<T>(border.Child));
            }
            else if (parent is ScrollViewer sv && sv.Content is DependencyObject svContent)
            {
                if (svContent is T typed) list.Add(typed);
                list.AddRange(FindDescendants<T>(svContent));
            }
            return list;
        }

        private static void CreateGeneralTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 語言
            var cardLang = CreateGroupCard(c, Strings.SecLanguage, _userAccentColor);
            CreateLanguageRow(cardLang);
            cardLang.Children.Add(new TextBlock
            {
                Text = Strings.NoteLanguageRestart,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 0, 6, 4)
            });

            // 2. 系統與啟動
            var cardStartup = CreateGroupCard(c, Strings.SecStartup, _userAccentColor);
            CreateCheckBox(cardStartup, Strings.OptStartWithWindows, "StartWithWindows", TrayManager.IsStartWithWindows);
            CreateCheckBox(cardStartup, Strings.OptTrayIcon, "EnableTrayIcon", SettingsManager.ShowInTray);
            CreateCheckBox(cardStartup, Strings.OptNewFrameContextMenu, "EnableContextMenu", SettingsManager.EnableContextMenu);
            CreateCheckBox(cardStartup, Strings.OptDisableScrollbars, "DisableFrameScrollbars", SettingsManager.DisableFrameScrollbars);

            // 3. 操作與吸附
            var cardInteractions = CreateGroupCard(c, Strings.Get("SecInteractions"), _userAccentColor);
            CreateCheckBox(cardInteractions, Strings.OptSingleClick, "SingleClickToLaunch", SettingsManager.SingleClickToLaunch);
            CreateCheckBox(cardInteractions, Strings.OptSnapNearFrames, "EnableSnapNearFrames", SettingsManager.IsSnapEnabled);
            CreateCheckBox(cardInteractions, Strings.OptDimensionSnap, "EnableDimensionSnap", SettingsManager.EnableDimensionSnap);

            CheckBox cbSounds = CreateCheckBoxReturn(cardInteractions, Strings.OptEnableSounds, "EnableSounds", SettingsManager.EnableSounds);
            Grid soundGrid = new Grid { Margin = new Thickness(26, 2, 0, 6) };
            soundGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            soundGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            TextBlock lblSound = new TextBlock { Text = Strings.LblNotificationSound, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblSound, 0);
            ComboBox cbSoundType = new ComboBox { Name = "NotificationSoundComboBox", Height = 25, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            cbSoundType.Items.Add(Strings.SndDefault);
            cbSoundType.Items.Add(Strings.SndDoubleDing);
            cbSoundType.Items.Add(Strings.SndSmoothTickle);
            cbSoundType.Items.Add(Strings.SndMessageDing);
            cbSoundType.Items.Add(Strings.SndGentleDing);
            cbSoundType.Items.Add(Strings.SndSoftDing);
            cbSoundType.SelectedIndex = SettingsManager.NotificationSound switch
            {
                NotificationSound.DoubleDing => 1,
                NotificationSound.SmoothTickle => 2,
                NotificationSound.MessageDing => 3,
                NotificationSound.GentleDing => 4,
                NotificationSound.SoftDing => 5,
                _ => 0
            };
            Grid.SetColumn(cbSoundType, 1);
            soundGrid.Children.Add(lblSound);
            soundGrid.Children.Add(cbSoundType);
            cardInteractions.Children.Add(soundGrid);
            soundGrid.IsEnabled = cbSounds.IsChecked == true;
            cbSounds.Click += (s, e) => soundGrid.IsEnabled = cbSounds.IsChecked == true;

            // 4. 資料夾鏡像面板
            var cardPortals = CreateGroupCard(c, Strings.Get("SecFolderPortals"), _userAccentColor);
            Grid portalViewGrid = new Grid { Margin = new Thickness(6, 4, 0, 6) };
            portalViewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            portalViewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            TextBlock lblPortalView = new TextBlock { Text = Strings.LblDefaultPortalView, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblPortalView, 0);
            ComboBox cbPortalView = new ComboBox { Name = "DefaultPortalViewComboBox", Height = 25, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            cbPortalView.Items.Add(new ComboBoxItem { Content = Strings.ViewIcons, Tag = "Icons" });
            cbPortalView.Items.Add(new ComboBoxItem { Content = Strings.ViewDetails, Tag = "Details" });
            cbPortalView.SelectedIndex = string.Equals(SettingsManager.DefaultPortalView, "Details", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Grid.SetColumn(cbPortalView, 1);
            portalViewGrid.Children.Add(lblPortalView);
            portalViewGrid.Children.Add(cbPortalView);
            cardPortals.Children.Add(portalViewGrid);
            CreateCheckBox(cardPortals, Strings.OptPortalWatermark, "EnablePortalWatermark", SettingsManager.ShowBackgroundImageOnPortalFrames);
            CreateCheckBox(cardPortals, Strings.OptRecycleBin, "UseRecycleBin", SettingsManager.UseRecycleBin);

            // 5. 公用桌面收納權限
            var cardPublicDesktop = CreateGroupCard(c, Strings.Get("SecPublicDesktop"), _userAccentColor);
            cardPublicDesktop.Children.Add(new TextBlock
            {
                Text = Strings.Get("DescPublicDesktop"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 0, 6, 8)
            });
            bool isCommonGranted = Services.FenceInventoryManager.HasCommonDesktopWritePermission();
            Grid publicDesktopGrid = new Grid { Margin = new Thickness(6, 4, 6, 6) };
            publicDesktopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            publicDesktopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            TextBlock lblCommonStatus = new TextBlock
            {
                Text = isCommonGranted ? Strings.Get("LblPublicDesktopGranted") : Strings.Get("LblPublicDesktopNotGranted"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = isCommonGranted ? new SolidColorBrush(Color.FromRgb(34, 139, 34)) : new SolidColorBrush(Color.FromRgb(210, 105, 30)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lblCommonStatus, 0);
            Button btnGrant = new Button
            {
                Content = isCommonGranted ? "✓ 已完成授權" : Strings.Get("BtnGrantPublicDesktop"),
                Height = 30,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = isCommonGranted ? new SolidColorBrush(Color.FromRgb(235, 235, 235)) : new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                Foreground = isCommonGranted ? Brushes.Gray : Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = isCommonGranted ? Cursors.Arrow : Cursors.Hand,
                IsEnabled = !isCommonGranted,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(btnGrant, 1);
            btnGrant.Click += (s, e) =>
            {
                bool success = Services.FenceInventoryManager.GrantCommonDesktopPermission();
                if (success)
                {
                    lblCommonStatus.Text = Strings.Get("LblPublicDesktopGranted");
                    lblCommonStatus.Foreground = new SolidColorBrush(Color.FromRgb(34, 139, 34));
                    btnGrant.Content = "✓ 已完成授權";
                    btnGrant.IsEnabled = false;
                    btnGrant.Foreground = Brushes.Gray;
                    btnGrant.Background = new SolidColorBrush(Color.FromRgb(235, 235, 235));
                    btnGrant.Cursor = Cursors.Arrow;
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm("公用桌面收納權限已成功授權！日後可直接拖曳收納公用捷徑。", Strings.DlgInfo);
                }
                else
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm("未取得系統管理員授權或已取消確認。", Strings.DlgInfo);
                }
            };
            publicDesktopGrid.Children.Add(lblCommonStatus);
            publicDesktopGrid.Children.Add(btnGrant);
            cardPublicDesktop.Children.Add(publicDesktopGrid);

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        private static void CreateStyleTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 外觀主題與色彩
            var cardAppearance = CreateGroupCard(c, Strings.SecAppearance, ColorStyle);
            var chamCb = CreateCheckBoxReturn(cardAppearance, Strings.OptChameleon, "EnableChameleon", SettingsManager.EnableChameleonMode);
            chamCb.ToolTip = Strings.TooltipChameleon;
            CreateSliderControl(cardAppearance, Strings.SldFrameTint, "TintSlider", SettingsManager.TintValue);
            CreateSliderControl(cardAppearance, Strings.SldMenuTint, "MenuTintSlider", SettingsManager.MenuTintValue);
            CreateColorAndEffectComboBoxes(cardAppearance, chamCb);
            CreateCheckBox(cardAppearance, Strings.OptFrameTint, "ApplyTintToIcons", SettingsManager.ApplyTintToIcons);

            // 2. 自動隱藏與閒置效果
            var cardAutoHide = CreateGroupCard(c, Strings.SecAutoHideFrames, ColorStyle);
            CreateCheckBox(cardAutoHide, Strings.OptAutoHideFrames, "AutoHideFrames", SettingsManager.AutoHideFrames);
            CreateSliderControl(cardAutoHide, Strings.SldAutoHideTime, "AutoHideTimeSlider", SettingsManager.AutoHideTime, 300);

            cardAutoHide.Children.Add(new Separator { Margin = new Thickness(6, 8, 6, 8), Background = new SolidColorBrush(Color.FromRgb(235, 235, 235)) });
            CreateCheckBox(cardAutoHide, Strings.OptIdleFadeOut, "FramesFadeOutFx", SettingsManager.FramesFadeOutFx);
            CreateSliderControl(cardAutoHide, Strings.SldIdleTime, "FadeOutTimeSlider", SettingsManager.FadeOutTime, 300);
            CreateSliderControl(cardAutoHide, Strings.SldFadeTargetOpacity, "FadeOutAlphaSlider", (int)(SettingsManager.FadeOutFxTargetAlpha * 100), 100);

            cardAutoHide.Children.Add(new Separator { Margin = new Thickness(6, 8, 6, 8), Background = new SolidColorBrush(Color.FromRgb(235, 235, 235)) });
            cardAutoHide.Children.Add(new TextBlock
            {
                Text = Strings.SecIdleAutoRoll + "：" + Strings.NoteAutoRoll,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(6, 2, 6, 6),
                TextWrapping = TextWrapping.Wrap
            });
            CreateSliderControl(cardAutoHide, Strings.SldIdleTime, "AutoRollTimeSlider", SettingsManager.AutoRollTime, 300);

            // 3. 桌面圖示顯示行為
            var cardDesktopIcons = CreateGroupCard(c, Strings.SecDesktopIconVisibility, ColorStyle);
            CreateCheckBox(cardDesktopIcons, Strings.OptHideIconsRunning, "HideDesktopElementsOnStart", SettingsManager.HideDesktopElementsOnStart);
            CreateCheckBox(cardDesktopIcons, Strings.OptHideIconsWhenHidden, "HideDesktopElementsOnAllFramesHide", SettingsManager.HideDesktopElementsOnAllFramesHide);
            CreateCheckBox(cardDesktopIcons, Strings.Get("OptShowDesktopDot"), "ShowDesktopDot", SettingsManager.ShowDesktopDot);

            // 4. 面板按鈕圖示樣式
            var cardIcons = CreateGroupCard(c, Strings.SecIcons, ColorStyle);
            Grid iconGrid = new Grid { Margin = new Thickness(6, 4, 0, 8) };
            iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel menuIconPanel = new StackPanel();
            menuIconPanel.Children.Add(new TextBlock { Text = Strings.LblMenuIcon, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            CreateIconRadioButtonGroup(menuIconPanel, "MenuIconGroup", new Dictionary<string, int> { { "♥", 0 }, { "☰", 1 }, { "≣", 2 }, { "𓃑", 3 } }, SettingsManager.MenuIcon);

            StackPanel lockIconPanel = new StackPanel();
            lockIconPanel.Children.Add(new TextBlock { Text = Strings.LblLockIcon, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            CreateIconRadioButtonGroup(lockIconPanel, "LockIconGroup", new Dictionary<string, int> { { "🛡️", 0 }, { "🔑", 1 }, { "🔐", 2 }, { "🔒", 3 } }, SettingsManager.LockIcon);

            Grid.SetColumn(menuIconPanel, 0);
            Grid.SetColumn(lockIconPanel, 1);
            iconGrid.Children.Add(menuIconPanel);
            iconGrid.Children.Add(lockIconPanel);
            cardIcons.Children.Add(iconGrid);

            // 5. 便箋預設樣式
            var cardNotes = CreateGroupCard(c, Strings.SecNotePreferences, ColorStyle);
            Grid noteStyleGrid = new Grid { Margin = new Thickness(6, 4, 0, 8) };
            noteStyleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            noteStyleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            noteStyleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            noteStyleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            noteStyleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            TextBlock lblNoteFont = new TextBlock { Text = Strings.LblNoteDefaultFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _noteDefaultFontCombo = new ComboBox { Name = "NoteDefaultFontComboBox", Height = 25, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _noteDefaultFontCombo.Items.Add(new ComboBoxItem { Content = "微軟正黑體", Tag = "Microsoft JhengHei" });
            _noteDefaultFontCombo.Items.Add(new ComboBoxItem { Content = "標楷體", Tag = "DFKai-SB" });
            _noteDefaultFontCombo.Items.Add(new ComboBoxItem { Content = "Segoe UI", Tag = "Segoe UI" });
            _noteDefaultFontCombo.Items.Add(new ComboBoxItem { Content = "系統預設", Tag = "" });
            var fontMatch = _noteDefaultFontCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, SettingsManager.NoteDefaultFontFamily, StringComparison.OrdinalIgnoreCase));
            _noteDefaultFontCombo.SelectedItem = fontMatch ?? _noteDefaultFontCombo.Items[0];
            Grid.SetRow(lblNoteFont, 0); Grid.SetColumn(lblNoteFont, 0);
            Grid.SetRow(_noteDefaultFontCombo, 0); Grid.SetColumn(_noteDefaultFontCombo, 1);
            noteStyleGrid.Children.Add(lblNoteFont);
            noteStyleGrid.Children.Add(_noteDefaultFontCombo);

            TextBlock lblNoteSize = new TextBlock { Text = Strings.LblNoteDefaultSize, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _noteDefaultSizeCombo = new ComboBox { Name = "NoteDefaultSizeComboBox", Height = 25, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _noteDefaultSizeCombo.Items.Add(new ComboBoxItem { Content = "小（12）", Tag = 12.0 });
            _noteDefaultSizeCombo.Items.Add(new ComboBoxItem { Content = "標準（14）", Tag = 14.0 });
            _noteDefaultSizeCombo.Items.Add(new ComboBoxItem { Content = "大（16）", Tag = 16.0 });
            _noteDefaultSizeCombo.Items.Add(new ComboBoxItem { Content = "特大（18）", Tag = 18.0 });
            var sizeMatch = _noteDefaultSizeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag is double d) && Math.Abs(d - SettingsManager.NoteDefaultFontSize) < 0.5);
            _noteDefaultSizeCombo.SelectedItem = sizeMatch ?? _noteDefaultSizeCombo.Items[1];
            Grid.SetRow(lblNoteSize, 1); Grid.SetColumn(lblNoteSize, 0);
            Grid.SetRow(_noteDefaultSizeCombo, 1); Grid.SetColumn(_noteDefaultSizeCombo, 1);
            noteStyleGrid.Children.Add(lblNoteSize);
            noteStyleGrid.Children.Add(_noteDefaultSizeCombo);

            TextBlock lblNoteColor = new TextBlock { Text = Strings.LblNoteDefaultColor, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _noteDefaultColorCombo = new ComboBox { Name = "NoteDefaultColorComboBox", Height = 25, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            foreach (var pal in Notes.NoteColors.AllPalettes)
            {
                _noteDefaultColorCombo.Items.Add(new ComboBoxItem { Content = pal.DisplayName, Tag = pal.Key });
            }
            var colorMatch = _noteDefaultColorCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, SettingsManager.NoteDefaultColor, StringComparison.OrdinalIgnoreCase));
            _noteDefaultColorCombo.SelectedItem = colorMatch ?? _noteDefaultColorCombo.Items[0];
            Grid.SetRow(lblNoteColor, 2); Grid.SetColumn(lblNoteColor, 0);
            Grid.SetRow(_noteDefaultColorCombo, 2); Grid.SetColumn(_noteDefaultColorCombo, 1);
            noteStyleGrid.Children.Add(lblNoteColor);
            noteStyleGrid.Children.Add(_noteDefaultColorCombo);
            cardNotes.Children.Add(noteStyleGrid);

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        private static void CreateToolsTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 資料備份與還原
            var cardBackup = CreateGroupCard(c, Strings.Get("SecBackup"), ColorTools, "隨時備份所有面板配置、捷徑資料與自訂樣式");
            Grid g = new Grid { Margin = new Thickness(6, 6, 6, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

            Button b1 = CreateStyledButton(Strings.BtnBackup, ColorTools); b1.Click += (s, e) => BackupManager.BackupData();
            Button b2 = CreateStyledButton(Strings.BtnRestore, Color.FromRgb(255, 152, 0)); b2.Click += (s, e) => RestoreBackup();
            Button b3 = CreateStyledButton(Strings.BtnOpenBackupsFolder, Color.FromRgb(0, 123, 191)); b3.Click += (s, e) => OpenBackupsFolder();
            Grid.SetRow(b1, 0); Grid.SetColumn(b1, 0);
            Grid.SetRow(b2, 0); Grid.SetColumn(b2, 2);
            Grid.SetRow(b3, 2); Grid.SetColumn(b3, 0); Grid.SetColumnSpan(b3, 3);
            g.Children.Add(b1); g.Children.Add(b2); g.Children.Add(b3);
            cardBackup.Children.Add(g);
            CreateCheckBox(cardBackup, Strings.OptAutomaticBackup, "EnableAutoBackup", SettingsManager.EnableAutoBackup);

            // 2. 面板邊界維護
            var cardMaint = CreateGroupCard(c, Strings.SecMaintenance, ColorTools, "多螢幕插拔或更換解析度時，將跑出螢幕外的面板拉回可視範圍");
            Button btnBound = CreateStyledButton(Strings.BtnScreenBoundFrames, ColorTools);
            btnBound.Width = 260;
            btnBound.Height = 40;
            btnBound.Margin = new Thickness(6, 4, 0, 6);
            btnBound.HorizontalAlignment = HorizontalAlignment.Left;
            btnBound.IsEnabled = !SettingsManager.AllowAutoReposition;
            if (btnBound.IsEnabled)
            {
                btnBound.Click += (s, e) =>
                {
                    Framemanager.ForceRepositionallFrames();
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgFramesMovedIntoBounds, Strings.DlgSuccess);
                };
            }
            else
            {
                btnBound.Opacity = 0.90;
                btnBound.ToolTip = Strings.TooltipAutoReposition;
            }
            cardMaint.Children.Add(btnBound);

            // 3. 系統重設與清除
            var cardReset = CreateGroupCard(c, Strings.Get("SecReset"), Color.FromRgb(220, 53, 69), "危險操作：重設個人外觀風格或徹底抹除本機資料");
            Button r1 = CreateStyledButton(Strings.BtnResetStyles, Color.FromRgb(108, 117, 125));
            r1.Width = 260; r1.Height = 38; r1.Margin = new Thickness(6, 4, 0, 10); r1.HorizontalAlignment = HorizontalAlignment.Left;
            r1.Click += (s, e) => { if (MessageBoxesManager.ShowCustomYesNoMessageBox(Strings.MsgConfirmResetCustomizations, Strings.BtnReset)) { Framemanager.ResetAllCustomizations(); _optionsWindow.Close(); } };

            Button r2 = CreateStyledButton(Strings.BtnClearAllData, Color.FromRgb(220, 53, 69));
            r2.Width = 260; r2.Height = 38; r2.Margin = new Thickness(6, 0, 0, 6); r2.HorizontalAlignment = HorizontalAlignment.Left;
            r2.Click += (s, e) => PerformFullFactoryReset();

            cardReset.Children.Add(r1);
            cardReset.Children.Add(r2);

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        private static void CreateProfilesTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 工作區版面管理
            var cardLayout = CreateGroupCard(c, Strings.Get("SecProfileLayouts"), ColorProfiles, "建立、切換與命名多個不同的桌面面板配置（例如：工作、娛樂、開發）");
            Button btnManageProfiles = CreateStyledButton(Strings.BtnManageProfiles, Color.FromRgb(34, 139, 34));
            btnManageProfiles.Width = 260; btnManageProfiles.Height = 40; btnManageProfiles.Margin = new Thickness(6, 4, 0, 6);
            btnManageProfiles.HorizontalAlignment = HorizontalAlignment.Left;
            btnManageProfiles.Click += (s, e) => { new ProfileManagerForm().ShowDialog(); };
            cardLayout.Children.Add(btnManageProfiles);

            // 2. 智慧情境自動化
            var cardAuto = CreateGroupCard(c, Strings.Get("SecProfileAutomation"), ColorProfiles, "當特定前景程式執行時自動切換至相應的工作區版面");
            CheckBox autoCb = new CheckBox
            {
                Name = "EnableProfileAutomation",
                Content = Strings.LblEnableProfileAutomation,
                IsChecked = SettingsManager.EnableProfileAutomation,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Margin = new Thickness(6, 4, 0, 10)
            };
            autoCb.Click += (s, e) => {
                bool isChecked = autoCb.IsChecked == true;
                SettingsManager.EnableProfileAutomation = isChecked;
                SettingsManager.SaveSettings();
                TrayManager.Instance?.UpdateAutomationMenuCheck(isChecked);
                if (isChecked) AutomationManager.Start();
            };
            cardAuto.Children.Add(autoCb);

            Button btnManageAutomation = CreateStyledButton(Strings.BtnManageAutomation, Color.FromRgb(0, 123, 191));
            btnManageAutomation.Width = 260; btnManageAutomation.Height = 40; btnManageAutomation.Margin = new Thickness(6, 0, 0, 6);
            btnManageAutomation.HorizontalAlignment = HorizontalAlignment.Left;
            btnManageAutomation.Click += (s, e) => { new AutomationRulesForm().ShowDialog(); };
            cardAuto.Children.Add(btnManageAutomation);

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        private static readonly Dictionary<string, int> AvailableKeys = new Dictionary<string, int>
        {
            {"A", 0x41}, {"B", 0x42}, {"C", 0x43}, {"D", 0x44}, {"E", 0x45}, {"F", 0x46}, {"G", 0x47}, {"H", 0x48}, {"I", 0x49}, {"J", 0x4A}, {"K", 0x4B}, {"L", 0x4C}, {"M", 0x4D}, {"N", 0x4E}, {"O", 0x4F}, {"P", 0x50}, {"Q", 0x51}, {"R", 0x52}, {"S", 0x53}, {"T", 0x54}, {"U", 0x55}, {"V", 0x56}, {"W", 0x57}, {"X", 0x58}, {"Y", 0x59}, {"Z", 0x5A},
            {"0", 0x30}, {"1", 0x31}, {"2", 0x32}, {"3", 0x33}, {"4", 0x34}, {"5", 0x35}, {"6", 0x36}, {"7", 0x37}, {"8", 0x38}, {"9", 0x39},
            {"F1", 0x70}, {"F2", 0x71}, {"F3", 0x72}, {"F4", 0x73}, {"F5", 0x74}, {"F6", 0x75}, {"F7", 0x76}, {"F8", 0x77}, {"F9", 0x78}, {"F10", 0x79}, {"F11", 0x7A}, {"F12", 0x7B},
            {"Comma (,)", 0xBC}, {"Period (.)", 0xBE}, {"Tilde (~)", 192}, {"Space", 32}, {"Tab", 9}, {"Enter", 13}, {"Escape", 27}
        };

        private static void CreateHotkeysTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 工作區切換快捷鍵
            var cardProf = CreateGroupCard(c, Strings.SecProfileSwitching, ColorHotkeys, "透過自訂快捷鍵即時切換不同的桌面配置版面");
            CheckBox cbProf = CreateCheckBoxReturn(cardProf, Strings.OptProfileHotkeys, "EnableProfileHotkeys", SettingsManager.EnableProfileHotkeys);
            Grid gProf1 = CreateHotkeyEditor(cardProf, Strings.HkDirectProfile, "ProfSwitch", SettingsManager.ProfileSwitchModifier, 0, false);
            Grid gProf2 = CreateHotkeyEditor(cardProf, Strings.HkPreviousProfile, "ProfPrev", SettingsManager.ProfilePrevModifier, SettingsManager.ProfilePrevKey, true);
            Grid gProf3 = CreateHotkeyEditor(cardProf, Strings.HkNextProfile, "ProfNext", SettingsManager.ProfileNextModifier, SettingsManager.ProfileNextKey, true);
            gProf1.IsEnabled = gProf2.IsEnabled = gProf3.IsEnabled = cbProf.IsChecked == true;
            cbProf.Click += (s, e) => gProf1.IsEnabled = gProf2.IsEnabled = gProf3.IsEnabled = cbProf.IsChecked == true;

            // 2. 輔助與搜尋快捷鍵
            var cardUtils = CreateGroupCard(c, Strings.SecUtilities, ColorHotkeys, "全域面板聚焦與即時 Spotlight 搜尋快捷鍵");
            CheckBox cbFocus = CreateCheckBoxReturn(cardUtils, Strings.OptFocusFrameHotkey, "EnableFocusFrameHotkey", SettingsManager.EnableFocusFrameHotkey);
            Grid gFocus = CreateHotkeyEditor(cardUtils, Strings.HkFocusFrame, "FocusFrame", SettingsManager.FocusFrameModifier, SettingsManager.FocusFrameKey, true);
            gFocus.IsEnabled = cbFocus.IsChecked == true;
            cbFocus.Click += (s, e) => gFocus.IsEnabled = cbFocus.IsChecked == true;

            CheckBox cbSpot = CreateCheckBoxReturn(cardUtils, Strings.OptSpotSearchHotkey, "EnableSpotSearchHotkey", SettingsManager.EnableSpotSearchHotkey);
            Grid gSpot = CreateHotkeyEditor(cardUtils, Strings.HkSpotSearch, "SpotSearch", SettingsManager.SpotSearchModifier, SettingsManager.SpotSearchKey, true);
            gSpot.IsEnabled = cbSpot.IsChecked == true;
            cbSpot.Click += (s, e) => gSpot.IsEnabled = cbSpot.IsChecked == true;

            cardUtils.Children.Add(new TextBlock
            {
                Text = Strings.NoteHotkeysRestart,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                Margin = new Thickness(6, 10, 6, 4)
            });

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        private static Grid CreateHotkeyEditor(StackPanel p, string label, string namePrefix, string currentMod, int currentKey, bool hasKeySelector)
        {
            Grid g = new Grid { Margin = new Thickness(6, 4, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0); g.Children.Add(lbl);

            StackPanel spMods = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string curModLower = (currentMod ?? "").ToLower();

            CheckBox chkCtrl = new CheckBox { Name = namePrefix + "Ctrl", Content = "Ctrl", IsChecked = curModLower.Contains("ctrl") || curModLower.Contains("control"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            CheckBox chkAlt = new CheckBox { Name = namePrefix + "Alt", Content = "Alt", IsChecked = curModLower.Contains("alt"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            CheckBox chkShift = new CheckBox { Name = namePrefix + "Shift", Content = "Shift", IsChecked = curModLower.Contains("shift"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            CheckBox chkWin = new CheckBox { Name = namePrefix + "Win", Content = "Win", IsChecked = curModLower.Contains("win"), Margin = new Thickness(0, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center };

            spMods.Children.Add(chkCtrl);
            spMods.Children.Add(chkAlt);
            spMods.Children.Add(chkShift);
            spMods.Children.Add(chkWin);

            if (hasKeySelector)
            {
                spMods.Children.Add(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                ComboBox cmb = new ComboBox { Name = namePrefix + "Key", Width = 100, VerticalAlignment = VerticalAlignment.Center };
                foreach (var kvp in AvailableKeys)
                {
                    ComboBoxItem item = new ComboBoxItem { Content = Strings.KeyLabel(kvp.Key), Tag = kvp.Value };
                    cmb.Items.Add(item);
                    if (kvp.Value == currentKey) cmb.SelectedItem = item;
                }
                if (cmb.SelectedIndex == -1 && cmb.Items.Count > 0) cmb.SelectedIndex = 0;
                spMods.Children.Add(cmb);
            }

            Grid.SetColumn(spMods, 1);
            g.Children.Add(spMods);
            p.Children.Add(g);
            return g;
        }

        private static void CreateSmartDesktopTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 自動整理引擎
            var cardAuto = CreateGroupCard(c, Strings.SecSmartDesktopAuto, ColorSmartDesktop, "背景監視桌面變更，自動將新加入桌面的檔案與捷徑分門別類收納至對應柵欄");
            CheckBox cbMain = CreateCheckBoxReturn(cardAuto, Strings.OptAutoOrganize, "EnableAutoOrganize", SettingsManager.EnableAutoOrganize);
            CheckBox cbNotif = CreateCheckBoxReturn(cardAuto, Strings.OptExecutionToasts, "EnableAutoOrganizeNotifications", SettingsManager.EnableAutoOrganizeNotifications);
            cbNotif.Margin = new Thickness(26, 2, 0, 6);
            cbNotif.IsEnabled = cbMain.IsChecked == true;

            // 2. 分類規則與執行
            var cardRules = CreateGroupCard(c, Strings.Get("SecSmartDesktopRules"), ColorSmartDesktop, "依檔案類型、副檔名或關鍵字設定自動歸檔規則，亦可手動單次觸發");
            StackPanel statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 0, 10) };
            TextBlock txtTotalRules = new TextBlock { Text = Strings.Get("LblTotalRules", AutoOrganizeManager.Rules.Count), FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.Medium };
            TextBlock txtSeparator = new TextBlock { Text = "   -   ", FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Brushes.Gray };
            TextBlock txtEnabledRules = new TextBlock { Text = Strings.Get("LblEnabledRules", AutoOrganizeManager.Rules.Count(r => r.IsEnabled)), FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(34, 139, 34)) };
            statsPanel.Children.Add(txtTotalRules);
            statsPanel.Children.Add(txtSeparator);
            statsPanel.Children.Add(txtEnabledRules);
            cardRules.Children.Add(statsPanel);

            Button btnManageRules = CreateStyledButton(Strings.BtnSmartDesktopRules, Color.FromRgb(0, 0, 128));
            btnManageRules.Width = 260; btnManageRules.Height = 40; btnManageRules.Margin = new Thickness(6, 0, 0, 10);
            btnManageRules.HorizontalAlignment = HorizontalAlignment.Left;
            btnManageRules.Click += (s, e) =>
            {
                new AutoOrganizeForm().ShowDialog();
                txtTotalRules.Text = Strings.Get("LblTotalRules", AutoOrganizeManager.Rules.Count);
                txtEnabledRules.Text = Strings.Get("LblEnabledRules", AutoOrganizeManager.Rules.Count(r => r.IsEnabled));
            };
            cardRules.Children.Add(btnManageRules);

            Button btnOrganizeNow = CreateStyledButton(Strings.BtnOrganizeNow, Color.FromRgb(139, 0, 0));
            btnOrganizeNow.Width = 260; btnOrganizeNow.Height = 40; btnOrganizeNow.Margin = new Thickness(6, 0, 0, 8);
            btnOrganizeNow.HorizontalAlignment = HorizontalAlignment.Left;
            btnOrganizeNow.Click += (s, e) =>
            {
                if (MessageBoxesManager.ShowCustomYesNoMessageBox(Strings.MsgConfirmSweepDesktop, Strings.DlgSweepDesktop))
                {
                    AutoOrganizeManager.ProcessDesktopNow();
                }
            };
            cardRules.Children.Add(btnOrganizeNow);

            cardRules.Children.Add(new TextBlock
            {
                Text = Strings.NoteAutoOrganize,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                Margin = new Thickness(6, 6, 6, 4),
                TextWrapping = TextWrapping.Wrap
            });

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        private static void CreateLookDeeperTab()
        {
            TabItem t = new TabItem();
            StackPanel c = new StackPanel();

            // 1. 診斷日誌
            var cardLog = CreateGroupCard(c, Strings.SecLog, ColorLookDeeper, "記錄執行過程與除錯診斷資訊至本機日誌檔");
            CreateCheckBox(cardLog, Strings.OptEnableLogging, "EnableLogging", SettingsManager.IsLogEnabled);
            Button b = CreateStyledButton(Strings.BtnOpenLog, ColorLookDeeper);
            b.Width = 120; b.Height = 32; b.Margin = new Thickness(6, 4, 0, 6); b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Click += (s, e) => OpenLogFile();
            cardLog.Children.Add(b);

            // 2. 日誌層級
            var cardConfig = CreateGroupCard(c, Strings.SecLogConfiguration, ColorLookDeeper, "設定寫入日誌檔的最低嚴重性等級");
            CreateLogLevelComboBox(cardConfig);

            // 3. 追蹤分類
            var cardCats = CreateGroupCard(c, Strings.SecLogCategories, ColorLookDeeper, "勾選欲納入日誌記錄的子系統模組");
            CreateLogCategoryCheckBoxes(cardCats);

            t.Content = new ScrollViewer { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _tabControl.Items.Add(t);
        }

        // --- Helpers ---
        private static void CreateSectionHeader(StackPanel p, string t, Color c) => p.Children.Add(new TextBlock { Text = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(c), Margin = new Thickness(0, 10, 0, 15) });
        private static void CreateCheckBox(StackPanel p, string t, string n, bool c) => p.Children.Add(new CheckBox { Name = n, Content = t, IsChecked = c, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Margin = new Thickness(15, 8, 0, 8) });
        private static CheckBox CreateCheckBoxReturn(StackPanel p, string t, string n, bool c) { var cb = new CheckBox { Name = n, Content = t, IsChecked = c, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Margin = new Thickness(15, 8, 0, 8) }; p.Children.Add(cb); return cb; }

        // FIX: Added 'max' parameter (defaulting to 100) to fix the Tint sliders while supporting AutoHideTime
        private static void CreateSliderControl(StackPanel p, string l, string n, int v, int max = 100)
        {
            Grid g = new Grid { Margin = new Thickness(15, 5, 0, 5) };

            // --- UI FIX: Increased to 205px to bump the sliders and "Effect" label to the right, adding a nice gap ---
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            // Slightly widened the third column from 50 to 65 to comfortably fit the NumericTextBox without clipping
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });

            // UI FIX: Changed HorizontalAlignment to Left so labels sit flush on the left margin
            TextBlock lbl = new TextBlock { Text = l, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 10, 0) };
            Slider sl = new Slider { Name = n, Minimum = 1, Maximum = max, Value = v, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };

            // --- TRIAL: Replaced TextBlock with interconnected NumericTextBox for micro-adjustments ---
            NumericTextBox nud = new NumericTextBox
            {
                Minimum = 1,
                Maximum = max,
                Value = v,
                Width = 55, // Exactly half of the standard width used in CustomizeFrameForm, plus a tiny bit for 3-digit numbers
                Height = 20, // Strict compact height to preserve the original line spacing and avoid blowing up vertical space
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            // Two-way binding between Slider and NumericTextBox
            sl.ValueChanged += (s, e) => { if (nud.Value != (int)e.NewValue) nud.Value = (int)e.NewValue; };
            nud.ValueChanged += (s, e) => { if (sl.Value != nud.Value) sl.Value = nud.Value; };

            Grid.SetColumn(lbl, 0); Grid.SetColumn(sl, 1); Grid.SetColumn(nud, 2);
            g.Children.Add(lbl); g.Children.Add(sl); g.Children.Add(nud);
            p.Children.Add(g);
        }

        private static void CreateIconRadioButtonGroup(StackPanel p, string gName, Dictionary<string, int> icons, int sel)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 5, 0, 15), Tag = gName };
            foreach (var i in icons) sp.Children.Add(new RadioButton { Content = i.Key, Tag = i.Value, GroupName = gName, IsChecked = i.Value == sel, Margin = new Thickness(0, 0, 15, 0), FontSize = 16, FontFamily = new FontFamily("Segoe UI Symbol") });
            p.Children.Add(sp);
        }

        //New Arrangement

        private static void CreateColorAndEffectComboBoxes(StackPanel p, CheckBox chamCb)
        {
            Grid g = new Grid { Margin = new Thickness(15, 10, 0, 10) };

            // --- UI FIX: Hardcode total to 205px (45 + 160) to match the sliders. 
            // The 160px column holds a 140px dropdown, leaving exactly a 20px gap before "Effect" ---
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            TextBlock lblColor = new TextBlock { Text = Strings.LblColor, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
            // Constrain Width to 140 and Left-align so it doesn't stretch to fill the 160px column, creating the gap automatically
            ComboBox cbColor = new ComboBox { Name = "ColorComboBox", Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Height = 25, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            foreach (string c in new[] { "Gray", "Black", "White", "Beige", "Green", "Purple", "Fuchsia", "Yellow", "Orange", "Red", "Blue", "Bismark" })
                cbColor.Items.Add(new ComboBoxItem { Content = Strings.Get("Color" + c), Tag = c });
            cbColor.SelectedItem = cbColor.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, SettingsManager.SelectedColor, StringComparison.OrdinalIgnoreCase))
                ?? cbColor.Items.OfType<ComboBoxItem>().FirstOrDefault();

            // --- BUG FIX: Disable Color dropdown if Chameleon mode is ON ---
            cbColor.IsEnabled = chamCb.IsChecked != true;
            chamCb.Click += (s, e) => cbColor.IsEnabled = chamCb.IsChecked != true;
            // ---------------------------------------------------------------

            // UI FIX: Starts perfectly flush at the new 205px mark
            TextBlock lblEffect = new TextBlock { Text = Strings.LblEffect, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 10, 0) };
            ComboBox cbEffect = new ComboBox { Name = "LaunchEffectComboBox", Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Height = 25, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            // Same order as the LaunchEffect enum: the value is the index, not the label.
            foreach (string e in new[] { Strings.FxZoom, Strings.FxBounce, Strings.FxFadeOut, Strings.FxSlideUp, Strings.FxRotate, Strings.FxAgitate, Strings.FxGrowAndFly, Strings.FxPulse, Strings.FxElastic, Strings.FxFlip3D, Strings.FxSpiral, Strings.FxShockwave, Strings.FxMatrix, Strings.FxSupernova, Strings.FxTeleport }) cbEffect.Items.Add(e);
            cbEffect.SelectedIndex = (int)SettingsManager.LaunchEffect;

            Grid.SetColumn(lblColor, 0); g.Children.Add(lblColor);
            Grid.SetColumn(cbColor, 1); g.Children.Add(cbColor);
            Grid.SetColumn(lblEffect, 2); g.Children.Add(lblEffect);
            Grid.SetColumn(cbEffect, 3); g.Children.Add(cbEffect);

            p.Children.Add(g);
        }

        private static Button CreateStyledButton(string t, Color c) => new Button { Content = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(c), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

        private static void CreateLogLevelComboBox(StackPanel p)
        {
            Grid g = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.Children.Add(new TextBlock { Text = Strings.LblMinimumLogLevel, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            ComboBox cb = new ComboBox { Name = "LogLevelComboBox", Height = 25, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            foreach (var l in new[] { "Debug", "Info", "Warn", "Error" }) cb.Items.Add(l);
            cb.SelectedItem = SettingsManager.MinLogLevel.ToString();
            Grid.SetColumn(cb, 1); g.Children.Add(cb); p.Children.Add(g);
        }

        // --- Log Categories (Optimized & Fixed) ---
        private static void CreateLogCategoryCheckBoxes(StackPanel p)
        {
            Grid g = new Grid { Name = "LogCategoryGrid" };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel l = new StackPanel(); StackPanel r = new StackPanel();

            // FIX: Filter out the "Error" category from UI, as it's a Level, not a Category
            var cats = Enum.GetValues(typeof(LogManager.LogCategory))
                .Cast<LogManager.LogCategory>()
                .Where(c => c != LogManager.LogCategory.Error) // Hide Error
                .ToList();

            int half = (cats.Count + 1) / 2;

            for (int i = 0; i < cats.Count; i++)
            {
                var cb = new CheckBox { Content = Strings.Get("LogCategory" + cats[i]), Tag = cats[i], IsChecked = SettingsManager.EnabledLogCategories.Contains(cats[i]), FontSize = 13, Margin = new Thickness(15, 8, 0, 8) };
                if (i < half) l.Children.Add(cb); else r.Children.Add(cb);
            }

            Grid.SetColumn(l, 0); Grid.SetColumn(r, 1);
            g.Children.Add(l); g.Children.Add(r);
            p.Children.Add(g);
        }

        private static StackPanel GetTabContentStackPanel(TabItem tabItem)
        {
            if (tabItem == null) return null;
            if (tabItem.Content is ScrollViewer sv && sv.Content is StackPanel spFromSv)
                return spFromSv;
            if (tabItem.Content is StackPanel sp)
                return sp;
            return null;
        }

        // --- SAVING ---
        private static void SaveOptions()
        {
            try
            {
                bool tempPortalImageState = SettingsManager.ShowBackgroundImageOnPortalFrames;
                bool newPortalWatermarkState = false;
                bool newShowInTrayState = false;

                // 1. General
                var generalContent = GetTabContentStackPanel((TabItem)_tabControl.Items[0]);
                if (generalContent != null)
                {
                    var genCheckBoxes = FindDescendants<CheckBox>(generalContent);
                    foreach (var cb in genCheckBoxes)
                    {
                        if (cb.Name == "StartWithWindows" && cb.IsChecked != TrayManager.IsStartWithWindows) TrayManager.Instance?.ToggleStartWithWindows(cb.IsChecked == true);
                        if (cb.Name == "SingleClickToLaunch") SettingsManager.SingleClickToLaunch = cb.IsChecked == true;
                        if (cb.Name == "EnableSnapNearFrames") SettingsManager.IsSnapEnabled = cb.IsChecked == true;
                        if (cb.Name == "EnableDimensionSnap") SettingsManager.EnableDimensionSnap = cb.IsChecked == true;
                        if (cb.Name == "UseRecycleBin") SettingsManager.UseRecycleBin = cb.IsChecked == true;
                        if (cb.Name == "EnableTrayIcon")
                        {
                            newShowInTrayState = cb.IsChecked == true;
                            SettingsManager.ShowInTray = newShowInTrayState;
                            if (TrayManager.Instance != null) TrayManager.Instance.GetType().GetField("Showintray", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(TrayManager.Instance, newShowInTrayState);
                        }

                        // NEW: Context Menu Registry Update
                        if (cb.Name == "EnableContextMenu")
                        {
                            bool newState = cb.IsChecked == true;
                            if (SettingsManager.EnableContextMenu != newState)
                            {
                                SettingsManager.EnableContextMenu = newState;
                                RegistryHelper.ToggleContextMenu(newState);
                            }
                        }

                        if (cb.Name == "EnablePortalWatermark") { newPortalWatermarkState = cb.IsChecked == true; SettingsManager.ShowBackgroundImageOnPortalFrames = newPortalWatermarkState; }
                        if (cb.Name == "DisableFrameScrollbars") SettingsManager.DisableFrameScrollbars = cb.IsChecked == true;
                        if (cb.Name == "EnableSounds") SettingsManager.EnableSounds = cb.IsChecked == true;
                    }

                    var sndCombo = FindDescendants<ComboBox>(generalContent).FirstOrDefault(c => c.Name == "NotificationSoundComboBox");
                    if (sndCombo != null)
                    {
                        SettingsManager.NotificationSound = sndCombo.SelectedIndex switch
                        {
                            1 => NotificationSound.DoubleDing,
                            2 => NotificationSound.SmoothTickle,
                            3 => NotificationSound.MessageDing,
                            4 => NotificationSound.GentleDing,
                            5 => NotificationSound.SoftDing,
                            _ => NotificationSound.DefaultSound
                        };
                    }

                    var pvCombo = FindDescendants<ComboBox>(generalContent).FirstOrDefault(c => c.Name == "DefaultPortalViewComboBox");
                    if (pvCombo?.SelectedItem != null)
                    {
                        SettingsManager.DefaultPortalView = (pvCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? SettingsManager.DefaultPortalView;
                    }
                }

                if ((_languageCombo?.SelectedItem as ComboBoxItem)?.Tag is string languageTag)
                {
                    _languageChanged = !string.Equals(SettingsManager.Language, languageTag, StringComparison.OrdinalIgnoreCase);
                    SettingsManager.Language = languageTag;
                }

                // 2. Style
                var styleContent = GetTabContentStackPanel((TabItem)_tabControl.Items[1]);
                if (styleContent != null)
                {
                    var styleCheckBoxes = FindDescendants<CheckBox>(styleContent);
                    foreach (var cb in styleCheckBoxes)
                    {
                        if (cb.Name == "EnableChameleon") SettingsManager.EnableChameleonMode = cb.IsChecked == true;
                        if (cb.Name == "ApplyTintToIcons") SettingsManager.ApplyTintToIcons = cb.IsChecked == true;
                        if (cb.Name == "AutoHideFrames") { SettingsManager.AutoHideFrames = cb.IsChecked == true; Framemanager.ResetAutoHideTimer(); }
                        if (cb.Name == "FramesFadeOutFx") SettingsManager.FramesFadeOutFx = cb.IsChecked == true;

                        if (cb.Name == "HideDesktopElementsOnStart")
                        {
                            SettingsManager.HideDesktopElementsOnStart = cb.IsChecked == true;
                            DesktopIconManager.SetDesktopIconsVisible(!SettingsManager.HideDesktopElementsOnStart);
                        }
                        if (cb.Name == "HideDesktopElementsOnAllFramesHide") SettingsManager.HideDesktopElementsOnAllFramesHide = cb.IsChecked == true;
                        if (cb.Name == "ShowDesktopDot")
                        {
                            SettingsManager.ShowDesktopDot = cb.IsChecked == true;
                            DesktopIconManager.UpdateDotVisibility();
                        }
                    }

                    var styleSliders = FindDescendants<Slider>(styleContent);
                    var tint = styleSliders.FirstOrDefault(s => s.Name == "TintSlider"); if (tint != null) SettingsManager.TintValue = (int)tint.Value;
                    var mtint = styleSliders.FirstOrDefault(s => s.Name == "MenuTintSlider"); if (mtint != null) SettingsManager.MenuTintValue = (int)mtint.Value;
                    var autoHideTime = styleSliders.FirstOrDefault(s => s.Name == "AutoHideTimeSlider"); if (autoHideTime != null) { SettingsManager.AutoHideTime = (int)autoHideTime.Value; Framemanager.ResetAutoHideTimer(); }
                    var fadeOutTime = styleSliders.FirstOrDefault(s => s.Name == "FadeOutTimeSlider"); if (fadeOutTime != null) SettingsManager.FadeOutTime = (int)fadeOutTime.Value;
                    var fadeOutAlpha = styleSliders.FirstOrDefault(s => s.Name == "FadeOutAlphaSlider"); if (fadeOutAlpha != null) SettingsManager.FadeOutFxTargetAlpha = fadeOutAlpha.Value / 100.0;
                    var autoRollTime = styleSliders.FirstOrDefault(s => s.Name == "AutoRollTimeSlider"); if (autoRollTime != null) SettingsManager.AutoRollTime = (int)autoRollTime.Value;

                    var styleCombos = FindDescendants<ComboBox>(styleContent);
                    var col = styleCombos.FirstOrDefault(c => c.Name == "ColorComboBox"); if ((col?.SelectedItem as ComboBoxItem)?.Tag is string colTag) SettingsManager.SelectedColor = colTag;
                    var eff = styleCombos.FirstOrDefault(c => c.Name == "LaunchEffectComboBox"); if (eff != null) SettingsManager.LaunchEffect = (LaunchEffectsManager.LaunchEffect)eff.SelectedIndex;

                    var radioButtons = FindDescendants<RadioButton>(styleContent);
                    foreach (var rb in radioButtons)
                    {
                        if (rb.IsChecked == true)
                        {
                            if (rb.GroupName == "MenuIconGroup" && rb.Tag is int mi) SettingsManager.MenuIcon = mi;
                            if (rb.GroupName == "LockIconGroup" && rb.Tag is int li) SettingsManager.LockIcon = li;
                        }
                    }
                }

                // Note Default Style
                if ((_noteDefaultFontCombo?.SelectedItem as ComboBoxItem)?.Tag is string fontTag) SettingsManager.NoteDefaultFontFamily = fontTag;
                if ((_noteDefaultSizeCombo?.SelectedItem as ComboBoxItem)?.Tag is double sizeVal) SettingsManager.NoteDefaultFontSize = sizeVal;
                if ((_noteDefaultColorCombo?.SelectedItem as ComboBoxItem)?.Tag is string colorTag) SettingsManager.NoteDefaultColor = colorTag;

                // 3. Tools
                var toolsContent = GetTabContentStackPanel((TabItem)_tabControl.Items[2]);
                if (toolsContent != null)
                {
                    var autoBackupCb = FindDescendants<CheckBox>(toolsContent).FirstOrDefault(cb => cb.Name == "EnableAutoBackup");
                    if (autoBackupCb != null) SettingsManager.EnableAutoBackup = autoBackupCb.IsChecked == true;
                }

                // 4. Hotkeys
                var hotkeysContent = GetTabContentStackPanel((TabItem)_tabControl.Items[4]);
                bool hotkeysChanged = false;
                if (hotkeysContent != null)
                {
                    foreach (var hotkeyCb in FindDescendants<CheckBox>(hotkeysContent))
                    {
                        if (hotkeyCb.Name == "EnableProfileHotkeys" && SettingsManager.EnableProfileHotkeys != (hotkeyCb.IsChecked == true)) { SettingsManager.EnableProfileHotkeys = hotkeyCb.IsChecked == true; hotkeysChanged = true; }
                        if (hotkeyCb.Name == "EnableFocusFrameHotkey" && SettingsManager.EnableFocusFrameHotkey != (hotkeyCb.IsChecked == true)) { SettingsManager.EnableFocusFrameHotkey = hotkeyCb.IsChecked == true; hotkeysChanged = true; }
                        if (hotkeyCb.Name == "EnableSpotSearchHotkey" && SettingsManager.EnableSpotSearchHotkey != (hotkeyCb.IsChecked == true)) { SettingsManager.EnableSpotSearchHotkey = hotkeyCb.IsChecked == true; hotkeysChanged = true; }
                    }

                    foreach (var g in FindDescendants<Grid>(hotkeysContent))
                    {
                        if (g.Children.Count > 1 && g.Children[1] is StackPanel spMods)
                        {
                            string prefix = "";
                            foreach (var elem in spMods.Children)
                            {
                                if (elem is CheckBox cb && cb.Name.EndsWith("Ctrl"))
                                {
                                    prefix = cb.Name.Substring(0, cb.Name.Length - 4);
                                    break;
                                }
                            }
                            if (!string.IsNullOrEmpty(prefix))
                            {
                                List<string> mods = new List<string>();
                                int key = 0;
                                foreach (var elem in spMods.Children)
                                {
                                    if (elem is CheckBox cb && cb.IsChecked == true)
                                    {
                                        if (cb.Name.EndsWith("Ctrl")) mods.Add("Control");
                                        else if (cb.Name.EndsWith("Alt")) mods.Add("Alt");
                                        else if (cb.Name.EndsWith("Shift")) mods.Add("Shift");
                                        else if (cb.Name.EndsWith("Win")) mods.Add("Win");
                                    }
                                    if (elem is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item && item.Tag is int val)
                                    {
                                        key = val;
                                    }
                                }
                                string modString = string.Join(", ", mods);

                                if (prefix == "ProfSwitch") { if (SettingsManager.ProfileSwitchModifier != modString) { SettingsManager.ProfileSwitchModifier = modString; hotkeysChanged = true; } }
                                if (prefix == "ProfPrev") { if (SettingsManager.ProfilePrevModifier != modString || SettingsManager.ProfilePrevKey != key) { SettingsManager.ProfilePrevModifier = modString; SettingsManager.ProfilePrevKey = key; hotkeysChanged = true; } }
                                if (prefix == "ProfNext") { if (SettingsManager.ProfileNextModifier != modString || SettingsManager.ProfileNextKey != key) { SettingsManager.ProfileNextModifier = modString; SettingsManager.ProfileNextKey = key; hotkeysChanged = true; } }
                                if (prefix == "FocusFrame") { if (SettingsManager.FocusFrameModifier != modString || SettingsManager.FocusFrameKey != key) { SettingsManager.FocusFrameModifier = modString; SettingsManager.FocusFrameKey = key; hotkeysChanged = true; } }
                                if (prefix == "SpotSearch") { if (SettingsManager.SpotSearchModifier != modString || SettingsManager.SpotSearchKey != key) { SettingsManager.SpotSearchModifier = modString; SettingsManager.SpotSearchKey = key; hotkeysChanged = true; } }
                            }
                        }
                    }
                }

                if (hotkeysChanged)
                {
                    SettingsManager.BroadcastHotkeysToAllProfiles();
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgHotkeysSavedRestartNeeded, Strings.DlgRestartRequired);
                }

                // 5. Smart Desktop (Auto-Organize)
                var smartDesktopContent = GetTabContentStackPanel((TabItem)_tabControl.Items[5]);
                if (smartDesktopContent != null)
                {
                    foreach (var cb in FindDescendants<CheckBox>(smartDesktopContent))
                    {
                        if (cb.Name == "EnableAutoOrganize")
                        {
                            bool wasEnabled = SettingsManager.EnableAutoOrganize;
                            SettingsManager.EnableAutoOrganize = cb.IsChecked == true;
                            TrayManager.Instance?.UpdateAutoOrganizeMenuCheck(SettingsManager.EnableAutoOrganize);
                            if (!wasEnabled && SettingsManager.EnableAutoOrganize) AutoOrganizeManager.Start();
                            else if (wasEnabled && !SettingsManager.EnableAutoOrganize) AutoOrganizeManager.Stop();
                        }
                        if (cb.Name == "EnableAutoOrganizeNotifications")
                        {
                            SettingsManager.EnableAutoOrganizeNotifications = cb.IsChecked == true;
                        }
                    }
                }

                // 6. Look Deeper (Logs)
                var logContent = GetTabContentStackPanel((TabItem)_tabControl.Items[6]);
                var newEnabledCategories = new List<LogManager.LogCategory>();
                newEnabledCategories.Add(LogManager.LogCategory.Error);

                if (logContent != null)
                {
                    var enableLoggingCb = FindDescendants<CheckBox>(logContent).FirstOrDefault(cb => cb.Name == "EnableLogging");
                    if (enableLoggingCb != null) SettingsManager.IsLogEnabled = enableLoggingCb.IsChecked == true;

                    var lvl = FindDescendants<ComboBox>(logContent).FirstOrDefault(c => c.Name == "LogLevelComboBox");
                    if (lvl?.SelectedItem != null && Enum.TryParse<LogManager.LogLevel>(lvl.SelectedItem.ToString(), out var ll))
                        SettingsManager.SetMinLogLevel(ll);

                    foreach (var catBox in FindDescendants<CheckBox>(logContent))
                    {
                        if (catBox.IsChecked == true && catBox.Tag is LogManager.LogCategory cat)
                        {
                            newEnabledCategories.Add(cat);
                            if (cat == LogManager.LogCategory.BackgroundValidation)
                                SettingsManager.EnableBackgroundValidationLogging = true;
                        }
                    }
                }

                if (!newEnabledCategories.Contains(LogManager.LogCategory.BackgroundValidation))
                    SettingsManager.EnableBackgroundValidationLogging = false;

                SettingsManager.SetEnabledLogCategories(newEnabledCategories);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings, "Options saved successfully");

                if (tempPortalImageState != newPortalWatermarkState) TrayManager.reloadallFrames();
                TrayManager.Instance?.UpdateTrayIcon();
                Utility.UpdateFrameVisuals();

                // Broadcast settings to all frames
                var allFrames = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                foreach (var frame in allFrames)
                {
                    frame.RefreshIdleSettings();
                }

                Framemanager.RefreshAutoRollSettings();
                Framemanager.RefreshAllIconsTint();
                Framemanager.RefreshScrollbarSettings();

                _optionsWindow.Close();
                OfferRestartAfterLanguageChange();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"Error saving options: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgGenericError", ex.Message), Strings.DlgSaveError);
            }
        }

        private static void CreateFooter(Grid mainGrid)
        {
            Border f = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)), BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 8, 20, 8) };
            Grid.SetRow(f, 2);
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            Button c = new Button { Content = Strings.BtnCancel, Width = 100, Height = 34, FontWeight = FontWeights.Bold, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand };
            c.Click += (s, e) => _optionsWindow.Close();

            Button sv = new Button { Content = Strings.BtnSave, Width = 100, Height = 34, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(_userAccentColor), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            sv.Click += (s, e) => SaveOptions();

            sp.Children.Add(c); sp.Children.Add(sv); f.Child = sp; mainGrid.Children.Add(f);
        }

        private static void CreateDonationSection(Grid mainGrid)
        {
            Border d = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 248, 225)), BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20) };
            Grid.SetRow(d, 3);
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = Strings.LblDonate, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 3)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 15, 0) });
            Button b = new Button { Content = Strings.BtnDonate, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(255, 193, 7)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(15, 6, 15, 6), Cursor = Cursors.Hand };
            b.Click += (s, e) => { try { Process.Start(new ProcessStartInfo { FileName = "https://www.paypal.com/donate/?hosted_button_id=PPLWC66UC8Q42", UseShellExecute = true }); } catch { } };
            sp.Children.Add(b); d.Child = sp; mainGrid.Children.Add(d);
        }

        private static void RestoreBackup()
        {
            try
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    // FIX: Use the Profile-Aware path helper
                    d.SelectedPath = BackupManager.GetBackupsFolderPath();
                    d.Description = "Select a backup folder to restore from";

                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        BackupManager.RestoreFromBackup(d.SelectedPath);
                        _optionsWindow.Close();
                        TrayManager.reloadallFrames();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgRestoreFailed", ex.Message), Strings.DlgError);
            }
        }
        private static void OpenBackupsFolder()
        {
            // Use the centralized BackupManager helper
            BackupManager.OpenBackupsFolder();
        }

        private static void OpenLogFile()
        {
            try
            {
                string p = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Desktop_Frames.log");
                if (System.IO.File.Exists(p)) Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true });
                else MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgLogFileNotFound, Strings.DlgInformation);
            }
            catch { }
        }

        private static void PerformFullFactoryReset()
        {
            if (MessageBoxesManager.ShowCustomYesNoMessageBox(Strings.MsgConfirmFactoryReset, Strings.DlgFactoryReset))
            {
                // KISS: Hijack cursor to show processing
                System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait);
                try
                {
                    // 1. Create a safety backup before wiping
                    string ts = DateTime.Now.ToString("yyMMddHHmm");
                    BackupManager.CreateBackup($"{ts}_backup_reset", silent: true);

                    // 2. Wipe Profile-Specific Folders
                    foreach (string f in new[] { "Temp Shortcuts", "Shortcuts", "Last Frame Deleted", "CopiedItem" })
                    {
                        string p = ProfileManager.GetProfileFilePath(f);
                        if (System.IO.Directory.Exists(p))
                        {
                            try
                            {
                                System.IO.Directory.Delete(p, true);
                                System.IO.Directory.CreateDirectory(p); // Recreate empty folder
                            }
                            catch { }
                        }
                    }

                    // 3. Wipe Profile-Specific Config Files (OVERWRITE INSTEAD OF DELETE)
                    // FIX: Pointed to frames.json and wrote empty array to prevent read crashes
                    string fj = ProfileManager.GetProfileFilePath("frames.json");
                    System.IO.File.WriteAllText(fj, "[]");

                    string oj = ProfileManager.GetProfileFilePath("options.json");
                    System.IO.File.WriteAllText(oj, "{}");

                    // 4. Force a clean OS-level restart (Guarantees all UI clears properly)
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgFactoryResetDone, Strings.DlgResetSuccessful);

                    string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c ping 127.0.0.1 -n 3 > nul & start \"\" \"{appPath}\"",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });

                    // Instantly kill current process
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Error, $"Factory reset failed: {ex.Message}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgResetFailed", ex.Message), Strings.DlgError);
                }
                finally
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Input.Mouse.OverrideCursor = null);
                }
            }
        }
    }
}
