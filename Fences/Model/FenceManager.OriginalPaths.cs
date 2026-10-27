using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoFences.Model
{
    public partial class FenceManager
    {
        private readonly Dictionary<Guid, FenceInfo> loadedInfos = new Dictionary<Guid, FenceInfo>();
        internal bool KeepOriginalPaths { get; set; }
        internal event Action MembershipChanged;
        internal event Action<IList<FenceInfo>> BeforeShowingFences;
        internal List<FenceInfo> LoadedFences => loadedInfos.Values.ToList();
        internal bool UsesOriginalPaths(FenceInfo info) => KeepOriginalPaths || info.KeepOriginalPaths;

        internal string[] OriginalPaths => loadedInfos.Values.Where(info => info.KeepOriginalPaths)
            .SelectMany(info => info.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private void ResolveDuplicateOriginalMemberships(List<FenceInfo> fences, Dictionary<Guid, DateTime> writeTimes)
        {
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A transfer writes the receiving fence first. If interrupted, its newer
            // record wins without copying, deleting or moving the shared real item.
            foreach (var info in fences.OrderByDescending(fence => writeTimes[fence.Id]).ThenBy(fence => fence.Id))
                if (info.Files.RemoveAll(path => !owners.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))) != 0)
                    WriteFence(info);
        }

        private void TransferOriginalItem(FenceInfo source, FenceInfo destination, string path)
        {
            if (source.Id == destination.Id) return;
            var index = source.Files.FindIndex(existing => PathsEqual(existing, path));
            if (index < 0 || !ItemExists(path)) throw new FileNotFoundException("源分区中的项目已不存在。", path);
            if (IsOwnedByFence(source, path)) throw new InvalidOperationException("请先完成源分区的原路径迁移，再转移此项目。");
            if (source.Locked) throw new InvalidOperationException("源分区已锁定。");
            var destinationFiles = destination.Files.ToList();
            var sourceFiles = source.Files.ToList();
            destination.KeepOriginalPaths = true;
            if (!ContainsPath(destination.Files, path)) destination.Files.Add(path);
            try
            {
                // Write the receiving membership first; a crash must not lose both records.
                WriteFence(destination);
                source.Files.RemoveAt(index);
                WriteFence(source);
            }
            catch
            {
                source.Files = sourceFiles;
                destination.Files = destinationFiles;
                WriteFence(destination);
                WriteFence(source);
                throw;
            }
        }

        internal void AssignOriginalItems(FenceInfo destination, IEnumerable<string> paths)
        {
            var changed = new List<FenceInfo> { destination };
            var snapshot = LoadedFences.ToDictionary(info => info.Id, info => info.Files.ToList());
            if (!snapshot.ContainsKey(destination.Id)) snapshot[destination.Id] = destination.Files.ToList();
            try
            {
                foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var original = ImportItem(destination, path);
                    foreach (var source in LoadedFences.Where(info => info.Id != destination.Id && info.KeepOriginalPaths))
                    {
                        if (!ContainsPath(source.Files, original)) continue;
                        if (source.Locked) throw new InvalidOperationException("源分区已锁定。");
                        source.Files.RemoveAll(existing => PathsEqual(existing, original));
                        if (!changed.Contains(source)) changed.Add(source);
                    }
                    if (!ContainsPath(destination.Files, original)) destination.Files.Add(original);
                }
                // Destination first also preserves membership if termination interrupts a transfer.
                foreach (var info in changed) WriteFence(info);
            }
            catch (Exception exception)
            {
                foreach (var info in changed) info.Files = snapshot[info.Id];
                var rollbackErrors = new List<Exception> { exception };
                foreach (var info in changed)
                {
                    try { WriteFence(info); }
                    catch (Exception rollback) { rollbackErrors.Add(rollback); }
                }
                if (rollbackErrors.Count > 1) throw new AggregateException("分区记录保存失败，文件仍保留在原路径。", rollbackErrors);
                throw;
            }
        }

        internal void OriginalPathChanged(string previous, string current)
        {
            previous = Path.GetFullPath(previous).TrimEnd(Path.DirectorySeparatorChar);
            var prefix = previous + Path.DirectorySeparatorChar;
            foreach (var info in LoadedFences.Where(fence => fence.KeepOriginalPaths))
            {
                var original = info.Files.ToList();
                var changed = false;
                for (var index = info.Files.Count - 1; index >= 0; --index)
                {
                    var path = info.Files[index];
                    if (!PathsEqual(path, previous) && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (current == null)
                    {
                        if (ItemExists(path)) continue;
                        info.Files.RemoveAt(index);
                    }
                    else info.Files[index] = current + path.Substring(previous.Length);
                    changed = true;
                }
                if (!changed) continue;
                try { WriteFence(info); }
                catch { info.Files = original; throw; }
            }
        }

        internal bool IsOnDesktop(string path)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
            return PathsEqual(parent, desktopPath) || PathsEqual(parent,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        }
    }
}
