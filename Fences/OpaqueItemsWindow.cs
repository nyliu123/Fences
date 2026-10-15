using System;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class OpaqueItemsWindow : LayeredVisualWindow
    {
        private readonly Action<Graphics> drawItems;

        public OpaqueItemsWindow(Action<Graphics> drawItems)
        {
            this.drawItems = drawItems ?? throw new ArgumentNullException(nameof(drawItems));
        }

        public void RefreshItems()
        {
            if (ClientSize.Width < 1 || ClientSize.Height < 1 || !IsHandleCreated)
                return;
            using (var bitmap = CreateLayerBitmap(ClientSize))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                drawItems(graphics);
                Present(bitmap);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshItems();
        }
    }
}
