using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences.Util
{
    internal static class FenceShutdown
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        internal static void CloseAllWindows()
        {
            // Snapshot the forms because closing a Fence also closes its visual layers.
            foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray())
                fence.Close();
            WaitForWindowUpdates();
        }

        internal static void WaitForWindowUpdates()
        {
            // Finish submitting the window removals before Explorer is notified of files.
            DwmFlush();
        }
    }
}
