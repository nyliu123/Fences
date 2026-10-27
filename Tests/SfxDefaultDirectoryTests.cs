using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

internal static class SfxDefaultDirectoryTests
{
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, StringBuilder text);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int action);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    private static int assertions;

    private static void Main()
    {
        var root = Path.GetFullPath(".");
        var version = FileVersionInfo.GetVersionInfo(Path.Combine(root, "Fences", "bin", "Release", "Fences.exe")).FileVersion;
        var release = Path.Combine(root, "artifacts", "Fences-" + new Version(version).ToString(3) + "-win-x64-sfx.exe");
        var testCopy = Path.Combine(root, "Fences", "obj", "SfxModule", "test-package.exe");
        Check(Level(release) == "requireAdministrator", "release SFX embeds the administrator manifest");
        Check(Level(Path.Combine(root, "Fences", "bin", "Release", "Fences.exe")) == "asInvoker",
            "Fences itself retains normal application permissions");
        Check(Level(testCopy) == "asInvoker", "non-elevating test copy avoids automating UAC approval");
        var temporary = Path.Combine(Path.GetTempPath(), "Fences-sfx-" + Guid.NewGuid());
        Directory.CreateDirectory(temporary);
        try
        {
            InspectDialog(testCopy, "", @"C:\Program Files\Fences", root, true);
            var destination = Path.Combine(temporary, "中文 path with spaces");
            InspectDialog(testCopy, "-o\"" + destination + "\"", destination, root, false);
            Directory.CreateDirectory(destination);
            var sentinel = Path.Combine(destination, "__fence_metadata.xml");
            File.WriteAllText(sentinel, "existing configuration stays untouched");
            using (var process = Process.Start(new ProcessStartInfo(testCopy, "-y -o\"" + destination + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
            {
                if (!process.WaitForExit(10000)) { process.Kill(); process.WaitForExit(); throw new Exception("SFX did not exit"); }
                Check(process.ExitCode == 0, "native extraction to an explicit Unicode/space path succeeds");
            }
            foreach (var name in new[] { "Fences.exe", "Fences.Recovery.exe", "Fences.Desktop.dll", "Fences.exe.config", "Fences.Recovery.exe.config",
                @"cs\Fences.resources.dll", @"zh-CN\Fences.resources.dll" })
                Check(File.ReadAllBytes(Path.Combine(destination, name)).SequenceEqual(
                    File.ReadAllBytes(Path.Combine(root, "Fences", "bin", "Release", name))), "extracted release matches: " + name);
            Check(File.ReadAllText(sentinel) == "existing configuration stays untouched", "personal configuration is not overwritten");
            Console.WriteLine("PASS: " + assertions + " SFX checks; UAC approval and writes to Program Files were not automated");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static void InspectDialog(string executable, string arguments, string expected, string root, bool capture)
    {
        using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        {
            try
            {
                IntPtr dialog = IntPtr.Zero, edit = IntPtr.Zero;
                var text = "";
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 10000)
                {
                    EnumWindows((window, parameter) =>
                    {
                        GetWindowThreadProcessId(window, out var owner);
                        if (owner == process.Id && ClassName(window) == "#32770") dialog = window;
                        return true;
                    }, IntPtr.Zero);
                    if (dialog != IntPtr.Zero)
                    {
                        EnumChildWindows(dialog, (window, parameter) =>
                        {
                            if (ClassName(window) == "Edit") edit = window;
                            return true;
                        }, IntPtr.Zero);
                        if (edit != IntPtr.Zero)
                        {
                            var buffer = new StringBuilder(4096);
                            SendMessage(edit, 0x000D, new IntPtr(buffer.Capacity), buffer);
                            text = buffer.ToString();
                            if (string.Equals(text.TrimEnd('\\'), expected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
                        }
                    }
                    Thread.Sleep(50);
                }
                Check(string.Equals(text.TrimEnd('\\'), expected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase),
                    "actual extraction dialog shows: " + text);
                Check(edit != IntPtr.Zero && (GetWindowLong(edit, -16) & 0x0800) == 0, "destination field remains editable");
                if (capture)
                {
                    var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                    try
                    {
                    ShowWindow(dialog, 1);
                    RedrawWindow(dialog, IntPtr.Zero, IntPtr.Zero, 0x0585);
                    Thread.Sleep(300);
                    GetWindowRect(dialog, out var bounds);
                    using (var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        var dc = graphics.GetHdc();
                        try { PrintWindow(dialog, dc, 0); }
                        finally { graphics.ReleaseHdc(dc); }
                        var output = Path.Combine(root, "Fences", "obj", "VisualChecks", "sfx-default-directory.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        bitmap.Save(output);
                    }
                    }
                    finally { SetThreadDpiAwarenessContext(previousDpi); }
                }
                if (dialog != IntPtr.Zero) PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
                if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(); throw new Exception("SFX preview did not exit"); }
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
        }
    }

    private static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }
    private static string Level(string executable)
    {
        var module = LoadLibraryEx(executable, IntPtr.Zero, 2);
        if (module == IntPtr.Zero) throw new Exception("Could not inspect executable resources");
        try
        {
            var resource = FindResource(module, new IntPtr(1), new IntPtr(24));
            var data = new byte[SizeofResource(module, resource)];
            Marshal.Copy(LockResource(LoadResource(module, resource)), data, 0, data.Length);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(data).Trim('\0', '\uFEFF'));
            return (string)xml.Descendants().Single(element => element.Name.LocalName == "requestedExecutionLevel").Attribute("level");
        }
        finally { FreeLibrary(module); }
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        assertions++;
        Console.WriteLine("PASS: " + name);
    }
}
