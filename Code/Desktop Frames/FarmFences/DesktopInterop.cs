using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Threading.Tasks;

namespace Desktop_Frames.FarmFences
{
    public class DesktopIconItem
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ResolvedPath { get; set; } = string.Empty;
        public Point ScreenPoint { get; set; }
        public Rect BoundsOnScreen { get; set; }
        public Size Spacing { get; set; }
        public bool IsSelected { get; set; }

        public override string ToString() => $"[{Index}] '{Name}' @ ({ScreenPoint.X:F0}, {ScreenPoint.Y:F0}) -> {ResolvedPath}";
    }

    public static class DesktopInterop
    {
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rectangle, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public RECT Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        public static List<Rect> GetWorkAreas()
        {
            var areas = new List<Rect>();
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info)) areas.Add(new Rect(info.Work.Left, info.Work.Top,
                    info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top));
                return true;
            }, IntPtr.Zero) || areas.Count == 0) throw new InvalidOperationException("無法取得螢幕工作區");
            return areas;
        }
        #region Win32 Constants and Structs

        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint SIGDN_NORMALDISPLAY = 0x00000000;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

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

        #endregion

        #region Shell COM & Accessibility Interfaces

        private const uint OBJID_CLIENT = 0xFFFFFFFC;
        private static readonly Guid IID_IAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

        [ComImport]
        [Guid("cde725b0-ccc9-4519-917e-325d72fab4ce")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFolderView
        {
            void GetCurrentViewMode(out uint pViewMode);
            void SetCurrentViewMode(uint ViewMode);
            void GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig]
            int Item(int iItemIndex, out IntPtr ppidl);
            [PreserveSig]
            int ItemCount(uint uFlags, out int pcItems);
            void Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            void GetSelectionMarkedItem(out int piItem);
            void GetFocusedItem(out int piItem);
            [PreserveSig]
            int GetItemPosition(IntPtr pidl, out POINT ppt);
            void GetSpacing(out POINT ppt);
            void GetDefaultSpacing(out POINT ppt);
            [PreserveSig]
            int GetAutoArrange();
            void SelectItem(int iItem, uint dwFlags);
            void SelectAndPositionItems(uint cItems, IntPtr apidl, IntPtr apt, uint dwFlags);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHCreateItemWithParent(IntPtr parentPidl,
            [MarshalAs(UnmanagedType.IUnknown)] object folder, IntPtr childPidl,
            ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        private static string ShellName(object folder, IntPtr pidl, uint kind)
        {
            Guid iid = typeof(IShellItem).GUID;
            SHCreateItemWithParent(IntPtr.Zero, folder, pidl, ref iid, out var item);
            try
            {
                item.GetDisplayName(kind, out var text);
                try { return Marshal.PtrToStringUni(text) ?? string.Empty; }
                finally { CoTaskMemFree(text); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }

        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellBrowser
        {
            // IOleWindow 方法 (vtable slot 3 & 4)
            [PreserveSig]
            int GetWindow(out IntPtr phwnd);
            [PreserveSig]
            int ContextSensitiveHelp(bool fEnterMode);

            // IShellBrowser 方法
            [PreserveSig]
            int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
            [PreserveSig]
            int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
            [PreserveSig]
            int RemoveMenusSB(IntPtr hmenuShared);
            [PreserveSig]
            int SetStatusTextSB(IntPtr pszStatusText);
            [PreserveSig]
            int EnableModelessSB(bool fEnable);
            [PreserveSig]
            int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
            [PreserveSig]
            int BrowseObject(IntPtr pidl, uint wFlags);
            [PreserveSig]
            int GetViewStateStream(uint mode, IntPtr ppStrm);
            [PreserveSig]
            int GetControlWindow(uint id, out IntPtr phwnd);
            [PreserveSig]
            int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr pret);
            [PreserveSig]
            int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object ppshv);
            [PreserveSig]
            int GetViewRect(out RECT prcView);
            [PreserveSig]
            int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
        }

        [ComImport]
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoints, uint cPoints);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        private static readonly Guid FOLDERID_Desktop = new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
        private static readonly Guid FOLDERID_PublicDesktop = new Guid("C4AA340D-F20F-4863-AFEF-F87EF2E65825");

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        #endregion

        #region 長駐單一 STA 桌面代理執行緒 (避免頻繁建立執行緒與資源洩漏)

        private static readonly BlockingCollection<Action> _desktopTaskQueue = new BlockingCollection<Action>();
        private static Thread? _workerThread;
        private static readonly object _threadLock = new object();
        private static IntPtr _savedDesktopHandle = IntPtr.Zero;

        private static void EnsureWorkerRunning()
        {
            lock (_threadLock)
            {
                if (_workerThread != null && _workerThread.IsAlive) return;

                _workerThread = new Thread(DesktopWorkerLoop)
                {
                    Name = "DesktopWorkerSTA",
                    IsBackground = true
                };
                _workerThread.SetApartmentState(ApartmentState.STA);
                _workerThread.Start();
            }
        }

        private static void DesktopWorkerLoop()
        {
            try
            {
                _savedDesktopHandle = OpenDesktop("Default", 0, false, 0x10000000);
                if (_savedDesktopHandle != IntPtr.Zero)
                {
                    SetThreadDesktop(_savedDesktopHandle);
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke($"切換桌面 Station 例外: {ex.Message}");
            }

            foreach (var action in _desktopTaskQueue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    DiagnosticLog?.Invoke($"DesktopWorkerAction 例外: {ex.Message}");
                }
            }

            if (_savedDesktopHandle != IntPtr.Zero)
            {
                CloseDesktop(_savedDesktopHandle);
                _savedDesktopHandle = IntPtr.Zero;
            }
        }

        public static T RunOnDesktopThread<T>(Func<T> func)
        {
            if (Thread.CurrentThread == _workerThread) return func();
            EnsureWorkerRunning();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _desktopTaskQueue.Add(() =>
            {
                try { completion.TrySetResult(func()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            // 呼叫端在背景執行緒等待完成，避免逾時後仍執行排隊中的位置變更。
            return completion.Task.GetAwaiter().GetResult();
        }

        public static void ShutdownWorker() => _desktopTaskQueue.CompleteAdding();

        #endregion

        public static event Action<string>? DiagnosticLog;

        /// <summary>
        /// 取得 Windows 桌面 SysListView32 視窗控制代碼
        /// </summary>
        public static IntPtr GetDesktopListViewHwnd()
        {
            return RunOnDesktopThread(GetDesktopListViewHwndInternal);
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
        /// 透過 Windows KnownFolder API 取得當前真實桌面路徑 (支援 OneDrive / 個人 / 公用桌面)
        /// </summary>
        public static List<string> GetActualDesktopDirectories()
        {
            var paths = new List<string>();

            if (SHGetKnownFolderPath(FOLDERID_Desktop, 0, IntPtr.Zero, out IntPtr userDesktopPtr) == 0)
            {
                string? p = Marshal.PtrToStringUni(userDesktopPtr);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) paths.Add(p);
                CoTaskMemFree(userDesktopPtr);
            }

            if (SHGetKnownFolderPath(FOLDERID_PublicDesktop, 0, IntPtr.Zero, out IntPtr publicDesktopPtr) == 0)
            {
                string? p = Marshal.PtrToStringUni(publicDesktopPtr);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) paths.Add(p);
                CoTaskMemFree(publicDesktopPtr);
            }

            if (paths.Count == 0)
            {
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            }

            return paths;
        }

        /// <summary>
        /// 透過 Windows Shell 官方 COM 物件取得桌面的 IFolderView (零進程注入、零防毒誤報)
        /// </summary>
        public static IFolderView? GetDesktopFolderView()
        {
            object? windows = null, dispatch = null, browserObject = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!);
                dynamic shellWindows = windows!;
                object location = Type.Missing, root = Type.Missing;
                int hwnd;
                dispatch = shellWindows.FindWindowSW(ref location, ref root, 8, out hwnd, 1);
                var provider = (IServiceProvider)dispatch;
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                Guid iid = typeof(IShellBrowser).GUID;
                Marshal.ThrowExceptionForHR(provider.QueryService(ref service, ref iid, out browserObject));
                Marshal.ThrowExceptionForHR(((IShellBrowser)browserObject).QueryActiveShellView(out var view));
                return (IFolderView)view;
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke($"Shell 桌面檢視無法取得：{ex.Message}");
                return null;
            }
            finally
            {
                if (browserObject != null) Marshal.ReleaseComObject(browserObject);
                if (dispatch != null) Marshal.ReleaseComObject(dispatch);
                if (windows != null) Marshal.ReleaseComObject(windows);
            }
        }

        /// <summary>
        /// 讀取桌面原生圖示清單與螢幕物理座標 (純 Win32 唯讀讀取，防禦 100% 相容，絕不按順序硬配)
        /// </summary>
        public static List<DesktopIconItem> GetAllDesktopIcons()
        {
            return RunOnDesktopThread(() =>
            {
                var result = new List<DesktopIconItem>();
                IntPtr hwnd = GetDesktopListViewHwndInternal();
                if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Explorer 桌面尚未就緒");
                var view = GetDesktopFolderView() ?? throw new InvalidOperationException("無法取得 Shell 桌面檢視");
                object? folder = null;
                try
                {
                    Guid iid = new Guid("000214E6-0000-0000-C000-000000000046");
                    view.GetFolder(ref iid, out folder);
                    Marshal.ThrowExceptionForHR(view.ItemCount(2, out int count));
                    view.GetSpacing(out var spacing);
                    var itemBounds = ReadNativeBounds(hwnd, count);
                    {
                        // Shell 身分來自父資料夾與子 PIDL，不猜名稱、不保存畫面索引。
                        for (int i = 0; i < count; i++)
                        {
                            Marshal.ThrowExceptionForHR(view.Item(i, out IntPtr pidl));
                            try
                            {
                                Marshal.ThrowExceptionForHR(view.GetItemPosition(pidl, out POINT screenPt));
                                MapWindowPoints(hwnd, IntPtr.Zero, ref screenPt, 1);
                                string name = ShellName(folder, pidl, SIGDN_NORMALDISPLAY);
                                string key = ShellName(folder, pidl, 0x80028000); // SIGDN_DESKTOPABSOLUTEPARSING
                                Rect bounds = Rect.Empty;
                                // 原生 ListView 提供實際圖示／標籤範圍；依位置匹配，不將索引當身分。
                                foreach (var candidate in itemBounds)
                                {
                                    if (candidate.Contains(new Point(screenPt.X + spacing.X / 2.0, screenPt.Y + spacing.Y / 2.0)))
                                    {
                                        bounds = candidate;
                                        break;
                                    }
                                }
                                result.Add(new DesktopIconItem
                                {
                                    Index = i, Name = name, ResolvedPath = key,
                                    IsSelected = (((long)SendMessage(hwnd, LVM_FIRST + 44, (IntPtr)i, (IntPtr)2)) & 2) != 0,
                                    ScreenPoint = new Point(screenPt.X, screenPt.Y),
                                    Spacing = new Size(Math.Max(1, spacing.X), Math.Max(1, spacing.Y)),
                                    BoundsOnScreen = bounds
                                });
                            }
                            finally { CoTaskMemFree(pidl); }
                        }
                    }
                }
                finally
                {
                    if (folder != null) Marshal.ReleaseComObject(folder);
                    Marshal.ReleaseComObject(view);
                }
                return result;
            });
        }

        private static List<Rect> ReadNativeBounds(IntPtr hwnd, int count)
        {
            if (count == 0) return new List<Rect>();
            GetWindowThreadProcessId(hwnd, out uint pid);
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (process == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr remote = IntPtr.Zero;
            IntPtr local = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
            try
            {
                uint size = checked((uint)(count * Marshal.SizeOf<RECT>()));
                remote = VirtualAllocEx(process, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remote == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var result = new List<Rect>();
                for (int i = 0; i < count; i++)
                {
                    // 每個區段初值為 0 (LVIR_BOUNDS)，不寫入 Explorer 記憶體。
                    IntPtr segment = IntPtr.Add(remote, i * Marshal.SizeOf<RECT>());
                    if (SendMessage(hwnd, LVM_FIRST + 14, (IntPtr)i, segment) == IntPtr.Zero) continue;
                    if (!ReadProcessMemory(process, segment, local, (uint)Marshal.SizeOf<RECT>(), out var read) || read.ToInt64() != Marshal.SizeOf<RECT>())
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    var rectangle = Marshal.PtrToStructure<RECT>(local);
                    POINT corner = new POINT { X = rectangle.Left, Y = rectangle.Top };
                    MapWindowPoints(hwnd, IntPtr.Zero, ref corner, 1);
                    result.Add(new Rect(corner.X, corner.Y, Math.Max(0, rectangle.Right - rectangle.Left), Math.Max(0, rectangle.Bottom - rectangle.Top)));
                }
                return result;
            }
            finally
            {
                if (remote != IntPtr.Zero) VirtualFreeEx(process, remote, 0, MEM_RELEASE);
                Marshal.FreeHGlobal(local);
                CloseHandle(process);
            }
        }

        public static bool MoveShellItem(string identity, Point screenPoint)
        {
            return RunOnDesktopThread(() =>
            {
                var view = GetDesktopFolderView() ?? throw new InvalidOperationException("無法取得 Shell 桌面檢視");
                object? folder = null;
                try
                {
                    Guid iid = new Guid("000214E6-0000-0000-C000-000000000046");
                    view.GetFolder(ref iid, out folder);
                    Marshal.ThrowExceptionForHR(view.ItemCount(2, out int count));
                    for (int i = 0; i < count; i++)
                    {
                        Marshal.ThrowExceptionForHR(view.Item(i, out IntPtr pidl));
                        try
                        {
                            if (!string.Equals(ShellName(folder, pidl, 0x80028000), identity, StringComparison.OrdinalIgnoreCase)) continue;
                            var point = new POINT { X = (int)Math.Round(screenPoint.X), Y = (int)Math.Round(screenPoint.Y) };
                            MapWindowPoints(IntPtr.Zero, GetDesktopListViewHwndInternal(), ref point, 1);
                            IntPtr items = Marshal.AllocHGlobal(IntPtr.Size);
                            IntPtr points = Marshal.AllocHGlobal(Marshal.SizeOf<POINT>());
                            try
                            {
                                Marshal.WriteIntPtr(items, pidl);
                                Marshal.StructureToPtr(point, points, false);
                                view.SelectAndPositionItems(1, items, points, 0x80); // SVSI_POSITIONITEM
                                Marshal.ThrowExceptionForHR(view.GetItemPosition(pidl, out var actual));
                                return Math.Abs(actual.X - point.X) <= 2 && Math.Abs(actual.Y - point.Y) <= 2;
                            }
                            finally { Marshal.FreeHGlobal(items); Marshal.FreeHGlobal(points); }
                        }
                        finally { CoTaskMemFree(pidl); }
                    }
                    return false;
                }
                finally
                {
                    if (folder != null) Marshal.ReleaseComObject(folder);
                    Marshal.ReleaseComObject(view);
                }
            });
        }

    }
}
