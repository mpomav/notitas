using System;
using System.Runtime.InteropServices;

namespace Notitas
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public static RECT At(int x, int y, int width, int height) =>
            new RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };

        public bool Intersects(RECT other) =>
            Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT Monitor, Work;
        public int Flags;
    }

    public static class Native
    {
        public const int WM_MOVING = 0x0216;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int WM_HOTKEY = 0x0312;
        public const int WS_MAXIMIZEBOX = 0x00010000;
        public const int WS_MINIMIZEBOX = 0x00020000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int VK_MENU = 0x12;
        public const int MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        private const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTONEAREST = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void ChangeStyle(IntPtr hwnd, int index, int add, int remove)
        {
            var style = (long)GetWindowLongPtr(hwnd, index);
            SetWindowLongPtr(hwnd, index, (IntPtr)((style | (long)add) & ~(long)remove));
        }

        public static void Move(IntPtr hwnd, int x, int y) =>
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        public static void RoundCorners(IntPtr hwnd)
        {
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        public static RECT WorkAreaOf(RECT rect)
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfoW(MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST), ref info);
            return info.Work;
        }

        public static RECT WorkAreaAtCursor()
        {
            GetCursorPos(out var point);
            var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfoW(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref info);
            return info.Work;
        }

        public static bool IsOnAnyMonitor(RECT rect) => MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }
}
