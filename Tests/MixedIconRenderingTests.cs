using NoFences.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Real, valid mixed-format fixtures. No user Desktop files/configuration are changed.
internal static class MixedIconRenderingTests
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; public Rectangle Value => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Item
    {
        public uint Mask; public int Index, Subitem; public uint State, StateMask; public IntPtr Text;
        public int TextLength, Image; public IntPtr Param; public int Indent, Group; public uint Columns; public IntPtr ColumnIndexes, ColumnFormats; public int GroupIndex;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Preferences { public uint Size, Flags; public int IconSize; public Guid SortFormat; public uint SortProperty; public int SortDirection; }
    private delegate bool Visitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr module, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Visitor visit, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, ref Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, ref Item item);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesViewStatus(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesVisibleCount(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FencesViewWindow(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesCaptionSurface(IntPtr context, IntPtr dc);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesGetPreferences(IntPtr context, ref Preferences value);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesSetPreferences(IntPtr context, ref Preferences value);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int FencesItemPosition(IntPtr context, int index, StringBuilder path, uint size, out Point point);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr pidl, uint attributes, out uint result);
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ShellLink
    {
        void GetPath(IntPtr path, int length, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
    }
    private static readonly Color Background = Color.FromArgb(30, 40, 52);
    private static readonly List<string> failures = new List<string>();
    private static string output;
    private static object captions;
    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
    }
    private static Bitmap Surface(IntPtr context, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try { Marshal.ThrowExceptionForHR(FencesCaptionSurface(context, dc)); }
            finally { graphics.ReleaseHdc(dc); }
        }
        return bitmap;
    }
    private static Bitmap NativePrint(IntPtr list, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Background);
            var dc = graphics.GetHdc();
            try { SendMessage(list, 0x318, dc, new IntPtr(0x0c)); }
            finally { graphics.ReleaseHdc(dc); }
        }
        return bitmap;
    }
    [STAThread] private static int Main(string[] args)
    {
        output = args.Length == 0 ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MixedIcons") : Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), "Fences-mixed-icons-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "desktop"); Directory.CreateDirectory(folder);
        IntPtr context = IntPtr.Zero;
        IDisposable integration = null;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            CreateFixtures(root, folder);
            using (var host = new Form { Text = "Fences mixed icon fixtures", ClientSize = new Size(1050, 820) })
            {
                host.Show();
                CreateWindowEx(0, "SysListView32", "Owned original icons", 0x50000000, 0, 0, host.Width, host.Height, host.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), members, null, new object[] { Path.Combine(root, "storage"), folder }, null);
                typeof(FenceManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
                var type = typeof(FenceManager).Assembly.GetType("NoFences.Util.DesktopIntegration");
                integration = (IDisposable)Activator.CreateInstance(type, members, null,
                    new object[] { manager, new Func<List<NoFences.FenceWindow>>(() => new List<NoFences.FenceWindow>()),
                        new Action<string>(message => { throw new Exception(message); }), Path.Combine(root, "state"), host.Handle, folder }, null);
                type.GetMethod("Start", members).Invoke(integration, null);
                manager.LoadFences();
                context = (IntPtr)type.GetField("context", members).GetValue(integration);
                captions = type.GetField("captions", members).GetValue(integration);
                var timer = Stopwatch.StartNew();
                while ((FencesViewStatus(context) != 0 || FencesVisibleCount(context) != Directory.GetFileSystemEntries(folder).Length) && timer.ElapsedMilliseconds < 10000) Pump(20);
                if (FencesViewStatus(context) != 0) throw new Exception("Mixed native view did not initialize.");
                var frame = FencesViewWindow(context); IntPtr list = IntPtr.Zero;
                EnumChildWindows(frame, (window, data) => { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); if (name.ToString() == "SysListView32") list = window; return true; }, IntPtr.Zero);
                if (list == IntPtr.Zero) throw new Exception("Native icon list missing.");
                Pump(100);
                GetClientRect(frame, out var client);
                using (var first = Surface(context, client.Value.Size)) first.Save(Path.Combine(output, "desktop-first.png"));
                // Read the already painted frame before asking the default ListView
                // painter for a reference. This is NOT a live opacity acceptance.
                Pump(3000);
                using (var actual = Surface(context, client.Value.Size))
                using (var reference = NativePrint(list, client.Value.Size)) CompareDesktop(context, list, actual, reference, "settled");
                Pump(3000);
                using (var actual = Surface(context, client.Value.Size))
                using (var reference = NativePrint(list, client.Value.Size)) CompareDesktop(context, list, actual, reference, "after-native-reference");
                RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); Pump(150);
                using (var actual = Surface(context, client.Value.Size))
                using (var reference = NativePrint(list, client.Value.Size)) CompareDesktop(context, list, actual, reference, "after-redraw");
                foreach (var size in new[] { 32, 96 })
                {
                    var preferences = new Preferences { Size = (uint)Marshal.SizeOf<Preferences>() };
                    Marshal.ThrowExceptionForHR(FencesGetPreferences(context, ref preferences));
                    preferences.IconSize = size; Marshal.ThrowExceptionForHR(FencesSetPreferences(context, ref preferences));
                    Pump(1500);
                    using (var actual = Surface(context, client.Value.Size))
                    using (var reference = NativePrint(list, client.Value.Size)) CompareDesktop(context, list, actual, reference, "size-" + size);
                }
                var oldCount = FencesVisibleCount(context);
                File.Copy(Path.Combine(folder, "03 自定义图标程序.exe"), Path.Combine(folder, "24 新增程序.exe"));
                var arrival = Stopwatch.StartNew();
                while (FencesVisibleCount(context) != oldCount + 1 && arrival.ElapsedMilliseconds < 8000) Pump(25);
                if (FencesVisibleCount(context) != oldCount + 1) failures.Add("New executable did not arrive without manual refresh.");
                Pump(1800);
                using (var actual = Surface(context, client.Value.Size))
                using (var reference = NativePrint(list, client.Value.Size)) CompareDesktop(context, list, actual, reference, "new-file");
                var update = Path.Combine(folder, "23 内容更新.png");
                using (var image = new Bitmap(64, 64)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.Magenta); image.Save(update, ImageFormat.Png); }
                var change = Stopwatch.StartNew(); bool changed = false;
                while (!changed && change.ElapsedMilliseconds < 8000)
                {
                    Pump(50);
                    using (var actual = Surface(context, client.Value.Size))
                    {
                        for (int i = 0; i < FencesVisibleCount(context); ++i)
                        {
                            var path = new StringBuilder(32768); FencesItemPosition(context, i, path, (uint)path.Capacity, out var position);
                            if (path.ToString() != update) continue;
                            var rect = new Rect { Left = 1 }; SendMessage(list, 0x100e, new IntPtr(i), ref rect);
                            int pixels = 0;
                            for (int y = rect.Top; y < rect.Bottom; ++y) for (int x = rect.Left; x < rect.Right; ++x)
                            { var color = actual.GetPixel(x, y); if (color.R > 200 && color.B > 200 && color.G < 80) ++pixels; }
                            changed = pixels > 100;
                        }
                    }
                }
                if (!changed) failures.Add("Changed PNG content did not update without manual refresh or reference painting.");
                File.Move(Path.Combine(folder, "08 宽图.png"), Path.Combine(folder, "08 已改名的宽图.png")); Pump(1800);
                using (var actual = Surface(context, client.Value.Size))
                using (var reference = NativePrint(list, client.Value.Size)) CompareDesktop(context, list, actual, reference, "content-change-and-rename");
                integration.Dispose(); integration = null; context = IntPtr.Zero;
            }
            File.WriteAllLines(Path.Combine(output, "failures.txt"), failures, Encoding.UTF8);
            Console.WriteLine("Mixed icon failures=" + failures.Count);
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        finally
        {
            integration?.Dispose();
            // Fixtures have custom folder attributes; normalize only this owned tree.
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            foreach (var path in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
    private static void CompareDesktop(IntPtr context, IntPtr list, Bitmap actual, Bitmap expected, string stage)
    {
        Console.WriteLine("native_image_preparations=" + SendMessage(FencesViewWindow(context), 0x8000 + 178, new IntPtr(7), IntPtr.Zero));
        actual.Save(Path.Combine(output, "desktop-" + stage + ".png"));
        expected.Save(Path.Combine(output, "native-" + stage + ".png"));
        var surfaceField = captions.GetType().GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic);
        int count = FencesVisibleCount(context);
        using (var presented = (Bitmap)((Bitmap)surfaceField.GetValue(captions)).Clone())
        using (var sheet = new Bitmap(650, count * 70 + 36))
        using (var graphics = Graphics.FromImage(sheet))
        using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10))
        {
            presented.Save(Path.Combine(output, "presented-" + stage + ".png"));
            graphics.Clear(Background);
            graphics.DrawString("Fences", font, Brushes.White, 10, 8);
            graphics.DrawString("原生绘制", font, Brushes.White, 95, 8);
            graphics.DrawString("项目 / RGB 差异像素", font, Brushes.White, 175, 8);
            for (int i = 0; i < count; ++i)
            {
                var path = new StringBuilder(32768); FencesItemPosition(context, i, path, (uint)path.Capacity, out var position);
                var bounds = new Rect { Left = 1 }; SendMessage(list, 0x100e, new IntPtr(i), ref bounds);
                var rect = Rectangle.Intersect(bounds.Value, new Rectangle(Point.Empty, actual.Size));
                if (rect.IsEmpty) continue;
                int mismatch = 0, ink = 0, expectedInk = 0;
                for (int y = rect.Top; y < rect.Bottom; ++y) for (int x = rect.Left; x < rect.Right; ++x)
                {
                    var a = actual.GetPixel(x, y); var b = expected.GetPixel(x, y);
                    if (a.R != b.R || a.G != b.G || a.B != b.B) ++mismatch;
                    if (a.ToArgb() != Background.ToArgb()) ++ink;
                    if (b.ToArgb() != Background.ToArgb()) ++expectedInk;
                }
                var item = new Item { Mask = 0x0a, StateMask = 0x0f00, Index = i };
                SendMessage(list, 0x104b, IntPtr.Zero, ref item);
                var name = Path.GetFileName(path.ToString());
                using (var crop = actual.Clone(rect, PixelFormat.Format32bppArgb))
                {
                    if ((name.StartsWith("02 ") || name.StartsWith("03 ") || name.StartsWith("04 ") || name.StartsWith("05 ") || name.StartsWith("06 ") || name.StartsWith("18 ") || name.StartsWith("19 ") || name.StartsWith("24 ")) && ColoredBounds(crop, false).IsEmpty)
                        failures.Add("Unresolved custom icon: " + stage + " " + name);
                    if (name.Contains("宽图") || name.Contains("长图"))
                    {
                        var content = ColoredBounds(crop, name.Contains("宽图"));
                        var ratio = content.IsEmpty ? 0 : name.Contains("宽图") ? (double)content.Width / content.Height : (double)content.Height / content.Width;
                        if (ratio < 2.4) failures.Add("Missing/distorted thumbnail: " + stage + " " + name + " aspect=" + ratio.ToString("F2"));
                    }
                }
                Console.WriteLine("desktop stage=" + stage + " file=" + name + " image=" + item.Image + " overlay=" + item.State + " ink=" + ink + "/" + expectedInk + " mismatch=" + mismatch);
                if (expectedInk > 40 && ink < expectedInk / 4) failures.Add("Desktop missing icon: " + stage + " " + name);
                if (mismatch != 0) failures.Add("Native RGB mismatch: " + stage + " " + name + " pixels=" + mismatch);
                int presentationMismatch = 0;
                for (int y = rect.Top; y < rect.Bottom; ++y) for (int x = rect.Left; x < rect.Right; ++x)
                {
                    var shown = presented.GetPixel(x, y); var source = actual.GetPixel(x, y);
                    if (shown.A != 255 || shown.ToArgb() != source.ToArgb()) ++presentationMismatch;
                }
                if (presentationMismatch != 0) failures.Add("Presented icon differs from the completed native surface: " + stage + " " + name);
                DrawIconCell(graphics, actual, rect, 10, 36 + i * 70);
                DrawIconCell(graphics, expected, rect, 95, 36 + i * 70);
                graphics.DrawString(name + "\n" + mismatch + " / " + (rect.Width * rect.Height), font, Brushes.White, 175, 38 + i * 70);
            }
            sheet.Save(Path.Combine(output, "compare-" + stage + ".png"));
        }
    }
    private static void DrawIconCell(Graphics graphics, Bitmap image, Rectangle source, int x, int y)
    {
        var scale = Math.Min(1.0, Math.Min(56.0 / source.Width, 56.0 / source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale)), height = Math.Max(1, (int)Math.Round(source.Height * scale));
        graphics.DrawImage(image, new Rectangle(x + (56 - width) / 2, y + (56 - height) / 2, width, height), source, GraphicsUnit.Pixel);
    }
    private static Rectangle ColoredBounds(Bitmap bitmap, bool red)
    {
        var points = new List<Point>();
        for (int y = 0; y < bitmap.Height; ++y) for (int x = 0; x < bitmap.Width; ++x)
        {
            var c = bitmap.GetPixel(x, y);
            if (c.A > 128 && (red ? c.R > 170 && c.G < 100 && c.B < 100 : c.B > 160 && c.R < 100 && c.G < 170)) points.Add(new Point(x, y));
        }
        return points.Count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1);
    }
    private static void CreateFixtures(string root, string folder)
    {
        var iconPath = Path.Combine(root, "blue.ico");
        using (var image = new Bitmap(48, 48))
        {
            using (var graphics = Graphics.FromImage(image)) { graphics.Clear(Color.Transparent); graphics.FillEllipse(Brushes.Blue, 2, 2, 44, 44); graphics.FillRectangle(Brushes.White, 19, 10, 10, 28); }
            var handle = image.GetHicon();
            try { using (var icon = Icon.FromHandle(handle)) using (var stream = File.Create(iconPath)) icon.Save(stream); }
            finally { DestroyIcon(handle); }
        }
        Directory.CreateDirectory(Path.Combine(folder, "01 普通目录"));
        var custom = Path.Combine(folder, "02 自定义目录"); Directory.CreateDirectory(custom);
        var ini = Path.Combine(custom, "desktop.ini");
        File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + iconPath + ",0\r\n", Encoding.Unicode);
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System); File.SetAttributes(custom, FileAttributes.ReadOnly);
        var executable = Path.Combine(folder, "03 自定义图标程序.exe");
        var source = Path.Combine(root, "Fixture.cs"); File.WriteAllText(source, "class Fixture { static void Main() {} }", Encoding.UTF8);
        CompileFixture(source, iconPath, executable, null);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            foreach (var pair in new[] { new[] { "04 程序快捷方式.lnk", executable }, new[] { "05 目录快捷方式.lnk", custom }, new[] { "06 失效快捷方式.lnk", Path.Combine(root, "missing.exe") } })
            {
                dynamic link = shell.CreateShortcut(Path.Combine(folder, pair[0]));
                try { link.TargetPath = pair[1]; link.IconLocation = iconPath + ",0"; link.Save(); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        File.WriteAllText(Path.Combine(folder, "07 网页快捷方式.url"), "[InternetShortcut]\r\nURL=https://example.com/\r\n", Encoding.UTF8);
        using (var image = new Bitmap(300, 100)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.Red); image.Save(Path.Combine(folder, "08 宽图.png"), ImageFormat.Png); }
        using (var image = new Bitmap(100, 300)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.Blue); image.Save(Path.Combine(folder, "09 长图.jpg"), ImageFormat.Jpeg); }
        using (var image = new Bitmap(64, 64)) { using (var g = Graphics.FromImage(image)) { g.Clear(Color.Lime); g.FillEllipse(Brushes.Magenta, 8, 8, 48, 48); } image.Save(Path.Combine(folder, "10 方图.bmp"), ImageFormat.Bmp); image.Save(Path.Combine(folder, "11 动图格式.gif"), ImageFormat.Gif); }
        WritePdf(Path.Combine(folder, "12 文档.pdf"));
        using (var archive = ZipFile.Open(Path.Combine(folder, "13 文档.docx"), ZipArchiveMode.Create))
        {
            WriteZip(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            WriteZip(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            WriteZip(archive, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Mixed icon fixture</w:t></w:r></w:p></w:body></w:document>");
        }
        using (var archive = ZipFile.Open(Path.Combine(folder, "14 压缩包.zip"), ZipArchiveMode.Create)) WriteZip(archive, "sample.txt", "Valid archive fixture");
        File.WriteAllText(Path.Combine(folder, "15 配置.json"), "{\"fixture\":true}", Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "16 未知格式.fencesfixture"), "unknown association", Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "17 对照.txt"), "text control", Encoding.UTF8);
        File.Copy(iconPath, Path.Combine(folder, "18 图标文件.ico"));
        var manifest = Path.Combine(root, "elevated.manifest");
        File.WriteAllText(manifest, "<assembly manifestVersion=\"1.0\" xmlns=\"urn:schemas-microsoft-com:asm.v1\"><assemblyIdentity name=\"MixedIconFixture\" version=\"1.0.0.0\"/><trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v3\"><security><requestedPrivileges><requestedExecutionLevel level=\"requireAdministrator\" uiAccess=\"false\"/></requestedPrivileges></security></trustInfo></assembly>", Encoding.UTF8);
        CompileFixture(source, iconPath, Path.Combine(folder, "19 管理员程序.exe"), manifest);
        using (var image = new Bitmap(80, 80, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.FromArgb(128, 255, 128, 0))) g.FillEllipse(brush, 4, 4, 72, 72);
                g.FillRectangle(Brushes.White, 30, 18, 20, 44);
            }
            image.Save(Path.Combine(folder, "20 透明图片.png"), ImageFormat.Png);
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "21 音频.wav"))))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 1600); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(1600); writer.Write(new byte[1600]);
        }
        using (var image = new Bitmap(64, 64)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.Lime); image.Save(Path.Combine(folder, "23 内容更新.png"), ImageFormat.Png); }
        var video = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Performance", "WinSAT", "winsat.wmv");
        if (File.Exists(video)) File.Copy(video, Path.Combine(folder, "22 视频.wmv"));
        else Console.WriteLine("SKIP: a valid system video fixture is unavailable; video thumbnails are not covered.");
        CreateNamespaceLink(Path.Combine(folder, "25 此电脑.lnk"), "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
        CreateNamespaceLink(Path.Combine(folder, "26 回收站.lnk"), "::{645FF040-5081-101B-9F08-00AA002F954E}");
    }
    private static void CreateNamespaceLink(string path, string target)
    {
        IntPtr pidl; uint attributes;
        Marshal.ThrowExceptionForHR(SHParseDisplayName(target, IntPtr.Zero, out pidl, 0, out attributes));
        var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        try { ((ShellLink)link).SetIDList(pidl); ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true); }
        finally { Marshal.FreeCoTaskMem(pidl); Marshal.FinalReleaseComObject(link); }
    }
    private static void CompileFixture(string source, string icon, string executable, string manifest)
    {
        var compiler = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "csc.exe");
        var arguments = "/nologo /target:winexe /win32icon:\"" + icon + "\" /out:\"" + executable + "\" " +
            (manifest == null ? "" : "/win32manifest:\"" + manifest + "\" ") + "\"" + source + "\"";
        using (var compile = Process.Start(new ProcessStartInfo(compiler, arguments) { UseShellExecute = false, CreateNoWindow = true }))
        { compile.WaitForExit(); if (compile.ExitCode != 0) throw new Exception("Cannot compile the owned executable fixture."); }
    }
    private static void WriteZip(ZipArchive archive, string path, string value) { using (var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(value); }
    private static void WritePdf(string path)
    {
        var body = "%PDF-1.4\n"; var offsets = new List<int>();
        foreach (var value in new[] { "<</Type /Catalog /Pages 2 0 R>>", "<</Type /Pages /Kids [3 0 R] /Count 1>>", "<</Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R>>", "<</Length 0>>\nstream\n\nendstream" })
        { offsets.Add(Encoding.ASCII.GetByteCount(body)); body += offsets.Count + " 0 obj\n" + value + "\nendobj\n"; }
        var start = Encoding.ASCII.GetByteCount(body); body += "xref\n0 5\n0000000000 65535 f \n";
        foreach (var offset in offsets) body += offset.ToString("D10") + " 00000 n \n";
        body += "trailer\n<</Size 5 /Root 1 0 R>>\nstartxref\n" + start + "\n%%EOF\n";
        File.WriteAllText(path, body, Encoding.ASCII);
    }
}
