using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NoFences;
using NoFences.Model;

internal static class FenceShutdownTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int assertions;
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) => { Console.WriteLine(e.Exception); Environment.Exit(1); };
        var root = Path.Combine(Path.GetTempPath(), "NoFences-shutdown-" + Guid.NewGuid());
        var desktop = Path.Combine(root, "desktop");
        Directory.CreateDirectory(desktop);
        using (var parent = new Form { Text = "NoFences lifecycle test", ClientSize = new Size(640, 480),
            BackColor = Color.SlateGray, StartPosition = FormStartPosition.CenterScreen })
        {
            try
            {
                var assembly = typeof(FenceWindow).Assembly;
                assembly.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static).SetValue(null, parent.Handle);
                var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                    new object[] { Path.Combine(root, "storage"), desktop }, null);
                typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
                if (args.Length == 2 && args[0] == "--preview")
                {
                    RunPreview(parent, manager, root, desktop, args[1]);
                    return;
                }

                var info = CreateInfo(manager, root, "remove.txt");
                var originalPath = info.Files[0];
                var window = new FenceWindow(info);
                window.Show();
                typeof(FenceWindow).GetMethod("BeginTitleEdit", Instance).Invoke(window, null);
                var title = (Form)typeof(FenceWindow).GetField("titleLayer", Instance).GetValue(window);
                var editor = (Form)title.GetType().GetField("activeEditor", Instance).GetValue(title);
                var items = (Form)typeof(FenceWindow).GetField("itemLayer", Instance).GetValue(window);
                var handles = new[] { window.Handle, title.Handle, editor.Handle, items.Handle };
                window.FormClosed += (sender, e) => Check(File.Exists(originalPath) && !File.Exists(Path.Combine(desktop, "remove.txt")),
                    "partition closes before its item is restored");
                typeof(FenceWindow).GetMethod("RemoveFenceAndClose", Instance).Invoke(window, null);
                Check(handles.All(handle => !IsWindow(handle)), "body, title, items and title editor are destroyed");
                Check(File.Exists(Path.Combine(desktop, "remove.txt")) &&
                    !Directory.Exists(Path.Combine(root, "storage", info.Id.ToString())),
                    "successful removal restores item and deletes only fence metadata");

                var failedInfo = CreateInfo(manager, root, "locked.txt");
                var failed = new FenceWindow(failedInfo);
                failed.Show();
                using (var locked = new FileStream(failedInfo.Files[0], FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    try
                    {
                        typeof(FenceWindow).GetMethod("RemoveFenceAndClose", Instance).Invoke(failed, null);
                        throw new Exception("Expected locked-file removal to fail");
                    }
                    catch (TargetInvocationException exception) when (exception.InnerException is IOException) { }
                    var replacement = Application.OpenForms.OfType<FenceWindow>().Single();
                    Check(failed.IsDisposed && replacement.Visible && File.Exists(failedInfo.Files[0]),
                        "failed removal reopens remaining membership without losing the locked item");
                    replacement.Close();
                }
                manager.RemoveFence(failedInfo);

                var firstInfo = CreateInfo(manager, root, "exit-one.txt");
                var secondInfo = CreateInfo(manager, root, "exit-two.txt");
                var first = new FenceWindow(firstInfo);
                var second = new FenceWindow(secondInfo);
                first.Show(); second.Show();
                foreach (var form in new[] { first, second })
                    form.FormClosed += (sender, e) => Check(File.Exists(firstInfo.Files[0]) && File.Exists(secondInfo.Files[0]),
                        "all items remain stored during partition shutdown");
                assembly.GetType("NoFences.Util.FenceShutdown").GetMethod("CloseAllWindows", Static).Invoke(null, null);
                Check(first.IsDisposed && second.IsDisposed && !Application.OpenForms.OfType<FenceWindow>().Any(),
                    "all partitions close before exit recovery begins");
                Check(manager.ReleaseStoredFences().Count == 0 && File.Exists(Path.Combine(desktop, "exit-one.txt")) &&
                    File.Exists(Path.Combine(desktop, "exit-two.txt")), "exit recovery restores every item after window shutdown");
                Console.WriteLine("PASS: " + assertions + " shutdown assertions");
            }
            finally
            {
                foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) { fence.Close(); fence.Dispose(); }
                Directory.Delete(root, true);
            }
        }
    }

    private static void RunPreview(Form parent, FenceManager manager, string root, string desktop, string eventName)
    {
        using (var advance = new EventWaitHandle(false, EventResetMode.AutoReset, eventName))
        using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
        {
            var stage = 0;
            FenceWindow fence = null;
            parent.Shown += (sender, e) =>
            {
                var info = CreateInfo(manager, root, "示例文件.txt");
                var point = parent.PointToScreen(new Point(70, 70));
                info.PosX = point.X; info.PosY = point.Y;
                fence = new FenceWindow(info);
                fence.Show();
            };
            timer.Tick += (sender, e) =>
            {
                if (!advance.WaitOne(0)) return;
                if (stage++ == 0)
                {
                    typeof(FenceWindow).GetMethod("RemoveFenceAndClose", Instance).Invoke(fence, null);
                    Check(File.Exists(Path.Combine(desktop, "示例文件.txt")), "preview item restored after partition close");
                    parent.Text = "NoFences lifecycle test - restored";
                }
                else parent.Close();
            };
            timer.Start();
            Application.Run(parent);
            timer.Stop();
        }
    }

    private static FenceInfo CreateInfo(FenceManager manager, string root, string name)
    {
        var info = new FenceInfo(Guid.NewGuid()) { Name = "关闭顺序验证", Width = 330, Height = 300 };
        var source = Path.Combine(root, name);
        File.WriteAllText(source, "fixture data");
        info.Files.Add(manager.ImportItem(info, source));
        manager.UpdateFence(info);
        return info;
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        assertions++;
        Console.WriteLine("PASS: " + name);
    }
}
