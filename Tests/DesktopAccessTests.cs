using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using NoFences.Model;

internal static class DesktopAccessTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int assertions;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--hold")
        {
            using (var stop = EventWaitHandle.OpenExisting(args[1]))
                stop.WaitOne(TimeSpan.FromSeconds(30));
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "NoFences-access-" + Guid.NewGuid());
        var desktop = Path.Combine(root, "desktop");
        Directory.CreateDirectory(desktop);
        var eventName = "NoFences_Access_Test_" + Guid.NewGuid().ToString("N");
        using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
        using (var child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
            "--hold " + eventName) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        {
            try
            {
                var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
                    new object[] { Path.Combine(root, "storage"), desktop }, null);
                var info = new FenceInfo(Guid.NewGuid());
                var source = Path.Combine(root, "sample.txt");
                File.WriteAllText(source, "original");
                info.Files.Add(manager.ImportItem(info, source));
                var originalStored = info.Files[0];
                manager.UpdateFence(info);
                File.WriteAllText(Path.Combine(desktop, "sample.txt"), "original");
                var first = Client(Process.GetCurrentProcess());
                var second = Client(child);
                var errors = Begin(manager, info, first);
                Check(errors.Count == 0 && !File.Exists(originalStored) && File.Exists(info.Files[0]),
                    "first client restores actual item to desktop");
                Check(Path.GetFileName(info.Files[0]) == "sample (2).txt" &&
                    File.Exists(Path.Combine(desktop, "sample.txt")), "equivalent desktop collision is preserved independently");
                var metadata = File.ReadAllText(Path.Combine(root, "storage", info.Id.ToString(), "__fence_metadata.xml"));
                Check(metadata.Contains("DesktopAccessClients") && metadata.Contains(info.Files[0]),
                    "client identity and released path are persisted");
                Begin(manager, info, first);
                Check(info.DesktopAccessClients.Count == 1, "repeated dialog does not duplicate a client");
                Begin(manager, info, second);
                End(manager, info, first);
                Check(info.DesktopAccessClients.Count == 1 && Path.GetDirectoryName(info.Files[0]) == desktop,
                    "one client exiting leaves files available for the other");
                var restoredPath = info.Files[0];
                File.WriteAllText(restoredPath, "edited by the consuming application");
                stop.Set();
                Check(child.WaitForExit(5000), "second client exits normally");
                using (var locked = new FileStream(restoredPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    errors = End(manager, info, second);
                    Check(errors.Count > 0 && info.Files[0] == restoredPath && File.Exists(restoredPath),
                        "locked file remains at its usable desktop path on reclaim failure");
                }
                errors = End(manager, info, second);
                Check(errors.Count == 0 && !File.Exists(restoredPath) && File.Exists(info.Files[0]) &&
                    File.ReadAllText(info.Files[0]) == "edited by the consuming application",
                    "reclaim preserves edits and restores fence membership");
                Check(File.ReadAllText(Path.Combine(desktop, "sample.txt")) == "original",
                    "unrelated desktop item is unchanged");
                var stale = new DesktopAccessClient { ProcessId = first.ProcessId,
                    StartTimeUtcTicks = first.StartTimeUtcTicks - 1 };
                Check(!(bool)typeof(DesktopAccessClient).GetMethod("IsRunning", Instance).Invoke(stale, null),
                    "reused PID cannot prolong an old desktop lease");

                var folder = Path.Combine(root, "folder");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "nested.txt"), "nested");
                info.Files.Add(manager.ImportItem(info, folder));
                Begin(manager, info, first);
                Check(Directory.Exists(info.Files[1]) && Path.GetDirectoryName(info.Files[1]) == desktop,
                    "folders are restored with their contents");
                End(manager, info, first);
                Check(File.ReadAllText(Path.Combine(info.Files[1], "nested.txt")) == "nested",
                    "folders return to the original fence");
                Console.WriteLine("PASS: " + assertions + " desktop-access assertions");
            }
            finally
            {
                stop.Set();
                if (!child.WaitForExit(5000))
                {
                    child.Kill();
                    child.WaitForExit();
                }
                // Only remove this test's GUID-named temporary directory.
                Directory.Delete(root, true);
            }
        }
    }

    private static DesktopAccessClient Client(Process process)
    {
        return new DesktopAccessClient { ProcessId = process.Id, StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks };
    }
    private static System.Collections.Generic.IList<string> Begin(FenceManager manager, FenceInfo info, DesktopAccessClient client)
    {
        return (System.Collections.Generic.IList<string>)typeof(FenceManager).GetMethod("BeginDesktopAccess", Instance)
            .Invoke(manager, new object[] { info, client });
    }
    private static System.Collections.Generic.IList<string> End(FenceManager manager, FenceInfo info, DesktopAccessClient client)
    {
        return (System.Collections.Generic.IList<string>)typeof(FenceManager).GetMethod("EndDesktopAccess", Instance)
            .Invoke(manager, new object[] { info, client });
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        assertions++;
        Console.WriteLine("PASS: " + name);
    }
}
