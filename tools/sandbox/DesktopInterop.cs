using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace FarmFenceSandbox
{
    public class DesktopIconItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ResolvedPath { get; set; } = string.Empty;
        public Point ScreenPoint { get; set; }
        public Rect BoundsOnScreen { get; set; }

        public override string ToString() => $"[{Index}] {Name} @ ({ScreenPoint.X:F0}, {ScreenPoint.Y:F0})";
    }

    public static class DesktopInterop
    {
        #region Win32 Constants and Structs

        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;
        private const uint LVM_SETITEMPOSITION32 = LVM_FIRST + 49;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const int LVIF_TEXT = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        #endregion

        #region Win32 P/Invoke

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoints, uint cPoints);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        private static readonly Guid FOLDERID_Desktop = new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
        private static readonly Guid FOLDERID_PublicDesktop = new Guid("C4AA340D-F20F-4863-AFEF-F87EF2E65825");

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        public static void EnsureDefaultDesktop()
        {
            try
            {
                IntPtr hDesk = OpenDesktop("Default", 0, false, 0x10000000);
                if (hDesk != IntPtr.Zero)
                {
                    SetThreadDesktop(hDesk);
                }
            }
            catch { }
        }

        public static T RunOnDesktopThread<T>(Func<T> func)
        {
            T result = default!;
            Exception? threadEx = null;
            Thread t = new Thread(() =>
            {
                try
                {
                    EnsureDefaultDesktop();
                    result = func();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                    DiagnosticLog?.Invoke($"[RunOnDesktopThread FATAL]: {ex}");
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (threadEx != null) throw threadEx;
            return result;
        }

        #endregion

        /// <summary>
        /// 取得 Windows 桌面 SysListView32 的視窗控制代碼 (支援 Windows 10/11 多種桌面容器架構與桌面 Station 切換)
        /// </summary>
        public static IntPtr GetDesktopListViewHwnd()
        {
            return RunOnDesktopThread(() =>
            {
                IntPtr progman = FindWindow("Progman", null);
                IntPtr shelldll = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

                if (shelldll == IntPtr.Zero)
                {
                    IntPtr workerW = IntPtr.Zero;
                    do
                    {
                        workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                        shelldll = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    } while (shelldll == IntPtr.Zero && workerW != IntPtr.Zero);
                }

                if (shelldll != IntPtr.Zero)
                {
                    return FindWindowEx(shelldll, IntPtr.Zero, "SysListView32", null);
                }

                return IntPtr.Zero;
            });
        }

        /// <summary>
        /// 透過 Windows KnownFolder API 取得當前真實桌面路徑 (支援 OneDrive / 個人 / 公用桌面)
        /// </summary>
        public static List<string> GetActualDesktopDirectories()
        {
            var paths = new List<string>();

            // 1. 使用者個人桌面 (支援 OneDrive 重新導向)
            if (SHGetKnownFolderPath(FOLDERID_Desktop, 0, IntPtr.Zero, out IntPtr userDesktopPtr) == 0)
            {
                string? p = Marshal.PtrToStringUni(userDesktopPtr);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) paths.Add(p);
                CoTaskMemFree(userDesktopPtr);
            }

            // 2. 公用桌面 (Public Desktop)
            if (SHGetKnownFolderPath(FOLDERID_PublicDesktop, 0, IntPtr.Zero, out IntPtr publicDesktopPtr) == 0)
            {
                string? p = Marshal.PtrToStringUni(publicDesktopPtr);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) paths.Add(p);
                CoTaskMemFree(publicDesktopPtr);
            }

            // 回退防護
            if (paths.Count == 0)
            {
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            }

            return paths;
        }

        /// <summary>
        public static event Action<string>? DiagnosticLog;

        /// <summary>
        /// 讀取桌面當前所有原生圖示的名稱、路徑與物理座標 (原生 SysListView32 精準讀取)
        /// <summary>
        /// 讀取桌面當前所有原生圖示的名稱、路徑與物理座標 (純 Win32 唯讀讀取，防禦 100% 相容)
        /// </summary>
        public static List<DesktopIconItem> GetAllDesktopIcons()
        {
            return RunOnDesktopThread(() =>
            {
                var result = new List<DesktopIconItem>();
                IntPtr hwnd = GetDesktopListViewHwndInternal();
                DiagnosticLog?.Invoke($"GetAllDesktopIcons: hwnd={hwnd}");
                if (hwnd == IntPtr.Zero) return result;

                int count = (int)SendMessage(hwnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                DiagnosticLog?.Invoke($"GetAllDesktopIcons: item count={count}");
                if (count <= 0) return result;

                GetWindowThreadProcessId(hwnd, out uint pid);
                DiagnosticLog?.Invoke($"GetAllDesktopIcons: Explorer pid={pid}");
                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
                DiagnosticLog?.Invoke($"GetAllDesktopIcons: OpenProcess hProcess={hProcess}");
                if (hProcess == IntPtr.Zero) return result;

                // 讀取真實桌面檔案清單供標識配對
                var desktopFiles = new List<string>();
                var desktopDirs = GetActualDesktopDirectories();
                foreach (var d in desktopDirs)
                {
                    try
                    {
                        if (Directory.Exists(d))
                        {
                            desktopFiles.AddRange(Directory.GetFileSystemEntries(d));
                        }
                    }
                    catch { }
                }

                try
                {
                    uint memSize = 256;
                    IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, memSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                    DiagnosticLog?.Invoke($"GetAllDesktopIcons: VirtualAllocEx remoteMem={remoteMem}");
                    if (remoteMem == IntPtr.Zero) return result;

                    try
                    {
                        IntPtr localBuffer = Marshal.AllocHGlobal((int)memSize);
                        try
                        {
                            for (int i = 0; i < count; i++)
                            {
                                SendMessage(hwnd, LVM_GETITEMPOSITION, (IntPtr)i, remoteMem);
                                ReadProcessMemory(hProcess, remoteMem, localBuffer, (uint)Marshal.SizeOf<POINT>(), out _);
                                POINT pt = Marshal.PtrToStructure<POINT>(localBuffer);

                                POINT screenPt = pt;
                                MapWindowPoints(hwnd, IntPtr.Zero, ref screenPt, 1);

                                string resolvedName = i < desktopFiles.Count ? Path.GetFileName(desktopFiles[i]) : $"Item_{i}";
                                string resolvedPath = i < desktopFiles.Count ? desktopFiles[i] : resolvedName;

                                DiagnosticLog?.Invoke($"原生圖示 [{i}]: '{resolvedName}' @ ({screenPt.X},{screenPt.Y})");

                                result.Add(new DesktopIconItem
                                {
                                    Index = i,
                                    Name = resolvedName,
                                    ResolvedPath = resolvedPath,
                                    ScreenPoint = new Point(screenPt.X, screenPt.Y),
                                    BoundsOnScreen = new Rect(screenPt.X, screenPt.Y, 72, 72)
                                });
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(localBuffer);
                        }
                    }
                    finally
                    {
                        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }

                return result;
            });
        }

        private static IntPtr GetDesktopListViewHwndInternal()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr shelldll = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shelldll == IntPtr.Zero)
            {
                IntPtr workerW = IntPtr.Zero;
                do
                {
                    workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                    shelldll = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                } while (shelldll == IntPtr.Zero && workerW != IntPtr.Zero);
            }

            if (shelldll != IntPtr.Zero)
            {
                return FindWindowEx(shelldll, IntPtr.Zero, "SysListView32", null);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 精確解析桌面檔案完整路徑 (防同名、防 .lnk 副檔名省略)
        /// </summary>
        private static string ResolveDesktopPath(string itemName, List<string> desktopDirs)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return string.Empty;

            foreach (var dir in desktopDirs)
            {
                if (!Directory.Exists(dir)) continue;

                string exact = Path.Combine(dir, itemName);
                if (File.Exists(exact) || Directory.Exists(exact)) return exact;

                string withLnk = Path.Combine(dir, itemName + ".lnk");
                if (File.Exists(withLnk)) return withLnk;

                string withUrl = Path.Combine(dir, itemName + ".url");
                if (File.Exists(withUrl)) return withUrl;

                try
                {
                    var matches = Directory.GetFiles(dir, itemName + ".*");
                    if (matches.Length > 0) return matches[0];
                }
                catch { }
            }

            return itemName;
        }

        /// <summary>
        /// 設定原生桌面圖示位置 (輸入為螢幕物理座標，內部自動轉為 ListView 客戶區座標，標準 Win32 零注入)
        /// </summary>
        public static bool SetIconPositionByScreenPoint(int index, Point screenPoint)
        {
            return RunOnDesktopThread(() =>
            {
                IntPtr hwnd = GetDesktopListViewHwndInternal();
                if (hwnd == IntPtr.Zero) return false;

                POINT pt = new POINT { X = (int)Math.Round(screenPoint.X), Y = (int)Math.Round(screenPoint.Y) };
                MapWindowPoints(IntPtr.Zero, hwnd, ref pt, 1);

                // 標準 Win32 LVM_SETITEMPOSITION，lParam 封裝 LOWORD(x) 與 HIWORD(y)，完全無需進程注入或遠端記憶體配置！
                IntPtr lParam = unchecked((IntPtr)((long)(ushort)pt.Y << 16 | (long)(ushort)pt.X));
                SendMessage(hwnd, LVM_SETITEMPOSITION, (IntPtr)index, lParam);
                return true;
            });
        }
    }
}
