using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Explicit live-desktop diagnostic: launches the actual executable as its own process.
// Loads real AppData on Explorer. Never substitutes reflection-hosted Main or a fake host.
internal static class LiveDesktopTests
{
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visitor visitor, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Visitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint color, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    private static readonly string output = AppDomain.CurrentDomain.BaseDirectory;
    private static string Class(IntPtr window) { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); return name.ToString(); }
    private static void Describe(IntPtr window, string prefix)
    {
        GetWindowThreadProcessId(window, out var process);
        var layered = GetLayeredWindowAttributes(window, out var color, out var alpha, out var flags);
        Console.WriteLine(prefix + " " + Class(window) + " hwnd=" + window + " pid=" + process + " visible=" + IsWindowVisible(window) +
            " dpi=" + GetDpiForWindow(window) + " dpiContext=" + GetWindowDpiAwarenessContext(window) +
            " ex=0x" + GetWindowLongPtr(window, -20).ToInt64().ToString("x") + " layered=" + layered + " key=" + color.ToString("x6") + " alpha=" + alpha + " flags=" + flags);
        if (Class(window) == "SysListView32")
            Console.WriteLine("LIST text=" + SendMessage(window, 0x1023, IntPtr.Zero, IntPtr.Zero).ToInt64().ToString("x") +
                " styles=" + SendMessage(window, 0x1037, IntPtr.Zero, IntPtr.Zero).ToInt64().ToString("x"));
        if (Class(window) == "FencesDesktopView17")
            Console.WriteLine("RENDER COUNTS: frames=" + SendMessage(window, 0x8000 + 178, IntPtr.Zero, IntPtr.Zero) +
                " explicit captions=" + SendMessage(window, 0x8000 + 178, new IntPtr(1), IntPtr.Zero));
    }
    private static void Capture(string name)
    {
        var area = Screen.PrimaryScreen.Bounds;
        using (var bitmap = new Bitmap(Math.Min(650, area.Width), area.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        { graphics.CopyFromScreen(area.Left, area.Top, 0, 0, bitmap.Size); bitmap.Save(Path.Combine(output, name + ".png")); }
        Console.WriteLine("CAPTURE " + name);
    }
    private static void CancelRename(int id)
    {
        EnumWindows((root, data) =>
        {
            EnumChildWindows(root, (window, parameter) =>
            {
                GetWindowThreadProcessId(window, out var process);
                if (process == id && Class(window) == "SysListView32")
                    SendMessageTimeout(window, 0x1000 + 179, IntPtr.Zero, IntPtr.Zero, 2, 2000, out var result);
                return true;
            }, IntPtr.Zero); return true;
        }, IntPtr.Zero);
    }
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--inspect")
        {
            int id = int.Parse(args[1]);
            EnumWindows((root, data) =>
            {
                EnumChildWindows(root, (window, parameter) =>
                {
                    GetWindowThreadProcessId(window, out var pid);
                    if (pid == id && (Class(window) == "FencesDesktopView17" || Class(window) == "SysListView32")) Describe(window, "INSPECT");
                    return true;
                }, IntPtr.Zero); return true;
            }, IntPtr.Zero); return 0;
        }
        if (args.Length == 2 && args[0] == "--exit")
        {
            int id = int.Parse(args[1]); uint thread = 0;
            using (var process = Process.GetProcessById(id))
            {
                if (process.ProcessName != "Fences") throw new Exception("Exit target is not Fences");
                EnumWindows((root, data) =>
                {
                    EnumChildWindows(root, (window, parameter) =>
                    {
                        var owner = GetWindowThreadProcessId(window, out var pid);
                        if (pid == id && Class(window) == "FencesDesktopView17") thread = owner;
                        return true;
                    }, IntPtr.Zero); return true;
                }, IntPtr.Zero);
                if (thread == 0) throw new Exception("No owned Fences desktop GUI thread found");
                CancelRename(id); PostThreadMessage(thread, 0x12, IntPtr.Zero, IntPtr.Zero);
                if (!process.WaitForExit(10000)) throw new Exception("Fences did not finish normal exit");
                Console.WriteLine("Selected Fences process exited normally"); return 0;
            }
        }
        if (args.Length != 1) { Console.Error.WriteLine("Pass the actual Fences.exe path."); return 2; }
        return RunExecutable(Path.GetFullPath(args[0]));
    }
    private static int RunExecutable(string executable)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        Process application = null; uint mainThread = 0;
        bool minimize = Environment.GetEnvironmentVariable("FENCES_LIVE_NO_MINIMIZE") != "1";
        try
        {
            if (minimize) shell.MinimizeAll();
            Thread.Sleep(700);
            application = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            Console.WriteLine("ACTUAL EXECUTABLE: " + executable + " pid=" + application.Id);
            var watch = Stopwatch.StartNew(); bool described = false, captured = false;
            var duration = int.TryParse(Environment.GetEnvironmentVariable("FENCES_LIVE_TEST_SECONDS"), out var seconds) ? Math.Max(10, Math.Min(seconds, 300)) * 1000 : 90000;
            while (!application.HasExited && watch.ElapsedMilliseconds < duration && !File.Exists(Path.Combine(output, "stop-direct")))
            {
                if (!described && watch.ElapsedMilliseconds > 5000)
                {
                    described = true;
                    EnumWindows((root, data) =>
                    {
                        var rootThread = GetWindowThreadProcessId(root, out var rootProcess);
                        if (rootProcess == application.Id) mainThread = rootThread;
                        if (Class(root) != "Progman" && Class(root) != "WorkerW") return true;
                        EnumChildWindows(root, (window, parameter) =>
                        {
                            var thread = GetWindowThreadProcessId(window, out var process);
                            if (process == application.Id && (Class(window) == "FencesDesktopView17" || Class(window) == "SHELLDLL_DefView" || Class(window) == "SysListView32"))
                            { mainThread = thread; Describe(window, "DIRECT"); }
                            return true;
                        }, IntPtr.Zero); return true;
                    }, IntPtr.Zero);
                    Console.WriteLine("No screen capture has been made during this direct run.");
                }
                if (!captured && File.Exists(Path.Combine(output, "capture-direct"))) { Capture("direct-executable"); captured = true; }
                Thread.Sleep(200);
            }
            if (!application.HasExited && mainThread != 0)
            {
                Console.WriteLine("POST NORMAL WM_QUIT"); CancelRename(application.Id); PostThreadMessage(mainThread, 0x12, IntPtr.Zero, IntPtr.Zero);
                if (!application.WaitForExit(10000)) throw new Exception("Actual application did not finish normal exit");
            }
            Console.WriteLine("ACTUAL EXIT: " + application.HasExited);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (application != null && !application.HasExited && mainThread != 0)
            { CancelRename(application.Id); PostThreadMessage(mainThread, 0x12, IntPtr.Zero, IntPtr.Zero); application.WaitForExit(10000); }
            application?.Dispose();
            if (minimize) shell.UndoMinimizeALL();
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
