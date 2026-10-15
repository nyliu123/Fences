using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences
{
    internal abstract class LayeredVisualWindow : Form
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;

            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr graphicsObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr windowHandle,
            IntPtr destinationDeviceContext,
            IntPtr destinationPoint,
            ref NativeSize size,
            IntPtr sourceDeviceContext,
            ref NativePoint sourcePoint,
            int colorKey,
            ref BlendFunction blend,
            int flags);

        protected LayeredVisualWindow()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected void Present(Bitmap bitmap)
        {
            PresentBitmap(this, bitmap);
        }

        internal static void PresentBitmap(Form window, Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 1 || bitmap.Height < 1 || window.IsDisposed)
                return;

            var screenDeviceContext = GetDC(IntPtr.Zero);
            var memoryDeviceContext = CreateCompatibleDC(screenDeviceContext);
            var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            var previousObject = SelectObject(memoryDeviceContext, bitmapHandle);
            try
            {
                // Bounds already positions the window. Do not reinterpret child coordinates
                // as screen coordinates when refreshing a layered desktop window.
                var source = new NativePoint(0, 0);
                var size = new NativeSize(bitmap.Width, bitmap.Height);
                var blend = new BlendFunction
                {
                    BlendOp = AC_SRC_OVER,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                if (!UpdateLayeredWindow(
                    window.Handle,
                    screenDeviceContext,
                    IntPtr.Zero,
                    ref size,
                    memoryDeviceContext,
                    ref source,
                    0,
                    ref blend,
                    ULW_ALPHA))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                SelectObject(memoryDeviceContext, previousObject);
                DeleteObject(bitmapHandle);
                DeleteDC(memoryDeviceContext);
                ReleaseDC(IntPtr.Zero, screenDeviceContext);
            }
        }

        protected static Bitmap CreateLayerBitmap(Size size)
        {
            return new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
        }
    }
}
