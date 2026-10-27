using Microsoft.Win32;
using NoFences.Model;
using NoFences.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NoFences.Util
{
    internal sealed class DesktopIntegration : IDisposable, IMessageFilter
    {
        private const uint NotificationMessage = 0x8000 + 117;
        private static DesktopIntegration current;
        private readonly FenceManager manager;
        private readonly Func<List<FenceWindow>> getFences;
        private readonly Action<string> reportError;
        private readonly string root;
        private readonly IntPtr testParent;
        private readonly string testFolder;
        private readonly Control dispatcher = new Control();
        private readonly Notifications notifications;
        private readonly OriginalPathMonitor monitor;
        private readonly System.Windows.Forms.Timer shellRetry = new System.Windows.Forms.Timer { Interval = 400 };
        private readonly System.Windows.Forms.Timer wallpaperRetry = new System.Windows.Forms.Timer();
        private int wallpaperAttempt;
        private static readonly int[] WallpaperDelays = { 150, 400, 1000, 2000 };
        private IntPtr context;
        private IntPtr viewWindow;
        private IntPtr originalIcons;
        private DesktopViewSession session;
        private DesktopViewSettings settings;
        private DesktopCaptionWindow captions;
        private DesktopRenameController rename;
        private string excludedSnapshot;
        private bool activated;
        private bool stopping;
        private bool recovering;
        private bool disposing;
        private int retryCount;

        internal DesktopIntegration(FenceManager manager, Func<List<FenceWindow>> getFences, Action<string> reportError,
            string root = null, IntPtr testParent = default(IntPtr), string testFolder = null)
        {
            this.manager = manager; this.getFences = getFences; this.reportError = reportError;
            this.root = root ?? AppDataPaths.RootPath; this.testParent = testParent; this.testFolder = testFolder;
            var handle = dispatcher.Handle;
            notifications = new Notifications(ReceiveNotification, () => BeginShellRecovery(), ScheduleWallpaperRefresh);
            monitor = new OriginalPathMonitor(manager, dispatcher, RefreshFenceWindows);
            manager.KeepOriginalPaths = true;
            manager.MembershipChanged += MembershipChanged;
            manager.BeforeShowingFences += BeforeShowingFences;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
            shellRetry.Tick += (sender, e) => RetryShell();
            wallpaperRetry.Tick += (sender, e) =>
            {
                wallpaperRetry.Stop();
                RefreshWallpaper();
                if (++wallpaperAttempt < WallpaperDelays.Length && !disposing && !stopping)
                {
                    wallpaperRetry.Interval = WallpaperDelays[wallpaperAttempt]; wallpaperRetry.Start();
                }
            };
            Application.AddMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message message) => !disposing && !stopping && context != IntPtr.Zero &&
            message.Msg >= 0x100 && message.Msg <= 0x109 &&
            DesktopViewNative.FencesTranslateKey(context, message.HWnd, (uint)message.Msg, message.WParam, message.LParam) == 0;

        internal void Start()
        {
            DesktopViewRecovery.RecoverFromRoot(root);
            var parent = testParent != IntPtr.Zero ? testParent : DesktopWindowHost.Handle;
            originalIcons = FindWindowEx(parent, IntPtr.Zero, "SysListView32", null);
            if (originalIcons == IntPtr.Zero) throw new InvalidOperationException("此 Windows 桌面没有可接管的原生图标层，Fences 未隐藏任何图标。");
        }

        private string MembershipSnapshot() => string.Join("\0", manager.OriginalPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) + "\0\0";

        private void CreateView()
        {
            // Load/migrate membership first so Shell enumerates only once.
            // Explorer remains visible until the filtered view is ready.
            var parent = testParent != IntPtr.Zero ? testParent : DesktopWindowHost.Handle;
            var initialSnapshot = MembershipSnapshot();
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesCreateFilteredView(parent, testFolder, testFolder == null,
                notifications.Handle, NotificationMessage, initialSnapshot, (uint)initialSnapshot.Length, out context));
            viewWindow = DesktopViewNative.FencesViewWindow(context);
            try
            {
                var timeout = Stopwatch.StartNew();
                int status;
                while ((status = DesktopViewNative.FencesViewStatus(context)) == unchecked((int)0x8000000A) && timeout.ElapsedMilliseconds < 8000)
                {
                    Application.DoEvents(); Thread.Sleep(10);
                }
                if (timeout.ElapsedMilliseconds >= 8000) throw new TimeoutException("原生桌面视图未能及时加载，原桌面保持可用。");
                Marshal.ThrowExceptionForHR(status);
                var path = Path.Combine(root, DesktopViewRecovery.SettingsFileName);
                settings = File.Exists(path) ? DesktopViewRecovery.ReadXml<DesktopViewSettings>(path)
                    : new DesktopViewSettings { ShowIcons = DesktopViewRecovery.IsWindowVisible(originalIcons) };
                if (DesktopViewNative.FencesOriginalMetrics(context, out var originalSize, out var originalSpacing) == 0)
                    settings.IconSize = originalSize;
                var native = settings.ToNative();
                Marshal.ThrowExceptionForHR(DesktopViewNative.FencesSetPreferences(context, ref native));
                captions = new DesktopCaptionWindow(context, FailView);
                captions.AttachToView();
                rename = new DesktopRenameController(context, error => { ErrorReporter.Write(error); reportError(error.Message); });
                excludedSnapshot = initialSnapshot;
                current = this;
            }
            catch { StopOverlay(); throw; }
        }

        private void BeforeShowingFences(IList<FenceInfo> fences)
        {
            if (context == IntPtr.Zero) CreateView();
            MembershipChanged();
            if (activated || context == IntPtr.Zero) return;
            // Journal before touching Explorer, so the exit watcher can undo a crash here.
            session = DesktopViewRecovery.Record(originalIcons, root);
            var window = DesktopViewNative.FencesViewWindow(context);
            DesktopViewRecovery.ShowWindow(originalIcons, 0);
            DesktopViewRecovery.ShowWindow(window, 8);
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesResizeView(context));
            activated = true;
            RestorePositions();
            captions.RefreshLabels();
        }

        private void MembershipChanged()
        {
            if (disposing || stopping) return;
            try
            {
                var snapshot = MembershipSnapshot();
                if (context != IntPtr.Zero && snapshot != excludedSnapshot)
                {
                    Marshal.ThrowExceptionForHR(DesktopViewNative.FencesExcludePaths(context, snapshot, (uint)snapshot.Length));
                    excludedSnapshot = snapshot;
                }
                monitor.Synchronize();
            }
            catch (Exception exception) { FailView(exception); }
        }

        internal void Refresh()
        {
            if (context == IntPtr.Zero)
            {
                Start();
                var errors = manager.LoadFences();
                if (errors.Count != 0) reportError(string.Join(Environment.NewLine, errors));
            }
            MembershipChanged();
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesRefreshView(context));
            RefreshFenceWindows();
        }

        private void RefreshFenceWindows()
        {
            if (disposing || stopping) return;
            foreach (var fence in getFences())
            {
                if (fence.IsDisposed) continue;
                fence.PrepareDesktopPathChange(); fence.Refresh();
            }
        }

        private void ReceiveNotification(uint kind, IntPtr source)
        {
            if (disposing || stopping || source != viewWindow) return;
            if (kind == 2) { BeginShellRecovery(); return; }
            if (context == IntPtr.Zero) return;
            if (kind == 1)
            {
                try { SavePreferences(); }
                catch (Exception exception) { ErrorReporter.Write(exception); reportError("桌面显示设置未能保存：" + exception.Message); }
            }
            if (kind == 4) RestorePositions();
            if (kind == 5 && activated) captions?.ScheduleRefresh();
            if (kind == 6 && activated) captions?.InvalidateBackground();
            if (kind == 7) ScheduleWallpaperRefresh();
            if (kind == 8 && activated) rename?.Begin();
            if (kind == 3) FailView(new InvalidOperationException("原生桌面视图发生错误，已恢复 Windows 桌面。可从托盘选择“刷新”重试。"));
        }

        private void SavePreferences()
        {
            if (!activated || settings == null) return;
            var preferences = DesktopPreferences.Empty;
            Marshal.ThrowExceptionForHR(DesktopViewNative.FencesGetPreferences(context, ref preferences));
            var value = DesktopViewSettings.FromNative(preferences);
            if (!value.AutoArrange)
            {
                var count = DesktopViewNative.FencesVisibleCount(context);
                for (var index = 0; index < count; ++index)
                {
                    var name = new StringBuilder(32768);
                    if (DesktopViewNative.FencesItemPosition(context, index, name, (uint)name.Capacity, out var point) < 0) continue;
                    value.Positions.Add(new DesktopIconPosition { Name = name.ToString(), X = point.X, Y = point.Y });
                }
            }
            var old = settings.ToNative();
            var positionsEqual = settings.Positions.Count == value.Positions.Count && settings.Positions.Zip(value.Positions,
                (first, second) => first.Name == second.Name && first.X == second.X && first.Y == second.Y).All(equal => equal);
            if (old.Flags == (preferences.Flags & 0x1005u) && old.IconSize == preferences.IconSize &&
                old.SortFormat == preferences.SortFormat && old.SortProperty == preferences.SortProperty &&
                old.SortDirection == preferences.SortDirection && positionsEqual) return;
            DesktopViewRecovery.WriteXml(Path.Combine(root, DesktopViewRecovery.SettingsFileName), value);
            settings = value;
        }

        private void RestorePositions()
        {
            if (!activated || settings == null || settings.AutoArrange || context == IntPtr.Zero) return;
            foreach (var position in settings.Positions)
                DesktopViewNative.FencesSetItemPosition(context, position.Name, new Point(position.X, position.Y));
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            if (disposing) return;
            try
            {
                dispatcher.BeginInvoke((Action)(() =>
                {
                    if (!disposing && context != IntPtr.Zero)
                    {
                        DesktopViewNative.FencesResizeView(context);
                    }
                }));
            }
            catch (InvalidOperationException) when (disposing || dispatcher.IsDisposed) { }
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (disposing || (e.Category != UserPreferenceCategory.Desktop && e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle && e.Category != UserPreferenceCategory.Icon &&
                e.Category != UserPreferenceCategory.General)) return;
            try
            {
                dispatcher.BeginInvoke((Action)(() =>
                {
                    if (!disposing && context != IntPtr.Zero)
                    {
                        if (e.Category == UserPreferenceCategory.Icon || e.Category == UserPreferenceCategory.VisualStyle)
                            DesktopViewNative.FencesRefreshView(context);
                    }
                }));
            }
            catch (InvalidOperationException) when (disposing || dispatcher.IsDisposed) { }
        }

        private void RefreshWallpaper()
        {
            if (!disposing && !stopping && context != IntPtr.Zero)
                DesktopViewNative.FencesRefreshBackground(context);
        }

        private void ScheduleWallpaperRefresh()
        {
            if (disposing || stopping) return;
            RefreshWallpaper();
            // Shell slideshow commands finish asynchronously. These bounded
            // follow-ups end after this change, rather than polling the desktop.
            wallpaperRetry.Stop(); wallpaperAttempt = 0;
            wallpaperRetry.Interval = WallpaperDelays[0]; wallpaperRetry.Start();
        }

        private void BeginShellRecovery()
        {
            if (recovering || disposing || stopping || testParent != IntPtr.Zero) return;
            recovering = true; retryCount = 0;
            FenceShutdown.CloseAllWindows(); StopOverlay();
            DesktopWindowHost.Reset(); shellRetry.Start();
        }

        private void RetryShell()
        {
            shellRetry.Stop();
            try
            {
                Start();
                var errors = manager.LoadFences();
                if (errors.Count != 0) reportError(string.Join(Environment.NewLine, errors));
                recovering = false;
            }
            catch (Exception exception)
            {
                if (++retryCount < 5) shellRetry.Start();
                else { recovering = false; ErrorReporter.Write(exception); reportError("Explorer 重启后桌面视图未能重建，原桌面仍可用。请选择“刷新”重试。"); }
            }
        }

        private void FailView(Exception exception)
        {
            ErrorReporter.Write(exception);
            if (disposing || stopping) return;
            try
            {
                var expectedWindow = viewWindow;
                dispatcher.BeginInvoke((Action)(() =>
                {
                    if (disposing || stopping || viewWindow != expectedWindow) return;
                    FenceShutdown.CloseAllWindows(); StopOverlay();
                    reportError(exception.Message);
                }));
            }
            catch (InvalidOperationException) when (disposing || dispatcher.IsDisposed) { }
        }

        private void StopOverlay()
        {
            stopping = true;
            wallpaperRetry.Stop();
            try
            {
                rename?.Dispose(); rename = null;
                captions?.Dispose(); captions = null;
                if (context != IntPtr.Zero)
                {
                    var previous = context; context = IntPtr.Zero; viewWindow = IntPtr.Zero;
                    DesktopViewNative.FencesDestroyView(previous);
                }
            }
            finally
            {
                if (session != null) DesktopViewRecovery.Restore(session, root);
                session = null; activated = false; excludedSnapshot = null; stopping = false;
                if (current == this) current = null;
            }
        }

        internal static bool IsBackgroundPoint(Point point) => current != null && current.context != IntPtr.Zero &&
            DesktopViewNative.FencesIsBackgroundPoint(current.context, point);

        public void Dispose()
        {
            if (disposing) return;
            disposing = true;
            Application.RemoveMessageFilter(this);
            manager.MembershipChanged -= MembershipChanged;
            manager.BeforeShowingFences -= BeforeShowingFences;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
            wallpaperRetry.Stop(); wallpaperRetry.Dispose();
            shellRetry.Dispose(); monitor.Dispose();
            FenceShutdown.CloseAllWindows(); StopOverlay();
            notifications.Dispose(); dispatcher.Dispose();
        }

        private sealed class Notifications : NativeWindow, IDisposable
        {
            private readonly Action<uint, IntPtr> callback;
            private readonly Action shellRestart;
            private readonly Action wallpaperChanged;
            private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            internal Notifications(Action<uint, IntPtr> callback, Action shellRestart, Action wallpaperChanged)
            {
                this.callback = callback; this.shellRestart = shellRestart; this.wallpaperChanged = wallpaperChanged;
                CreateHandle(new CreateParams { Caption = "Fences Desktop Notifications", Style = unchecked((int)0x80000000), ExStyle = 0x80 });
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == NotificationMessage) callback((uint)message.WParam.ToInt64(), message.LParam);
                if (message.Msg == taskbarCreated) shellRestart();
                // SPI_SETDESKWALLPAPER only. Theme, color, icon and generic
                // setting broadcasts must not reload the desktop wallpaper.
                if (message.Msg == 0x1A && message.WParam.ToInt64() == 0x14) wallpaperChanged();
                base.WndProc(ref message);
            }
            public void Dispose() { DestroyHandle(); }
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string title);
    }
}
