using System;
using System.IO;

namespace NoFences.Util
{
    internal static class AppDataPaths
    {
        internal const string DirectoryName = "Fences";
        internal const string LegacyDirectoryName = "NoFences";

        internal static string RootPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DirectoryName);

        internal static string LogsPath => Path.Combine(RootPath, "Logs");
    }
}
