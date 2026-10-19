using NoFences.Model;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NoFences.Util;

namespace NoFences
{
    public partial class FenceWindow
    {
        private string contextMenuItem;
        private ToolStripMenuItem runAsAdministratorMenuItem;
        private ToolStripMenuItem openShortcutLocationMenuItem;
        private ToolStripMenuItem recycleItemMenuItem;
        private ToolStripSeparator recycleItemSeparator;

        private void InitializeItemDeleteMenu()
        {
            recycleItemMenuItem = new ToolStripMenuItem("删除", null, RecycleContextItem)
            {
                Name = "recycleItemMenuItem",
                Font = deleteItemToolStripMenuItem.Font,
                AccessibleName = "删除（移入回收站）",
                ToolTipText = "将项目移入回收站"
            };
            recycleItemSeparator = new ToolStripSeparator();
            var index = appContextMenu.Items.IndexOf(deleteItemToolStripMenuItem);
            appContextMenu.Items.Insert(index, recycleItemSeparator);
            appContextMenu.Items.Insert(index + 2, recycleItemMenuItem);
        }

        private void InitializeShortcutLocationMenu()
        {
            openShortcutLocationMenuItem = new ToolStripMenuItem("打开文件所在位置", null, (sender, e) =>
            {
                try { ShellShortcut.OpenTargetLocation(contextMenuItem); }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "无法打开所在位置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }) { Name = "openShortcutLocationMenuItem" };
            appContextMenu.Items.Insert(appContextMenu.Items.IndexOf(runAsAdministratorMenuItem), openShortcutLocationMenuItem);
        }

        private void UpdateShortcutLocationMenu()
        {
            var target = ShellShortcut.GetExistingTargetPath(contextMenuItem);
            openShortcutLocationMenuItem.Visible = target != null;
            openShortcutLocationMenuItem.Text = Directory.Exists(target) ? "打开文件夹所在位置" : "打开文件所在位置";
        }

        private void RecycleContextItem(object sender, EventArgs e)
        {
            var path = contextMenuItem;
            if (string.IsNullOrEmpty(path))
                return;
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var question = Directory.Exists(path)
                ? "将文件夹“" + name + "”及其内容移入回收站？"
                : "将“" + name + "”移入回收站？";
            if (MessageBox.Show(this, question, "删除项目", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                if (itemEditor != null)
                    CancelItemRename();
                FenceManager.Instance.RecycleItem(fenceInfo, path);
                selectedItem = hoveringItem = draggedItem = contextMenuItem = null;
                Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法删除项目", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeAdministratorMenu()
        {
            runAsAdministratorMenuItem = new ToolStripMenuItem("以管理员身份运行", null, (sender, e) =>
            {
                try
                {
                    ExecutableLauncher.RunAsAdministrator(contextMenuItem);
                }
                catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
                {
                    // Cancelling the Windows elevation prompt is a normal outcome.
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "无法以管理员身份运行", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
            appContextMenu.Items.Insert(0, runAsAdministratorMenuItem);
        }

        private void ShowDragDropError(string message, string title)
        {
            // Return to the OLE drag source before opening a modal dialog.
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed)
                    MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }

        private void ClearItemHover()
        {
            hoveringItem = null;
            hoveringItemOrigin = Point.Empty;
            hoveringItemIconBounds = Rectangle.Empty;
            hoveringItemTextBounds = Rectangle.Empty;
            hasHoverUpdated = false;
            Refresh();
        }

        internal void ClearItemHoverIfOutside(Point screenPoint)
        {
            if (hoveringItem != null && !RectangleToScreen(ClientRectangle).Contains(screenPoint))
                ClearItemHover();
        }

        internal void CloseContextMenuIfOutside(Point screenPoint)
        {
            if (appContextMenu.Visible && !appContextMenu.Bounds.Contains(screenPoint))
                appContextMenu.Close(ToolStripDropDownCloseReason.AppClicked);
        }

        private string HitTestContextItem(Point point)
        {
            if (!ClientRectangle.Contains(point) || point.Y < titleHeight)
                return null;
            var x = itemPadding;
            var y = titleHeight + itemPadding - scrollOffset;
            string hit = null;
            foreach (var path in fenceInfo.Files)
            {
                if (!ItemExists(path))
                    continue;
                if (new Rectangle(x, y, itemWidth, itemHeight).Contains(point))
                    hit = path;
                // Expanded selected names are drawn last and can extend over the next row.
                if (string.Equals(selectedItem, path, StringComparison.OrdinalIgnoreCase))
                {
                    using (var graphics = CreateGraphics())
                    using (var format = new StringFormat { Alignment = StringAlignment.Center })
                    {
                        var height = itemIconSize + MeasureItemVisibleTextHeight(
                            graphics, FenceEntry.FromPath(path), true, format) + 7;
                        if (new Rectangle(x - 2, y - 2, itemWidth + 2, height).Contains(point))
                            return path;
                    }
                }
                x += itemWidth + itemSpacing;
                if (x + itemWidth > Width)
                {
                    x = itemPadding;
                    y += itemHeight + itemSpacing;
                }
            }
            return hit;
        }

        private void ShowFenceContextMenu(Point point)
        {
            ConfirmTitleEditBeforeContextMenu();
            contextMenuItem = HitTestContextItem(point);
            if (contextMenuItem != null && ModifierKeys.HasFlag(Keys.Shift))
                shellContextMenu.ShowContextMenu(new[] { new FileInfo(contextMenuItem) }, PointToScreen(point));
            else
                appContextMenu.Show(this, point);
        }

        private void TitleLayer_ContextMenuRequested(Point screenPoint)
        {
            ShowFenceContextMenu(PointToClient(screenPoint));
        }
    }
}
