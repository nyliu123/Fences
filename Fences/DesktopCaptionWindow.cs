using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NoFences
{
    // Shares the per-pixel presentation mechanism used by Fence item text.
    // Input and item interaction remain entirely with the underlying Shell view.
    internal sealed class DesktopCaptionWindow : LayeredVisualWindow
    {
        private sealed class Caption
        {
            internal string Text;
            internal Rectangle Bounds;
            internal bool Selected;
            internal Rectangle ItemBounds;
            internal int VisualState;
            internal int Index;
            internal Rectangle Backdrop => Rectangle.Union(ItemBounds, Bounds);
            internal bool Same(Caption other) => Text == other.Text && Bounds == other.Bounds && ItemBounds == other.ItemBounds &&
                Selected == other.Selected && VisualState == other.VisualState;
        }

        private readonly IntPtr context;
        private readonly Timer refresh = new Timer { Interval = 15 };
        private UIntPtr lastRevision;
        private bool hasRevision;
        private bool fullRefresh = true;
        private Bitmap wallpaper;
        private Bitmap canvas;
        private Font drawingFont;
        private IntPtr fontHandle;
        private int fontPixels;
        private readonly StringBuilder nameBuffer = new StringBuilder(512);
        private Dictionary<int, Caption> previousLabels = new Dictionary<int, Caption>();
        private bool releasing;
        internal Action<Bitmap> FrameRendered { get; set; }

        internal DesktopCaptionWindow(IntPtr context, Action<Exception> failed)
        {
            this.context = context;
            Text = "Fences Desktop Captions";
            refresh.Tick += (sender, e) =>
            {
                refresh.Stop();
                try { RefreshLabels(); }
                catch (Exception error) { failed(error); }
            };
        }

        internal void AttachToView()
        {
            DesktopWindowHost.Attach(this, DesktopViewNative.FencesViewWindow(context));
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesSetCaptionWindow(context, Handle));
        }

        internal void ScheduleRefresh()
        {
            if (!releasing && !IsDisposed && !refresh.Enabled) refresh.Start();
        }

        internal void InvalidateBackground()
        {
            fullRefresh = true;
            ScheduleRefresh();
        }

        internal void RefreshLabels()
        {
            if (releasing || IsDisposed) return;
            var revision = DesktopViewNative.FencesCaptionRevision(context);
            if (!fullRefresh && hasRevision && revision == lastRevision && Visible) return;
            var preferences = DesktopPreferences.Empty;
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesGetPreferences(context, ref preferences));
            if ((preferences.Flags & 0x1000) != 0) { Hide(); fullRefresh = true; return; }
            var handle = DesktopViewNative.FencesCaptionMetrics(context, out var area, out var pixels);
            if (handle == IntPtr.Zero || pixels < 1) throw new InvalidOperationException("无法读取桌面项目名称字体。");
            var client = area.Rectangle;
            if (client.Width < 1 || client.Height < 1) { Hide(); fullRefresh = true; return; }
            if (Bounds != client) Bounds = client;
            bool all = fullRefresh || canvas == null || canvas.Size != client.Size || drawingFont == null || fontHandle != handle || fontPixels != pixels;
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesCaptionDirty(context, out var nativeDirty));
            var dirty = all ? client : Rectangle.Intersect(client, nativeDirty.Rectangle);
            var labels = new List<Caption>();
            var nextLabels = new Dictionary<int, Caption>();
            for (int index = 0, count = DesktopViewNative.FencesVisibleCount(context); index < count; ++index)
            {
                nameBuffer.Clear();
                var result = DesktopViewNative.FencesCaptionAt(context, index, nameBuffer, (uint)nameBuffer.Capacity, out var bounds, out var state);
                if (result == unchecked((int)0x8007007A))
                {
                    nameBuffer.EnsureCapacity(32768);
                    result = DesktopViewNative.FencesCaptionAt(context, index, nameBuffer, (uint)nameBuffer.Capacity, out bounds, out state);
                }
                if (result == 1) continue;
                Marshal.ThrowExceptionForHR(result);
                var rectangle = bounds.Rectangle;
                if (!rectangle.IntersectsWith(client)) continue;
                var label = new Caption { Index = index, Text = nameBuffer.ToString(), Bounds = rectangle, Selected = (state & 2) != 0 };
                Marshal.ThrowExceptionForHR(DesktopViewNative.FencesCaptionDecoration(context, index, out var itemBounds, out var visualState));
                label.ItemBounds = itemBounds.Rectangle; label.VisualState = visualState;
                labels.Add(label);
                nextLabels[index] = label;
                if (!all && (!previousLabels.TryGetValue(index, out var previous) || !label.Same(previous)))
                {
                    dirty = Include(dirty, label.Backdrop);
                    if (previous != null) dirty = Include(dirty, previous.Backdrop);
                }
            }
            if (!all)
                foreach (var previous in previousLabels)
                    if (!nextLabels.ContainsKey(previous.Key)) dirty = Include(dirty, previous.Value.Backdrop);
            dirty = Rectangle.Intersect(dirty, client);
            if (dirty.Width <= 0 || dirty.Height <= 0)
            {
                previousLabels = nextLabels; lastRevision = revision; hasRevision = true;
                DesktopViewNative.FencesAcknowledgeCaption(context, revision); RaiseWithoutActivation(); return;
            }
            if (wallpaper == null || wallpaper.Size != client.Size)
            {
                wallpaper?.Dispose();
                wallpaper = new Bitmap(client.Width, client.Height, PixelFormat.Format32bppRgb);
            }
            using (var background = Graphics.FromImage(wallpaper))
            {
                var dc = background.GetHdc();
                var region = new DesktopCaptionBounds { Left = dirty.Left, Top = dirty.Top, Right = dirty.Right, Bottom = dirty.Bottom };
                try { Marshal.ThrowExceptionForHR(DesktopViewNative.FencesCaptionSurfaceRegion(context, dc, ref region)); }
                finally { background.ReleaseHdc(dc); }
            }
            if (all)
            {
                drawingFont?.Dispose();
                using (var native = Font.FromHfont(handle)) drawingFont = new Font(native.FontFamily, pixels, native.Style, GraphicsUnit.Pixel);
                fontHandle = handle; fontPixels = pixels;
            }
            if (canvas == null || canvas.Size != client.Size)
            {
                canvas?.Dispose(); canvas = CreateLayerBitmap(client.Size);
            }
            using (var graphics = Graphics.FromImage(canvas))
            using (var white = new SolidBrush(Color.White))
            using (var shadow = new SolidBrush(Color.Black))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoClip })
            {
                graphics.SetClip(dirty);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.FillRectangle(Brushes.Transparent, dirty);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int pass = 0; pass < 2; ++pass)
                foreach (var label in labels)
                {
                    if (label.Selected != (pass == 1)) continue;
                    var layout = label.Bounds;
                    // Present the icon, connecting space and name on ONE layered
                    // surface. A label-only patch still leaves a compositor seam.
                    var backdrop = Rectangle.Union(label.ItemBounds, layout);
                    if (!backdrop.IntersectsWith(dirty)) continue;
                    var saved = graphics.Save();
                    graphics.SetClip(backdrop, System.Drawing.Drawing2D.CombineMode.Intersect);
                    // Copy the same completed pixels used beneath the icon.
                    // No second theme/alpha operation is performed for names.
                    var visible = Rectangle.Intersect(backdrop, client);
                    graphics.DrawImage(wallpaper, visible, visible, GraphicsUnit.Pixel);
                    var offset = layout; offset.Offset(1, 1);
                    graphics.DrawString(label.Text, drawingFont, shadow, offset, format);
                    graphics.DrawString(label.Text, drawingFont, white, layout, format);
                    graphics.Restore(saved);
                }
                Present(canvas);
                FrameRendered?.Invoke(canvas);
            }
            previousLabels = nextLabels; lastRevision = revision; hasRevision = true; fullRefresh = false;
            DesktopViewNative.FencesAcknowledgeCaption(context, revision);
            if (!Visible) Show();
            RaiseWithoutActivation();
        }

        private static Rectangle Include(Rectangle area, Rectangle item) => area.IsEmpty ? item : Rectangle.Union(area, item);

        private void RaiseWithoutActivation() => WindowUtil.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            WindowUtil.SWP_NOMOVE | WindowUtil.SWP_NOSIZE | WindowUtil.SWP_NOACTIVATE);

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
            base.WndProc(ref message);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !releasing)
            {
                releasing = true; refresh.Stop(); refresh.Dispose();
                wallpaper?.Dispose(); wallpaper = null;
                canvas?.Dispose(); canvas = null;
                drawingFont?.Dispose(); drawingFont = null;
                DesktopViewNative.FencesSetCaptionWindow(context, IntPtr.Zero);
            }
            base.Dispose(disposing);
        }
    }
}
