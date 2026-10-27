using NoFences.Model;
using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using NoFences.Win32;
using NoFences.Util;

namespace NoFences
{
    static class Program
    {
        internal const string AppName = "Fences";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (ExitRecovery.RunIfRequested(args))
                return;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += ErrorReporter.ReportThreadException;
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                    ErrorReporter.Write(exception);
            };
            //allows the context menu to be in dark mode
            //inherits from the system settings
            WindowUtil.SetPreferredAppMode(1);

            // Retain the legacy mutex so migration cannot race a compatible older release.
            using (var mutex = new Mutex(true, "No_fences", out var createdNew))
            {
                if (createdNew)
                {
                    try
                    {
                        ExitRecovery.Start();
                    }
                    catch (Exception exception)
                    {
                        MessageBox.Show(exception.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    try
                    {
                        using (var trayContext = new TrayApplicationContext())
                        {
                            try { trayContext.StartDesktopIntegration(); }
                            catch (Exception exception)
                            {
                                ErrorReporter.Write(exception);
                                MessageBox.Show("无法启动原生桌面视图，Windows 桌面保持可用。\n\n" + exception.Message,
                                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
                            var loadErrors = FenceManager.Instance.LoadFences();
                            if (loadErrors.Count > 0)
                            {
                                MessageBox.Show(
                                    "部分桌面项目无法加载：\n\n" + string.Join("\n", loadErrors),
                                    AppName,
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                            }
                            if (!Application.OpenForms.OfType<FenceWindow>().Any())
                                FenceManager.Instance.CreateFence("常用");

                            Application.Run(trayContext);
                        }
                    }
                    catch (Exception exception)
                    {
                        ErrorReporter.Write(exception);
                        MessageBox.Show("Fences 未能完成启动，已停止桌面接管。\n\n" + exception.Message,
                            AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        FenceShutdown.CloseAllWindows();
                        ExitRecovery.ReleaseItems();
                    }
                }
            }
        }

    }
}
