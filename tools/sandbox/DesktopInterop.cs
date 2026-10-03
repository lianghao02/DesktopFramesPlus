using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

        public override string ToString() => $"[{Index}] '{Name}' @ ({ScreenPoint.X:F0}, {ScreenPoint.Y:F0}) -> {ResolvedPath}";
    }

    public static class DesktopInterop
    {
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
        [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IAccessible
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object get_accParent();
            int get_accChildCount();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object get_accChild(object varChild);
            [return: MarshalAs(UnmanagedType.BStr)]
            string get_accName(object varChild);
            [return: MarshalAs(UnmanagedType.BStr)]
            string get_accValue(object varChild);
            [return: MarshalAs(UnmanagedType.BStr)]
            string get_accDescription(object varChild);
            object get_accRole(object varChild);
            object get_accState(object varChild);
            [return: MarshalAs(UnmanagedType.BStr)]
            string get_accHelp(object varChild);
            int get_accHelpTopic(out string pszHelpFile, object varChild);
            [return: MarshalAs(UnmanagedType.BStr)]
            string get_accKeyboardShortcut(object varChild);
            object get_accFocus();
            object get_accSelection();
            [return: MarshalAs(UnmanagedType.BStr)]
            string get_accDefaultAction(object varChild);
            void accSelect(int flagsSelect, object varChild);
            void accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, object varChild);
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object accNavigate(int navDir, object varStart);
            object accHitTest(int xLeft, int yTop);
            void accDoDefaultAction(object varChild);
            void set_accName(object varChild, [MarshalAs(UnmanagedType.BStr)] string pszName);
            void set_accValue(object varChild, [MarshalAs(UnmanagedType.BStr)] string pszValue);
        }

        [DllImport("oleacc.dll", ExactSpelling = true, PreserveSig = false)]
        [return: MarshalAs(UnmanagedType.Interface)]
        public static extern object AccessibleObjectFromWindow(IntPtr hwnd, uint dwId, ref Guid riid);

        [ComImport]
        [Guid("cde725b0-ccc9-451e-800e-8507c5e2e0e9")]
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
            void GetAutoArrange();
            void SelectItem(int iItem, uint dwFlags);
            void SelectAndPositionItems(uint cItems, IntPtr apidl, IntPtr apt, uint dwFlags);
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

        [ComImport]
        [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        public interface IShellWindows
        {
            int Count { get; }
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object Item([MarshalAs(UnmanagedType.Struct)] object index);
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object pvarLoc, [MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot, int swClass, out int pHWND, int swflags);
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

        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

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
            EnsureWorkerRunning();
            T result = default!;
            Exception? caughtEx = null;
            using var doneEvent = new ManualResetEvent(false);

            _desktopTaskQueue.Add(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    caughtEx = ex;
                }
                finally
                {
                    doneEvent.Set();
                }
            });

            if (!doneEvent.WaitOne(8000))
            {
                throw new TimeoutException("RunOnDesktopThread 背景桌面執行緒操作逾時 (8 秒)");
            }
            if (caughtEx != null) throw caughtEx;
            return result;
        }

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
            try
            {
                DiagnosticLog?.Invoke("GetDesktopFolderView: 嘗試取得 Type CLSID");
                Type? shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
                if (shellWindowsType == null)
                {
                    DiagnosticLog?.Invoke("GetDesktopFolderView: CLSID Type 為 null");
                    return null;
                }

                DiagnosticLog?.Invoke("GetDesktopFolderView: 嘗試 Activator.CreateInstance");
                object? swObj = Activator.CreateInstance(shellWindowsType);
                if (swObj == null)
                {
                    DiagnosticLog?.Invoke("GetDesktopFolderView: swObj 為 null");
                    return null;
                }

                DiagnosticLog?.Invoke($"GetDesktopFolderView: 成功建立 COM 物件: {swObj.GetType().FullName}");
                if (swObj is not IShellWindows shellWindows)
                {
                    DiagnosticLog?.Invoke("GetDesktopFolderView: swObj 無法轉為 IShellWindows");
                    return null;
                }

                DiagnosticLog?.Invoke("GetDesktopFolderView: 呼叫 FindWindowSW...");
                object loc = 8; // SWC_DESKTOP
                object empty = Type.Missing;
                int pHwnd = 0;
                int SWFO_NEEDDISPATCH = 1;

                object spDisp = shellWindows.FindWindowSW(ref loc, ref empty, 8, out pHwnd, SWFO_NEEDDISPATCH);
                DiagnosticLog?.Invoke($"GetDesktopFolderView: FindWindowSW 回傳: {spDisp != null}, pHwnd: {pHwnd}");

                if (spDisp is IServiceProvider sp)
                {
                    DiagnosticLog?.Invoke("GetDesktopFolderView: spDisp is IServiceProvider");
                    Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                    Guid IID_IShellBrowser = typeof(IShellBrowser).GUID;

                    int hr = sp.QueryService(ref SID_STopLevelBrowser, ref IID_IShellBrowser, out object browserObj);
                    DiagnosticLog?.Invoke($"GetDesktopFolderView: QueryService hr={hr}, browserObj={browserObj != null}");

                    if (hr == 0 && browserObj is IShellBrowser browser)
                    {
                        int hrView = browser.QueryActiveShellView(out object viewObj);
                        DiagnosticLog?.Invoke($"GetDesktopFolderView: QueryActiveShellView hr={hrView}, viewObj={viewObj != null}");
                        if (hrView == 0 && viewObj != null)
                        {
                            Guid SID_SFolderView = new Guid("cde725b0-ccc9-451e-800e-8507c5e2e0e9");
                            Guid IID_IFolderView = typeof(IFolderView).GUID;

                            // 1. 嘗試由 viewObj 本身 QueryService
                            if (viewObj is IServiceProvider viewSp)
                            {
                                int hrFv = viewSp.QueryService(ref SID_SFolderView, ref IID_IFolderView, out object fvObj);
                                DiagnosticLog?.Invoke($"viewSp.QueryService hr={hrFv}, fvObj={fvObj != null}");
                                if (hrFv == 0 && fvObj is IFolderView fv)
                                {
                                    DiagnosticLog?.Invoke("成功透過 viewSp.QueryService 取得 IFolderView!");
                                    return fv;
                                }
                            }

                            // 2. 嘗試由 top-level sp QueryService
                            int hrTopFv = sp.QueryService(ref SID_SFolderView, ref IID_IFolderView, out object topFvObj);
                            DiagnosticLog?.Invoke($"topSp.QueryService hr={hrTopFv}, topFvObj={topFvObj != null}");
                            if (hrTopFv == 0 && topFvObj is IFolderView topFv)
                            {
                                DiagnosticLog?.Invoke("成功透過 topSp.QueryService 取得 IFolderView!");
                                return topFv;
                            }

                            // 3. 嘗試直接 cast
                            try
                            {
                                var folderView = (IFolderView)viewObj;
                                DiagnosticLog?.Invoke("成功直接強制轉為 IFolderView!");
                                return folderView;
                            }
                            catch (Exception exCast)
                            {
                                DiagnosticLog?.Invoke($"直接轉 IFolderView 失敗: {exCast.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke($"GetDesktopFolderView 例外: {ex.Message} -> {ex}");
            }

            return null;
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
                DiagnosticLog?.Invoke($"GetAllDesktopIcons: hwnd={hwnd}");
                if (hwnd == IntPtr.Zero) return result;

                int count = (int)SendMessage(hwnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                DiagnosticLog?.Invoke($"GetAllDesktopIcons: item count={count}");
                if (count <= 0) return result;

                GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero) return result;

                try
                {
                    uint memSize = 256;
                    IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, memSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
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

                                // 物理同源唯一標識：絕不胡亂硬配無關目錄項，以圖示在桌面上的物理位置與索引作為可信錨點
                                string itemName = $"桌面項目 #{i}";
                                string itemIdentifier = $"DesktopItem_{i} @ ({screenPt.X:F0},{screenPt.Y:F0})";

                                result.Add(new DesktopIconItem
                                {
                                    Index = i,
                                    Name = itemName,
                                    ResolvedPath = itemIdentifier,
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

                DiagnosticLog?.Invoke($"成功讀取 {result.Count} 個原生圖示物理座標與識別錨點。");
                return result;
            });
        }

        /// <summary>
        /// 精確解析桌面檔案完整路徑 (防同名、防 .lnk 副檔名省略)
        /// </summary>
        public static string ResolveDesktopPath(string itemName, List<string> desktopDirs)
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
        /// 設定原生桌面圖示位置，並進行嚴格的讀回驗證 (Read-Back Verification)
        /// 若設定後讀回座標誤差大於 10 像素 (如遇 Windows 自動排列圖示強行拉回)，判定移動失敗並回傳 false。
        /// </summary>
        public static bool SetIconPositionByScreenPoint(int index, Point screenPoint)
        {
            return RunOnDesktopThread(() =>
            {
                IntPtr hwnd = GetDesktopListViewHwndInternal();
                if (hwnd == IntPtr.Zero) return false;

                POINT targetClientPt = new POINT { X = (int)Math.Round(screenPoint.X), Y = (int)Math.Round(screenPoint.Y) };
                MapWindowPoints(IntPtr.Zero, hwnd, ref targetClientPt, 1);

                // 標準 Win32 LVM_SETITEMPOSITION
                IntPtr lParam = unchecked((IntPtr)((long)(ushort)targetClientPt.Y << 16 | (long)(ushort)targetClientPt.X));
                SendMessage(hwnd, LVM_SETITEMPOSITION, (IntPtr)index, lParam);

                // 立即進行讀回驗證
                GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
                if (hProcess == IntPtr.Zero) return false;

                try
                {
                    uint memSize = 256;
                    IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, memSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                    if (remoteMem == IntPtr.Zero) return false;

                    try
                    {
                        IntPtr localBuffer = Marshal.AllocHGlobal((int)memSize);
                        try
                        {
                            SendMessage(hwnd, LVM_GETITEMPOSITION, (IntPtr)index, remoteMem);
                            ReadProcessMemory(hProcess, remoteMem, localBuffer, (uint)Marshal.SizeOf<POINT>(), out _);
                            POINT verifiedPt = Marshal.PtrToStructure<POINT>(localBuffer);

                            int diffX = Math.Abs(verifiedPt.X - targetClientPt.X);
                            int diffY = Math.Abs(verifiedPt.Y - targetClientPt.Y);

                            bool isSuccess = diffX <= 10 && diffY <= 10;
                            if (!isSuccess)
                            {
                                DiagnosticLog?.Invoke($"[移動失敗校驗] 圖示 [{index}] 目標客戶區 ({targetClientPt.X},{targetClientPt.Y})，實際讀回 ({verifiedPt.X},{verifiedPt.Y})，誤差 ({diffX},{diffY}) 超標 (可能開啟自動排列圖示)！");
                            }
                            return isSuccess;
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
            });
        }
    }
}
