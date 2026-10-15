using System;
using System.IO;
using System.Windows.Forms;

namespace NoFences.Util
{
    internal static class ErrorReporter
    {
        internal static string Write(Exception exception)
        {
            try
            {
                var directory = AppDataPaths.LogsPath;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                File.WriteAllText(path, DateTimeOffset.Now + Environment.NewLine + exception);
                return path;
            }
            catch
            {
                return null; // Error reporting must not cause a second unhandled exception.
            }
        }

        internal static void ReportThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            var path = Write(e.Exception);
            MessageBox.Show("发生未处理的错误：\n\n" + e.Exception +
                (path == null ? "" : "\n\n错误详情已保存到：\n" + path),
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
