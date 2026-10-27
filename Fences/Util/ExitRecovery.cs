using NoFences.Model;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NoFences.Util
{
    internal static class ExitRecovery
    {
        private const string WatchArgument = "--watch-exit";
        private const string RecoveryMutexName = "NoFences_ExitRecovery";

        public static bool RunIfRequested(string[] args)
        {
            if (args.Length != 3 || args[0] != WatchArgument)
                return false;

            using (var ready = EventWaitHandle.OpenExisting(args[2]))
            using (var recoveryMutex = new Mutex(false, RecoveryMutexName))
            using (var parent = Process.GetProcessById(int.Parse(args[1])))
            {
                // Open the process handle before acknowledging readiness, avoiding PID reuse.
                var parentHandle = parent.Handle;
                Acquire(recoveryMutex);
                try
                {
                    ready.Set();
                    parent.WaitForExit();
                    ReleaseItems();
                }
                finally
                {
                    recoveryMutex.ReleaseMutex();
                }
            }
            return true;
        }

        public static void Start()
        {
            // Wait for a previous session's recovery before loading or moving files.
            using (var recoveryMutex = new Mutex(false, RecoveryMutexName))
            {
                Acquire(recoveryMutex);
                try
                {
                    AppDataMigration.Migrate(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                    DesktopViewRecovery.Recover();
                }
                finally
                {
                    recoveryMutex.ReleaseMutex();
                }
            }

            var eventName = "NoFences_RecoveryReady_" + Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
            using (var parent = Process.GetCurrentProcess())
            using (var watcher = Process.Start(new ProcessStartInfo(
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), Program.AppName + ".Recovery.exe"),
                string.Format("{0} {1} {2}", WatchArgument, parent.Id, eventName))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                if (!ready.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new InvalidOperationException("无法启动桌面项目恢复守护进程，" + Program.AppName + " 未加载分区。");
            }
        }

        public static void ReleaseItems()
        {
            try
            {
                DesktopViewRecovery.Recover();
                var errors = FenceManager.Instance.ReleaseStoredFences();
                if (errors.Count > 0)
                    MessageBox.Show(string.Join(Environment.NewLine, errors), "无法释放部分桌面项目",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "无法释放桌面项目",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Acquire(Mutex mutex)
        {
            try
            {
                mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // Ownership is granted when the previous owner was forcibly terminated.
            }
        }
    }
}
