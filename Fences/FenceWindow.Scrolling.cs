using NoFences.Model;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences
{
    public partial class FenceWindow
    {
        private const int ScrollbarGutter = 12;
        private const int ScrollbarWidth = 6;
        private const int MinimumScrollThumb = 24;
        private Rectangle scrollTrack;
        private Rectangle scrollThumb;
        private bool draggingScrollThumb;
        private int scrollGrabOffset;
        private int itemLayoutWidth;

        private List<FenceEntry> UpdateScrollLayout()
        {
            var entries = new List<FenceEntry>();
            foreach (var path in fenceInfo.Files)
            {
                var entry = FenceEntry.FromPath(path);
                if (entry != null) entries.Add(entry);
            }
            itemLayoutWidth = ClientSize.Width;
            var viewport = Math.Max(0, ClientSize.Height - titleHeight);
            var content = ScrollContentHeight(entries.Count, itemLayoutWidth);
            if (content > viewport)
            {
                itemLayoutWidth = Math.Max(1, ClientSize.Width - ScrollbarGutter);
                content = ScrollContentHeight(entries.Count, itemLayoutWidth);
            }
            scrollHeight = Math.Max(0, content - viewport);
            scrollOffset = Math.Max(0, Math.Min(scrollHeight, scrollOffset));
            scrollTrack = scrollThumb = Rectangle.Empty;
            if (scrollHeight > 0 && viewport > 8 && !isMinified)
            {
                var curveInset = Math.Min(GetAppearance().CornerRadius, Math.Min(ClientSize.Width, ClientSize.Height) / 2);
                var topEdge = Math.Max(titleHeight + 4, curveInset);
                var bottomEdge = ClientSize.Height - Math.Max(10, curveInset);
                scrollTrack = new Rectangle(ClientSize.Width - ScrollbarWidth - 3, topEdge, ScrollbarWidth, Math.Max(0, bottomEdge - topEdge));
                if (scrollTrack.Height == 0)
                {
                    scrollTrack = Rectangle.Empty;
                    if (draggingScrollThumb) StopScrollbarDrag();
                    return entries;
                }
                var height = Math.Min(scrollTrack.Height, Math.Max(MinimumScrollThumb,
                    (int)Math.Round((double)scrollTrack.Height * viewport / content)));
                var top = scrollTrack.Top + (int)Math.Round((double)scrollOffset / scrollHeight * (scrollTrack.Height - height));
                scrollThumb = new Rectangle(scrollTrack.Left, top, scrollTrack.Width, height);
            }
            if (scrollTrack.IsEmpty && draggingScrollThumb) StopScrollbarDrag();
            return entries;
        }

        private int ScrollContentHeight(int count, int width)
        {
            if (count == 0) return 0;
            var columns = Math.Max(1, (width - itemPadding - itemWidth) / (itemWidth + itemSpacing) + 1);
            var rows = (count + columns - 1) / columns;
            return itemPadding * 2 + (rows - 1) * (itemHeight + itemSpacing) + itemHeight;
        }

        private void DrawScrollbar(Graphics graphics)
        {
            if (scrollTrack.IsEmpty) return;
            using (var thumb = new SolidBrush(Color.FromArgb(255, Color.FromArgb(GetAppearance().BodyBackgroundColorArgb))))
                graphics.FillRectangle(thumb, scrollThumb);
        }

        private bool IsScrollbarPoint(Point point) => !scrollTrack.IsEmpty &&
            new Rectangle(ClientSize.Width - ScrollbarGutter, scrollTrack.Top, ScrollbarGutter - 2, scrollTrack.Height).Contains(point);

        private bool ScrollbarMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !IsScrollbarPoint(e.Location)) return false;
            draggedItem = hoveringItem = null;
            if (scrollThumb.Contains(e.Location))
            {
                draggingScrollThumb = true; scrollGrabOffset = e.Y - scrollThumb.Top; Capture = true; Refresh();
            }
            else SetScrollOffset(scrollOffset + (e.Y < scrollThumb.Top ? -1 : 1) * Math.Max(1, ClientSize.Height - titleHeight));
            return true;
        }

        private bool ScrollbarMouseMove(MouseEventArgs e)
        {
            if (!draggingScrollThumb) return false;
            var travel = scrollTrack.Height - scrollThumb.Height;
            if (travel > 0) SetScrollOffset((int)Math.Round((double)(e.Y - scrollGrabOffset - scrollTrack.Top) * scrollHeight / travel));
            return true;
        }

        private void StopScrollbarDrag()
        {
            if (!draggingScrollThumb) return;
            draggingScrollThumb = false; Capture = false;
            if (!IsDisposed && !Disposing) Refresh();
        }

        private void SetScrollOffset(int value)
        {
            value = Math.Max(0, Math.Min(scrollHeight, value));
            if (value == scrollOffset) return;
            scrollOffset = value; hoveringItem = null; Refresh();
        }
    }
}
