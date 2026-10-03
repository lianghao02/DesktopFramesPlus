using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace FarmFenceSandbox
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        private FenceManager? _fenceManager;
        private ControlPanelWindow? _controlPanel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
            var allArgs = Environment.GetCommandLineArgs();
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Started with args: {string.Join(" ", allArgs)}\n");

            void Log(string line)
            {
                try { Console.WriteLine(line); } catch { }
                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
            }

            DesktopInterop.DiagnosticLog += Log;

            if (allArgs.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)) || (e.Args != null && e.Args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase))))
            {
                Log("進入 --test CLI 模式");
                try
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    Log("================ 農場柵欄底層實測 (CLI 模式) ================");

                    // 1. HWND 取得
                    Log("正在取得桌面 SysListView32 HWND...");
                    IntPtr hwnd = DesktopInterop.GetDesktopListViewHwnd();
                    Log($"[1] 桌面 SysListView32 HWND: {hwnd}");
                    if (hwnd == IntPtr.Zero)
                    {
                        Log("[FAIL] 無法找到桌面 SysListView32 視窗！");
                        Shutdown(1);
                        return;
                    }

                    // 2. 桌面真實目錄解析
                    Log("\n[2] 解析有效桌面目錄 (支援 OneDrive 重新導向):");
                    var dirs = DesktopInterop.GetActualDesktopDirectories();
                    foreach (var dir in dirs)
                    {
                        Log($"    - {dir} (存在: {Directory.Exists(dir)})");
                    }

                    // 3. 讀取原生圖示列表與座標
                    Log("\n[3] 讀取桌面原生圖示清單與螢幕物理座標:");
                    var icons = DesktopInterop.GetAllDesktopIcons();
                    Log($"    共讀取到 {icons.Count} 個項目：");
                    foreach (var icon in icons.Take(25))
                    {
                        Log($"    - [{icon.Index}] '{icon.Name}' @ ({icon.ScreenPoint.X:F0}, {icon.ScreenPoint.Y:F0})");
                        if (!string.IsNullOrEmpty(icon.ResolvedPath))
                        {
                            Log($"      -> 真實路徑: {icon.ResolvedPath}");
                        }
                    }
                    if (icons.Count > 25)
                    {
                        Log($"    ... (其餘 {icons.Count - 25} 個省略)");
                    }

                    Log("\n================ 實測完成，底層通訊與座標讀取正常 ================\n");
                    Shutdown(0);
                    return;
                }
                catch (Exception ex)
                {
                    string err = $"[FATAL ERROR in --test]: {ex}\nInner: {ex.InnerException}";
                    Log(err);
                    try { Console.Error.WriteLine(err); } catch { }
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_error.txt"), err); } catch { }
                    Shutdown(1);
                    return;
                }
            }

            // 正常 GUI 啟動
            _fenceManager = new FenceManager();
            _controlPanel = new ControlPanelWindow(_fenceManager);
            _controlPanel.Show();
            _fenceManager.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _fenceManager?.Stop();
            base.OnExit(e);
        }
    }
}
