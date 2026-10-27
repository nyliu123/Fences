using System;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class DragGhostWindow : LayeredVisualWindow
    {
        private readonly Bitmap image;
        private readonly Point cursorOffset;
        private readonly Timer follow = new Timer { Interval = 15 };

        public DragGhostWindow(Bitmap image, Point cursorOffset)
        {
            this.image = image ?? throw new ArgumentNullException(nameof(image));
            this.cursorOffset = cursorOffset;
            ClientSize = image.Size;
            TopMost = true;
            follow.Tick += (sender, e) => FollowCursor(MousePosition);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Present(image);
            follow.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) follow.Stop();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
            base.WndProc(ref message);
        }

        public void FollowCursor(Point cursorPosition)
        {
            var position = new Point(cursorPosition.X - cursorOffset.X, cursorPosition.Y - cursorOffset.Y);
            if (Location != position) Location = position;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                follow.Stop();
                follow.Dispose();
                image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
