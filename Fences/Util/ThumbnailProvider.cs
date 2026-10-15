using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoFences.Win32;

namespace NoFences.Util
{
    public class ThumbnailProvider : IDisposable
    {
        // Supported .NET images as per https://docs.microsoft.com/en-us/dotnet/api/system.drawing.image.fromfile
        private static readonly string[] SupportedExtensions =
        {
            ".bmp",
            ".gif",
            ".jpg",
            ".jpeg",
            ".png",
            ".tiff",
            ".tif"
        };

        private class ThumbnailState
        {
            public volatile Icon icon;
            public Icon fallback;
        }

        // Only allow 4 concurrent images to be decoded to try and prevent OOM errors
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(4);
        private readonly IDictionary<string, ThumbnailState> iconCache = new Dictionary<string, ThumbnailState>();
        private readonly object cacheLock = new object();
        private bool disposed;
        public event EventHandler IconThumbnailLoaded;

        public bool IsSupported(string path)
        {
            return SupportedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        public Icon GenerateThumbnail(string path)
        {
            lock (cacheLock)
            {
                if (disposed)
                    return SystemIcons.Application;
                if (!iconCache.ContainsKey(path))
                    return SubmitGeneratorTask(path).icon;
                return iconCache[path].icon;
            }
        }

        private ThumbnailState SubmitGeneratorTask(string path)
        {
            Icon fallback;
            try { fallback = Icon.ExtractAssociatedIcon(path); }
            catch (IOException) { fallback = (Icon)SystemIcons.Application.Clone(); }
            catch (ArgumentException) { fallback = (Icon)SystemIcons.Application.Clone(); }
            var state = new ThumbnailState() { icon = fallback, fallback = fallback };
            iconCache[path] = state;

            Task.Run(() =>
            {
                semaphore.Wait();
                try
                {
                    lock (cacheLock)
                        if (disposed)
                            return;
                    using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                    using (var img = Image.FromStream(ms))
                    using (var thumb = new Bitmap(img, 48, 48))
                    {
                        var handle = thumb.GetHicon();
                        Icon icon;
                        try
                        {
                            using (var nativeIcon = Icon.FromHandle(handle))
                                icon = (Icon)nativeIcon.Clone();
                        }
                        finally { IconUtil.DestroyIcon(handle); }
                        lock (cacheLock)
                        {
                            if (disposed)
                            {
                                icon.Dispose();
                                return;
                            }
                            state.icon = icon;
                        }
                        IconThumbnailLoaded?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
                catch (OutOfMemoryException) { } // GDI+ also uses this for invalid image data.
                finally { semaphore.Release(); }
            });
            return state;
        }

        public void Dispose()
        {
            lock (cacheLock)
            {
                disposed = true;
                foreach (var state in iconCache.Values)
                {
                    if (state.icon != state.fallback)
                        state.icon?.Dispose();
                    state.fallback?.Dispose();
                }
                iconCache.Clear();
            }
        }

    }
}
