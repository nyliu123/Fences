using NoFences.Model;
using NoFences.Win32;
using NoFences.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly ToolStripMenuItem lockedMenuItem;
        private readonly ToolStripMenuItem minifyMenuItem;
        private readonly ToolStripMenuItem showFileExtensionsMenuItem;
        private readonly ToolStripMenuItem personalizationMenuItem;
        private readonly GlobalMouseClickMonitor mouseClickMonitor;
        private DesktopFileAccess desktopFileAccess;
        private DesktopIntegration desktopIntegration;

        public TrayApplicationContext()
        {
            lockedMenuItem = CreateBatchMenuItem("锁定所有分区", ApplyLocked);
            minifyMenuItem = CreateBatchMenuItem("收起所有分区", ApplyCanMinify);
            showFileExtensionsMenuItem = CreateBatchMenuItem("显示所有文件扩展名", ApplyShowFileExtensions);
            personalizationMenuItem = new ToolStripMenuItem("批量个性化...", null, PersonalizeAll);

            contextMenu = new ContextMenuStrip
            {
                RenderMode = ToolStripRenderMode.Professional
            };
            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                lockedMenuItem,
                minifyMenuItem,
                showFileExtensionsMenuItem,
                personalizationMenuItem,
                new ToolStripSeparator(),
                new ToolStripMenuItem("刷新", null, RefreshFences),
                new ToolStripMenuItem("新建桌面分区", null, CreateFence),
                new ToolStripSeparator(),
                new ToolStripMenuItem("退出 " + Program.AppName, null, ExitApplication)
            });
            contextMenu.Opening += ContextMenu_Opening;

            notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = contextMenu,
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = Program.AppName,
                Visible = true
            };

            mouseClickMonitor = new GlobalMouseClickMonitor();
            mouseClickMonitor.Clicked += MouseClickMonitor_Clicked;
            mouseClickMonitor.Moved += MouseClickMonitor_Moved;

        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                desktopFileAccess?.Dispose();
                desktopIntegration?.Dispose();
                mouseClickMonitor.Clicked -= MouseClickMonitor_Clicked;
                mouseClickMonitor.Moved -= MouseClickMonitor_Moved;
                mouseClickMonitor.Dispose();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                contextMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        internal void StartDesktopFileAccess()
        {
            desktopFileAccess = new DesktopFileAccess(GetFences, message =>
                notifyIcon.ShowBalloonTip(8000, "桌面项目恢复", message, ToolTipIcon.Warning));
            desktopFileAccess.Start();
        }

        internal void StartDesktopIntegration()
        {
            desktopIntegration = new DesktopIntegration(FenceManager.Instance, GetFences, message =>
                notifyIcon.ShowBalloonTip(8000, "桌面视图", message, ToolTipIcon.Warning));
            desktopIntegration.Start();
        }

        private ToolStripMenuItem CreateBatchMenuItem(string text, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private void ContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var fences = GetFences();
            var hasFences = fences.Count > 0;
            lockedMenuItem.Enabled = hasFences;
            minifyMenuItem.Enabled = hasFences;
            showFileExtensionsMenuItem.Enabled = hasFences;
            personalizationMenuItem.Enabled = hasFences;

            lockedMenuItem.CheckState = GetCheckState(fences, fence => fence.IsLocked);
            minifyMenuItem.CheckState = GetCheckState(fences, fence => fence.CanMinify);
            showFileExtensionsMenuItem.CheckState = GetCheckState(fences, fence => fence.ShowFileExtensions);
        }

        private void MouseClickMonitor_Clicked(object sender, MouseClickedEventArgs e)
        {
            foreach (var fence in GetFences())
            {
                fence.CommitRenameIfOutside(e.ScreenPoint);
                fence.ClearItemSelectionIfOutside(e.ScreenPoint);
                fence.CloseContextMenuIfOutside(e.ScreenPoint);
            }
        }

        private void MouseClickMonitor_Moved(object sender, MouseClickedEventArgs e)
        {
            foreach (Form form in Application.OpenForms)
                (form as FenceWindow)?.ClearItemHoverIfOutside(e.ScreenPoint);
        }

        private void ApplyLocked(object sender, EventArgs e)
        {
            var value = lockedMenuItem.CheckState != CheckState.Checked;
            foreach (var fence in GetFences())
                fence.SetLocked(value);
        }

        private void ApplyCanMinify(object sender, EventArgs e)
        {
            var value = minifyMenuItem.CheckState != CheckState.Checked;
            foreach (var fence in GetFences())
                fence.SetCanMinify(value);
        }

        private void ApplyShowFileExtensions(object sender, EventArgs e)
        {
            var value = showFileExtensionsMenuItem.CheckState != CheckState.Checked;
            foreach (var fence in GetFences())
                fence.SetShowFileExtensions(value);
        }

        private void PersonalizeAll(object sender, EventArgs e)
        {
            var fences = GetFences();
            if (fences.Count == 0)
                return;

            using (var dialog = fences[0].CreatePersonalizationDialog(true))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                foreach (var fence in fences)
                    fence.ApplyPersonalization(dialog.Appearance);
            }
        }

        private void RefreshFences(object sender, EventArgs e)
        {
            if (desktopIntegration != null)
            {
                try { desktopIntegration.Refresh(); }
                catch (Exception exception)
                {
                    ErrorReporter.Write(exception);
                    notifyIcon.ShowBalloonTip(8000, "无法刷新桌面", exception.Message, ToolTipIcon.Warning);
                }
                return;
            }
            if (desktopFileAccess == null)
                StartDesktopFileAccess();
            desktopFileAccess.Refresh();
        }

        private void CreateFence(object sender, EventArgs e)
        {
            FenceManager.Instance.CreateFence("新建分区");
        }

        private void ExitApplication(object sender, EventArgs e)
        {
            desktopFileAccess?.Dispose();
            desktopFileAccess = null;
            FenceShutdown.CloseAllWindows();
            desktopIntegration?.Dispose();
            desktopIntegration = null;
            notifyIcon.Visible = false;
            Application.Exit();
        }

        private List<FenceWindow> GetFences()
        {
            return Application.OpenForms.OfType<FenceWindow>().ToList();
        }

        private CheckState GetCheckState(IList<FenceWindow> fences, Func<FenceWindow, bool> selector)
        {
            if (fences.Count == 0)
                return CheckState.Unchecked;

            var enabledCount = fences.Count(selector);
            if (enabledCount == 0)
                return CheckState.Unchecked;
            return enabledCount == fences.Count ? CheckState.Checked : CheckState.Indeterminate;
        }
    }
}
