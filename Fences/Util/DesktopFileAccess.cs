using NoFences.Model;
using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace NoFences.Util
{
    internal sealed class DesktopFileAccess : IDisposable
    {
        private readonly Func<List<FenceWindow>> getFences;
        private readonly Action<string> reportError;
        private readonly Control dispatcher = new Control();
        private readonly Dictionary<DesktopAccessClient, Process> clients = new Dictionary<DesktopAccessClient, Process>();
        private readonly FileDialogMonitor monitor;
        private bool disposed;

        internal DesktopFileAccess(Func<List<FenceWindow>> getFences, Action<string> reportError)
        {
            this.getFences = getFences;
            this.reportError = reportError;
            var handle = dispatcher.Handle;
            monitor = new FileDialogMonitor();
            monitor.DialogShown += OnDialogShown;
        }

        internal void Start()
        {
            foreach (var client in getFences().SelectMany(window => window.Info.DesktopAccessClients).ToList())
            {
                if (clients.Keys.Any(existing => existing.Matches(client)))
                    continue;
                try
                {
                    var process = Process.GetProcessById(client.ProcessId);
                    if (process.StartTime.ToUniversalTime().Ticks != client.StartTimeUtcTicks)
                    {
                        process.Dispose();
                        OnClientExited(client);
                        continue;
                    }
                    Track(client, process);
                }
                catch (ArgumentException) { OnClientExited(client); }
                catch (InvalidOperationException) { OnClientExited(client); }
                catch (Win32Exception exception) { Report(exception); }
            }
            monitor.InspectExistingDialogs();
        }

        internal void Refresh()
        {
            if (disposed)
                return;
            monitor.InspectExistingDialogs();
            foreach (var client in clients.Keys.Where(client => !client.IsRunning()).ToList())
            {
                clients[client].Dispose();
                clients.Remove(client);
            }
            foreach (var fence in getFences())
            {
                fence.PrepareDesktopPathChange();
                try { Report(FenceManager.Instance.RefreshDesktopAccess(fence.Info)); }
                catch (Exception exception) { Report(exception); }
                fence.Refresh();
            }
        }

        private void OnDialogShown(IntPtr window, uint processId)
        {
            if (disposed || processId == Process.GetCurrentProcess().Id)
                return;
            try
            {
                var process = Process.GetProcessById((int)processId);
                var client = new DesktopAccessClient
                {
                    ProcessId = process.Id,
                    StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks
                };
                if (clients.Keys.Any(existing => existing.Matches(client)))
                    process.Dispose();
                else
                    Track(client, process);
                foreach (var fence in getFences())
                {
                    fence.PrepareDesktopPathChange();
                    try { Report(FenceManager.Instance.BeginDesktopAccess(fence.Info, client)); }
                    catch (Exception exception) { Report(exception); }
                    fence.Refresh();
                }
            }
            catch (ArgumentException) { } // The dialog's owning process already exited.
            catch (InvalidOperationException) { }
            catch (Win32Exception exception) { Report(exception); }
        }

        private void Track(DesktopAccessClient client, Process process)
        {
            clients.Add(client, process);
            process.Exited += (sender, e) =>
            {
                if (disposed)
                    return;
                try { dispatcher.BeginInvoke((Action)(() => OnClientExited(client))); }
                catch (InvalidOperationException) when (disposed || dispatcher.IsDisposed) { }
            };
            process.EnableRaisingEvents = true;
        }

        private void OnClientExited(DesktopAccessClient client)
        {
            if (disposed)
                return;
            if (clients.TryGetValue(client, out var process))
            {
                clients.Remove(client);
                process.Dispose();
            }
            foreach (var fence in getFences())
            {
                fence.PrepareDesktopPathChange();
                try { Report(FenceManager.Instance.EndDesktopAccess(fence.Info, client)); }
                catch (Exception exception) { Report(exception); }
                fence.Refresh();
            }
        }

        private void Report(IList<string> errors)
        {
            if (errors.Count != 0)
                Report(new System.IO.IOException(string.Join(Environment.NewLine, errors)));
        }

        private void Report(Exception exception)
        {
            ErrorReporter.Write(exception);
            reportError("部分桌面项目未能移动，文件仍保留在当前路径。\n" + exception.Message);
        }

        public void Dispose()
        {
            disposed = true;
            monitor.DialogShown -= OnDialogShown;
            monitor.Dispose();
            foreach (var process in clients.Values)
                process.Dispose();
            clients.Clear();
            dispatcher.Dispose();
        }
    }
}
