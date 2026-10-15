using NoFences.Model;
using NoFences.Util;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace NoFences
{
    public partial class FenceWindow
    {
        private bool isRefreshingBackgroundLayer;

        public new void Refresh()
        {
            base.Refresh();
            RefreshBackgroundLayer();
        }

        private void RefreshBackgroundLayer()
        {
            if (!IsHandleCreated || IsDisposed || Disposing || titleFont == null ||
                ClientSize.Width < 1 || ClientSize.Height < 1 || isRefreshingBackgroundLayer)
                return;

            isRefreshingBackgroundLayer = true;
            try
            {
                // Update the existing item layout and hover state even when a layered
                // child window does not receive WM_PAINT after Refresh().
                using (var stateBitmap = new Bitmap(1, 1))
                using (var graphics = Graphics.FromImage(stateBitmap))
                using (var paint = new PaintEventArgs(graphics, ClientRectangle))
                    FenceWindow_Paint(this, paint);

                using (var bitmap = CreateBackgroundBitmap())
                    LayeredVisualWindow.PresentBitmap(this, bitmap);
            }
            finally
            {
                isRefreshingBackgroundLayer = false;
            }
        }

        private Bitmap CreateBackgroundBitmap()
        {
            var appearance = GetAppearance();
            var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    using (var outline = WindowGeometry.CreateOutline(ClientRectangle, appearance.CornerRadius))
                        graphics.SetClip(outline);
                    using (var body = new SolidBrush(Color.FromArgb(
                        (int)Math.Round((100 - appearance.BodyTransparency) * 2.55),
                        Color.FromArgb(appearance.BodyBackgroundColorArgb))))
                        graphics.FillRectangle(body, ClientRectangle);

                    var header = new Rectangle(0, 0, ClientSize.Width, Math.Min(titleHeight, ClientSize.Height));
                    using (var titleBackground = new SolidBrush(Color.FromArgb(255,
                        Color.FromArgb(appearance.TitleBackgroundColorArgb))))
                        graphics.FillRectangle(titleBackground, header);
                    using (var titleText = new SolidBrush(Color.FromArgb(appearance.TitleTextColorArgb)))
                    using (var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap
                    })
                    {
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        graphics.DrawString(Text, titleFont, titleText, header, format);
                    }

                    DrawItemBackgrounds(graphics);
                    using (var border = new Pen(acrylicBorder))
                    using (var outline = WindowGeometry.CreateOutline(
                        new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), appearance.CornerRadius))
                        graphics.DrawPath(border, outline);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private void DrawItemBackgrounds(Graphics graphics)
        {
            var state = graphics.Save();
            try
            {
                graphics.SetClip(new Rectangle(0, titleHeight, ClientSize.Width,
                    Math.Max(0, ClientSize.Height - titleHeight)), CombineMode.Intersect);
                var x = itemPadding;
                var y = titleHeight + itemPadding - scrollOffset;
                foreach (var path in fenceInfo.Files)
                {
                    if (!ItemExists(path))
                        continue;
                    var editing = string.Equals(editingItemPath, path, StringComparison.OrdinalIgnoreCase);
                    var selected = !editing && string.Equals(selectedItem, path, StringComparison.OrdinalIgnoreCase);
                    var hovered = !editing && string.Equals(hoveringItem, path, StringComparison.OrdinalIgnoreCase);
                    if (selected || hovered)
                    {
                        var visibleTextHeight = textHeight;
                        if (selected)
                        {
                            var entry = FenceEntry.FromPath(path);
                            using (var format = new StringFormat { Alignment = StringAlignment.Center })
                                visibleTextHeight = MeasureItemVisibleTextHeight(graphics, entry, true, format);
                        }
                        var outline = new Rectangle(x - 2, y - 2,
                            itemWidth + 2, itemIconSize + visibleTextHeight + 7);
                        using (var fill = new SolidBrush(selected
                            ? Color.FromArgb(hovered ? 100 : 80,
                                hovered ? SystemColors.GradientActiveCaption : SystemColors.GradientInactiveCaption)
                            : Color.FromArgb(80, SystemColors.ActiveCaption)))
                        using (var border = new Pen(Color.FromArgb(120, SystemColors.ActiveBorder)))
                        {
                            graphics.FillRectangle(fill, outline);
                            graphics.DrawRectangle(border, outline.X + 1, outline.Y + 1,
                                outline.Width - 2, outline.Height - 2);
                        }
                    }
                    x += itemWidth + itemSpacing;
                    if (x + itemWidth > Width)
                    {
                        x = itemPadding;
                        y += itemHeight + itemSpacing;
                    }
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }
    }
}
