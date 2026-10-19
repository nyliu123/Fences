using System;
using System.Diagnostics;
using System.IO;

namespace NoFences.Util
{
    internal static class ExecutableLauncher
    {
        public static bool CanRunAsAdministrator(string path)
        {
            return CreateStartInfo(path) != null;
        }

        public static void RunAsAdministrator(string path)
        {
            var startInfo = CreateStartInfo(path);
            if (startInfo == null)
                throw new InvalidOperationException("该项目不是可执行文件，或快捷方式的目标已不存在。");
            using (var process = Process.Start(startInfo)) { }
        }

        private static ProcessStartInfo CreateStartInfo(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            var target = path;
            var arguments = string.Empty;
            var workingDirectory = Path.GetDirectoryName(path);
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var shortcut = ShellShortcut.Resolve(path);
                if (shortcut == null)
                    return null;
                target = shortcut.TargetPath;
                arguments = shortcut.Arguments;
                workingDirectory = shortcut.WorkingDirectory;
            }
            var extension = Path.GetExtension(target);
            if (!File.Exists(target) ||
                !(string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)))
                return null;
            return new ProcessStartInfo(target, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = workingDirectory
            };
        }
    }
}
