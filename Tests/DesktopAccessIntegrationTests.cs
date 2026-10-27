using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NoFences;
using NoFences.Model;

internal static class DesktopAccessIntegrationTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) => { Console.WriteLine(e.Exception); Environment.Exit(1); };
        if (args.Length > 0 && args[0] == "--dialog")
        {
            RunDialog(args);
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "NoFences-integration-" + Guid.NewGuid());
        var desktop = Path.Combine(root, "desktop");
        Directory.CreateDirectory(desktop);
        var prefix = "NoFences_Access_Integration_" + Guid.NewGuid().ToString("N");
        using (var dialogClosed = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "_closed"))
        using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "_stop"))
        using (var parent = new Form())
        {
            Process child = null;
            IDisposable access = null;
            var fences = new List<FenceWindow>();
            try
            {
                var assembly = typeof(FenceWindow).Assembly;
                assembly.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static).SetValue(null, parent.Handle);
                var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                    new object[] { Path.Combine(root, "storage"), desktop }, null);
                typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
                var info = new FenceInfo(Guid.NewGuid()) { Name = "Test", Width = 300, Height = 300 };
                var source = Path.Combine(root, "sample.txt");
                File.WriteAllText(source, "integration data");
                info.Files.Add(manager.ImportItem(info, source));
                manager.UpdateFence(info);
                fences.Add(new FenceWindow(info));
                fences[0].Show();
                var errors = new List<string>();
                var serviceType = assembly.GetType("NoFences.Util.DesktopFileAccess");
                Func<List<FenceWindow>> getFences = () => fences.ToList();
                Action<string> report = message => errors.Add(message);
                access = (IDisposable)Activator.CreateInstance(serviceType, Instance, null,
                    new object[] { getFences, report }, null);
                serviceType.GetMethod("Start", Instance).Invoke(access, null);
                child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                    "--dialog " + prefix + " \"" + desktop + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                PumpUntil(() => dialogClosed.WaitOne(0), "file-dialog helper sees the restored file and closes");
                Check(File.Exists(Path.Combine(desktop, "sample.txt")) && !child.HasExited,
                    "dialog closure keeps the real file available while the consuming app lives");
                RefreshFromTray(access, false);
                Check(File.Exists(Path.Combine(desktop, "sample.txt")),
                    "tray refresh preserves paths used by a live consuming app");

                // Simulate restarting NoFences while the consumer is still using the path.
                access.Dispose();
                access = null;
                fences[0].Close();
                fences[0].Dispose();
                fences.Clear();
                var loadErrors = manager.LoadFences();
                Check(loadErrors.Count == 0, "fences reload during an active desktop lease");
                fences.AddRange(Application.OpenForms.OfType<FenceWindow>().ToList());
                Check(fences.Count == 1 && File.Exists(Path.Combine(desktop, "sample.txt")),
                    "restart does not reclaim a path still used by the consuming app");
                access = (IDisposable)Activator.CreateInstance(serviceType, Instance, null,
                    new object[] { getFences, report }, null);
                serviceType.GetMethod("Start", Instance).Invoke(access, null);
                stop.Set();
                PumpUntil(() => child.HasExited && !File.Exists(Path.Combine(desktop, "sample.txt")),
                    "process-exit event automatically returns the file to its fence");
                var reloadedInfo = (FenceInfo)typeof(FenceWindow).GetProperty("Info", Instance).GetValue(fences[0]);
                Check(reloadedInfo.DesktopAccessClients.Count == 0 && File.ReadAllText(reloadedInfo.Files[0]) == "integration data",
                    "restored membership and data survive the full lifecycle");
                Check(errors.Count == 0, "no file-access errors were reported");

                var expiredClient = new DesktopAccessClient { ProcessId = child.Id, StartTimeUtcTicks = 1 };
                var begin = typeof(FenceManager).GetMethod("BeginDesktopAccess", Instance);
                begin.Invoke(manager, new object[] { reloadedInfo, expiredClient });
                var restored = Path.Combine(desktop, "sample.txt");
                using (var locked = new FileStream(restored, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    RefreshFromTray(access, true);
                    Check(File.Exists(restored) && errors.Count == 1,
                        "refresh reports a locked-file failure and preserves the desktop file");
                    Check(reloadedInfo.DesktopAccessClients.Count == 0,
                        "refresh removes an expired client without a process-exit notification");
                }
                errors.Clear();
                RefreshFromTray(access, false);
                Check(!File.Exists(restored) && File.ReadAllText(reloadedInfo.Files[0]) == "integration data",
                    "tray refresh retries reclaim after the file lock is released");
                RefreshFromTray(access, false);
                Check(errors.Count == 0 && File.Exists(reloadedInfo.Files[0]),
                    "repeated tray refresh is safe after successful reclaim");
            }
            finally
            {
                stop.Set();
                access?.Dispose();
                foreach (var fence in fences) { fence.Close(); fence.Dispose(); }
                if (child != null)
                {
                    if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
                    child.Dispose();
                }
                Directory.Delete(root, true);
            }
        }
    }

    private static void RefreshFromTray(IDisposable access, bool render)
    {
        var type = typeof(FenceWindow).Assembly.GetType("NoFences.TrayApplicationContext");
        using (var tray = (ApplicationContext)Activator.CreateInstance(type))
        {
            var service = type.GetField("desktopFileAccess", Instance);
            service.SetValue(tray, access);
            try
            {
                var menu = (ContextMenuStrip)type.GetField("contextMenu", Instance).GetValue(tray);
                var refresh = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "刷新");
                if (render)
                {
                    menu.Show(new Point(40, 40));
                    Application.DoEvents();
                    var directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "VisualChecks");
                    Directory.CreateDirectory(directory);
                    using (var bitmap = new Bitmap(menu.Width, menu.Height))
                    {
                        menu.DrawToBitmap(bitmap, menu.ClientRectangle);
                        bitmap.Save(Path.Combine(directory, "tray-refresh-menu.png"));
                    }
                    menu.Close();
                }
                refresh.PerformClick();
            }
            finally { service.SetValue(tray, null); }
        }
    }

    private static void RunDialog(string[] args)
    {
        using (var closed = EventWaitHandle.OpenExisting(args[1] + "_closed"))
        using (var stop = EventWaitHandle.OpenExisting(args[1] + "_stop"))
        using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
        {
            var timeout = Stopwatch.StartNew();
            timer.Tick += (sender, e) =>
            {
                if (!File.Exists(Path.Combine(args[2], "sample.txt")) && timeout.ElapsedMilliseconds < 7000)
                    return;
                EnumThreadWindows(GetCurrentThreadId(), (window, parameter) =>
                {
                    var name = new StringBuilder(256);
                    GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "#32770") PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            };
            timer.Start();
            using (var dialog = new OpenFileDialog { InitialDirectory = args[2] }) dialog.ShowDialog();
            timer.Stop();
            if (!File.Exists(Path.Combine(args[2], "sample.txt"))) Environment.Exit(2);
            closed.Set();
            stop.WaitOne(TimeSpan.FromSeconds(15));
        }
    }

    private static void PumpUntil(Func<bool> condition, string name)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.ElapsedMilliseconds < 10000)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(condition(), name);
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
