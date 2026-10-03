using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Desktop_Frames.FarmFences
{
    // 只觀察輸入；不攔截、改寫或重送 Explorer 的滑鼠／鍵盤事件。
    public sealed class DesktopDragMonitor : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData { public DesktopInterop.POINT Point; public uint Data, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(DesktopInterop.POINT point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        private readonly HookProc _mouseCallback, _keyCallback;
        private IntPtr _mouse, _keyboard;
        private bool _active, _dragged, _cancelled;
        private Point _start;
        public IntPtr DesktopWindow { get; set; }
        public event Action<Point>? Pressed;
        public event Action<Point, bool>? Released;
        public event Action<string>? Diagnostic;

        public DesktopDragMonitor()
        {
            _mouseCallback = OnMouse;
            _keyCallback = OnKey;
            _mouse = SetWindowsHookEx(14, _mouseCallback, GetModuleHandle(null), 0);
            _keyboard = SetWindowsHookEx(13, _keyCallback, GetModuleHandle(null), 0);
            if (_mouse == IntPtr.Zero || _keyboard == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Dispose();
                throw new System.ComponentModel.Win32Exception(error);
            }
        }

        private bool IsDesktop(DesktopInterop.POINT point)
        {
            var window = WindowFromPoint(point);
            return DesktopWindow != IntPtr.Zero && (window == DesktopWindow || IsChild(DesktopWindow, window));
        }

        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                var input = Marshal.PtrToStructure<MouseData>(data);
                var point = new Point(input.Point.X, input.Point.Y);
                if (message == (IntPtr)0x201)
                {
                    _active = IsDesktop(input.Point);
                    Diagnostic?.Invoke($"按下：{point}，命中 HWND={WindowFromPoint(input.Point)}，桌面 HWND={DesktopWindow}，desktop={_active}");
                    _dragged = _cancelled = false;
                    _start = point;
                    if (_active) Pressed?.Invoke(point);
                }
                else if (_active && message == (IntPtr)0x200)
                {
                    _dragged |= Math.Abs(point.X - _start.X) >= GetSystemMetrics(68) || Math.Abs(point.Y - _start.Y) >= GetSystemMetrics(69);
                }
                else if (_active && message == (IntPtr)0x202)
                {
                    _active = false;
                    Diagnostic?.Invoke($"放開：{point}，dragged={_dragged}，cancelled={_cancelled}，desktop={IsDesktop(input.Point)}");
                    Released?.Invoke(point, _dragged && !_cancelled && IsDesktop(input.Point));
                }
            }
            return CallNextHookEx(_mouse, code, message, data);
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && _active && (message == (IntPtr)0x100 || message == (IntPtr)0x104))
            {
                int key = Marshal.ReadInt32(data);
                // Esc 或排序／重新整理快捷鍵出現時，放棄本次歸屬變更。
                if (key == 0x1B || key == 0x74) _cancelled = true;
            }
            return CallNextHookEx(_keyboard, code, message, data);
        }

        public void Dispose()
        {
            if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
            if (_keyboard != IntPtr.Zero) UnhookWindowsHookEx(_keyboard);
            _mouse = _keyboard = IntPtr.Zero;
            _active = false;
        }
    }
}
