using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using System.Xml.Serialization;
using NoFences;
using NoFences.Model;

internal static class AppDataMigrationTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Metadata = "__fence_metadata.xml";
    private static int assertions;

    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "Fences-migration-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var paths = typeof(FenceWindow).Assembly.GetType("NoFences.Util.AppDataPaths");
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fences");
            Check((string)paths.GetProperty("RootPath", Static).GetValue(null) == expected &&
                (string)paths.GetProperty("LogsPath", Static).GetValue(null) == Path.Combine(expected, "Logs"),
                "configuration, stored items and logs use the Fences data root");
            Check((string)typeof(FenceManager).GetField("basePath", Instance).GetValue(new FenceManager()) == expected,
                "default FenceManager actually uses the new storage path");
            FreshAndLegacy(Path.Combine(root, "legacy"));
            MergeIndependentFences(Path.Combine(root, "merge"));
            ResumeInterruptedMove(Path.Combine(root, "interrupted"));
            RefuseConflicts(Path.Combine(root, "conflict"));
            RetryAfterLockedMetadata(Path.Combine(root, "locked"));
            Console.WriteLine("PASS: " + assertions + " migration assertions; real user data was not migrated");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void FreshAndLegacy(string local)
    {
        var oldRoot = Path.Combine(local, "NoFences");
        var newRoot = Path.Combine(local, "Fences");
        Migrate(local);
        Check(Directory.Exists(newRoot) && !Directory.Exists(oldRoot), "fresh startup creates only the new directory");
        var info = CreateFence(oldRoot, "旧配置 & 空格", "owned.txt");
        var originalOwned = info.Files[0];
        var desktop = Path.Combine(local, "desktop");
        Directory.CreateDirectory(desktop);
        var external = Path.Combine(desktop, "external.txt");
        File.WriteAllText(external, "external");
        var neighbor = Path.Combine(local, "NoFences-other", "neighbor.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(neighbor));
        File.WriteAllText(neighbor, "neighbor");
        info.Files.Add(external);
        info.Files.Add(neighbor);
        var pending = Path.Combine(oldRoot, info.Id.ToString(), "pending.txt");
        var released = Path.Combine(desktop, "pending.txt");
        File.WriteAllText(released, "pending release");
        info.Files.Add(pending);
        info.PendingDesktopReleases.Add(new FenceDesktopRelease { SourcePath = pending, DesktopPath = released });
        info.Appearance.CornerRadius = 23;
        info.DesktopAccessClients.Add(new DesktopAccessClient { ProcessId = 1234, StartTimeUtcTicks = 5678 });
        Save(oldRoot, info);
        var metadata = MetadataPath(oldRoot, info);
        var document = XDocument.Load(metadata);
        document.Root.Add(new XElement("FutureSetting", "preserve me"));
        document.Save(metadata);
        var nested = Path.Combine(oldRoot, info.Id.ToString(), "nested", Metadata);
        Directory.CreateDirectory(Path.GetDirectoryName(nested));
        File.WriteAllText(nested, "user-owned file, not application configuration");
        Directory.CreateDirectory(Path.Combine(oldRoot, "Logs"));
        File.WriteAllText(Path.Combine(oldRoot, "Logs", "old.log"), "old diagnostic log");

        Migrate(local);
        var migrated = Read(newRoot, info);
        Check(!Directory.Exists(oldRoot) && File.ReadAllText(migrated.Files[0]) == "owned.txt",
            "stored files move to the new directory and their absolute paths are updated");
        Check(migrated.Files[0] == Path.Combine(newRoot, info.Id.ToString(), "owned.txt") && !File.Exists(originalOwned),
            "no stored item still depends on the legacy directory");
        Check(migrated.Files[1] == external && migrated.Files[2] == neighbor,
            "desktop/external paths and similarly named neighboring directories stay unchanged");
        Check(migrated.PendingDesktopReleases[0].SourcePath == Path.Combine(newRoot, info.Id.ToString(), "pending.txt") &&
            migrated.PendingDesktopReleases[0].DesktopPath == released,
            "incomplete desktop-release journal is rebased without altering the desktop destination");
        Check(migrated.Appearance.CornerRadius == 23 && migrated.DesktopAccessClients[0].StartTimeUtcTicks == 5678 &&
            XDocument.Load(MetadataPath(newRoot, info)).Root.Element("FutureSetting").Value == "preserve me",
            "appearance, consuming processes and unknown XML fields survive migration");
        Check(File.ReadAllText(Path.Combine(newRoot, info.Id.ToString(), "nested", Metadata)) ==
            "user-owned file, not application configuration", "nested user files named like metadata are untouched");
        Check(File.ReadAllText(Path.Combine(newRoot, "Logs", "old.log")) == "old diagnostic log",
            "existing logs move to Fences/Logs");
        var before = File.ReadAllBytes(MetadataPath(newRoot, info));
        Migrate(local);
        Check(before.SequenceEqual(File.ReadAllBytes(MetadataPath(newRoot, info))), "repeat startup does not rewrite migrated metadata");
        var manager = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null,
            new object[] { newRoot, desktop }, null);
        Check(manager.ReleaseStoredFences().Count == 0 && File.Exists(Path.Combine(desktop, "owned.txt")),
            "exit recovery can release migrated items and resolve the migrated pending journal");
    }

    private static void MergeIndependentFences(string local)
    {
        var oldRoot = Path.Combine(local, "NoFences");
        var newRoot = Path.Combine(local, "Fences");
        var old = CreateFence(oldRoot, "old", "old.txt");
        var current = CreateFence(newRoot, "new", "new.txt");
        Directory.CreateDirectory(Path.Combine(oldRoot, "Logs"));
        Directory.CreateDirectory(Path.Combine(newRoot, "Logs"));
        File.WriteAllText(Path.Combine(oldRoot, "Logs", "error.log"), "old log");
        File.WriteAllText(Path.Combine(newRoot, "Logs", "error.log"), "new log");
        var currentBytes = File.ReadAllBytes(MetadataPath(newRoot, current));
        Migrate(local);
        Check(File.Exists(Read(newRoot, old).Files[0]) && File.Exists(Read(newRoot, current).Files[0]) &&
            currentBytes.SequenceEqual(File.ReadAllBytes(MetadataPath(newRoot, current))),
            "independent old and new fences coexist without overwriting current configuration");
        Check(File.ReadAllText(Path.Combine(newRoot, "Logs", "error.log")) == "new log" &&
            File.ReadAllText(Path.Combine(newRoot, "Logs", "error.log.legacy-2")) == "old log",
            "colliding logs are retained independently");
    }

    private static void ResumeInterruptedMove(string local)
    {
        var oldRoot = Path.Combine(local, "NoFences");
        var newRoot = Path.Combine(local, "Fences");
        var first = CreateFence(oldRoot, "first", "first.txt");
        var second = CreateFence(oldRoot, "second", "second.txt");
        Directory.CreateDirectory(newRoot);
        Directory.Move(Path.Combine(oldRoot, first.Id.ToString()), Path.Combine(newRoot, first.Id.ToString()));
        Migrate(local);
        Check(File.Exists(Read(newRoot, first).Files[0]) && File.Exists(Read(newRoot, second).Files[0]),
            "partial directory migration resumes and repairs both sets of paths");
        var interruptedLocal = Path.Combine(local, "complete-move");
        var interruptedOld = Path.Combine(interruptedLocal, "NoFences");
        var interruptedNew = Path.Combine(interruptedLocal, "Fences");
        var info = CreateFence(interruptedOld, "interrupted", "data.txt");
        Directory.Move(interruptedOld, interruptedNew);
        Migrate(interruptedLocal);
        Check(File.Exists(Read(interruptedNew, info).Files[0]),
            "path repair resumes after the old directory has already disappeared");
    }

    private static void RefuseConflicts(string local)
    {
        var oldRoot = Path.Combine(local, "NoFences");
        var newRoot = Path.Combine(local, "Fences");
        var info = CreateFence(oldRoot, "old", "old.txt");
        var sameId = new FenceInfo(info.Id) { Name = "current" };
        Save(newRoot, sameId);
        var original = Snapshot(local);
        try { Migrate(local); throw new Exception("Expected conflicting fence migration to fail"); }
        catch (IOException) { }
        var after = Snapshot(local);
        Check(original.Count == after.Count && original.All(pair => after[pair.Key] == pair.Value),
            "same-ID conflicts leave both existing configurations and all files intact");
    }

    private static void RetryAfterLockedMetadata(string local)
    {
        var oldRoot = Path.Combine(local, "NoFences");
        var newRoot = Path.Combine(local, "Fences");
        var info = CreateFence(oldRoot, "locked", "data.txt");
        using (var locked = new FileStream(MetadataPath(oldRoot, info), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { Migrate(local); throw new Exception("Expected locked metadata migration to fail"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Check(File.Exists(Path.Combine(oldRoot, info.Id.ToString(), "data.txt")) ||
                File.Exists(Path.Combine(newRoot, info.Id.ToString(), "data.txt")),
                "a blocked migration retains the actual stored item");
        }
        Migrate(local);
        Check(File.Exists(Read(newRoot, info).Files[0]) && !Directory.Exists(oldRoot),
            "migration retries successfully after the blocking handle closes");
    }

    private static Dictionary<string, string> Snapshot(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => Convert.ToBase64String(File.ReadAllBytes(path)));
    }

    private static FenceInfo CreateFence(string root, string name, string filename)
    {
        var info = new FenceInfo(Guid.NewGuid()) { Name = name };
        var directory = Path.Combine(root, info.Id.ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        File.WriteAllText(path, filename);
        info.Files.Add(path);
        Save(root, info);
        return info;
    }

    private static string MetadataPath(string root, FenceInfo info) => Path.Combine(root, info.Id.ToString(), Metadata);
    private static void Save(string root, FenceInfo info)
    {
        Directory.CreateDirectory(Path.Combine(root, info.Id.ToString()));
        using (var writer = new StreamWriter(MetadataPath(root, info))) new XmlSerializer(typeof(FenceInfo)).Serialize(writer, info);
    }
    private static FenceInfo Read(string root, FenceInfo info)
    {
        using (var reader = new StreamReader(MetadataPath(root, info))) return (FenceInfo)new XmlSerializer(typeof(FenceInfo)).Deserialize(reader);
    }
    private static void Migrate(string local)
    {
        try
        {
            typeof(FenceWindow).Assembly.GetType("NoFences.Util.AppDataMigration").GetMethod("Migrate", Static)
                .Invoke(null, new object[] { local });
        }
        catch (TargetInvocationException exception)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        assertions++;
        Console.WriteLine("PASS: " + description);
    }
}
