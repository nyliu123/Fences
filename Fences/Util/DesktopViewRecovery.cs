using NoFences.Model;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Serialization;

namespace NoFences.Util
{
    internal static class DesktopViewRecovery
    {
        internal const string SessionFileName = "desktop-view-session.xml";
        internal const string SettingsFileName = "desktop-view-settings.xml";

        internal static void WriteXml<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
                    new XmlSerializer(typeof(T)).Serialize(writer, value);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static T ReadXml<T>(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
                return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }

        internal static DesktopViewSession Record(IntPtr original, string root)
        {
            GetWindowThreadProcessId(original, out var shellId);
            using (var own = Process.GetCurrentProcess())
            using (var shell = Process.GetProcessById((int)shellId))
            {
                var result = new DesktopViewSession
                {
                    OwnerProcessId = own.Id, OwnerStartTime = own.StartTime.ToUniversalTime().Ticks,
                    OriginalWindow = original.ToInt64(), OriginalParent = GetParent(original).ToInt64(),
                    ShellProcessId = shell.Id, ShellStartTime = shell.StartTime.ToUniversalTime().Ticks,
                    OriginallyVisible = IsWindowVisible(original)
                };
                WriteXml(Path.Combine(root, SessionFileName), result);
                return result;
            }
        }

        internal static void Restore(DesktopViewSession state, string root)
        {
            var original = new IntPtr(state.OriginalWindow);
            var parent = new IntPtr(state.OriginalParent);
            GetWindowThreadProcessId(original, out var processId);
            if (IsWindow(original) && GetParent(original) == parent && processId == state.ShellProcessId &&
                ClassName(original) == "SysListView32" && ClassName(parent) == "SHELLDLL_DefView" &&
                IsSameProcess(state.ShellProcessId, state.ShellStartTime, false))
                ShowWindow(original, state.OriginallyVisible ? 8 : 0);
            var path = Path.Combine(root, SessionFileName);
            if (File.Exists(path)) File.Delete(path);
        }

        internal static void Recover() => RecoverFromRoot(AppDataPaths.RootPath);

        internal static void RecoverFromRoot(string root)
        {
            var path = Path.Combine(root, SessionFileName);
            if (!File.Exists(path)) return;
            var state = ReadXml<DesktopViewSession>(path);
            if (!IsSameProcess(state.OwnerProcessId, state.OwnerStartTime, true)) Restore(state, root);
        }

        private static bool IsSameProcess(int id, long start, bool keepOnAccessDenied)
        {
            try { using (var process = Process.GetProcessById(id)) return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == start; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return keepOnAccessDenied; }
        }

        private static string ClassName(IntPtr window)
        {
            var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); return name.ToString();
        }

        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    }
}
