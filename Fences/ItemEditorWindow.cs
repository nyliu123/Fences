using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class ItemEditorWindow : Form
    {
        private readonly TextBox editor;
        private readonly bool wrapName;
        private readonly int preferredTop;
        private bool resizing;
        private int wrappedPadding = -1;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        public event EventHandler CommitRequested;
        public event EventHandler CancelRequested;

        public string EditedName => editor.Text;
        internal bool IsDismissing { get; private set; }

        public ItemEditorWindow(string name, bool isFile, Font font, Rectangle bounds)
            : this(name, isFile, font, bounds, false)
        {
        }

        internal ItemEditorWindow(string name, bool isFile, Font font, Rectangle bounds, bool wrapName)
        {
            this.wrapName = wrapName;
            preferredTop = bounds.Top;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.None;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            editor = new TextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Font = font,
                ForeColor = Color.Black,
                HideSelection = false,
                Multiline = wrapName,
                WordWrap = wrapName,
                Text = name,
                TextAlign = HorizontalAlignment.Center
            };
            editor.KeyDown += Editor_KeyDown;
            Controls.Add(editor);
            var editorSize = new Size(bounds.Width, wrapName ? Math.Max(bounds.Height, editor.PreferredHeight) : editor.PreferredHeight);
            MinimumSize = wrapName ? new Size(editorSize.Width, editor.PreferredHeight) : editorSize;
            MaximumSize = wrapName ? new Size(editorSize.Width, Screen.GetWorkingArea(bounds).Height) : editorSize;
            Bounds = new Rectangle(bounds.Location, editorSize);
            if (wrapName)
            {
                var handle = editor.Handle;
                ResizeWrappedEditor();
                editor.TextChanged += (sender, e) => ResizeWrappedEditor();
            }
            Deactivate += (sender, e) =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && !ContainsFocus)
                        {
                            IsDismissing = true;
                            try { CommitRequested?.Invoke(this, EventArgs.Empty); }
                            finally { IsDismissing = false; }
                        }
                    }));
            };

            var stem = Path.GetFileNameWithoutExtension(name);
            var selectionLength = isFile && !string.IsNullOrEmpty(stem) ? stem.Length : name.Length;
            Shown += (sender, e) =>
            {
                editor.Focus();
                editor.Select(0, selectionLength);
                ResizeWrappedEditor();
            };
        }

        private void ResizeWrappedEditor()
        {
            if (!wrapName || resizing || !editor.IsHandleCreated || IsDisposed) return;
            resizing = true;
            try
            {
                const uint GetLineCount = 0x00BA;
                var lines = Math.Max(1, SendMessage(editor.Handle, GetLineCount, IntPtr.Zero, IntPtr.Zero).ToInt32());
                var lineHeight = TextRenderer.MeasureText("Wg", editor.Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
                if (wrappedPadding < 0)
                    wrappedPadding = Math.Max(editor.Height - editor.ClientSize.Height, Height - lines * lineHeight);
                var work = Screen.GetWorkingArea(Bounds);
                var height = Math.Min(work.Height, Math.Max(MinimumSize.Height,
                    lines * lineHeight + wrappedPadding));
                SetBounds(Left, Math.Max(work.Top, Math.Min(preferredTop, work.Bottom - height)), Width, height);
            }
            finally { resizing = false; }
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
