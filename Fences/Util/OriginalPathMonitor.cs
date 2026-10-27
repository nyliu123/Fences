using NoFences.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NoFences.Util
{
    internal sealed class OriginalPathMonitor : IDisposable
    {
        private readonly FenceManager manager;
        private readonly Control dispatcher;
        private readonly Action refresh;
        private readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer debounce = new Timer { Interval = 150 };
        private bool disposed;

        internal OriginalPathMonitor(FenceManager manager, Control dispatcher, Action refresh)
        {
            this.manager = manager; this.dispatcher = dispatcher; this.refresh = refresh;
            debounce.Tick += (sender, e) => { debounce.Stop(); if (!disposed) refresh(); };
        }

        internal void Synchronize()
        {
            var directories = new HashSet<string>(manager.OriginalPaths.Select(Path.GetDirectoryName).Where(Directory.Exists), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in watchers.ToArray())
                if (!directories.Contains(pair.Key)) { pair.Value.Dispose(); watchers.Remove(pair.Key); }
            foreach (var directory in directories)
            {
                if (watchers.ContainsKey(directory)) continue;
                try
                {
                    var watcher = new FileSystemWatcher(directory)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    watcher.Renamed += (sender, e) => Dispatch(() => manager.OriginalPathChanged(e.OldFullPath, e.FullPath));
                    watcher.Deleted += (sender, e) => Dispatch(() => manager.OriginalPathChanged(e.FullPath, null));
                    watcher.Changed += (sender, e) => Dispatch(null);
                    watcher.Created += (sender, e) => Dispatch(null);
                    watcher.Error += (sender, e) => Dispatch(() => ErrorReporter.Write(e.GetException()));
                    watchers.Add(directory, watcher);
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception exception) { ErrorReporter.Write(exception); }
            }
        }

        private void Dispatch(Action action)
        {
            if (disposed) return;
            try
            {
                dispatcher.BeginInvoke((Action)(() =>
                {
                    if (disposed) return;
                    try { action?.Invoke(); }
                    catch (Exception exception) { ErrorReporter.Write(exception); }
                    debounce.Stop(); debounce.Start();
                }));
            }
            catch (InvalidOperationException) when (disposed || dispatcher.IsDisposed) { }
        }

        public void Dispose()
        {
            disposed = true; debounce.Dispose();
            foreach (var watcher in watchers.Values) watcher.Dispose();
            watchers.Clear();
        }
    }
}
