using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using NoFences.Util;
using NoFences.Win32;

namespace NoFences.Model
{
    public class FenceManager
    {
        public static FenceManager Instance { get; } = new FenceManager();

        private const string MetaFileName = "__fence_metadata.xml";
        private const string LegacyShortcutDirectoryName = ".shortcuts";
        private readonly string basePath;
        private readonly string desktopPath;

        public FenceManager() : this(
            AppDataPaths.RootPath,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
        {
        }

        internal FenceManager(string basePath, string desktopPath)
        {
            this.basePath = basePath;
            this.desktopPath = desktopPath;
            EnsureDirectoryExists(basePath);
        }

        public IList<string> ReleaseStoredFences()
        {
            var errors = new List<string>();
            foreach (var directoryPath in Directory.GetDirectories(basePath))
            {
                var metaFile = Path.Combine(directoryPath, MetaFileName);
                if (!File.Exists(metaFile))
                    continue;
                try
                {
                    FenceInfo fence;
                    using (var reader = new StreamReader(metaFile))
                        fence = new XmlSerializer(typeof(FenceInfo)).Deserialize(reader) as FenceInfo;
                    if (fence == null || !PathsEqual(directoryPath, GetFolderPath(fence)))
                        continue;
                    ResolvePendingDesktopReleases(fence);
                    // A forced exit may happen between moving an item and saving its metadata.
                    RecoverUnlistedOwnedItems(fence);
                    errors.AddRange(ReleaseAllItems(fence, true));
                }
                catch (Exception exception)
                {
                    errors.Add(string.Format("{0}: {1}", directoryPath, exception.Message));
                }
            }
            return errors;
        }

        public IList<string> LoadFences()
        {
            var errors = new List<string>();
            var loadedFences = new List<FenceInfo>();
            foreach (var directoryPath in Directory.EnumerateDirectories(basePath))
            {
                var metaFile = Path.Combine(directoryPath, MetaFileName);
                if (!File.Exists(metaFile))
                    continue;

                try
                {
                    FenceInfo fence;
                    var serializer = new XmlSerializer(typeof(FenceInfo));
                    using (var reader = new StreamReader(metaFile))
                        fence = serializer.Deserialize(reader) as FenceInfo;
                    if (fence == null)
                        continue;

                    var changed = LocalizeLegacyName(fence);
                    changed |= ResolvePendingDesktopReleases(fence);
                    changed |= fence.DesktopAccessClients.RemoveAll(client => !client.IsRunning()) > 0;
                    if (fence.DesktopAccessClients.Count == 0)
                        changed |= MoveExternalItemsIntoFence(fence, errors);
                    changed |= RecoverUnlistedOwnedItems(fence);
                    var legacyShortcutDirectory = Path.Combine(directoryPath, LegacyShortcutDirectoryName);
                    if (Directory.Exists(legacyShortcutDirectory))
                    {
                        Directory.Delete(legacyShortcutDirectory, true);
                        changed = true;
                    }

                    if (changed)
                        WriteFence(fence);
                    // Do not display a fence if an item could not be moved off the desktop.
                    if (fence.DesktopAccessClients.Count == 0 &&
                        fence.Files.Exists(path => ItemExists(path) && !IsOwnedByFence(fence, path)))
                        continue;
                    loadedFences.Add(fence);
                }
                catch (Exception exception)
                {
                    errors.Add(string.Format("{0}: {1}", directoryPath, exception.Message));
                }
            }

            // Move every fence's items first, then flush Explorer's desktop notifications
            // before constructing any windows (including their icon/title layers).
            DesktopUtil.FlushDesktopChanges(desktopPath);
            foreach (var fence in loadedFences)
            {
                try
                {
                    new FenceWindow(fence).Show();
                }
                catch (Exception exception)
                {
                    errors.Add(string.Format("{0}: {1}", GetFolderPath(fence), exception.Message));
                }
            }

            return errors;
        }

        public void CreateFence(string name)
        {
            var fenceInfo = new FenceInfo(Guid.NewGuid())
            {
                Name = name,
                PosX = 100,
                PosY = 250,
                Height = 300,
                Width = 300
            };
            UpdateFence(fenceInfo);
            new FenceWindow(fenceInfo).Show();
        }

        public void RemoveFence(FenceInfo info)
        {
            var errors = ReleaseAllItems(info);
            if (errors.Count > 0)
                throw new IOException(string.Join(Environment.NewLine, errors));
            Directory.Delete(GetFolderPath(info), true);
        }

        public string ImportItem(FenceInfo info, string itemPath)
        {
            if (!ItemExists(itemPath))
                throw new FileNotFoundException("要添加的项目已不存在。", itemPath);
            if (IsOwnedByFence(info, itemPath))
                return itemPath;

            var destinationPath = GetAvailablePath(
                GetFolderPath(info), GetItemName(itemPath), Directory.Exists(itemPath));
            var isDirectory = Directory.Exists(itemPath);
            MoveItem(itemPath, destinationPath);
            DesktopUtil.NotifyItemMoved(itemPath, destinationPath, isDirectory);
            return destinationPath;
        }

        public string FindEquivalentImportedItem(FenceInfo info, string originalPath, string importedPath)
        {
            if (!File.Exists(importedPath))
                return null;

            var originalName = GetItemName(originalPath);
            foreach (var existingPath in info.Files)
            {
                if (PathsEqual(existingPath, importedPath) || !File.Exists(existingPath) ||
                    !string.Equals(GetItemName(existingPath), originalName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (FileContentComparer.AreEquivalent(existingPath, importedPath))
                    return existingPath;
            }
            return null;
        }

        public void CompleteDuplicateImport(string importedPath)
        {
            DeleteItem(importedPath);
        }

        public void CancelImport(string originalPath, string importedPath)
        {
            if (!ItemExists(importedPath))
                return;
            if (ItemExists(originalPath))
                throw new IOException("原位置已经存在同名项目，无法恢复拖放：" + originalPath);
            var isDirectory = Directory.Exists(importedPath);
            MoveItem(importedPath, originalPath);
            DesktopUtil.NotifyItemMoved(importedPath, originalPath, isDirectory);
        }

        public void ReleaseItem(FenceInfo info, string itemPath)
        {
            var itemIndex = info.Files.FindIndex(path => PathsEqual(path, itemPath));
            if (itemIndex < 0)
                return;

            var releasedPath = MoveOwnedItemToDesktop(info, itemPath);
            info.Files.RemoveAt(itemIndex);
            try
            {
                WriteFence(info);
            }
            catch
            {
                info.Files.Insert(itemIndex, itemPath);
                if (!PathsEqual(releasedPath, itemPath) && ItemExists(releasedPath) && !ItemExists(itemPath))
                    MoveItem(releasedPath, itemPath);
                throw;
            }
        }

        public void RecycleItem(FenceInfo info, string itemPath)
        {
            RecycleItem(info, itemPath, ItemRecycler.Recycle);
        }

        internal void RecycleItem(FenceInfo info, string itemPath, Action<string> recycle)
        {
            var index = info.Files.FindIndex(path => PathsEqual(path, itemPath));
            if (index < 0)
                throw new FileNotFoundException("要删除的项目已不在此分区中。", itemPath);
            var recordedPath = info.Files[index];

            // Persist first: a metadata write failure must never recycle the actual item.
            info.Files.RemoveAt(index);
            try { WriteFence(info); }
            catch
            {
                info.Files.Insert(index, recordedPath);
                throw;
            }
            try
            {
                if (ItemExists(itemPath))
                    recycle(itemPath);
                if (ItemExists(itemPath))
                    throw new IOException("项目未能移入回收站，仍保留在原位置。");
            }
            catch (Exception exception)
            {
                if (ItemExists(itemPath))
                {
                    if (!ContainsPath(info.Files, itemPath))
                        info.Files.Insert(Math.Min(index, info.Files.Count), recordedPath);
                    try { WriteFence(info); }
                    catch (Exception saveException)
                    {
                        throw new AggregateException("项目仍保留在原位置，但分区记录未能恢复。", exception, saveException);
                    }
                }
                throw;
            }
        }

        public string RenameItem(FenceInfo info, string itemPath, string newName)
        {
            newName = (newName ?? string.Empty).Trim();
            if (newName.Length == 0)
                throw new ArgumentException("名称不能为空。");
            if (newName == "." || newName == ".." || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("名称包含 Windows 不允许使用的字符。");
            if (newName.EndsWith(".", StringComparison.Ordinal) || newName.EndsWith(" ", StringComparison.Ordinal))
                throw new ArgumentException("名称不能以句点或空格结尾。");

            var sourceIndex = info.Files.FindIndex(path => PathsEqual(path, itemPath));
            if (sourceIndex < 0 || !ItemExists(itemPath))
                throw new FileNotFoundException("要重命名的项目已不存在。", itemPath);
            var parentPath = Path.GetDirectoryName(itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var destinationPath = Path.Combine(parentPath, newName);
            if (PathsEqual(itemPath, destinationPath))
                return itemPath;
            if (ItemExists(destinationPath))
                throw new IOException("同一位置已经存在同名项目。");

            MoveItem(itemPath, destinationPath);
            info.Files[sourceIndex] = destinationPath;
            try
            {
                WriteFence(info);
                return destinationPath;
            }
            catch
            {
                info.Files[sourceIndex] = itemPath;
                if (ItemExists(destinationPath) && !ItemExists(itemPath))
                    MoveItem(destinationPath, itemPath);
                throw;
            }
        }

        public void TransferItem(FenceInfo source, FenceInfo destination, string itemPath)
        {
            if (source.Id == destination.Id)
                return;
            var sourceIndex = source.Files.FindIndex(path => PathsEqual(path, itemPath));
            if (sourceIndex < 0 || !ItemExists(itemPath))
                throw new FileNotFoundException("源分区中的项目已不存在。", itemPath);

            var destinationPath = GetAvailablePath(
                GetFolderPath(destination), GetItemName(itemPath), Directory.Exists(itemPath));
            MoveItem(itemPath, destinationPath);
            source.Files.RemoveAt(sourceIndex);
            destination.Files.Add(destinationPath);
            try
            {
                WriteFence(source);
                WriteFence(destination);
            }
            catch
            {
                destination.Files.Remove(destinationPath);
                source.Files.Insert(sourceIndex, itemPath);
                if (ItemExists(destinationPath) && !ItemExists(itemPath))
                    MoveItem(destinationPath, itemPath);
                WriteFence(source);
                WriteFence(destination);
                throw;
            }
        }

        public void CompleteExternalMove(FenceInfo source, string itemPath)
        {
            var sourceIndex = source.Files.FindIndex(path => PathsEqual(path, itemPath));
            if (sourceIndex < 0)
                return;
            source.Files.RemoveAt(sourceIndex);
            WriteFence(source);
        }

        public void UpdateFence(FenceInfo fenceInfo)
        {
            WriteFence(fenceInfo);
        }

        internal IList<string> BeginDesktopAccess(FenceInfo info, DesktopAccessClient client)
        {
            if (!info.DesktopAccessClients.Exists(existing => existing.Matches(client)))
                info.DesktopAccessClients.Add(client);
            // Persist the client before releasing anything. Existing release journaling
            // preserves membership even if NoFences exits halfway through a move.
            WriteFence(info);
            return ReleaseAllItems(info, true, false);
        }

        internal IList<string> EndDesktopAccess(FenceInfo info, DesktopAccessClient client)
        {
            var errors = new List<string>();
            info.DesktopAccessClients.RemoveAll(existing => existing.Matches(client) || !existing.IsRunning());
            if (info.DesktopAccessClients.Count == 0)
                MoveExternalItemsIntoFence(info, errors);
            WriteFence(info);
            return errors;
        }

        private bool LocalizeLegacyName(FenceInfo fence)
        {
            if (fence.Name == "First fence")
            {
                fence.Name = "常用";
                return true;
            }
            if (fence.Name == "New fence" || fence.Name == "New Fence")
            {
                fence.Name = "新建分区";
                return true;
            }
            return false;
        }

        private bool MoveExternalItemsIntoFence(FenceInfo info, IList<string> errors)
        {
            var changed = false;
            for (var index = 0; index < info.Files.Count; index++)
            {
                var sourcePath = info.Files[index];
                if (!ItemExists(sourcePath) || IsOwnedByFence(info, sourcePath))
                    continue;
                try
                {
                    info.Files[index] = ImportItem(info, sourcePath);
                    changed = true;
                }
                catch (Exception exception)
                {
                    errors.Add(string.Format("{0}: {1}", sourcePath, exception.Message));
                }
            }
            return changed;
        }

        private bool RecoverUnlistedOwnedItems(FenceInfo info)
        {
            var changed = false;
            foreach (var itemPath in Directory.GetFileSystemEntries(GetFolderPath(info)))
            {
                var itemName = Path.GetFileName(itemPath);
                if (string.Equals(itemName, MetaFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(itemName, MetaFileName + ".tmp", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(itemName, LegacyShortcutDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                    ContainsPath(info.Files, itemPath))
                    continue;
                info.Files.Add(itemPath);
                changed = true;
            }
            return changed;
        }

        private IList<string> ReleaseAllItems(FenceInfo info, bool preserveMembership = false,
            bool reuseEquivalentDesktopItem = true)
        {
            var errors = new List<string>();
            foreach (var itemPath in new List<string>(info.Files))
            {
                if (!ItemExists(itemPath) || !IsOwnedByFence(info, itemPath))
                    continue;
                try
                {
                    FenceDesktopRelease pendingRelease = null;
                    var releasedPath = MoveOwnedItemToDesktop(info, itemPath, preserveMembership ?
                        (Action<string>)(destinationPath =>
                        {
                            // Persist the destination before moving, so termination during exit
                            // cannot lose the item's fence membership, including renamed collisions.
                            pendingRelease = new FenceDesktopRelease
                            {
                                SourcePath = itemPath,
                                DesktopPath = destinationPath
                            };
                            info.PendingDesktopReleases.Add(pendingRelease);
                            WriteFence(info);
                        }) : null, reuseEquivalentDesktopItem);
                    if (preserveMembership)
                    {
                        for (var index = 0; index < info.Files.Count; index++)
                            if (PathsEqual(info.Files[index], itemPath))
                                info.Files[index] = releasedPath;
                        info.PendingDesktopReleases.Remove(pendingRelease);
                        // Keep the desktop path so LoadFences imports the item into its original fence.
                        WriteFence(info);
                    }
                    else
                    {
                        info.Files.RemoveAll(path => PathsEqual(path, itemPath));
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(string.Format("{0}: {1}", itemPath, exception.Message));
                }
            }
            WriteFence(info);
            return errors;
        }

        private bool ResolvePendingDesktopReleases(FenceInfo info)
        {
            if (info.PendingDesktopReleases.Count == 0)
                return false;
            foreach (var release in info.PendingDesktopReleases)
            {
                var currentPath = ItemExists(release.SourcePath) ? release.SourcePath : release.DesktopPath;
                for (var index = 0; index < info.Files.Count; index++)
                    if (PathsEqual(info.Files[index], release.SourcePath))
                        info.Files[index] = currentPath;
            }
            info.PendingDesktopReleases.Clear();
            return true;
        }

        private string MoveOwnedItemToDesktop(FenceInfo info, string itemPath, Action<string> beforeMove = null,
            bool reuseEquivalentDesktopItem = true)
        {
            if (!ItemExists(itemPath) || !IsOwnedByFence(info, itemPath))
                return itemPath;
            var destinationPath = Path.Combine(desktopPath, GetItemName(itemPath));
            if (reuseEquivalentDesktopItem && File.Exists(itemPath) && File.Exists(destinationPath) &&
                FileContentComparer.AreEquivalent(itemPath, destinationPath))
            {
                beforeMove?.Invoke(destinationPath);
                File.Delete(itemPath);
                return destinationPath;
            }
            if (ItemExists(destinationPath))
                destinationPath = GetAvailablePath(desktopPath, GetItemName(itemPath), Directory.Exists(itemPath));
            var isDirectory = Directory.Exists(itemPath);
            beforeMove?.Invoke(destinationPath);
            MoveItem(itemPath, destinationPath);
            DesktopUtil.NotifyItemMoved(itemPath, destinationPath, isDirectory);
            return destinationPath;
        }

        private void WriteFence(FenceInfo fenceInfo)
        {
            var path = GetFolderPath(fenceInfo);
            EnsureDirectoryExists(path);
            var metaFile = Path.Combine(path, MetaFileName);
            var tempFile = Path.Combine(path, MetaFileName + ".tmp");
            var serializer = new XmlSerializer(typeof(FenceInfo));
            try
            {
                using (var writer = new StreamWriter(tempFile))
                    serializer.Serialize(writer, fenceInfo);
                if (File.Exists(metaFile))
                    File.Replace(tempFile, metaFile, null);
                else
                    File.Move(tempFile, metaFile);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        private string GetFolderPath(FenceInfo fenceInfo)
        {
            return Path.Combine(basePath, fenceInfo.Id.ToString());
        }

        private void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
        }

        private bool ContainsPath(IList<string> paths, string itemPath)
        {
            foreach (var path in paths)
                if (PathsEqual(path, itemPath))
                    return true;
            return false;
        }

        private bool IsOwnedByFence(FenceInfo info, string itemPath)
        {
            var parentPath = Path.GetDirectoryName(Path.GetFullPath(itemPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return PathsEqual(parentPath, GetFolderPath(info));
        }

        private bool ItemExists(string itemPath)
        {
            return File.Exists(itemPath) || Directory.Exists(itemPath);
        }

        private bool PathsEqual(string first, string second)
        {
            return string.Equals(
                Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private string GetAvailablePath(string directoryPath, string itemName, bool isDirectory)
        {
            var candidate = Path.Combine(directoryPath, itemName);
            if (!ItemExists(candidate))
                return candidate;
            var name = isDirectory ? itemName : Path.GetFileNameWithoutExtension(itemName);
            var extension = isDirectory ? string.Empty : Path.GetExtension(itemName);
            var suffix = 2;
            do
            {
                candidate = Path.Combine(directoryPath, string.Format("{0} ({1}){2}", name, suffix, extension));
                suffix++;
            }
            while (ItemExists(candidate));
            return candidate;
        }

        private string GetItemName(string itemPath)
        {
            return new FileInfo(itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
        }

        private void DeleteItem(string itemPath)
        {
            if (Directory.Exists(itemPath))
                Directory.Delete(itemPath, true);
            else if (File.Exists(itemPath))
                File.Delete(itemPath);
        }

        private void MoveItem(string sourcePath, string destinationPath)
        {
            EnsureDirectoryExists(Path.GetDirectoryName(destinationPath));
            if (Directory.Exists(sourcePath))
            {
                if (PathsEqual(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destinationPath)))
                {
                    Directory.Move(sourcePath, destinationPath);
                    return;
                }
                CopyDirectory(sourcePath, destinationPath);
                Directory.Delete(sourcePath, true);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        }

        private void CopyDirectory(string sourcePath, string destinationPath)
        {
            var source = new DirectoryInfo(sourcePath);
            if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("目录链接不能移动到其他驱动器上的分区中。");
            Directory.CreateDirectory(destinationPath);
            try
            {
                foreach (var file in source.GetFiles())
                    file.CopyTo(Path.Combine(destinationPath, file.Name));
                foreach (var directory in source.GetDirectories())
                    CopyDirectory(directory.FullName, Path.Combine(destinationPath, directory.Name));
            }
            catch
            {
                Directory.Delete(destinationPath, true);
                throw;
            }
        }
    }
}
