using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class TitleEditorWindow : Form
    {
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_GETMINMAXINFO = 0x0024;
        private readonly TitleTextBox editor;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        public event EventHandler CommitRequested;
        public event EventHandler CancelRequested;
        public event Action<Point> ContextMenuRequested;

        public string EditedTitle => editor.Text;

        public TitleEditorWindow(string title, Font font, Color textColor,
            Color backgroundColor, Rectangle bounds)
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = backgroundColor;
            FormBorderStyle = FormBorderStyle.None;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            editor = new TitleTextBox
            {
                AutoSize = false,
                BackColor = backgroundColor,
                BorderStyle = BorderStyle.None,
                Font = font,
                ForeColor = textColor,
                HideSelection = false,
                Text = title,
                TextAlign = HorizontalAlignment.Center
            };
            editor.KeyDown += Editor_KeyDown;
            editor.ContextMenuRequested = point => ContextMenuRequested?.Invoke(point);
            Controls.Add(editor);
            Bounds = bounds;
            LayoutEditor();
            Shown += (sender, e) =>
            {
                Activate();
                editor.Focus();
                editor.SelectAll();
            };
            Deactivate += (sender, e) =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && !ContainsFocus)
                            CommitRequested?.Invoke(this, EventArgs.Empty);
                    }));
            };
        }

        public void UpdateAppearance(Font font, Color textColor,
            Color backgroundColor, Rectangle bounds)
        {
            BackColor = backgroundColor;
            editor.BackColor = backgroundColor;
            editor.ForeColor = textColor;
            editor.Font = font;
            Bounds = bounds;
            LayoutEditor();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutEditor();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WM_GETMINMAXINFO)
            {
                var limits = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
                limits.MinTrackSize.Y = 1;
                Marshal.StructureToPtr(limits, message.LParam, false);
            }
        }

        private void LayoutEditor()
        {
            if (editor == null)
                return;
            var height = Math.Min(ClientSize.Height, editor.PreferredHeight);
            var top = Math.Max(0, (ClientSize.Height - height) / 2) - 1;
            editor.Bounds = new Rectangle(0, top,
                ClientSize.Width, Math.Max(1, height));
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

        private sealed class TitleTextBox : TextBox
        {
            public Action<Point> ContextMenuRequested { get; set; }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WM_CONTEXTMENU)
                {
                    ContextMenuRequested?.Invoke(message.LParam == new IntPtr(-1)
                        ? MousePosition : new Point(unchecked((int)message.LParam.ToInt64())));
                    return;
                }
                base.WndProc(ref message);
            }
        }
    }
}
