using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NoFences.Win32
{
    // Observe Windows shell file dialogs without injecting into the owning application.
    internal sealed class FileDialogMonitor : IDisposable
    {
        private readonly Control dispatcher;
        private readonly WinEventCallback callback;
        private IntPtr showHook;
        private IntPtr foregroundHook;
        private IntPtr destroyHook;
        private readonly HashSet<IntPtr> observedDialogs = new HashSet<IntPtr>();
        private bool disposed;

        internal event Action<IntPtr, uint> DialogShown;

        internal FileDialogMonitor()
        {
            dispatcher = new Control();
            var handle = dispatcher.Handle;
            callback = OnWindowEvent;
            showHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, callback, 0, 0, 0);
            foregroundHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, callback, 0, 0, 0);
            destroyHook = SetWinEventHook(0x8001, 0x8001, IntPtr.Zero, callback, 0, 0, 0);
            if (showHook == IntPtr.Zero || foregroundHook == IntPtr.Zero || destroyHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                Dispose();
                throw new Win32Exception(error, "无法监听文件选择窗口。");
            }
        }

        private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr window,
            int objectId, int childId, uint threadId, uint timestamp)
        {
            if (disposed || window == IntPtr.Zero || objectId != 0 || childId != 0)
                return;
            if (eventType == 0x8001)
            {
                observedDialogs.Remove(window);
                return;
            }
            var root = GetAncestor(window, 2);
            if (root == IntPtr.Zero || ClassName(root) != "#32770")
                return;
            // Return from the accessibility callback before doing any file operations.
            dispatcher.BeginInvoke((Action)(() => Inspect(root)));
        }

        private void Inspect(IntPtr window)
        {
            if (disposed || !IsFileDialog(window))
                return;
            if (!observedDialogs.Add(window))
                return;
            GetWindowThreadProcessId(window, out var processId);
            if (processId != 0)
                DialogShown?.Invoke(window, processId);
        }

        internal void InspectExistingDialogs()
        {
            EnumWindows((window, parameter) =>
            {
                Inspect(window);
                return true;
            }, IntPtr.Zero);
        }

        internal static bool IsFileDialog(IntPtr window)
        {
            if (!IsWindowVisible(window) || ClassName(window) != "#32770")
                return false;
            var shellView = false;
            var directUi = false;
            var fileName = false;
            EnumChildWindows(window, (child, parameter) =>
            {
                var name = ClassName(child);
                shellView |= name == "SHELLDLL_DefView";
                directUi |= name == "DirectUIHWND";
                fileName |= GetDlgCtrlID(child) == 1148;
                return true;
            }, IntPtr.Zero);
            return shellView || (directUi && fileName);
        }

        private static string ClassName(IntPtr window)
        {
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        public void Dispose()
        {
            disposed = true;
            if (showHook != IntPtr.Zero)
                UnhookWinEvent(showHook);
            if (foregroundHook != IntPtr.Zero)
                UnhookWinEvent(foregroundHook);
            if (destroyHook != IntPtr.Zero)
                UnhookWinEvent(destroyHook);
            showHook = foregroundHook = destroyHook = IntPtr.Zero;
            dispatcher.Dispose();
        }

        private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window,
            int objectId, int childId, uint threadId, uint timestamp);
        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
            WinEventCallback callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr window, WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
