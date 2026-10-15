using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NoFences.Util
{
    internal static class WindowGeometry
    {
        internal static Rectangle FitToWorkingArea(Rectangle bounds, Rectangle workingArea)
        {
            var width = Math.Min(Math.Max(1, bounds.Width), workingArea.Width);
            var height = Math.Min(Math.Max(1, bounds.Height), workingArea.Height);
            return new Rectangle(
                Math.Max(workingArea.Left, Math.Min(bounds.X, workingArea.Right - width)),
                Math.Max(workingArea.Top, Math.Min(bounds.Y, workingArea.Bottom - height)), width, height);
        }

        internal static GraphicsPath CreateOutline(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0)
                path.AddRectangle(bounds);
            else
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
            }
            return path;
        }
    }
}
