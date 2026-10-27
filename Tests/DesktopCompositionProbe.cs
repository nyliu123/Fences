using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

// Explicit live A/B probe. A second process restores the Explorer window even
// if the probing process exits unexpectedly. No capture or file movement.
internal static class DesktopCompositionProbe
{
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visitor visitor, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Visitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint color, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    private static string Class(IntPtr window) { var text = new StringBuilder(128); GetClassName(window, text, text.Capacity); return text.ToString(); }
    private static void Repaint(IntPtr window) { SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x37); RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x185); }
    private static void Restore(string path)
    {
        var data = XElement.Load(path); var window = new IntPtr((long)data.Attribute("window"));
        GetWindowThreadProcessId(window, out var id);
        if (id != (uint)data.Attribute("shell") || Class(window) != "SHELLDLL_DefView") return;
        using (var shell = Process.GetProcessById((int)id))
            if (shell.StartTime.ToUniversalTime().Ticks != (long)data.Attribute("start")) return;
        var style = GetWindowLongPtr(window, -20).ToInt64();
        SetWindowLongPtr(window, -20, new IntPtr((style & ~0x80000L) | ((long)data.Attribute("style") & 0x80000L)));
        if (((long)data.Attribute("style") & 0x80000) != 0)
            SetLayeredWindowAttributes(window, (uint)data.Attribute("color"), (byte)(int)data.Attribute("alpha"), (uint)data.Attribute("flags"));
        Repaint(window); Console.WriteLine("RESTORED ORIGINAL DESKTOP COMPOSITION");
    }
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--restore")
        {
            try
            {
                using (var process = Process.GetProcessById(int.Parse(args[1])))
                using (var ready = EventWaitHandle.OpenExisting(args[3]))
                { var handle = process.Handle; ready.Set(); process.WaitForExit(); }
                Restore(args[2]); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        if (args.Length != 1) return 2;
        var journal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "composition-before.xml");
        bool changed = false;
        try
        {
            int target = int.Parse(args[0]); IntPtr host = IntPtr.Zero;
            EnumWindows((root, data) =>
            {
                if (Class(root) != "Progman" && Class(root) != "WorkerW") return true;
                EnumChildWindows(root, (window, parameter) =>
                {
                    GetWindowThreadProcessId(window, out var process);
                    if (process == target && Class(window) == "FencesDesktopView17") host = GetParent(window);
                    return true;
                }, IntPtr.Zero); return true;
            }, IntPtr.Zero);
            if (host == IntPtr.Zero || Class(host) != "SHELLDLL_DefView") throw new Exception("No matching live desktop host");
            GetWindowThreadProcessId(host, out var shellId);
            var style = GetWindowLongPtr(host, -20).ToInt64();
            if ((style & 0x80000) == 0) throw new Exception("Host is already non-layered");
            if (!GetLayeredWindowAttributes(host, out var color, out var alpha, out var flags)) throw new Exception("Cannot record original layer attributes");
            using (var shell = Process.GetProcessById((int)shellId))
                new XElement("desktop", new XAttribute("window", host.ToInt64()), new XAttribute("shell", shellId),
                    new XAttribute("start", shell.StartTime.ToUniversalTime().Ticks), new XAttribute("style", style),
                    new XAttribute("color", color), new XAttribute("alpha", alpha), new XAttribute("flags", flags)).Save(journal);
            var eventName = "Fences_CompositionProbe_" + Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
            using (var recovery = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                "--restore " + Process.GetCurrentProcess().Id + " \"" + journal + "\" " + eventName)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                if (!ready.WaitOne(5000)) throw new Exception("Composition recovery watcher did not become ready");
            changed = true; SetWindowLongPtr(host, -20, new IntPtr(style & ~0x80000L)); Repaint(host);
            if ((GetWindowLongPtr(host, -20).ToInt64() & 0x80000) != 0) throw new Exception("Layered style was not disabled");
            Console.WriteLine("LIVE A/B: Explorer parent layering disabled for 120 seconds, no screenshots");
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 120000 && !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop-composition"))) Thread.Sleep(200);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (changed) Restore(journal); }
    }
}
