using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using NoFences;

internal static class FileDialogMonitorTests
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var type = typeof(FenceWindow).Assembly.GetType("NoFences.Win32.FileDialogMonitor");
        var count = 0;
        using (var monitor = (IDisposable)Activator.CreateInstance(type, true))
        using (var timeout = new Timer { Interval = 3000 })
        {
            Action<IntPtr, uint> handler = (window, process) =>
            {
                if (process != Process.GetCurrentProcess().Id)
                    return;
                count++;
                PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            };
            type.GetEvent("DialogShown", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetAddMethod(true).Invoke(monitor, new object[] { handler });
            timeout.Tick += (sender, e) => CloseOwnDialogs();
            timeout.Start();
            using (var dialog = new OpenFileDialog())
                dialog.ShowDialog();
            if (count == 0) throw new Exception("OpenFileDialog was not detected");
            Console.WriteLine("PASS: native OpenFileDialog detected and cancelled");
            count = 0;
            using (var dialog = new SaveFileDialog())
                dialog.ShowDialog();
            if (count == 0) throw new Exception("SaveFileDialog was not detected");
            Console.WriteLine("PASS: native SaveFileDialog detected and cancelled");
            count = 0;
            MessageBox.Show("Ordinary dialog should not trigger desktop restoration.", "NoFences test");
            if (count != 0) throw new Exception("Ordinary MessageBox misidentified as a file dialog");
            Console.WriteLine("PASS: ordinary MessageBox ignored");
            timeout.Stop();
        }
    }

    private static void CloseOwnDialogs()
    {
        EnumThreadWindows(GetCurrentThreadId(), (window, parameter) =>
        {
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "#32770")
                PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }
}
