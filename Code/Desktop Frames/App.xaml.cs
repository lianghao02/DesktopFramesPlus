using DesktopFrames;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Desktop_Frames.Localization;

namespace Desktop_Frames
{
    public partial class App : Application
    {
        private TrayManager _trayManager;
        private TargetChecker _targetChecker;
        private static bool _desktopIsShown = false;
        private static Mutex _mutex;
        public static bool IsPrimaryInstance { get; private set; } = false;
        private const string UNIQUE_APP_NAME = "Global\\DesktopFramesPlus_Mutex_UniqueId_v2";

        public App()
        {
            this.DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    string logDir = AppDomain.CurrentDomain.BaseDirectory;
                    string crashPath = System.IO.Path.Combine(logDir, "crash.log");
                    string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DispatcherUnhandledException]\n{e.Exception}\n\n";
                    System.IO.File.AppendAllText(crashPath, logMessage);
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Error, $"DispatcherUnhandledException: {e.Exception}");
                }
                catch { }

                MessageBox.Show(
                    $"應用程式發生未攔截的錯誤：\n{e.Exception.Message}\n\n詳細資訊已記錄至 crash.log。",
                    "Desktop Frames 執行期錯誤",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                e.Handled = true; // 阻斷進一步拋出，防止應用程式直接閃退
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string logDir = AppDomain.CurrentDomain.BaseDirectory;
                    string crashPath = System.IO.Path.Combine(logDir, "crash.log");
                    string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [AppDomain UnhandledException]\n{e.ExceptionObject}\n\n";
                    System.IO.File.AppendAllText(crashPath, logMessage);
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Error, $"AppDomain UnhandledException: {e.ExceptionObject}");
                }
                catch { }
            };
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var startupTimer = System.Diagnostics.Stopwatch.StartNew();
            long previousCheckpoint = 0;
            void MarkStartup(string stage)
            {
                long elapsed = startupTimer.ElapsedMilliseconds;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"[啟動計時] {stage}：本段 {elapsed - previousCheckpoint} ms，累計 {elapsed} ms");
                previousCheckpoint = elapsed;
            }
            // 確保視窗關閉時絕不自動觸發應用程式結束（由使用者顯式關閉或托盤選單控制）
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // --- 1. INITIALIZE PROFILES & SETTINGS FIRST ---
            // This ensures we know the user's true DisableSingleInstance preference immediately
            try
            {
                ProfileManager.Initialize();
                System.IO.Directory.SetCurrentDirectory(ProfileManager.CurrentProfileDir);
                SettingsManager.LoadSettings();
                // Before any window is built, or half the interface would be
                // laid out in the language the setting is replacing.
                Desktop_Frames.Localization.Strings.SetLanguage(SettingsManager.Language);
                MarkStartup("工作區與首次設定載入");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.Get("MsgProfileInitializationError", ex.Message), Strings.Get("DlgStartupError"), MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // --- 2. SINGLE INSTANCE PROTECTION START ---
            bool isNewInstance;
            _mutex = new Mutex(true, UNIQUE_APP_NAME, out isNewInstance);
            IsPrimaryInstance = isNewInstance;

            // Only exit if it's not a new instance AND the user hasn't explicitly disabled the check
            if (!isNewInstance && !SettingsManager.DisableSingleInstance)
            {
                // --- DEBUGGING GHOSTS START ---
                try
                {
                    string debugLog = $"[{DateTime.Now}] Instance 2 Started.\n";
                    debugLog += $"Args (e.Args): {string.Join(" | ", e.Args)}\n";
                    debugLog += $"Args (Environment): {string.Join(" | ", Environment.GetCommandLineArgs())}\n";

                    bool isDrawCommand = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                         || Environment.GetCommandLineArgs().Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isDrawCommand)
                    {
                        RegistryHelper.WriteTrigger($"CMD_DRAW|{Guid.NewGuid()}");
                    }
                    else
                    {
                        RegistryHelper.WriteTrigger(null);
                    }
                }
                catch { }
                // --- DEBUGGING GHOSTS END ---

                Shutdown();
                return;
            }
            // --- SINGLE INSTANCE PROTECTION END ---

            try
            {
                // --- NEW: Sanitize Registry on Startup ---
                RegistryHelper.DeleteTrigger();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Startup: Working Directory set to {ProfileManager.CurrentProfileDir}");

                // 3. Continue Normal Startup
                {
                    // --- NEW: Self-Heal Context Menu Path ---
                    // Ensures the registry key points to the current EXE location
                    RegistryHelper.RefreshContextMenuPath();

                    // Initialize InterCore system
                    InterCore.Initialize();
                    MarkStartup("單一執行個體、設定與互動核心");

                    // --- CHAMELEON ENGINE ---
                    WallpaperColorManager.Initialize();
                    // --- AUTO-ORGANIZE ENGINE ---
                    AutoOrganizeManager.Initialize();
                    WallpaperColorManager.WallpaperColorChanged += (s, ev) =>
                    {
                        // Magically update all visuals live when Windows wallpaper changes
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Utility.UpdateFrameVisuals();
                        }));
                    };

                    // Initialize TrayManager BEFORE frames
                    _trayManager = new TrayManager();
                    _trayManager.InitializeTray();
                    MarkStartup("桌布、整理引擎與系統匣");

         

                    // Initialize TargetChecker
                    _targetChecker = new TargetChecker(1000);
                    _targetChecker.Start();

                    // Start the Background Icon Loader Engine
                    LazyIconLoader.Start();

                    // Load frames (Now loads from Profile/frames.json)
                    Framemanager.LoadAndCreateFrames(_targetChecker);
                    MarkStartup("面板資料與視窗建立");

                    // --- PRODUCTION START LOGIC ---
                    if (SettingsManager.EnableProfileAutomation)
                    {
                        AutomationManager.Start();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Startup: Profile Automation Engine ignited.");
                    }

                    // Ensure UI reflects the current state of profiles and automation
                    _trayManager.UpdateProfilesMenu();
                    _trayManager.UpdateTrayIcon();
                    _trayManager.UpdateHiddenFramesMenu();

                    // Initialize global hotkey monitoring
                    try
                    {
                        GlobalHotkeyManager.WindowsPlusDDetected += OnWindowsPlusDDetected;
                        GlobalHotkeyManager.StartMonitoring();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                            "GlobalHotkeyManager: Successfully initialized hotkey monitoring");
                    }
                    catch (System.Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            $"GlobalHotkeyManager: Failed to initialize: {ex.Message}");
                    }



                    // --- NEW: Start Desktop Double-Click Listener ---
                    try
                    {
                        DesktopMouseHook.Start();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "DesktopMouseHook started.");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to start DesktopMouseHook: {ex.Message}");
                    }



                    // --- NEW: Start Desktop Sticky Notes ---
                    MarkStartup("系統匣更新與全域監聽");
                    try
                    {
                        Desktop_Frames.Notes.Services.NoteManager.Initialize();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to initialize NoteManager: {ex.Message}");
                    }

                    // --- NEW: Direct Draw Mode Check ---
                    MarkStartup("便箋初始化");
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                        new Action(() =>
                        {
                            MarkStartup("首次 UI 閒置（非冷開機量測）");
                            if (SettingsManager.IsWriteProtected || FrameDataManager.IsWriteProtected)
                                MessageBox.Show(Strings.Get("MsgConfigurationProtected"), Strings.DlgError,
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                        }));

                    // If this MAIN instance was started via Context Menu, trigger draw mode now.
                    // Use the same robust check as above.
                    var allArgs = Environment.GetCommandLineArgs();
                    bool isDrawStartup = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                         || allArgs.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isDrawStartup)
                    {
                        // Wait 500ms for UI to settle, then draw
                        Task.Delay(500).ContinueWith(t => Dispatcher.Invoke(() => Framemanager.StartDrawMode()));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.Get("MsgCriticalStartupError", ex.Message), Strings.DlgError, MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Desktop_Frames.Notes.Services.NoteManager.Instance?.FlushAndCloseAll();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error in NoteManager.FlushAndCloseAll: {ex.Message}");
            }

            InterCore.Cleanup();
            try
            {
                GlobalHotkeyManager.StopMonitoring();
            }
            catch { }

            // --- NEW: Stop Desktop Double-Click Listener ---
            try
            {
                DesktopMouseHook.Stop();
            }
            catch { }

            _trayManager?.Dispose();
            base.OnExit(e);
        }

        private static void OnWindowsPlusDDetected(object sender, System.EventArgs e)
        {
            try
            {
                // 只要偵測到 Win+D 或點擊工作列顯示桌面按鈕，使用者的意圖皆為回到桌面
                // 立即設定極短延遲（80ms）等待 Windows DWM 最小化動作完成後，無條件喚醒所有桌面框架視窗
                var restoreTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(80)
                };
                restoreTimer.Tick += (timerSender, timerArgs) =>
                {
                    restoreTimer.Stop();
                    RestoreAllframeWindows();
                };
                restoreTimer.Start();
            }
            catch { }
        }

        private static void RestoreAllframeWindows()
        {
            try
            {
                var allWindows = System.Windows.Application.Current.Windows
                    .Cast<Window>()
                    .ToList();

                foreach (var win in allWindows)
                {
                    try
                    {
                        // 包含傳統桌面框架、原生農場柵欄 (FenceWindow) 以及桌面便箋視窗
                        bool isDesktopWidget = win is NonActivatingWindow ||
                                              win is Desktop_Frames.FarmFences.FenceWindow ||
                                              win.GetType().Name.Contains("NoteWindow");

                        if (!isDesktopWidget) continue;

                        if (win.WindowState == WindowState.Minimized)
                            win.WindowState = WindowState.Normal;

                        if (!win.IsVisible)
                            win.Show();

                        // 針對 NonActivatingWindow 重新刷過 Z-Order 確保顯示
                        if (win is NonActivatingWindow frameWindow)
                        {
                            frameWindow.Topmost = true;
                            frameWindow.Topmost = false;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
