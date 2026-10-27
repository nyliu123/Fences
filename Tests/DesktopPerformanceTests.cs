using NoFences;
using NoFences.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Runs an owned 80-item desktop. No real desktop files/configuration are used.
internal static class DesktopPerformanceTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Visitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, ref Rect rect);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr module, IntPtr data);
    [DllImport("comctl32.dll")] private static extern void InitCommonControls();
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesVisibleCount(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int FencesItemPosition(IntPtr context, int index, StringBuilder name, uint capacity, out Point point);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int FencesCaptionAt(IntPtr context, int index, StringBuilder name, uint capacity, out Rect rect, out uint state);
    private static object Call(object target, string method) => target.GetType().GetMethod(method, Instance).Invoke(target, null);
    private static long Counter(IntPtr frame, int kind) => SendMessage(frame, 0x8000 + 178, new IntPtr(kind), IntPtr.Zero).ToInt64();
    private static void Pump(int milliseconds)
    {
        var time = Stopwatch.StartNew();
        while (time.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
    }
    [STAThread] private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "Fences-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        IDisposable integration = null;
        var assembly = typeof(FenceWindow).Assembly;
        var hostField = assembly.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static);
        var previousHost = hostField.GetValue(null);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var desktop = Path.Combine(root, "desktop"); Directory.CreateDirectory(desktop);
            for (int i = 0; i < 80; ++i) File.WriteAllText(Path.Combine(desktop, "性能项目 " + i.ToString("D2") + ".txt"), "fixture", Encoding.UTF8);
            var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                new object[] { Path.Combine(root, "storage"), desktop }, null);
            typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
            int expectedCount = 80;
            if (args.Contains("--members"))
            {
                var info = new FenceInfo(Guid.NewGuid()) { Name = "Startup fixture", PosX = 750, PosY = 350, Width = 240, Height = 300, KeepOriginalPaths = true };
                info.Files.AddRange(Directory.GetFiles(desktop).OrderBy(path => path).Take(40));
                manager.UpdateFence(info); expectedCount -= info.Files.Count;
            }
            var fixtureSize = args.Contains("--large") ? SystemInformation.VirtualScreen.Size : new Size(1000, 700);
            using (var host = new Form { Text = "Owned desktop performance fixture", ClientSize = fixtureSize })
            {
                InitCommonControls(); host.Show();
                var originalHandle = CreateWindowEx(0, "SysListView32", "Owned original icons", 0x50000000, 0, 0, fixtureSize.Width, fixtureSize.Height, host.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (originalHandle == IntPtr.Zero) throw new Exception("Cannot create the owned original list.");
                hostField.SetValue(null, host.Handle);
                integration = (IDisposable)Activator.CreateInstance(assembly.GetType("NoFences.Util.DesktopIntegration"), Instance, null,
                    new object[] { manager, new Func<List<FenceWindow>>(() => new List<FenceWindow>()),
                        new Action<string>(message => { throw new Exception(message); }), Path.Combine(root, "state"), host.Handle, desktop }, null);
                var startup = Stopwatch.StartNew();
                Call(integration, "Start");
                var createdAt = startup.Elapsed.TotalMilliseconds;
                manager.LoadFences();
                var activatedAt = startup.Elapsed.TotalMilliseconds;
                Pump(1);
                Console.WriteLine("startup_create_ms=" + createdAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("startup_activate_ms=" + activatedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                var type = integration.GetType();
                var context = (IntPtr)type.GetField("context", Instance).GetValue(integration);
                var frame = (IntPtr)type.GetField("viewWindow", Instance).GetValue(integration);
                Console.WriteLine("startup_navigations=" + Counter(frame, 5));
                Console.WriteLine("startup_view_refreshes=" + Counter(frame, 6));
                if (args.Contains("--verify") && (Counter(frame, 5) != 1 || Counter(frame, 6) != 0))
                    throw new Exception("Startup repeated its initial navigation/refresh.");
                var captions = type.GetField("captions", Instance).GetValue(integration);
                IntPtr list = IntPtr.Zero;
                EnumChildWindows(frame, (window, data) => { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); if (name.ToString() == "SysListView32") list = window; return true; }, IntPtr.Zero);
                while (FencesVisibleCount(context) != expectedCount && startup.ElapsedMilliseconds < 8000) Pump(5);
                if (FencesVisibleCount(context) != expectedCount) throw new Exception("Startup enumeration incomplete.");
                RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); Call(captions, "RefreshLabels");
                // Enumeration/presentation timing only. MixedIconRenderingTests
                // separately verifies actual icon and thumbnail contents.
                Console.WriteLine("startup_first_enumerated_frame_ms=" + startup.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                Pump(800); RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); Pump(100); Call(captions, "RefreshLabels");
                if (FencesVisibleCount(context) != expectedCount) throw new Exception("Fixture enumeration incomplete.");
                Console.WriteLine("fixture_client=" + host.ClientSize.Width + "x" + host.ClientSize.Height);
                var snapshots = 0;
                captions.GetType().GetProperty("FrameRendered", Instance).SetValue(captions, new Action<Bitmap>(image => ++snapshots));
                var queries = Counter(frame, 1);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                for (int i = 0; i < 100; ++i) Call(captions, "RefreshLabels");
                timer.Stop();
                Console.WriteLine("unchanged_100_ms=" + timer.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("unchanged_100_allocated=" + (GC.GetAllocatedBytesForCurrentThread() - allocated));
                Console.WriteLine("unchanged_100_shell_queries=" + (Counter(frame, 1) - queries));
                Console.WriteLine("unchanged_100_presentations=" + snapshots);
                var unchangedQueries = Counter(frame, 1) - queries;
                queries = Counter(frame, 1); snapshots = 0;
                var imagePasses = Counter(frame, 7);
                allocated = GC.GetAllocatedBytesForCurrentThread();
                timer.Restart();
                long nativeTicks = 0, managedTicks = 0;
                for (int i = 0; i < 30; ++i)
                {
                    var startTick = Stopwatch.GetTimestamp();
                    var rect = new Rect { Left = 1 }; SendMessage(list, 0x100e, new IntPtr(i % 12), ref rect);
                    int x = (rect.Left + rect.Right) / 2, y = (rect.Top + rect.Bottom) / 2;
                    SendMessage(list, 0x200, IntPtr.Zero, new IntPtr((y << 16) | (x & 0xffff)));
                    RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x100);
                    var nativeEnd = Stopwatch.GetTimestamp(); nativeTicks += nativeEnd - startTick;
                    Call(captions, "RefreshLabels");
                    managedTicks += Stopwatch.GetTimestamp() - nativeEnd;
                }
                timer.Stop();
                Console.WriteLine("hover_30_ms=" + timer.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("hover_30_allocated=" + (GC.GetAllocatedBytesForCurrentThread() - allocated));
                Console.WriteLine("hover_30_shell_queries=" + (Counter(frame, 1) - queries));
                Console.WriteLine("hover_30_presentations=" + snapshots);
                Console.WriteLine("hover_30_image_preparations=" + (Counter(frame, 7) - imagePasses));
                if (args.Contains("--verify") && Counter(frame, 7) != imagePasses) throw new Exception("Hover unnecessarily repeated native image preparation.");
                Console.WriteLine("hover_30_native_ms=" + (nativeTicks * 1000.0 / Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("hover_30_managed_ms=" + (managedTicks * 1000.0 / Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                if (args.Contains("--verify") && unchangedQueries != 0) throw new Exception("Unchanged frames queried Shell names.");
                if (args.Contains("--verify"))
                {
                    var canvasField = captions.GetType().GetField("canvas", Instance);
                    for (int i = 0; i < 12; ++i)
                    {
                        var rect = new Rect { Left = 1 }; SendMessage(list, 0x100e, new IntPtr(i), ref rect);
                        int x = (rect.Left + rect.Right) / 2, y = (rect.Top + rect.Bottom) / 2;
                        SendMessage(list, 0x200, IntPtr.Zero, new IntPtr((y << 16) | (x & 0xffff)));
                        RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x100); Call(captions, "RefreshLabels");
                        var partial = Pixels((Bitmap)canvasField.GetValue(captions));
                        Call(captions, "InvalidateBackground"); Call(captions, "RefreshLabels");
                        var complete = Pixels((Bitmap)canvasField.GetValue(captions));
                        if (!partial.SequenceEqual(complete)) throw new Exception("Partial rendering differs from a full composition at hover " + i);
                    }
                    Console.WriteLine("PASS: 12 incremental hover frames are byte-identical to complete compositions");
                    var changed = Path.Combine(desktop, "外部改名检查.txt");
                    var visiblePath = new StringBuilder(32768);
                    Marshal.ThrowExceptionForHR(FencesItemPosition(context, 0, visiblePath, (uint)visiblePath.Capacity, out var oldPosition));
                    File.Move(visiblePath.ToString(), changed);
                    var timeout = Stopwatch.StartNew(); bool updated = false;
                    while (!updated && timeout.ElapsedMilliseconds < 5000)
                    {
                        Pump(25);
                        for (int i = 0; i < FencesVisibleCount(context); ++i)
                        {
                            var path = new StringBuilder(32768); FencesItemPosition(context, i, path, (uint)path.Capacity, out var position);
                            if (path.ToString() != changed) continue;
                            var caption = new StringBuilder(512);
                            updated = FencesCaptionAt(context, i, caption, (uint)caption.Capacity, out var rect, out var state) == 0 && caption.ToString() == Path.GetFileName(changed);
                        }
                    }
                    if (!updated) throw new Exception("External rename left a stale caption cache entry.");
                    Console.WriteLine("PASS: external rename invalidates cached names without manual refresh");
                }
                foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) fence.Close();
                integration.Dispose(); integration = null;
            }
            return 0;
        }
        catch (Exception error) { while (error.InnerException != null) error = error.InnerException; Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
        finally
        {
            foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) fence.Close();
            integration?.Dispose(); hostField.SetValue(null, previousHost); Directory.Delete(root, true);
        }
    }
    private static byte[] Pixels(Bitmap image)
    {
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); return bytes;
        }
        finally { image.UnlockBits(data); }
    }
}
