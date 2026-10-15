using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Threading;
using System.Windows.Forms;
using NoFences;

internal static class BrandingCompatibilityTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        var assembly = typeof(FenceWindow).Assembly;
        var product = FileVersionInfo.GetVersionInfo(assembly.Location);
        Check(assembly.GetName().Name == "Fences" && product.ProductName == "Fences" && product.FileDescription == "Fences",
            "assembly and executable product metadata use Fences");
        var resources = new ResourceManager("NoFences.FenceWindow", assembly);
        Check(assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo("zh-CN")).GetName().Name == "Fences.resources",
            "Chinese satellite assembly is distributed with the new assembly name");
        Check(resources.GetString("titleSizeToolStripMenuItem.Text", CultureInfo.GetCultureInfo("zh-CN")) == "个性化...",
            "renamed satellite assembly resolves legacy resource namespace");
        using (var mutex = new Mutex(false, "No_fences"))
        using (var process = Process.Start(new ProcessStartInfo(assembly.Location)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        {
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                process.WaitForExit();
                throw new Exception("Renamed application did not respect the existing single-instance mutex");
            }
            Check(process.ExitCode == 0, "existing single-instance mutex prevents a second application loading storage");
        }
        var type = assembly.GetType("NoFences.TrayApplicationContext");
        using (var context = (IDisposable)Activator.CreateInstance(type, true))
        using (var host = new Form { Text = "Fences branding test", ClientSize = new Size(360, 300) })
        {
            var icon = (NotifyIcon)type.GetField("notifyIcon", Instance).GetValue(context);
            var menu = (ContextMenuStrip)type.GetField("contextMenu", Instance).GetValue(context);
            Check(icon.Text == "Fences" && menu.Items[menu.Items.Count - 1].Text == "退出 Fences",
                "tray tooltip and exit action use the new product name");
            host.Show();
            menu.Show(host, new Point(20, 20));
            Application.DoEvents();
            var output = Path.GetFullPath(args.Length == 0 ? "Fences/obj/VisualChecks" : args[0]);
            Directory.CreateDirectory(output);
            using (var image = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                image.Save(Path.Combine(output, "fences-tray-menu.png"));
            }
            menu.Close();
            host.Close();
        }
        Console.WriteLine("PASS: branding, resources and compatibility checks completed");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
