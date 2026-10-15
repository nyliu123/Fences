using System;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    public class DesktopUtil
    {
        private const Int32 GWL_STYLE = -16;
        private const Int32 GWL_HWNDPARENT = -8;
        private const Int32 WS_MAXIMIZEBOX = 0x00010000;
        private const Int32 WS_MINIMIZEBOX = 0x00020000;
        private const uint SHCNE_CREATE = 0x00000002;
        private const uint SHCNE_DELETE = 0x00000004;
        private const uint SHCNE_MKDIR = 0x00000008;
        private const uint SHCNE_RMDIR = 0x00000010;
        private const uint SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSH = 0x1000;
        private const uint SHCNF_FLUSHNOWAIT = 0x2000;
        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_UPDATENOW = 0x0100;

        [DllImport("User32.dll", EntryPoint = "GetWindowLong")]
        private extern static Int32 GetWindowLongPtr(IntPtr hWnd, Int32 nIndex);

        [DllImport("User32.dll", EntryPoint = "SetWindowLong")]
        private extern static Int32 SetWindowLongPtr(IntPtr hWnd, Int32 nIndex, Int32 dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(
            uint eventId,
            uint flags,
            [MarshalAs(UnmanagedType.LPWStr)] string item1,
            [MarshalAs(UnmanagedType.LPWStr)] string item2);

        public static void PreventMinimize(IntPtr handle)
        {
            Int32 windowStyle = GetWindowLongPtr(handle, GWL_STYLE);
            SetWindowLongPtr(handle, GWL_STYLE, windowStyle & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
        }

        public static void GlueToDesktop(IntPtr handle)
        {
            IntPtr nWinHandle = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            SetWindowLongPtr(handle, GWL_HWNDPARENT, nWinHandle.ToInt32());
        }

        public static void NotifyItemMoved(string sourcePath, string destinationPath, bool isDirectory)
        {
            // Finish Explorer's source removal before the Fence can paint the import.
            var flags = SHCNF_PATHW | SHCNF_FLUSH;
            SHChangeNotify(isDirectory ? SHCNE_RMDIR : SHCNE_DELETE, flags, sourcePath, null);
            SHChangeNotify(isDirectory ? SHCNE_MKDIR : SHCNE_CREATE, flags, destinationPath, null);
        }

        public static void FlushDesktopChanges(string desktopPath)
        {
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, desktopPath, null);
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), null);

            // Explorer hosts the desktop view under Progman or a WorkerW window.
            EnumWindows((window, parameter) =>
            {
                var desktopView = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (desktopView == IntPtr.Zero)
                    return true;
                RedrawWindow(desktopView, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
                return false;
            }, IntPtr.Zero);
        }
    }
}
