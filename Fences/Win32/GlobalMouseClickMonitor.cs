using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    internal sealed class MouseClickedEventArgs : EventArgs
    {
        public Point ScreenPoint { get; }

        public MouseClickedEventArgs(Point screenPoint)
        {
            ScreenPoint = screenPoint;
        }
    }

    internal sealed class GlobalMouseClickMonitor : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;

        private readonly LowLevelMouseProc hookCallback;
        private IntPtr hookHandle;

        public event EventHandler<MouseClickedEventArgs> Clicked;
        public event EventHandler<MouseClickedEventArgs> Moved;

        public GlobalMouseClickMonitor()
        {
            hookCallback = HookCallback;
            hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookCallback, GetModuleHandle(null), 0);
            if (hookHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监听桌面点击事件。");
        }

        public void Dispose()
        {
            if (hookHandle == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            var message = wParam.ToInt32();
            if (code >= 0 && (IsButtonDown(message) || message == WM_MOUSEMOVE))
            {
                var data = Marshal.PtrToStructure<LowLevelMouseHookData>(lParam);
                try
                {
                    var args = new MouseClickedEventArgs(new Point(data.Point.X, data.Point.Y));
                    if (message == WM_MOUSEMOVE)
                        Moved?.Invoke(this, args);
                    else
                        Clicked?.Invoke(this, args);
                }
                catch
                {
                }
            }

            return CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        private bool IsButtonDown(int message)
        {
            return message == WM_LBUTTONDOWN ||
                   message == WM_RBUTTONDOWN ||
                   message == WM_MBUTTONDOWN ||
                   message == WM_XBUTTONDOWN;
        }

        private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LowLevelMouseHookData
        {
            public NativePoint Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookId,
            LowLevelMouseProc callback,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int code,
            IntPtr wParam,
            IntPtr lParam);
    }
}
