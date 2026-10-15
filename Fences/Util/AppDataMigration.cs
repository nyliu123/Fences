using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace NoFences.Util
{
    internal static class AppDataMigration
    {
        private const string MetadataFileName = "__fence_metadata.xml";

        // Run under the recovery mutex before starting the new recovery process or UI.
        internal static void Migrate(string localApplicationData)
        {
            var legacyRoot = Path.Combine(localApplicationData, AppDataPaths.LegacyDirectoryName);
            var currentRoot = Path.Combine(localApplicationData, AppDataPaths.DirectoryName);
            if (Directory.Exists(legacyRoot))
            {
                var entries = Directory.GetFileSystemEntries(legacyRoot);
                // Refuse collisions before moving anything; neither configuration wins silently.
                foreach (var source in entries)
                {
                    var destination = Path.Combine(currentRoot, Path.GetFileName(source));
                    if ((File.Exists(destination) || Directory.Exists(destination)) &&
                        !(Path.GetFileName(source) == "Logs" && Directory.Exists(source) && Directory.Exists(destination)))
                        throw new IOException("新旧数据目录存在同名项目，未覆盖任何配置：" + destination);
                }
                Directory.CreateDirectory(currentRoot);
                foreach (var source in entries)
                {
                    var destination = Path.Combine(currentRoot, Path.GetFileName(source));
                    if (Path.GetFileName(source) == "Logs" && Directory.Exists(source) && Directory.Exists(destination))
                        MergeLogs(source, destination);
                    else
                        MoveEntry(source, destination);
                }
                Directory.Delete(legacyRoot, false);
            }
            Directory.CreateDirectory(currentRoot);

            // Resume path updates even when an interrupted directory move already removed
            // the old root but some metadata still contains its original absolute paths.
            foreach (var directory in Directory.GetDirectories(currentRoot))
            {
                if (!Guid.TryParse(Path.GetFileName(directory), out var fenceId))
                    continue;
                var metadata = Path.Combine(directory, MetadataFileName);
                if (File.Exists(metadata))
                    RebaseMetadata(metadata, legacyRoot, currentRoot);
            }
        }

        private static void MergeLogs(string source, string destination)
        {
            foreach (var entry in Directory.GetFileSystemEntries(source))
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(destination, name);
                var suffix = 2;
                while (File.Exists(target) || Directory.Exists(target))
                    target = Path.Combine(destination, name + ".legacy-" + suffix++);
                MoveEntry(entry, target);
            }
            Directory.Delete(source, false);
        }

        private static void MoveEntry(string source, string destination)
        {
            if (Directory.Exists(source))
                Directory.Move(source, destination);
            else
                File.Move(source, destination);
        }

        private static void RebaseMetadata(string metadata, string legacyRoot, string currentRoot)
        {
            XDocument document;
            try { document = XDocument.Load(metadata, LoadOptions.PreserveWhitespace); }
            catch (XmlException) { return; } // LoadFences reports invalid configurations individually.
            if (document.Root == null || document.Root.Name.LocalName != "FenceInfo")
                return;
            var ns = document.Root.Name.Namespace;
            var files = document.Root.Element(ns + "Files");
            var pending = document.Root.Element(ns + "PendingDesktopReleases");
            var paths = (files == null ? Enumerable.Empty<XElement>() : files.Elements(ns + "string"))
                .Concat(pending == null ? Enumerable.Empty<XElement>() :
                    pending.Elements(ns + "FenceDesktopRelease").Elements()
                        .Where(element => element.Name == ns + "SourcePath" || element.Name == ns + "DesktopPath"));
            var changed = false;
            foreach (var element in paths)
            {
                var value = RebasePath(element.Value, legacyRoot, currentRoot);
                if (value == element.Value)
                    continue;
                element.Value = value;
                changed = true;
            }
            if (!changed)
                return;

            // Preserve unknown fields; use the atomic-write temporary name already excluded
            // from FenceManager's unlisted-item recovery.
            var temporary = metadata + ".tmp";
            try
            {
                document.Save(temporary, SaveOptions.DisableFormatting);
                File.Replace(temporary, metadata, null);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string RebasePath(string value, string legacyRoot, string currentRoot)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                return value;
            string fullPath;
            try { fullPath = Path.GetFullPath(value); }
            catch (ArgumentException) { return value; }
            var prefix = Path.GetFullPath(legacyRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(currentRoot, fullPath.Substring(prefix.Length));
            return value;
        }
    }
}
