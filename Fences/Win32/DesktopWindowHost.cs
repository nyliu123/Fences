using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences.Win32
{
    internal static class DesktopWindowHost
    {
        private const int GWL_STYLE = -16;
        private const long WS_CHILD = 0x40000000;
        private const long WS_POPUP = 0x80000000;
        private static IntPtr desktopHost;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr window, IntPtr parent);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);

        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);

        private static IntPtr Host
        {
            get
            {
                if (IsWindow(desktopHost))
                    return desktopHost;
                desktopHost = IntPtr.Zero;
                EnumWindows((window, parameter) =>
                {
                    var className = new System.Text.StringBuilder(64);
                    GetClassName(window, className, className.Capacity);
                    if (className.ToString() != "Progman" && className.ToString() != "WorkerW")
                        return true;
                    var desktopView = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (desktopView == IntPtr.Zero)
                        return true;
                    // The icon ListView is a child of DefView. A sibling of
                    // DefView can be covered by the shell's desktop surface.
                    desktopHost = desktopView;
                    return false;
                }, IntPtr.Zero);
                if (desktopHost == IntPtr.Zero)
                    throw new InvalidOperationException("无法找到 Windows 桌面图标视图。");
                return desktopHost;
            }
        }

        internal static IntPtr Handle => Host;
        internal static void Reset() { desktopHost = IntPtr.Zero; }

        public static void Attach(Form window, IntPtr parent)
        {
            window.TopMost = false;
            // Create layered Forms as valid top-level windows before converting them;
            // creating a layered child without its native parent is rejected by Win32.
            var handle = window.Handle;
            window.TopLevel = false;
            var style = WindowUtil.GetWindowLong(window.Handle, GWL_STYLE).ToInt64();
            WindowUtil.SetWindowLong(window.Handle, GWL_STYLE, new IntPtr((style | WS_CHILD) & ~WS_POPUP));
            WindowUtil.SetLastError(0);
            if (SetParent(window.Handle, parent) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            WindowUtil.SetWindowPos(window.Handle, IntPtr.Zero, 0, 0, 0, 0,
                WindowUtil.SWP_NOMOVE | WindowUtil.SWP_NOSIZE | WindowUtil.SWP_NOACTIVATE);
        }

        public static void Attach(Form window)
        {
            Attach(window, Host);
        }

        public static Rectangle FromScreen(Rectangle bounds)
        {
            var origin = Point.Empty;
            ClientToScreen(Host, ref origin);
            bounds.Offset(-origin.X, -origin.Y);
            return bounds;
        }

        public static Point ToScreen(Point location)
        {
            ClientToScreen(Host, ref location);
            return location;
        }
    }
}
