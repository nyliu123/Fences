using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using NoFences;
using NoFences.Model;

// Compile against the built Fences.exe and run on Windows with .NET Framework 4.8.
internal static class DragDropAndElevationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [STAThread]
    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "NoFences-regression-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var assembly = typeof(FenceWindow).Assembly;
            CheckDragGhost(assembly);
            var hostType = assembly.GetType("NoFences.Win32.DesktopWindowHost");
            var hostField = hostType.GetField("desktopHost", BindingFlags.Static | BindingFlags.NonPublic);
            var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), PrivateInstance,
                null, new object[] { Path.Combine(root, "storage"), root }, null);
            typeof(FenceManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, manager);
            using (var parent = new Form())
            {
                // Use a hidden test parent, never the user's Explorer desktop.
                hostField.SetValue(null, parent.Handle);
                var info = new FenceInfo(Guid.NewGuid()) { Name = "Test", Width = 300, Height = 300 };
                using (var window = new FenceWindow(info))
                {
                    var busy = Path.Combine(root, "busy.exe");
                    File.WriteAllText(busy, "locked file");
                    using (var stream = new FileStream(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        var watch = Stopwatch.StartNew();
                        var enter = Drag(busy);
                        Invoke(window, "FenceWindow_DragEnter", null, enter);
                        Check(enter.Effect == DragDropEffects.Move && File.Exists(busy), "hover leaves busy source in place");
                        Invoke(window, "FenceWindow_DragLeave", null, EventArgs.Empty);
                        Check(File.Exists(busy) && info.Files.Count == 0, "leaving does not change files or metadata");
                        var drop = Drag(busy);
                        Invoke(window, "FenceWindow_DragDrop", null, drop);
                        Check(drop.Effect == DragDropEffects.None && File.Exists(busy) && info.Files.Count == 0,
                            "busy drop fails without moving source or changing metadata");
                        Check(watch.Elapsed < TimeSpan.FromSeconds(5), "busy callbacks return without a modal dialog");

                        var free = Path.Combine(root, "free.txt");
                        File.WriteAllText(free, "rollback me");
                        var batch = Drag(free, busy);
                        Invoke(window, "FenceWindow_DragDrop", null, batch);
                        Check(batch.Effect == DragDropEffects.None && File.Exists(free) && File.Exists(busy)
                            && info.Files.Count == 0, "batch failure restores earlier moves");
                    }

                    var success = Drag(busy);
                    Invoke(window, "FenceWindow_DragDrop", null, success);
                    Check(success.Effect == DragDropEffects.Move && !File.Exists(busy) && info.Files.Count == 1
                        && File.Exists(info.Files[0]), "unlocked drop moves and records file");
                    using (var stream = new FileStream(info.Files[0], FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        typeof(FenceWindow).GetField("activeDragItem", PrivateInstance).SetValue(window, info.Files[0]);
                        var reorder = Drag(info.Files[0]);
                        Invoke(window, "FenceWindow_DragEnter", null, reorder);
                        Check(File.Exists(info.Files[0]), "local reorder does not move a busy file");
                        Invoke(window, "FenceWindow_DragLeave", null, EventArgs.Empty);
                        typeof(FenceWindow).GetField("activeDragItem", PrivateInstance).SetValue(window, null);
                    }
                    var copyOnly = new DragEventArgs(new DataObject(DataFormats.FileDrop, new[] { info.Files[0] }),
                        0, 0, 100, DragDropEffects.Copy, DragDropEffects.None);
                    Invoke(window, "FenceWindow_DragEnter", null, copyOnly);
                    Check(copyOnly.Effect == DragDropEffects.None, "copy-only source is not moved");
                    // Do not pump messages: failure dialogs are deliberately queued until after Drop returns.
                }
            }

            var launcher = assembly.GetType("NoFences.Util.ExecutableLauncher");
            var create = launcher.GetMethod("CreateStartInfo", BindingFlags.Static | BindingFlags.NonPublic);
            var executable = Path.Combine(root, "app.EXE");
            File.WriteAllText(executable, "test target");
            var direct = (ProcessStartInfo)create.Invoke(null, new object[] { executable });
            Check(direct.UseShellExecute && direct.Verb == "runas" && direct.FileName == executable,
                "executables request Shell elevation");
            var linkPath = Path.Combine(root, "app.lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic link = shell.CreateShortcut(linkPath);
            try
            {
                link.TargetPath = executable;
                link.Arguments = "--name \"two words\"";
                link.WorkingDirectory = root;
                link.Save();
                var linked = (ProcessStartInfo)create.Invoke(null, new object[] { linkPath });
                Check(linked.FileName == executable && linked.Arguments == "--name \"two words\""
                    && linked.WorkingDirectory == root && linked.Verb == "runas", "shortcut preserves target, arguments and directory");
                link.TargetPath = Path.Combine(root, "free.txt");
                link.Save();
                Check(create.Invoke(null, new object[] { linkPath }) == null, "document shortcuts do not offer elevation");
                link.TargetPath = Path.Combine(root, "missing.exe");
                link.Save();
                Check(create.Invoke(null, new object[] { linkPath }) == null, "broken shortcuts do not offer elevation");
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            Check(create.Invoke(null, new object[] { root }) == null, "folders do not offer elevation");
            Console.WriteLine("PASS: " + assertions + " assertions; no elevation prompt was launched.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static DragEventArgs Drag(params string[] paths)
    {
        return new DragEventArgs(new DataObject(DataFormats.FileDrop, paths), 0, 50, 100,
            DragDropEffects.Move, DragDropEffects.None);
    }

    private static void CheckDragGhost(Assembly assembly)
    {
        var type = assembly.GetType("NoFences.DragGhostWindow");
        var bitmap = new Bitmap(48, 48);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.OrangeRed);
        var offset = new Point(8, 8);
        using (var ghost = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { bitmap, offset }, null))
        {
            var timer = (System.Windows.Forms.Timer)type.GetField("follow", PrivateInstance).GetValue(ghost);
            var observedTracking = false;
            timer.Tick += (sender, e) =>
            {
                // Sample in the same tick as the production handler: the user
                // may still be moving the mouse between separate test statements.
                var current = Control.MousePosition;
                if (ghost.Location != new Point(-1000, -1000) &&
                    Math.Abs(ghost.Left - current.X + offset.X) <= 3 && Math.Abs(ghost.Top - current.Y + offset.Y) <= 3)
                    observedTracking = true;
            };
            ghost.Show();
            // Deliberately displace it without sending GiveFeedback. The timer
            // must continue tracking even when another Shell target owns drag feedback.
            ghost.Location = new Point(-1000, -1000);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 150) { Application.DoEvents(); Thread.Sleep(5); }
            Check(observedTracking && ghost.TopLevel && ghost.TopMost,
                "drag ghost follows the cursor outside its source without GiveFeedback and remains a top-level overlay");
            Check(WindowFromPoint(new Point(ghost.Left + 20, ghost.Top + 20)) != ghost.Handle, "drag image does not intercept destination hit testing");
            ghost.Hide();
            Check(!timer.Enabled, "hiding the drag image before destination hit testing stops tracking");
            ghost.Close(); ghost.Dispose();
            Check(!timer.Enabled, "closing the drag ghost stops its tracking timer");
        }
    }

    private static void Invoke(FenceWindow window, string method, params object[] args)
    {
        typeof(FenceWindow).GetMethod(method, PrivateInstance).Invoke(window, args);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            Console.Error.WriteLine("FAIL: " + message);
            throw new Exception("FAIL: " + message);
        }
        assertions++;
        Console.WriteLine("PASS: " + message);
    }
}
