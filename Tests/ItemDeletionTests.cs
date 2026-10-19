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
using System.Xml.Serialization;
using NoFences;
using NoFences.Model;

internal static class ItemDeletionTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static int assertions;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) => { Console.WriteLine(e.Exception); Environment.Exit(1); };
        var root = Path.Combine(Path.GetTempPath(), "Fences-delete-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                new object[] { Path.Combine(root, "storage"), root }, null);
            typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
            var info = new FenceInfo(Guid.NewGuid()) { Name = "删除菜单验证", Width = 350, Height = 300 };
            foreach (var name in new[] { "first.txt", "second.txt", "third.txt" })
            {
                var source = Path.Combine(root, name);
                File.WriteAllText(source, name);
                info.Files.Add(manager.ImportItem(info, source));
            }
            manager.UpdateFence(info);
            var original = info.Files.ToArray();
            var metadata = Path.Combine(root, "storage", info.Id.ToString(), "__fence_metadata.xml");
            var called = false;
            using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Expect<IOException>(() => Recycle(manager, info, original[1], path => called = true));
                Check(!called && info.Files.SequenceEqual(original) && File.Exists(original[1]),
                    "metadata write failure prevents recycling and preserves membership/order");
            }
            Expect<OperationCanceledException>(() => Recycle(manager, info, original[1], path => { throw new OperationCanceledException(); }));
            Check(info.Files.SequenceEqual(original) && Read(metadata).Files.SequenceEqual(original),
                "cancellation restores both live and persisted membership");
            Expect<IOException>(() => Recycle(manager, info, original[1], path => { throw new IOException("locked"); }));
            Check(info.Files.SequenceEqual(original) && File.Exists(original[1]), "recycle failure keeps the actual item and its order");
            Expect<IOException>(() => Recycle(manager, info, original[1], path => { }));
            Check(Read(metadata).Files.SequenceEqual(original), "a recycler that skips the item does not discard metadata");

            var recycled = Path.Combine(root, "test-trash.txt");
            Recycle(manager, info, original[1], path => File.Move(path, recycled));
            Check(info.Files.SequenceEqual(new[] { original[0], original[2] }) && File.Exists(recycled),
                "successful recycling removes only the target membership");
            Check(!Read(metadata).Files.Contains(original[1]), "deleted membership is durably saved");
            Check(manager.ReleaseStoredFences().Count == 0 && !File.Exists(Path.Combine(root, "second.txt")),
                "exit recovery does not restore a successfully deleted item");

            var foreign = Path.Combine(root, "foreign.txt");
            File.WriteAllText(foreign, "unrelated");
            called = false;
            Expect<FileNotFoundException>(() => Recycle(manager, info, foreign, path => called = true));
            Check(!called && File.ReadAllText(foreign) == "unrelated", "paths outside the Fence membership cannot be deleted");

            var borrowed = Path.Combine(root, "borrowed.txt");
            File.WriteAllText(borrowed, "borrowed");
            info.Files.Add(borrowed);
            manager.UpdateFence(info);
            Recycle(manager, info, borrowed, path => File.Move(path, Path.Combine(root, "borrowed-trash.txt")));
            Check(!info.Files.Contains(borrowed), "temporarily restored desktop items can be deleted from their Fence");
            var missing = Path.Combine(root, "already-gone.txt");
            info.Files.Add(missing);
            called = false;
            Recycle(manager, info, missing, path => called = true);
            Check(!called && !Read(metadata).Files.Contains(missing), "already removed items have stale membership cleaned up");

            MenuChecks(root, manager);
            NativeRecycleRoundTrip(root);
            NativeRecycleRoundTrip(root, true);
            Console.WriteLine("PASS: " + assertions + " deletion assertions; no existing user files were deleted");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void MenuChecks(string root, FenceManager manager)
    {
        var info = new FenceInfo(Guid.NewGuid()) { Name = "删除菜单验证", Width = 350, Height = 300 };
        var source = Path.Combine(root, "菜单示例.txt");
        File.WriteAllText(source, "menu fixture");
        info.Files.Add(manager.ImportItem(info, source));
        manager.UpdateFence(info);
        using (var parent = new Form())
        {
            typeof(FenceWindow).Assembly.GetType("NoFences.Win32.DesktopWindowHost")
                .GetField("desktopHost", Static).SetValue(null, parent.Handle);
            using (var window = new FenceWindow(info))
            {
                window.Show();
                var menu = (ContextMenuStrip)typeof(FenceWindow).GetField("appContextMenu", Instance).GetValue(window);
                var deletion = (ToolStripMenuItem)typeof(FenceWindow).GetField("recycleItemMenuItem", Instance).GetValue(window);
                var desktop = (ToolStripMenuItem)typeof(FenceWindow).GetField("deleteItemToolStripMenuItem", Instance).GetValue(window);
                typeof(FenceWindow).GetField("contextMenuItem", Instance).SetValue(window, info.Files[0]);
                menu.Show(window, new Point(30, 80));
                Application.DoEvents();
                Check(deletion.Visible && deletion.Text == "删除" && desktop.Visible && desktop.Text == "移回桌面",
                    "item menu has separate Delete and Move to desktop actions");
                var separator = (ToolStripSeparator)typeof(FenceWindow).GetField("recycleItemSeparator", Instance).GetValue(window);
                Check(menu.Items.IndexOf(separator) + 1 == menu.Items.IndexOf(desktop) &&
                    menu.Items.IndexOf(desktop) + 1 == menu.Items.IndexOf(deletion),
                    "divider sits above Move to desktop, followed immediately by Delete");
                Check(deletion.Font.Bold && desktop.Font.Bold, "both desktop release and Delete are bold");
                var output = Path.GetFullPath("Fences/obj/VisualChecks/item-delete-menu.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (var image = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                    image.Save(output);
                }
                menu.Close();
                typeof(FenceWindow).GetField("contextMenuItem", Instance).SetValue(window, null);
                menu.Show(window, new Point(30, 80));
                Check(!deletion.Visible && !desktop.Visible, "Fence background menu does not offer item deletion");
                menu.Close();
                window.Close();
            }
        }
    }

    private static void NativeRecycleRoundTrip(string root, bool folder = false)
    {
        var file = Path.Combine(root, "Fences-recycle-" + Guid.NewGuid().ToString("N") + (folder ? "" : ".txt"));
        var content = folder ? Path.Combine(file, "nested", "child.txt") : file;
        if (folder)
            Directory.CreateDirectory(Path.GetDirectoryName(content));
        File.WriteAllText(content, "native recycle round trip");
        Func<bool> exists = () => folder ? Directory.Exists(file) : File.Exists(file);
        Invoke(typeof(FenceWindow).Assembly.GetType("NoFences.Win32.ItemRecycler").GetMethod("Recycle", Static), null, file);
        Check(!exists(), "native Windows recycling removes the test " + (folder ? "folder" : "file") + " from its original path");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        dynamic bin = shell.NameSpace(10);
        dynamic items = bin.Items();
        var restored = false;
        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                dynamic entry = items.Item(index);
                try
                {
                    var originalFolder = (string)entry.ExtendedProperty("System.Recycle.DeletedFrom");
                    var originalName = (string)entry.ExtendedProperty("System.ItemNameDisplay");
                    if (!string.Equals(originalFolder, root, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(originalName, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
                        continue;
                    entry.InvokeVerb("undelete");
                    restored = true;
                    break;
                }
                finally { Marshal.FinalReleaseComObject(entry); }
            }
            Check(restored, "the native operation used the Recycle Bin rather than permanent deletion");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!exists() && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(20); }
            Check(exists() && File.ReadAllText(content) == "native recycle round trip",
                "the test recycle-bin entry restores successfully without changing its contents");
        }
        finally
        {
            Marshal.FinalReleaseComObject(items);
            Marshal.FinalReleaseComObject(bin);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static FenceInfo Read(string metadata)
    {
        using (var reader = new StreamReader(metadata)) return (FenceInfo)new XmlSerializer(typeof(FenceInfo)).Deserialize(reader);
    }
    private static void Recycle(FenceManager manager, FenceInfo info, string path, Action<string> recycle)
    {
        Invoke(typeof(FenceManager).GetMethod("RecycleItem", Instance, null,
            new[] { typeof(FenceInfo), typeof(string), typeof(Action<string>) }, null), manager, info, path, recycle);
    }
    private static void Invoke(MethodInfo method, object target, params object[] args)
    {
        try { method.Invoke(target, args); }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); throw new Exception("Expected " + typeof(T).Name); }
        catch (T) { }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
        Console.WriteLine("PASS: " + description);
    }
}
