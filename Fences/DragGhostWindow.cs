using System;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class DragGhostWindow : LayeredVisualWindow
    {
        private readonly Bitmap image;
        private readonly Point cursorOffset;

        public DragGhostWindow(Bitmap image, Point cursorOffset)
        {
            this.image = image ?? throw new ArgumentNullException(nameof(image));
            this.cursorOffset = cursorOffset;
            ClientSize = image.Size;
            TopMost = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Present(image);
        }

        public void FollowCursor(Point cursorPosition)
        {
            Location = new Point(cursorPosition.X - cursorOffset.X, cursorPosition.Y - cursorOffset.Y);
            if (IsHandleCreated)
                Present(image);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                image.Dispose();
            base.Dispose(disposing);
        }
    }
}
