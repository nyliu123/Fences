using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NoFences.Util
{
    internal static class ShellShortcut
    {
        internal sealed class TargetInfo
        {
            internal string TargetPath { get; set; }
            internal string Arguments { get; set; }
            internal string WorkingDirectory { get; set; }
        }

        internal static TargetInfo Resolve(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) ||
                !string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                return null;
            object shell = null;
            object shortcut = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return null;
                shell = Activator.CreateInstance(shellType);
                shortcut = ((dynamic)shell).CreateShortcut(path);
                var target = Environment.ExpandEnvironmentVariables((string)((dynamic)shortcut).TargetPath);
                var workingDirectory = (string)((dynamic)shortcut).WorkingDirectory;
                return new TargetInfo
                {
                    TargetPath = target,
                    Arguments = (string)((dynamic)shortcut).Arguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Path.GetDirectoryName(target) : Environment.ExpandEnvironmentVariables(workingDirectory)
                };
            }
            catch (Exception)
            {
                // Broken or unsupported Shell links do not offer target-dependent actions.
                return null;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static string GetExistingTargetPath(string path)
        {
            var target = Resolve(path)?.TargetPath;
            return !string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target))
                ? Path.GetFullPath(target) : null;
        }

        internal static void OpenTargetLocation(string path)
        {
            var target = GetExistingTargetPath(path);
            if (target == null)
                throw new FileNotFoundException("快捷方式的目标已不存在，或目标不是文件／文件夹。", path);
            IntPtr item = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(target, IntPtr.Zero, out item, 0, out var attributes));
                // A zero item count opens the parent folder and selects this absolute item.
                Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0));
            }
            finally
            {
                if (item != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(item);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item,
            uint attributesIn, out uint attributesOut);
        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr items, uint flags);
    }
}
