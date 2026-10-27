using NoFences.Model;
using NoFences.Util;
using System;
using System.Linq;
using System.Windows.Forms;

namespace NoFences
{
    public partial class FenceWindow
    {
        private bool originalDragWasDropped;
        private bool originalDragWasCancelled;
        private Keys originalDragModifiers;

        private DragDropEffects ImportEffect(DragDropEffects allowed)
        {
            if (!FenceManager.Instance.UsesOriginalPaths(fenceInfo)) return allowed & DragDropEffects.Move;
            // A grouping reference must not claim a copy/move that never happened.
            // Copy-only sources may delete their temporary extraction after Drop returns.
            return allowed & DragDropEffects.Link;
        }

        private void DropOriginalItems(DragEventArgs e, string[] paths)
        {
            var effect = ImportEffect(e.AllowedEffect);
            if (effect == DragDropEffects.None) return;
            try
            {
                FenceManager.Instance.AssignOriginalItems(fenceInfo, paths);
                e.Effect = effect;
                foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) fence.Refresh();
            }
            catch (Exception exception)
            {
                e.Effect = DragDropEffects.None;
                ShowDragDropError(exception.Message, "无法添加项目");
            }
        }

        private void TrackOriginalDrag(object sender, QueryContinueDragEventArgs e)
        {
            originalDragModifiers = ((e.KeyState & 8) != 0 ? Keys.Control : Keys.None) |
                ((e.KeyState & 32) != 0 ? Keys.Alt : Keys.None);
            if (e.EscapePressed || e.Action == DragAction.Cancel) originalDragWasCancelled = true;
            if (e.Action == DragAction.Drop) originalDragWasDropped = true;
        }

        private bool CanReleaseOriginalToDesktop(string path, DragDropEffects effect, System.Drawing.Point point, Keys modifiers) =>
            path != null && (effect == DragDropEffects.None || effect == DragDropEffects.Move) &&
            (modifiers & (Keys.Control | Keys.Alt)) == 0 && !originalDragWasCancelled &&
            FenceManager.Instance.UsesOriginalPaths(fenceInfo) && ContainsPath(fenceInfo.Files, path) && ItemExists(path) &&
            FenceManager.Instance.IsOnDesktop(path) && DesktopIntegration.IsBackgroundPoint(point);

        private void UpdateOriginalDragFeedback(GiveFeedbackEventArgs e, System.Drawing.Point point, Keys modifiers)
        {
            e.UseDefaultCursors = true;
            // Shell rejects a move back to the file's existing directory. Fences
            // can still release its membership there without moving that file.
            if (e.Effect != DragDropEffects.None || !CanReleaseOriginalToDesktop(activeDragItem, e.Effect, point, modifiers)) return;
            e.UseDefaultCursors = false;
            Cursor.Current = Cursors.Default;
        }

        private void CompleteOriginalDesktopDrop(string path, DragDropEffects effect)
        {
            if (!originalDragWasDropped || !CanReleaseOriginalToDesktop(path, effect, MousePosition, originalDragModifiers)) return;
            FenceManager.Instance.ReleaseItem(fenceInfo, path);
            selectedItem = hoveringItem = null;
        }
    }
}
