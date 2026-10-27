using NoFences;
using NoFences.Model;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class ScrollingInteractionTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Instance).GetValue(value);
    private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Instance).Invoke(value, args);
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); ++checks; Console.WriteLine("PASS: " + name); }
    [STAThread] private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "Fences-scroll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var hostField = typeof(FenceWindow).Assembly.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static);
        var previousHost = hostField.GetValue(null);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                new object[] { Path.Combine(root, "storage"), root }, null);
            typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
            using (var host = new Form { ClientSize = new Size(650, 520) })
            {
                host.Show(); hostField.SetValue(null, host.Handle);
                var info = new FenceInfo(Guid.NewGuid()) { Name = "滚动检查", PosX = 80, PosY = 100, Width = 300, Height = 260, KeepOriginalPaths = true };
                info.Appearance.CornerRadius = 32;
                info.Appearance.BodyBackgroundColorArgb = Color.FromArgb(44, 88, 132).ToArgb();
                info.Appearance.BodyTransparency = 65;
                for (int i = 0; i < 30; ++i) { var path = Path.Combine(root, "项目 " + i.ToString("D2")); Directory.CreateDirectory(path); info.Files.Add(path); }
                using (var fence = new FenceWindow(info))
                {
                    fence.Show(); fence.Refresh();
                    var track = Field<Rectangle>(fence, "scrollTrack"); var thumb = Field<Rectangle>(fence, "scrollThumb");
                    Check(track.Height > 0 && thumb.Height > 0 && track.Contains(thumb), "overflow produces a positive bounded scroll thumb");
                    Check(Field<int>(fence, "itemLayoutWidth") < fence.ClientSize.Width, "overflow reserves a gutter for every item layout");
                    Check(thumb.Top == track.Top, "top scroll position maps to top of track");
                    Save(fence, "scroll-top.png");
                    using (var background = (Bitmap)Call(fence, "CreateBackgroundBitmap"))
                    {
                        var pixel = background.GetPixel(thumb.Left + 2, thumb.Top + 2);
                        Check(pixel.ToArgb() == Color.FromArgb(255, 44, 88, 132).ToArgb(),
                            "scroll thumb matches the configured panel RGB and is fully opaque");
                    }
                    Call(fence, "ScrollbarMouseDown", new MouseEventArgs(MouseButtons.Left, 1, thumb.Left + 2, thumb.Top + 2, 0));
                    Check(Field<bool>(fence, "draggingScrollThumb"), "thumb click begins dragging");
                    using (var background = (Bitmap)Call(fence, "CreateBackgroundBitmap"))
                        Check(background.GetPixel(thumb.Left + 2, thumb.Top + 2).ToArgb() == Color.FromArgb(255, 44, 88, 132).ToArgb(),
                            "dragging preserves the same opaque panel color");
                    Call(fence, "ScrollbarMouseMove", new MouseEventArgs(MouseButtons.Left, 0, thumb.Left + 2, track.Bottom + 100, 0));
                    Check(Field<int>(fence, "scrollOffset") == Field<int>(fence, "scrollHeight"), "drag beyond the track clamps to the last content row");
                    Call(fence, "StopScrollbarDrag");
                    Check(!Field<bool>(fence, "draggingScrollThumb") && !fence.Capture, "drag completion releases capture");
                    thumb = Field<Rectangle>(fence, "scrollThumb");
                    Check(thumb.Bottom == track.Bottom, "last scroll position maps to bottom of track");
                    Save(fence, "scroll-bottom.png");
                    var before = Field<int>(fence, "scrollOffset");
                    Call(fence, "ScrollbarMouseDown", new MouseEventArgs(MouseButtons.Left, 1, track.Left + 2, track.Top + 1, 0));
                    Check(Field<int>(fence, "scrollOffset") < before, "track click pages upwards");
                    Call(fence, "SetScrollOffset", int.MaxValue); before = Field<int>(fence, "scrollOffset");
                    Call(fence, "FenceWindow_MouseWheel", fence, new MouseEventArgs(MouseButtons.None, 0, 20, 80, 120));
                    Check(Field<int>(fence, "scrollOffset") == before - 10, "wheel retains its existing scroll step");
                    Check(Call(fence, "HitTestContextItem", new Point(track.Left + 2, track.Top + 2)) == null, "scrollbar gutter never opens an item context menu");
                    Check(Field<string>(fence, "draggedItem") == null, "scrollbar dragging does not begin a file drag");
                    info.Appearance.BodyBackgroundColorArgb = Color.FromArgb(136, 62, 41).ToArgb();
                    fence.Refresh(); thumb = Field<Rectangle>(fence, "scrollThumb");
                    using (var background = (Bitmap)Call(fence, "CreateBackgroundBitmap"))
                        Check(background.GetPixel(thumb.Left + 2, thumb.Top + 2).ToArgb() == Color.FromArgb(255, 136, 62, 41).ToArgb(),
                            "changing personalization immediately updates scrollbar RGB");
                    Save(fence, "scroll-custom-color.png");
                    info.Files.Clear(); fence.Refresh();
                    Check(Field<Rectangle>(fence, "scrollTrack").IsEmpty && Field<int>(fence, "scrollOffset") == 0, "removing overflow hides the bar and resets the offset");
                    info.Files.Add(root); fence.Refresh();
                    Check(Field<Rectangle>(fence, "scrollTrack").IsEmpty && Field<int>(fence, "itemLayoutWidth") == fence.ClientSize.Width,
                        "a fitting item keeps the full original layout width");
                }
            }
            Console.WriteLine("PASS: " + checks + " scrolling assertions"); return 0;
        }
        catch (Exception error) { while (error.InnerException != null) error = error.InnerException; Console.Error.WriteLine(error); return 1; }
        finally { hostField.SetValue(null, previousHost); Directory.Delete(root, true); }
    }
    private static void Save(FenceWindow fence, string name)
    {
        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisualChecks"); Directory.CreateDirectory(directory);
        using (var bitmap = (Bitmap)Call(fence, "CreateBackgroundBitmap"))
        using (var preview = new Bitmap(bitmap.Width, bitmap.Height))
        {
            using (var graphics = Graphics.FromImage(preview))
            {
                graphics.Clear(Color.FromArgb(200, 200, 200));
                graphics.DrawImageUnscaled(bitmap, Point.Empty);
                Call(fence, "DrawOpaqueItems", graphics);
            }
            preview.Save(Path.Combine(directory, name));
        }
    }
}
