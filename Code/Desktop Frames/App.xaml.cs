using DesktopFrames;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Runtime.InteropServices;
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
            // 常駐系統匣與桌面工具：必須以明確關閉為準，防止視窗所有權交由桌面 Progman 後被 WPF 自動關閉
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
            // --- 0. DIRECTORY GUARDRAIL: PREVENT PARALLEL UNIVERSES ---
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (appBaseDir.IndexOf(@"\Code\Desktop Frames\bin", StringComparison.OrdinalIgnoreCase) >= 0 && !System.Diagnostics.Debugger.IsAttached)
            {
                MessageBox.Show(
                    "【桌面整理工具防呆提示】\n\n" +
                    "偵測到您正嘗試從開發編譯目錄（Code\\bin）啟動。\n" +
                    "為防止設定與便箋產生平行時空，正式運行請一律使用：\n" +
                    "dist\\DesktopFramesPlus\\Desktop Frames.exe\n\n" +
                    "此開發執行個體將立即退出。",
                    "Desktop Frames 啟動防呆",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            // --- 1. SINGLE INSTANCE FAST-PATH FOR SECONDARY (-create) COMMANDS ---
            bool isNewInstance;
            _mutex = new Mutex(true, UNIQUE_APP_NAME, out isNewInstance);
            IsPrimaryInstance = isNewInstance;

            bool isDrawCommand = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                 || Environment.GetCommandLineArgs().Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isNewInstance && isDrawCommand)
            {
                try { RegistryHelper.WriteTrigger($"CMD_DRAW|{Guid.NewGuid()}"); } catch { }
                Shutdown();
                return;
            }

            // --- 2. INITIALIZE PROFILES & SETTINGS ---
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

            // Only exit if it's not a new instance AND the user hasn't explicitly disabled the check
            if (!isNewInstance && !SettingsManager.DisableSingleInstance)
            {
                try
                {
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
                    RegistryHelper.RefreshStartupPath();

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static void OnWindowsPlusDDetected(object sender, System.EventArgs e)
        {
            try
            {
                // 分段確認：涵蓋 Windows DWM 最小化與顯示桌面動畫週期（100ms 與 300ms），確保平滑浮現不閃爍
                ScheduleRestoreCheck(100);
                ScheduleRestoreCheck(300);
            }
            catch { }
        }

        private static void ScheduleRestoreCheck(int delayMs)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                RestoreAllframeWindows();
            };
            timer.Start();
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

                        // 針對分類面板（NonActivatingWindow）使用平滑 Win32 喚醒，徹底取代舊版 Topmost 閃爍刷屏
                        if (win is NonActivatingWindow frameWindow)
                        {
                            if (!frameWindow.Topmost)
                            {
                                var hwnd = new System.Windows.Interop.WindowInteropHelper(frameWindow).Handle;
                                if (hwnd != IntPtr.Zero)
                                {
                                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
