using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FarmFenceSandbox
{
    public class DesktopIconInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string ItemType { get; set; } = "Unknown"; // File, Folder, Shortcut, Url
        public int X { get; set; }
        public int Y { get; set; }

        public override string ToString() => $"[{Index}] {Name} ({ItemType}) at ({X}, {Y}) -> {FullPath}";
    }

    public static class DesktopShellService
    {
        #region Win32 P/Invoke
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int LVS_AUTOARRANGE = 0x0100;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint LVM_GETITEMCOUNT = 0x1004;
        private const uint LVM_GETITEMPOSITION = 0x1010;
        private const uint LVM_SETITEMPOSITION32 = 0x1031;
        private const uint LVM_GETITEMTEXTW = 0x1073;
        #endregion

        private static readonly string UserDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        private static readonly string CommonDesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        public static IntPtr GetDesktopListViewHandle()
        {
            IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
            if (hDesk == IntPtr.Zero) return IntPtr.Zero;

            IntPtr listHwnd = IntPtr.Zero;
            try
            {
                EnumDesktopWindows(hDesk, (hWnd, lParam) =>
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetClassName(hWnd, sb, 256);
                    string cls = sb.ToString();
                    if (cls == "Progman" || cls == "WorkerW")
                    {
                        IntPtr defView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (defView != IntPtr.Zero)
                        {
                            IntPtr list = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                            if (list != IntPtr.Zero)
                            {
                                listHwnd = list;
                                return false; // Stop enumeration
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            finally
            {
                CloseDesktop(hDesk);
            }

            return listHwnd;
        }

        public static bool IsAutoArrangeEnabled(IntPtr listHwnd)
        {
            if (listHwnd == IntPtr.Zero) return false;
            IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
            if (hDesk != IntPtr.Zero)
            {
                try
                {
                    SetThreadDesktop(hDesk);
                    int style = GetWindowLong(listHwnd, GWL_STYLE);
                    return (style & LVS_AUTOARRANGE) != 0;
                }
                finally
                {
                    CloseDesktop(hDesk);
                }
            }
            return false;
        }

        public static List<DesktopIconInfo> GetAllIcons(IntPtr listHwnd)
        {
            var results = new List<DesktopIconInfo>();
            if (listHwnd == IntPtr.Zero) return results;

            IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
            if (hDesk == IntPtr.Zero) return results;

            try
            {
                SetThreadDesktop(hDesk);
                uint pid;
                GetWindowThreadProcessId(listHwnd, out pid);
                if (pid == 0) return results;

                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
                if (hProcess == IntPtr.Zero) return results;

                try
                {
                    int count = SendMessage(listHwnd, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                    if (count <= 0) return results;

                    IntPtr pRemotePoint = VirtualAllocEx(hProcess, IntPtr.Zero, 8, MEM_COMMIT, PAGE_READWRITE);
                    int lvItemSize = 80;
                    int textBufferSize = 512;
                    IntPtr pRemoteItem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)(lvItemSize + textBufferSize), MEM_COMMIT, PAGE_READWRITE);
                    IntPtr pRemoteText = new IntPtr(pRemoteItem.ToInt64() + lvItemSize);

                    for (int i = 0; i < count; i++)
                    {
                        // 1. Get Physical Position
                        SendMessage(listHwnd, LVM_GETITEMPOSITION, new IntPtr(i), pRemotePoint);
                        byte[] ptBytes = new byte[8];
                        ReadProcessMemory(hProcess, pRemotePoint, ptBytes, 8, out _);
                        int posX = BitConverter.ToInt32(ptBytes, 0);
                        int posY = BitConverter.ToInt32(ptBytes, 4);

                        // 2. Get Icon Text
                        byte[] lvItemBytes = new byte[lvItemSize];
                        Array.Copy(BitConverter.GetBytes(pRemoteText.ToInt64()), 0, lvItemBytes, 24, 8);
                        Array.Copy(BitConverter.GetBytes(256), 0, lvItemBytes, 32, 4);
                        WriteProcessMemory(hProcess, pRemoteItem, lvItemBytes, (uint)lvItemSize, out _);

                        SendMessage(listHwnd, LVM_GETITEMTEXTW, new IntPtr(i), pRemoteItem);

                        byte[] textBytes = new byte[512];
                        ReadProcessMemory(hProcess, pRemoteText, textBytes, 512, out _);
                        string text = Encoding.Unicode.GetString(textBytes).Split('\0')[0].Trim();

                        var info = new DesktopIconInfo
                        {
                            Index = i,
                            Name = text,
                            X = posX,
                            Y = posY
                        };

                        ResolveItemPath(info);
                        results.Add(info);
                    }

                    VirtualFreeEx(hProcess, pRemotePoint, 0, MEM_RELEASE);
                    VirtualFreeEx(hProcess, pRemoteItem, 0, MEM_RELEASE);
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            finally
            {
                CloseDesktop(hDesk);
            }

            return results;
        }

        public static bool SetIconPosition(IntPtr listHwnd, int index, int x, int y)
        {
            if (listHwnd == IntPtr.Zero) return false;

            IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
            if (hDesk == IntPtr.Zero) return false;

            try
            {
                SetThreadDesktop(hDesk);
                uint pid;
                GetWindowThreadProcessId(listHwnd, out pid);
                if (pid == 0) return false;

                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE, false, pid);
                if (hProcess == IntPtr.Zero) return false;

                try
                {
                    IntPtr pRemotePoint = VirtualAllocEx(hProcess, IntPtr.Zero, 8, MEM_COMMIT, PAGE_READWRITE);
                    byte[] ptBytes = new byte[8];
                    Array.Copy(BitConverter.GetBytes(x), 0, ptBytes, 0, 4);
                    Array.Copy(BitConverter.GetBytes(y), 0, ptBytes, 4, 4);

                    WriteProcessMemory(hProcess, pRemotePoint, ptBytes, 8, out _);
                    int res = SendMessage(listHwnd, LVM_SETITEMPOSITION32, new IntPtr(index), pRemotePoint);

                    VirtualFreeEx(hProcess, pRemotePoint, 0, MEM_RELEASE);
                    return res != 0;
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            finally
            {
                CloseDesktop(hDesk);
            }
        }

        private static void ResolveItemPath(DesktopIconInfo info)
        {
            if (string.IsNullOrEmpty(info.Name)) return;

            // 1. Check exact name match in User or Common Desktop
            string userExact = Path.Combine(UserDesktopDir, info.Name);
            string commonExact = Path.Combine(CommonDesktopDir, info.Name);

            if (File.Exists(userExact)) { AssignPath(info, userExact); return; }
            if (Directory.Exists(userExact)) { AssignPath(info, userExact, isFolder: true); return; }
            if (File.Exists(commonExact)) { AssignPath(info, commonExact); return; }
            if (Directory.Exists(commonExact)) { AssignPath(info, commonExact, isFolder: true); return; }

            // 2. Search files with extensions (.lnk, .url, .exe, etc.)
            string[] searchDirs = { UserDesktopDir, CommonDesktopDir };
            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                // Check .lnk
                string lnk = Path.Combine(dir, info.Name + ".lnk");
                if (File.Exists(lnk)) { AssignPath(info, lnk, isShortcut: true); return; }

                // Check .url
                string url = Path.Combine(dir, info.Name + ".url");
                if (File.Exists(url)) { AssignPath(info, url, isUrl: true); return; }

                // Check all files starting with name
                try
                {
                    var matches = Directory.GetFiles(dir, info.Name + ".*");
                    if (matches.Length > 0)
                    {
                        AssignPath(info, matches[0]);
                        return;
                    }
                }
                catch { }
            }

            // 3. Virtual system icons (e.g. This PC, Recycle Bin, Control Panel)
            info.FullPath = "::{Virtual Shell Namespace}";
            info.ItemType = "SpecialFolder";
        }

        private static void AssignPath(DesktopIconInfo info, string fullPath, bool isFolder = false, bool isShortcut = false, bool isUrl = false)
        {
            info.FullPath = fullPath;
            if (isFolder || Directory.Exists(fullPath)) info.ItemType = "Folder";
            else if (isShortcut || fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) info.ItemType = "Shortcut";
            else if (isUrl || fullPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) info.ItemType = "Url";
            else info.ItemType = "File";
        }
    }
}
