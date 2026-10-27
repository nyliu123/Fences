using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using NoFences;

// Uses the current wallpaper read-only, in an isolated host. Never takes over Explorer.
internal static class DesktopWallpaperTests
{
    private static object captions;
    private static IntPtr activeContext;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Preferences { public uint Size, Flags; public int IconSize; public Guid SortFormat; public uint SortProperty; public int SortDirection; }
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int FencesCreateView(IntPtr parent, string folder, bool wallpaper, IntPtr notify, uint message, out IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void FencesDestroyView(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FencesViewWindow(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesViewStatus(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesVisibleCount(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesRefreshView(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesCaptionBackground(IntPtr context, IntPtr dc);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesResizeView(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int FencesExcludePaths(IntPtr context, string paths, uint characters);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesGetPreferences(IntPtr context, ref Preferences preferences);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesSetPreferences(IntPtr context, ref Preferences preferences);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Visitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool PaintDesktop(IntPtr dc);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, ref Rect rect);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, ref Rect rect, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Colors, Important; public uint Color; }
    private static int assertions;
    [STAThread] private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "Fences-wallpaper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        IntPtr context = IntPtr.Zero;
        try
        {
            Application.EnableVisualStyles();
            for (int index = 0; index < 30; ++index) File.WriteAllText(Path.Combine(root, "图标 " + index.ToString("D2") + ".txt"), "wallpaper regression", Encoding.UTF8);
            using (var host = new Form { Text = "Fences wallpaper regression", TopMost = true, StartPosition = FormStartPosition.Manual, Location = new Point(73, 91), ClientSize = new Size(880, 650) })
            {
                host.Show();
                Check(FencesCreateView(host.Handle, root, true, IntPtr.Zero, 0, out context) >= 0, "create wallpaper-backed native view");
                var watch = Stopwatch.StartNew();
                while ((FencesViewStatus(context) != 0 || FencesVisibleCount(context) != 30) && watch.ElapsedMilliseconds < 10000) Pump(20);
                Check(FencesViewStatus(context) == 0 && FencesVisibleCount(context) == 30, "wallpaper view contains all fixture icons");
                var frame = FencesViewWindow(context); ShowWindow(frame, 4); Pump(3500);
                activeContext = context;
                var captionType = typeof(FenceWindow).Assembly.GetType("NoFences.DesktopCaptionWindow");
                captions = Activator.CreateInstance(captionType, Instance, null,
                    new object[] { context, new Action<Exception>(error => { throw error; }) }, null);
                captionType.GetMethod("AttachToView", Instance).Invoke(captions, null);
                IntPtr list = IntPtr.Zero;
                EnumChildWindows(frame, (window, data) => { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); if (name.ToString() == "SysListView32") list = window; return true; }, IntPtr.Zero);
                Check(list != IntPtr.Zero, "desktop uses native list view");
                Verify(list, "wallpaper-initial.png", 30);
                foreach (var index in new[] { 0, 8, 17, 29 })
                {
                    var rect = new Rect(); SendMessage(list, 0x100E, new IntPtr(index), ref rect);
                    InvalidateRect(list, ref rect, true); Pump(80);
                }
                Verify(list, "wallpaper-partial.png", 30);
                RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x185); Pump(300);
                Verify(list, "wallpaper-redraw.png", 30);
                host.Location = new Point(191, 137); host.ClientSize = new Size(1030, 720);
                Check(FencesResizeView(context) >= 0 && FencesRefreshView(context) >= 0, "resize and refresh wallpaper view"); Pump(900);
                Verify(list, "wallpaper-resized.png", 30);
                var paths = Path.Combine(root, "图标 00.txt") + "\0\0";
                Check(FencesExcludePaths(context, paths, (uint)paths.Length) >= 0, "filter membership with wallpaper enabled"); Pump(900);
                Check(FencesVisibleCount(context) == 29, "filtered icon disappears and remaining icons compact");
                var prefs = new Preferences { Size = (uint)Marshal.SizeOf(typeof(Preferences)) };
                Check(FencesGetPreferences(context, ref prefs) >= 0, "read native icon preferences");
                prefs.IconSize = 32;
                Check(FencesSetPreferences(context, ref prefs) >= 0, "change icon size with wallpaper enabled"); Pump(700);
                Verify(list, "wallpaper-filtered-small.png", 29);
                // Never send WM_PRINTCLIENT before any on-screen acceptance image:
                // it can repair the very compositor fault under investigation.
                VerifyBackgroundAlpha(list);
                Check((SendMessage(list, 0x1037, IntPtr.Zero, IntPtr.Zero).ToInt64() & 0x800000) == 0,
                    "normal label painting does not use the alpha-shadow text path");
                ((IDisposable)captions).Dispose(); captions = null;
                FencesDestroyView(context); context = IntPtr.Zero;
            }
            Console.WriteLine("PASS: " + assertions + " wallpaper assertions"); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
        finally
        {
            (captions as IDisposable)?.Dispose(); captions = null; activeContext = IntPtr.Zero;
            if (context != IntPtr.Zero) FencesDestroyView(context);
            SetThreadDpiAwarenessContext(previousDpi);
            Directory.Delete(root, true);
        }
    }
    private static void VerifyBackgroundAlpha(IntPtr list)
    {
        GetWindowRect(list, out var bounds);
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var bitmap = CreateDIBSection(dc, ref info, 0, out var pixels, IntPtr.Zero, 0);
        var previous = SelectObject(dc, bitmap);
        try
        {
            Check(bitmap != IntPtr.Zero, "create alpha-aware desktop paint target");
            SendMessage(GetParent(list), 0x318, dc, new IntPtr(8)); GdiFlush();
            Check((uint)Marshal.ReadInt32(pixels, ((height / 2) * width + width / 2) * 4) >> 24 == 255,
                "wallpaper background stays opaque under transparent-label composition");
        }
        finally { SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); }
    }
    private static void Verify(IntPtr list, string name, int count)
    {
        var previewDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisualChecks"); Directory.CreateDirectory(previewDirectory);
        var frameProperty = captions.GetType().GetProperty("FrameRendered", Instance);
        frameProperty.SetValue(captions, new Action<Bitmap>(bitmap => bitmap.Save(Path.Combine(previewDirectory, "captions-" + name))));
        captions.GetType().GetMethod("InvalidateBackground", Instance).Invoke(captions, null);
        captions.GetType().GetMethod("RefreshLabels", Instance).Invoke(captions, null);
        frameProperty.SetValue(captions, null);
        SetWindowPos(GetAncestor(list, 2), new IntPtr(-1), 0, 0, 0, 0, 0x13);
        var host = Control.FromHandle(GetAncestor(list, 2)) as Form;
        host?.Activate(); host?.BringToFront();
        Pump(200);
        GetWindowRect(list, out var bounds);
        Console.WriteLine("VISIBLE OWNERSHIP " + name + ": root=" + GetAncestor(list, 2) + ", actual=" + GetAncestor(WindowFromPoint(new Point(bounds.Left + 2, bounds.Top + 2)), 2));
        Check(GetAncestor(list, 2) == GetAncestor(WindowFromPoint(new Point(bounds.Left + 2, bounds.Top + 2)), 2),
            "screen capture is not covered by another application's window");
        GetWindowRect(FencesViewWindow(activeContext), out var frame);
        var screen = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        using (var expected = new Bitmap(screen.Width, screen.Height))
        using (var actual = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
        {
            using (var graphics = Graphics.FromImage(expected))
            {
                var dc = graphics.GetHdc();
                try { Check(FencesCaptionBackground(activeContext, dc) >= 0, "render full-frame wallpaper reference without item repaint offsets"); }
                finally { graphics.ReleaseHdc(dc); }
            }
            using (var graphics = Graphics.FromImage(actual)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, actual.Size);
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisualChecks"); Directory.CreateDirectory(directory);
            actual.Save(Path.Combine(directory, name)); expected.Save(Path.Combine(directory, "wallpaper-reference.png"));
            int wrong = 0, samples = 0;
            // Cell corners lie outside icon/label artwork but inside each item's background repaint.
            for (int index = 0; index < count; ++index)
            {
                var rect = new Rect { Left = 0 }; // LVIR_BOUNDS
                SendMessage(list, 0x100E, new IntPtr(index), ref rect);
                foreach (var point in new[] { new Point(rect.Left + 2, rect.Top + 2), new Point(rect.Right - 3, rect.Top + 2) })
                {
                    int x = bounds.Left - screen.Left + point.X, y = bounds.Top - screen.Top + point.Y;
                    if (point.X < 0 || point.Y < 0 || point.X >= actual.Width || point.Y >= actual.Height || x < 0 || y < 0 || x >= expected.Width || y >= expected.Height) continue;
                    var a = actual.GetPixel(point.X, point.Y); var e = expected.GetPixel(x, y);
                    if (Math.Abs(a.R - e.R) + Math.Abs(a.G - e.G) + Math.Abs(a.B - e.B) > 12) ++wrong;
                    ++samples;
                }
            }
            Console.WriteLine(name + ": " + wrong + "/" + samples + " background samples differ");
            Check(samples == count * 2 && wrong == 0, "item background coordinates match full wallpaper: " + name);
            int readable = 0, icons = 0;
            for (int index = 0; index < count; ++index)
            {
                var icon = new Rect { Left = 1 }; SendMessage(list, 0x100E, new IntPtr(index), ref icon);
                int ink = 0;
                for (int y = Math.Max(0, icon.Top); y < Math.Min(actual.Height, icon.Bottom); ++y)
                    for (int x = Math.Max(0, icon.Left); x < Math.Min(actual.Width, icon.Right); ++x)
                    {
                        var a = actual.GetPixel(x, y); var e = expected.GetPixel(bounds.Left - screen.Left + x, bounds.Top - screen.Top + y);
                        if (Math.Abs(a.R - e.R) + Math.Abs(a.G - e.G) + Math.Abs(a.B - e.B) > 30) ++ink;
                    }
                if (ink >= 24) ++icons;
                var rect = new Rect { Left = 2 }; // LVIR_LABEL
                SendMessage(list, 0x100E, new IntPtr(index), ref rect);
                int white = 0;
                for (int y = Math.Max(0, rect.Top); y < Math.Min(actual.Height, rect.Bottom); ++y)
                    for (int x = Math.Max(0, rect.Left); x < Math.Min(actual.Width, rect.Right); ++x)
                    {
                        var color = actual.GetPixel(x, y);
                        var background = expected.GetPixel(bounds.Left - screen.Left + x, bounds.Top - screen.Top + y);
                        if (color.R >= 240 && color.G >= 240 && color.B >= 240 && background.R + background.G + background.B < 660) ++white;
                    }
                if (white >= 12) ++readable;
            }
            Check(icons == count, "native icon artwork remains visible for every fixture: " + name);
            Check(readable == count, "captured names contain white pixels (not live-opacity acceptance): " + name);
        }
    }
    private static void Pump(int milliseconds) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); ++assertions; Console.WriteLine("PASS: " + message); }
}
