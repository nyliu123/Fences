using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using NoFences;
using NoFences.Model;
using NoFences.Util;

internal static class AppearanceAndDisplayTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int assertions;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main(string[] args)
    {
        Control.CheckForIllegalCrossThreadCalls = true;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) =>
        {
            Console.WriteLine("FAIL: " + e.Exception);
            Environment.Exit(1);
        };
        Application.EnableVisualStyles();
        var assembly = typeof(FenceWindow).Assembly;
        var output = Path.GetFullPath(args.Length == 0 ? "Fences/obj/VisualChecks" : args[0]);
        Directory.CreateDirectory(output);
        var geometry = assembly.GetType("NoFences.Util.WindowGeometry");
        var fit = geometry.GetMethod("FitToWorkingArea", Static);
        foreach (var area in new[] { new Rectangle(0, 0, 1920, 1040),
            new Rectangle(-2560, -500, 2560, 1400), new Rectangle(1920, 200, 1280, 680) })
        {
            var result = (Rectangle)fit.Invoke(null, new object[] { new Rectangle(-9000, 9000, 4000, 2000), area });
            Check(area.Contains(result) && result.Width > 0 && result.Height > 0,
                "oversized/offscreen bounds fit working area " + area);
            var inside = new Rectangle(area.X + 10, area.Y + 10, 300, 300);
            Check((Rectangle)fit.Invoke(null, new object[] { inside, area }) == inside,
                "valid bounds preserved " + area);
        }

        var appearance = new FenceAppearance { CornerRadius = 24 };
        var serializer = new XmlSerializer(typeof(FenceAppearance));
        using (var text = new StringWriter())
        {
            serializer.Serialize(text, appearance);
            using (var reader = new StringReader(text.ToString()))
                Check(((FenceAppearance)serializer.Deserialize(reader)).CornerRadius == 24, "radius persists in XML");
        }
        using (var reader = new StringReader("<FenceAppearance />"))
            Check(((FenceAppearance)serializer.Deserialize(reader)).CornerRadius == 0, "legacy appearance keeps square corners");
        Check(appearance.Clone().CornerRadius == 24, "batch/clone retains radius");
        appearance.CornerRadius = 999;
        appearance.Normalize();
        Check(appearance.CornerRadius == 48, "radius is bounded");
        appearance.CornerRadius = 24;

        using (var thumbnails = new ThumbnailProvider())
        using (var completed = new ManualResetEvent(false))
        {
            Check(thumbnails.IsSupported("PHOTO.PNG"), "image extensions are case insensitive");
            for (var i = 0; i < 6; i++)
            {
                var corrupt = Path.Combine(output, "corrupt-" + i + ".png");
                File.WriteAllText(corrupt, "invalid image");
                thumbnails.GenerateThumbnail(corrupt);
            }
            var valid = Path.Combine(output, "thumbnail.png");
            using (var image = new Bitmap(16, 16)) image.Save(valid);
            thumbnails.IconThumbnailLoaded += (sender, e) => completed.Set();
            thumbnails.GenerateThumbnail(valid);
            Check(completed.WaitOne(5000), "corrupt images release decode slots for later thumbnails");
        }

        var info = new FenceInfo(Guid.NewGuid()) { Name = "常用 · 圆角预览", Width = 330, Height = 390 };
        var dialogType = assembly.GetType("NoFences.PersonalizationDialog");
        using (var dialog = (Form)Activator.CreateInstance(dialogType, Instance, null,
            new object[] { appearance, false, info }, null))
        {
            dialog.Show();
            Application.DoEvents();
            foreach (var name in new[] { "bodyTransparencyInput", "iconSizeInput", "iconSpacingInput", "cornerRadiusInput" })
            {
                var slider = (TrackBar)dialogType.GetField(name, Instance).GetValue(dialog);
                slider.Focus();
                var before = slider.Value;
                slider.GetType().GetMethod("OnMouseWheel", Instance).Invoke(slider,
                    new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, 120) });
                Check(slider.Value == before, "wheel leaves value unchanged: " + name);
                foreach (var message in new[] { 0x020A, 0x020E })
                {
                    foreach (var delta in new[] { 120, -120, 30, -30 })
                        SendMessage(slider.Handle, message, new IntPtr(delta << 16), IntPtr.Zero);
                    Check(slider.Value == before, "native wheel message is blocked: " + name + "/" + message);
                }
                dialog.ActiveControl = null;
                SendMessage(slider.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                Check(slider.Value == before, "unfocused slider ignores native wheel: " + name);
                slider.Focus();
                SendMessage(slider.Handle, 0x0100, new IntPtr(0x27), IntPtr.Zero);
                SendMessage(slider.Handle, 0x0101, new IntPtr(0x27), IntPtr.Zero);
                Check(slider.Value > before, "keyboard arrows still adjust slider: " + name);
                slider.Value = before;
            }
            var radius = (TrackBar)dialogType.GetField("cornerRadiusInput", Instance).GetValue(dialog);
            radius.Value = 36;
            Check(((FenceAppearance)dialogType.GetProperty("Appearance").GetValue(dialog)).CornerRadius == 36,
                "slider updates appearance snapshot");
            ScrollSettings(dialog, false);
            Render(dialog, Path.Combine(output, "personalization.png"));
            dialog.ClientSize = new Size(760, 540);
            Application.DoEvents();
            ScrollSettings(dialog, true);
            Render(dialog, Path.Combine(output, "personalization-small.png"));
            dialog.Close();
        }

        var host = assembly.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static);
        var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
            new object[] { Path.Combine(output, "storage"), output }, null);
        typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
        using (var parent = new Form { Bounds = new Rectangle(80, 90, 700, 600) })
        {
            host.SetValue(null, parent.Handle);
            info.Appearance = appearance;
            using (var window = new FenceWindow(info))
            {
                window.Show();
                typeof(FenceWindow).GetMethod("ApplyPersonalization", Instance).Invoke(window, new object[] { appearance });
                Application.DoEvents();
                Check(info.Appearance.CornerRadius == 24, "personalization applies to live layers without disposed font errors");
                var expected = window.Location;
                window.Refresh();
                Check(window.Location == expected, "layer refresh preserves child coordinates");
                Check(!window.Region.IsVisible(0, 0) && window.Region.IsVisible(160, 100),
                    "native rounded region clips corners and keeps content");
                typeof(FenceWindow).GetMethod("BeginTitleEdit", Instance).Invoke(window, null);
                var titleLayer = typeof(FenceWindow).GetField("titleLayer", Instance).GetValue(window);
                var titleEditor = (Form)titleLayer.GetType().GetField("activeEditor", Instance).GetValue(titleLayer);
                Check(titleEditor != null && !titleEditor.Region.IsVisible(0, 0),
                    "title editor preserves rounded top corners");
                Render(titleEditor, Path.Combine(output, "title-editor.png"));
                typeof(FenceWindow).GetMethod("CancelTitleEdit", Instance).Invoke(window, null);
                Exception backgroundError = null;
                var thread = new Thread(() =>
                {
                    try { typeof(FenceWindow).GetMethod("ThumbnailProvider_IconThumbnailLoaded", Instance)
                        .Invoke(window, new object[] { null, EventArgs.Empty }); }
                    catch (Exception e) { backgroundError = e; }
                });
                thread.Start();
                thread.Join();
                Application.DoEvents();
                Check(backgroundError == null, "thumbnail completion marshals to UI thread");
                using (var bitmap = (Bitmap)typeof(FenceWindow).GetMethod("CreateBackgroundBitmap", Instance).Invoke(window, null))
                {
                    Check(bitmap.GetPixel(0, 0).A == 0, "rendered body corner is transparent");
                    bitmap.Save(Path.Combine(output, "fence-rounded.png"));
                }
                window.Close();
            }
        }
        Console.WriteLine("PASS: " + assertions + " assertions; rendered checks saved to " + output);
    }

    private static void Render(Form form, string path)
    {
        using (var image = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            image.Save(path);
        }
    }

    private static void ScrollSettings(Control control, bool bottom)
    {
        var panel = control as Panel;
        if (panel != null && panel.AutoScroll)
            panel.AutoScrollPosition = new Point(0, bottom ? panel.VerticalScroll.Maximum : 0);
        foreach (Control child in control.Controls)
            ScrollSettings(child, bottom);
        Application.DoEvents();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        assertions++;
        Console.WriteLine("PASS: " + name);
    }
}
