using NoFences.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NoFences
{
    // The editor deliberately stays a normal top-level window. Attaching it to
    // Explorer puts its native EDIT back into the failing desktop compositor.
    internal sealed class DesktopRenameController : IDisposable
    {
        private readonly IntPtr context;
        private readonly Action<Exception> failed;
        private IntPtr target;
        private ItemEditorWindow editor;
        private Font editingFont;
        private bool completing;
        private bool disposed;
        internal ItemEditorWindow Editor => editor;

        internal DesktopRenameController(IntPtr context, Action<Exception> failed)
        {
            this.context = context; this.failed = failed;
        }

        internal void Begin()
        {
            if (disposed) return;
            var pendingSession = IntPtr.Zero;
            try
            {
                var name = new StringBuilder(32768);
                var result = DesktopViewNative.FencesTakeRename(context, name, (uint)name.Capacity, out var area, out var file, out pendingSession);
                if (result == 1) return;
                Marshal.ThrowExceptionForHR(result);
                if ((editor != null && !Complete(true)) || disposed)
                {
                    DesktopViewNative.FencesReleaseRename(pendingSession); return;
                }
                target = pendingSession; pendingSession = IntPtr.Zero;
                DesktopViewNative.FencesActivateRename(context, target);
                var font = DesktopViewNative.FencesCaptionMetrics(context, out var client, out var pixels);
                using (var native = Font.FromHfont(font)) editingFont = new Font(native.FontFamily, pixels, native.Style, GraphicsUnit.Pixel);
                var bounds = area.Rectangle;
                var work = Screen.GetWorkingArea(new Point(bounds.Left + bounds.Width / 2, bounds.Top));
                var width = Math.Min(work.Width, bounds.Width);
                bounds = new Rectangle(Math.Max(work.Left, Math.Min(bounds.Left + (bounds.Width - width) / 2, work.Right - width)),
                    bounds.Top, width, bounds.Height);
                editor = new ItemEditorWindow(name.ToString(), file, editingFont, bounds, true);
                editor.Top = Math.Max(work.Top, Math.Min(editor.Top, work.Bottom - editor.Height));
                editor.CommitRequested += (sender, e) => Complete(true);
                editor.CancelRequested += (sender, e) => Complete(false);
                var current = editor;
                current.Show(); if (!current.IsDisposed) current.Activate();
            }
            catch (Exception error)
            {
                if (pendingSession != IntPtr.Zero) DesktopViewNative.FencesReleaseRename(pendingSession);
                Complete(false); if (!disposed) failed(error);
            }
        }

        private bool Complete(bool commit)
        {
            if (completing || disposed) return false;
            completing = true;
            var restoreFocus = !disposed && editor != null && !editor.IsDismissing && editor.ContainsFocus;
            try
            {
                if (commit && editor != null)
                {
                    var result = DesktopViewNative.FencesCommitRename(target, editor.Handle, editor.EditedName);
                    if (disposed) return false;
                    if (result < 0)
                    {
                        MessageBox.Show(editor, Marshal.GetExceptionForHR(result).Message, "无法重命名项目", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        if (!disposed) editor.Activate(); return false;
                    }
                }
                CloseEditor(!disposed && restoreFocus);
                return true;
            }
            finally { completing = false; }
        }

        private void CloseEditor(bool restoreFocus)
        {
            var previous = editor; editor = null;
            previous?.Close(); previous?.Dispose();
            if (target != IntPtr.Zero)
            {
                DesktopViewNative.FencesEndRename(context, restoreFocus);
                DesktopViewNative.FencesReleaseRename(target); target = IntPtr.Zero;
            }
            editingFont?.Dispose(); editingFont = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; CloseEditor(false);
        }
    }
}
