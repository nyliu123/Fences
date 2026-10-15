using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

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
                object shell = null;
                object shortcut = null;
                try
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null)
                        return null;
                    shell = Activator.CreateInstance(shellType);
                    shortcut = ((dynamic)shell).CreateShortcut(path);
                    target = Environment.ExpandEnvironmentVariables((string)((dynamic)shortcut).TargetPath);
                    arguments = (string)((dynamic)shortcut).Arguments;
                    var shortcutDirectory = (string)((dynamic)shortcut).WorkingDirectory;
                    workingDirectory = string.IsNullOrWhiteSpace(shortcutDirectory)
                        ? Path.GetDirectoryName(target) : Environment.ExpandEnvironmentVariables(shortcutDirectory);
                }
                catch (Exception)
                {
                    // Broken or unsupported Shell links do not offer elevation.
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
