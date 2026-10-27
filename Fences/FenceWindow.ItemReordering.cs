using NoFences.Util;
using NoFences.Model;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences
{
    public partial class FenceWindow
    {
        private ItemInsertionTarget? itemInsertionTarget;
        private ToolStripMenuItem moveItemEarlierMenuItem;
        private ToolStripMenuItem moveItemLaterMenuItem;
        private string contextReorderItem;
        private DateTime lastDragScrollUtc;

        private void InitializeItemReordering()
        {
            DragOver += FenceWindow_DragOver;
            moveItemEarlierMenuItem = new ToolStripMenuItem("向前移动", null, (sender, e) => MoveContextItem(-1));
            moveItemLaterMenuItem = new ToolStripMenuItem("向后移动", null, (sender, e) => MoveContextItem(1));
            appContextMenu.Items.Insert(0, moveItemLaterMenuItem);
            appContextMenu.Items.Insert(0, moveItemEarlierMenuItem);
        }

        private bool IsLocalItemDrag(IDataObject data)
        {
            if (activeDragItem == null)
                return false;
            var paths = data?.GetData(DataFormats.FileDrop) as string[];
            return activeDragItem != null && paths != null && paths.Length == 1 &&
                PathsEqual(paths[0], activeDragItem) && ContainsPath(fenceInfo.Files, activeDragItem);
        }

        private void FenceWindow_DragOver(object sender, DragEventArgs e)
        {
            if (IsLocalItemDrag(e.Data))
                UpdateItemInsertionTarget(e);
            else
                e.Effect = !lockedToolStripMenuItem.Checked && e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? ImportEffect(e.AllowedEffect) : DragDropEffects.None;
        }

        private void UpdateItemInsertionTarget(DragEventArgs e, bool allowScroll = true)
        {
            var cursor = PointToClient(new Point(e.X, e.Y));
            if (lockedToolStripMenuItem.Checked || cursor.Y < titleHeight || !ClientRectangle.Contains(cursor) || IsScrollbarPoint(cursor))
            {
                e.Effect = DragDropEffects.None;
                ClearItemInsertionTarget();
                return;
            }
            if (allowScroll && (DateTime.UtcNow - lastDragScrollUtc).TotalMilliseconds >= 100)
            {
                var edge = Math.Min(24, Math.Max(1, (Height - titleHeight) / 4));
                var direction = cursor.Y < titleHeight + edge ? -1 : cursor.Y > Height - edge ? 1 : 0;
                scrollOffset = Math.Max(0, Math.Min(Math.Max(0, scrollHeight), scrollOffset + direction * 12));
                lastDragScrollUtc = DateTime.UtcNow;
            }
            var visiblePaths = fenceInfo.Files.FindAll(ItemExists);
            var target = ItemInsertionLayout.Locate(cursor, visiblePaths.Count, itemLayoutWidth, titleHeight,
                scrollOffset, itemPadding, itemWidth, itemHeight, itemSpacing);
            var visibleIndex = target.Index;
            target.Index = visibleIndex == visiblePaths.Count ? fenceInfo.Files.Count :
                fenceInfo.Files.FindIndex(path => PathsEqual(path, visiblePaths[visibleIndex]));
            itemInsertionTarget = target;
            e.Effect = DragDropEffects.Move;
            Refresh();
        }

        private void ClearItemInsertionTarget()
        {
            if (!itemInsertionTarget.HasValue)
                return;
            itemInsertionTarget = null;
            Refresh();
        }

        private void CommitItemReorder(DragEventArgs e)
        {
            UpdateItemInsertionTarget(e, false);
            try
            {
                if (itemInsertionTarget.HasValue)
                    MoveItemToInsertionIndex(activeDragItem, itemInsertionTarget.Value.Index);
            }
            catch (Exception exception)
            {
                e.Effect = DragDropEffects.None;
                ShowDragDropError(exception.Message, "无法调整项目顺序");
            }
            finally
            {
                ClearItemInsertionTarget();
            }
        }

        private void MoveItemToInsertionIndex(string path, int insertionIndex)
        {
            if (lockedToolStripMenuItem.Checked || path == null)
                return;
            var originalOrder = new List<string>(fenceInfo.Files);
            var sourceIndex = fenceInfo.Files.FindIndex(item => PathsEqual(item, path));
            if (!ItemInsertionLayout.Move(fenceInfo.Files, sourceIndex, insertionIndex))
                return;
            try
            {
                Save();
                selectedItem = path;
            }
            catch
            {
                fenceInfo.Files.Clear();
                fenceInfo.Files.AddRange(originalOrder);
                throw;
            }
            finally
            {
                Refresh();
            }
        }

        private void UpdateItemReorderMenu()
        {
            contextReorderItem = contextMenuItem;
            var index = contextReorderItem == null ? -1 :
                fenceInfo.Files.FindIndex(path => PathsEqual(path, contextReorderItem));
            moveItemEarlierMenuItem.Visible = moveItemLaterMenuItem.Visible = index >= 0;
            moveItemEarlierMenuItem.Enabled = !lockedToolStripMenuItem.Checked && index > 0;
            moveItemLaterMenuItem.Enabled = !lockedToolStripMenuItem.Checked && index >= 0 && index < fenceInfo.Files.Count - 1;
        }

        private void MoveContextItem(int direction)
        {
            var index = fenceInfo.Files.FindIndex(path => PathsEqual(path, contextReorderItem));
            if (index < 0)
                return;
            try
            {
                MoveItemToInsertionIndex(contextReorderItem, direction < 0 ? index - 1 : index + 2);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法调整项目顺序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int MeasureItemVisibleTextHeight(Graphics graphics, FenceEntry entry,
            bool showFullText, StringFormat format)
        {
            var measuredHeight = (int)Math.Ceiling(graphics.MeasureString(GetDisplayName(entry),
                iconFont, new SizeF(itemWidth, showFullText ? 10000f : textHeight), format).Height);
            return showFullText ? Math.Max(textHeight, measuredHeight) : Math.Min(textHeight, measuredHeight);
        }

        private void DrawItemInsertionMarker(Graphics graphics)
        {
            if (!itemInsertionTarget.HasValue)
                return;
            var entry = FenceEntry.FromPath(activeDragItem);
            if (entry == null)
                return;
            var marker = itemInsertionTarget.Value.Marker;
            var showFullText = string.Equals(selectedItem, entry.Path, StringComparison.OrdinalIgnoreCase);
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                if (!showFullText)
                {
                    format.FormatFlags = StringFormatFlags.LineLimit;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                }
                marker.Y -= 2;
                marker.Height = itemIconSize + MeasureItemVisibleTextHeight(graphics, entry, showFullText, format) + 7;
            }
            using (var brush = new SolidBrush(Color.FromArgb(GetAppearance().BodyBackgroundColorArgb)))
                graphics.FillRectangle(brush, marker);
        }
    }
}
