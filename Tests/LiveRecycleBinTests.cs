using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

// Explicit live check against an already-running Fences process. Uses only its
// own new fixture; restores it rather than emptying any user's Recycle Bin.
internal static class LiveRecycleBinTests
{
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visitor visitor, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Visitor visitor, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct BinInfo { public int Size; public long Bytes, Count; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string root, ref BinInfo info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Operation
    {
        public IntPtr Owner; public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr Mappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string Progress;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref Operation operation);
    private static int assertions;
    [STAThread] private static int Main(string[] args)
    {
        string root = null, fixture = null;
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass the actual running Fences PID.");
            var process = Process.GetProcessById(int.Parse(args[0]));
            Check(process.ProcessName == "Fences", "target is the actual Fences executable");
            IntPtr frame = IntPtr.Zero;
            Visitor find = (window, data) =>
            {
                GetWindowThreadProcessId(window, out var id);
                var name = new System.Text.StringBuilder(128); GetClassName(window, name, name.Capacity);
                if (id == process.Id && name.ToString() == "FencesDesktopView17") frame = window;
                return true;
            };
            EnumWindows((window, data) => { find(window, data); EnumChildWindows(window, find, data); return true; }, IntPtr.Zero);
            Check(frame != IntPtr.Zero, "find the production desktop view owned by that process");
            Wait(() => SendMessage(frame, 0x8000 + 179, new IntPtr(5), IntPtr.Zero).ToInt32() >= 0,
                "the actual desktop contains a Recycle Bin icon");
            Check(Count() == 0, "Recycle Bin is initially empty; no existing entries will be removed");
            Wait(() => Value(frame, 2) > Value(frame, 0) && Value(frame, 3) > Value(frame, 1),
                "the actual bin finishes native icon layout");
            var wallpaperBefore = SendMessage(frame, 0x8000 + 178, new IntPtr(3), IntPtr.Zero);
            var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisualChecks"); Directory.CreateDirectory(output);
            root = Path.Combine(Path.GetTempPath(), "Fences-live-bin-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            for (int cycle = 1; cycle <= 2; ++cycle)
            {
                var empty = Pixels(frame); empty.Save(Path.Combine(output, "recycle-empty-" + cycle + ".png"));
                fixture = Path.Combine(root, "owned-test-" + cycle + ".txt"); File.WriteAllText(fixture, "Fences live recycle icon regression");
                var operation = new Operation { Function = 3, From = fixture + "\0\0", Flags = 0x454 };
                Check(SHFileOperation(ref operation) == 0 && !operation.Aborted && !File.Exists(fixture), "recycle only the owned fixture");
                Wait(() => Count() == 1, "actual bin changes from empty to one item");
                Wait(() => SendMessage(frame, 0x8000 + 179, new IntPtr(4), IntPtr.Zero).ToInt32() == 1 && Different(empty, frame),
                    "actual displayed icon pixels change from empty to full without manual refresh");
                using (var full = Pixels(frame)) full.Save(Path.Combine(output, "recycle-full-" + cycle + ".png"));
                Restore(fixture);
                Wait(() => Count() == 0 && File.Exists(fixture), "restoring the fixture makes the actual bin empty again");
                Wait(() => SendMessage(frame, 0x8000 + 179, new IntPtr(4), IntPtr.Zero).ToInt32() == 0 && !Different(empty, frame),
                    "actual displayed icon pixels return to the original empty image without manual refresh");
                using (var restored = Pixels(frame)) restored.Save(Path.Combine(output, "recycle-restored-" + cycle + ".png"));
                empty.Dispose();
                File.Delete(fixture); fixture = null;
            }
            Check(SendMessage(frame, 0x8000 + 178, new IntPtr(3), IntPtr.Zero) == wallpaperBefore,
                "recycling and restoring files never reload the desktop wallpaper");
            Console.WriteLine("PASS: " + assertions + " live Recycle Bin assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
        finally
        {
            if (fixture != null && !File.Exists(fixture))
            {
                try { Restore(fixture); } catch (Exception error) { Console.Error.WriteLine("Fixture recovery: " + error.Message); }
            }
            if (fixture != null && File.Exists(fixture)) File.Delete(fixture);
            if (root != null && Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root);
        }
    }
    private static long Count()
    {
        var info = new BinInfo { Size = Marshal.SizeOf(typeof(BinInfo)) };
        Marshal.ThrowExceptionForHR(SHQueryRecycleBin(null, ref info)); return info.Count;
    }
    private static Bitmap Pixels(IntPtr frame)
    {
        int left = Value(frame, 0), top = Value(frame, 1), width = Value(frame, 2) - left, height = Value(frame, 3) - top;
        if (width < 1 || height < 1 || width > 512 || height > 512) throw new InvalidOperationException("Invalid Recycle Bin icon bounds: " + left + "," + top + " " + width + "x" + height);
        var image = new Bitmap(width, height);
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
                image.SetPixel(x, y, Color.FromArgb(unchecked((int)SendMessage(frame, 0x8000 + 180, new IntPtr((y << 16) | x), IntPtr.Zero).ToInt64())));
        return image;
    }
    private static int Value(IntPtr frame, int field) => SendMessage(frame, 0x8000 + 179, new IntPtr(field), IntPtr.Zero).ToInt32();
    private static bool Different(Bitmap baseline, IntPtr frame)
    {
        using (var next = Pixels(frame))
        {
            if (baseline.Size != next.Size) return true;
            int changed = 0;
            for (int y = 0; y < next.Height; ++y) for (int x = 0; x < next.Width; ++x)
                if (baseline.GetPixel(x, y).ToArgb() != next.GetPixel(x, y).ToArgb()) ++changed;
            return changed > 24;
        }
    }
    private static void Restore(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        dynamic bin = shell.NameSpace(10); dynamic items = bin.Items();
        try
        {
            for (int i = 0; i < items.Count; ++i)
            {
                dynamic item = items.Item(i);
                try
                {
                    if (!string.Equals((string)item.ExtendedProperty("System.Recycle.DeletedFrom"), Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals((string)item.ExtendedProperty("System.ItemNameDisplay"), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) continue;
                    item.InvokeVerb("undelete"); return;
                }
                finally { Marshal.FinalReleaseComObject(item); }
            }
            throw new InvalidOperationException("The owned recycle fixture was not found; no other entry was changed.");
        }
        finally { Marshal.FinalReleaseComObject(items); Marshal.FinalReleaseComObject(bin); Marshal.FinalReleaseComObject(shell); }
    }
    private static void Wait(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10000) { if (condition()) { Check(true, message); return; } Thread.Sleep(100); }
        Check(false, message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        ++assertions; Console.WriteLine("PASS: " + message);
    }
}
