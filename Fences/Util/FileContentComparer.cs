using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NoFences.Util
{
    internal static class FileContentComparer
    {
        public static bool AreEquivalent(string firstPath, string secondPath)
        {
            if (!File.Exists(firstPath) || !File.Exists(secondPath))
                return false;

            if (string.Equals(Path.GetExtension(firstPath), ".lnk", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(secondPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                ShortcutContent firstShortcut;
                ShortcutContent secondShortcut;
                if (TryReadShortcut(firstPath, out firstShortcut) && TryReadShortcut(secondPath, out secondShortcut))
                    return firstShortcut.Equals(secondShortcut);
            }

            var firstInfo = new FileInfo(firstPath);
            var secondInfo = new FileInfo(secondPath);
            if (firstInfo.Length != secondInfo.Length)
                return false;

            const int bufferSize = 81920;
            var firstBuffer = new byte[bufferSize];
            var secondBuffer = new byte[bufferSize];
            using (var firstStream = File.OpenRead(firstPath))
            using (var secondStream = File.OpenRead(secondPath))
            {
                int bytesRead;
                while ((bytesRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length)) > 0)
                {
                    if (secondStream.Read(secondBuffer, 0, bytesRead) != bytesRead)
                        return false;

                    for (var index = 0; index < bytesRead; index++)
                        if (firstBuffer[index] != secondBuffer[index])
                            return false;
                }
            }

            return true;
        }

        private static bool TryReadShortcut(string path, out ShortcutContent content)
        {
            content = null;
            object shell = null;
            object shortcut = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return false;

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { path });
                if (shortcut == null)
                    return false;

                var shortcutType = shortcut.GetType();
                content = new ShortcutContent(
                    GetShortcutProperty(shortcutType, shortcut, "TargetPath"),
                    GetShortcutProperty(shortcutType, shortcut, "Arguments"),
                    GetShortcutProperty(shortcutType, shortcut, "WorkingDirectory"));
                return !string.IsNullOrEmpty(content.TargetPath);
            }
            catch (COMException)
            {
                return false;
            }
            catch (System.Reflection.TargetInvocationException)
            {
                return false;
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        private static string GetShortcutProperty(Type shortcutType, object shortcut, string propertyName)
        {
            return (shortcutType.InvokeMember(
                propertyName,
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string ?? string.Empty).Trim();
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        private sealed class ShortcutContent
        {
            public string TargetPath { get; }
            private string Arguments { get; }
            private string WorkingDirectory { get; }

            public ShortcutContent(string targetPath, string arguments, string workingDirectory)
            {
                TargetPath = NormalizePath(targetPath);
                Arguments = arguments;
                WorkingDirectory = NormalizePath(workingDirectory);
            }

            public override bool Equals(object obj)
            {
                var other = obj as ShortcutContent;
                return other != null &&
                    string.Equals(TargetPath, other.TargetPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Arguments, other.Arguments, StringComparison.Ordinal) &&
                    string.Equals(WorkingDirectory, other.WorkingDirectory, StringComparison.OrdinalIgnoreCase);
            }

            public override int GetHashCode()
            {
                return 0;
            }

            private static string NormalizePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return string.Empty;

                try
                {
                    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch (Exception)
                {
                    return path.Trim();
                }
            }
        }
    }
}
