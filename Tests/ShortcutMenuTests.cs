using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NoFences;
using NoFences.Model;

internal static class ShortcutMenuTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static int assertions;
    private static readonly Type Shortcut = typeof(FenceWindow).Assembly.GetType("NoFences.Util.ShellShortcut");

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) => { Console.WriteLine(e.Exception); Environment.Exit(1); };
        var root = Path.Combine(Path.GetTempPath(), "Fences-shortcuts-" + Guid.NewGuid());
        var targets = Path.Combine(root, "中文 targets, with spaces");
        Directory.CreateDirectory(targets);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            var executable = Path.Combine(targets, "sample.exe");
            var document = Path.Combine(targets, "document.txt");
            var folder = Path.Combine(targets, "目标文件夹");
            File.WriteAllText(executable, "not executed");
            File.WriteAllText(document, "document");
            Directory.CreateDirectory(folder);
            var exeLink = CreateLink(shell, Path.Combine(root, "程序.lnk"), executable);
            var docLink = CreateLink(shell, Path.Combine(root, "文档.LNK"), document);
            var folderLink = CreateLink(shell, Path.Combine(root, "文件夹.lnk"), folder);
            var broken = CreateLink(shell, Path.Combine(root, "失效.lnk"), Path.Combine(targets, "missing.exe"));
            var corrupt = Path.Combine(root, "corrupt.lnk");
            File.WriteAllText(corrupt, "not a Shell link");
            Check(Resolve(exeLink) == executable && Resolve(docLink) == document && Resolve(folderLink) == folder,
                "shared resolver finds file/folder targets, including Unicode, spaces and uppercase extensions");
            Check(Resolve(broken) == null && Resolve(corrupt) == null && Resolve(document) == null,
                "missing/corrupt shortcuts and ordinary files do not produce target-location actions");

            var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                new object[] { Path.Combine(root, "storage"), root }, null);
            typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
            using (var host = new Form())
            {
                typeof(FenceWindow).Assembly.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static)
                    .SetValue(null, host.Handle);
                var info = new FenceInfo(Guid.NewGuid()) { Name = "快捷方式菜单", Width = 350, Height = 300 };
                info.Files.AddRange(new[] { exeLink, docLink, folderLink });
                manager.UpdateFence(info);
                using (var window = new FenceWindow(info))
                {
                    window.Show();
                    CheckMenu(window, exeLink, "打开文件所在位置", true, "shortcut-executable-menu.png");
                    CheckMenu(window, docLink, "打开文件所在位置", false, "shortcut-document-menu.png");
                    CheckMenu(window, folderLink, "打开文件夹所在位置", false, "shortcut-folder-menu.png");
                    CheckMenu(window, document, null, false, null);
                    CheckMenu(window, broken, null, false, null);
                    CheckMenu(window, corrupt, null, false, null);
                    CheckMenu(window, null, null, false, null);
                    window.Close();
                }
            }
            if (args.Contains("--explorer"))
                ExplorerChecks(exeLink, folderLink, executable, folder, targets);
            Check(File.ReadAllText(executable) == "not executed", "target-location inspection never executes or alters the shortcut target");
            Console.WriteLine("PASS: " + assertions + " shortcut assertions");
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
            Directory.Delete(root, true);
        }
    }

    private static string CreateLink(dynamic shell, string path, string target)
    {
        dynamic link = shell.CreateShortcut(path);
        try { link.TargetPath = target; link.Arguments = "--unused-argument"; link.Save(); }
        finally { Marshal.FinalReleaseComObject(link); }
        return path;
    }

    private static string Resolve(string path) => (string)Invoke(Shortcut.GetMethod("GetExistingTargetPath", Static), null, path);

    private static void CheckMenu(FenceWindow window, string path, string expectedText, bool admin, string imageName)
    {
        typeof(FenceWindow).GetField("contextMenuItem", Instance).SetValue(window, path);
        var menu = (ContextMenuStrip)typeof(FenceWindow).GetField("appContextMenu", Instance).GetValue(window);
        var location = (ToolStripMenuItem)typeof(FenceWindow).GetField("openShortcutLocationMenuItem", Instance).GetValue(window);
        var elevation = (ToolStripMenuItem)typeof(FenceWindow).GetField("runAsAdministratorMenuItem", Instance).GetValue(window);
        menu.Show(window, new Point(30, 80));
        Application.DoEvents();
        Check(location.Visible == (expectedText != null) && (expectedText == null || location.Text == expectedText),
            "appropriate target-location text/visibility: " + (Path.GetFileName(path) ?? "background"));
        Check(elevation.Visible == admin, "elevation availability stays unchanged: " + (Path.GetFileName(path) ?? "background"));
        if (expectedText != null)
            Check(menu.Items.IndexOf(location) + 1 == menu.Items.IndexOf(elevation), "target location precedes Run as administrator");
        if (imageName != null)
        {
            var output = Path.GetFullPath(Path.Combine("Fences/obj/VisualChecks", imageName));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (var image = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                image.Save(output);
            }
        }
        menu.Close();
    }

    private static void ExplorerChecks(string fileLink, string folderLink, string file, string folder, string parent)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        dynamic windows = shell.Windows();
        var existingHandles = new HashSet<long>();
        for (var i = 0; i < windows.Count; i++)
        {
            dynamic window = windows.Item(i);
            try { existingHandles.Add(Convert.ToInt64(window.HWND)); }
            finally { Marshal.FinalReleaseComObject(window); }
        }
        try
        {
            foreach (var pair in new[] { new[] { fileLink, file }, new[] { folderLink, folder } })
            {
                Invoke(Shortcut.GetMethod("OpenTargetLocation", Static), null, pair[0]);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var selected = false;
                while (!selected && watch.ElapsedMilliseconds < 5000)
                {
                    for (var i = 0; i < windows.Count; i++)
                    {
                        dynamic window = windows.Item(i);
                        try
                        {
                            if (!string.Equals((string)window.Document.Folder.Self.Path, parent, StringComparison.OrdinalIgnoreCase))
                                continue;
                            dynamic selection = window.Document.SelectedItems();
                            try
                            {
                                for (var j = 0; j < selection.Count; j++)
                                {
                                    dynamic item = selection.Item(j);
                                    try { selected |= string.Equals((string)item.Path, pair[1], StringComparison.OrdinalIgnoreCase); }
                                    finally { Marshal.FinalReleaseComObject(item); }
                                }
                            }
                            finally { Marshal.FinalReleaseComObject(selection); }
                        }
                        catch (Exception) { /* Explorer may still be navigating. */ }
                        finally { Marshal.FinalReleaseComObject(window); }
                    }
                    if (!selected) { Application.DoEvents(); Thread.Sleep(50); }
                }
                Check(selected, "Explorer opens the actual parent and selects the target: " + Path.GetFileName(pair[1]));
            }
        }
        finally
        {
            for (var i = windows.Count - 1; i >= 0; i--)
            {
                dynamic window = windows.Item(i);
                try
                {
                    if (!existingHandles.Contains(Convert.ToInt64(window.HWND)) &&
                        string.Equals((string)window.Document.Folder.Self.Path, parent, StringComparison.OrdinalIgnoreCase))
                        window.Quit();
                }
                catch (Exception) { }
                finally { Marshal.FinalReleaseComObject(window); }
            }
            Marshal.FinalReleaseComObject(windows);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static object Invoke(MethodInfo method, object target, params object[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); return null; }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
        Console.WriteLine("PASS: " + description);
    }
}
