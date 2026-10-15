using System;
using System.Drawing;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class OpaqueTitleWindow : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        private readonly TitleTextBox editor;
        private TitleEditorWindow activeEditor;
        private bool isEditing;

        public event EventHandler CommitRequested;
        public event EventHandler CancelRequested;
        public event Action<Point> ContextMenuRequested;

        public bool IsEditing => isEditing;
        public string EditedTitle => activeEditor?.EditedTitle ?? editor.Text;
        public bool EditorContains(Point screenPoint) =>
            activeEditor != null && activeEditor.Bounds.Contains(screenPoint);

        public OpaqueTitleWindow(string title, Font font, Color textColor, Color backgroundColor)
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = backgroundColor;
            FormBorderStyle = FormBorderStyle.None;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = title;

            editor = new TitleTextBox
            {
                AutoSize = false,
                BackColor = BackColor,
                BorderStyle = BorderStyle.None,
                Font = font,
                ForeColor = textColor,
                HideSelection = false,
                ReadOnly = true,
                PassThrough = true,
                TabStop = false,
                Visible = false,
                Text = title,
                TextAlign = HorizontalAlignment.Center
            };
            editor.KeyDown += Editor_KeyDown;
            editor.TextChanged += (sender, e) => RefreshTitle();
            editor.ContextMenuRequested = point => ContextMenuRequested?.Invoke(point);
            Controls.Add(editor);
            Deactivate += OpaqueTitleWindow_Deactivate;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var createParams = base.CreateParams;
                createParams.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                return createParams;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x007B)
            {
                ContextMenuRequested?.Invoke(message.LParam == new IntPtr(-1) ? MousePosition : new Point(unchecked((int)message.LParam.ToInt64())));
                return;
            }
            if (message.Msg == WM_NCHITTEST && !isEditing)
            {
                message.Result = new IntPtr(HTTRANSPARENT);
                return;
            }

            base.WndProc(ref message);
        }

        public void UpdateTitle(string title, Font font, Color textColor, Color backgroundColor)
        {
            Text = title;
            BackColor = backgroundColor;
            editor.BackColor = backgroundColor;
            editor.Font = font;
            editor.ForeColor = textColor;
            if (!isEditing)
                editor.Text = title;
            LayoutEditor();
            activeEditor?.UpdateAppearance(font, textColor, backgroundColor,
                RectangleToScreen(ClientRectangle));
            UpdateEditorRegion();
            RefreshTitle();
        }

        public void BeginEdit()
        {
            if (isEditing)
                return;

            isEditing = true;
            editor.ReadOnly = false;
            editor.HideSelection = false;
            editor.PassThrough = false;
            editor.TabStop = true;
            editor.Cursor = Cursors.IBeam;
            RefreshTitle();
            activeEditor = new TitleEditorWindow(editor.Text, editor.Font,
                editor.ForeColor, BackColor, RectangleToScreen(ClientRectangle));
            UpdateEditorRegion();
            activeEditor.CommitRequested += ActiveEditor_CommitRequested;
            activeEditor.CancelRequested += ActiveEditor_CancelRequested;
            activeEditor.ContextMenuRequested += ActiveEditor_ContextMenuRequested;
            activeEditor.Show();
        }

        private void UpdateEditorRegion()
        {
            if (activeEditor == null)
                return;
            var previous = activeEditor.Region;
            var region = Parent?.Region?.Clone();
            if (region != null)
            {
                region.Translate(-Left, -Top);
                region.Intersect(ClientRectangle);
            }
            activeEditor.Region = region;
            previous?.Dispose();
        }

        public void EndEdit(string title)
        {
            isEditing = false;
            CloseActiveEditor();
            Text = title;
            editor.Text = title;
            editor.SelectionStart = 0;
            editor.SelectionLength = 0;
            editor.ReadOnly = true;
            editor.HideSelection = true;
            editor.PassThrough = true;
            editor.TabStop = false;
            editor.Cursor = Cursors.Default;
            editor.Visible = false;
            ActiveControl = null;
            editor.Invalidate();
            editor.Update();
            RefreshTitle();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CloseActiveEditor();
            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutEditor();
            RefreshTitle();
        }

        private void RefreshTitle()
        {
            if (editor == null || !IsHandleCreated || IsDisposed || Disposing ||
                ClientSize.Width < 1 || ClientSize.Height < 1)
                return;

            using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(editor.ForeColor))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                graphics.Clear(Color.Transparent);
                if (isEditing)
                {
                    graphics.Clear(BackColor);
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    graphics.DrawString(editor.Text, editor.Font, brush, ClientRectangle, format);
                }
                LayeredVisualWindow.PresentBitmap(this, bitmap);
            }
        }

        private void LayoutEditor()
        {
            if (editor == null)
                return;

            var editorHeight = Math.Min(ClientSize.Height, editor.PreferredHeight);
            editor.Bounds = new Rectangle(
                0,
                Math.Max(0, (ClientSize.Height - editorHeight) / 2),
                ClientSize.Width,
                Math.Max(1, editorHeight));
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

        private void OpaqueTitleWindow_Deactivate(object sender, EventArgs e)
        {
            if (activeEditor == null && IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)(() =>
                {
                    if (isEditing && !ContainsFocus)
                        CommitRequested?.Invoke(this, EventArgs.Empty);
                }));
        }

        private void ActiveEditor_CommitRequested(object sender, EventArgs e)
        {
            CommitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ActiveEditor_CancelRequested(object sender, EventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ActiveEditor_ContextMenuRequested(Point screenPoint)
        {
            ContextMenuRequested?.Invoke(screenPoint);
        }

        private void CloseActiveEditor()
        {
            if (activeEditor == null)
                return;
            var closing = activeEditor;
            activeEditor = null;
            closing.CommitRequested -= ActiveEditor_CommitRequested;
            closing.CancelRequested -= ActiveEditor_CancelRequested;
            closing.ContextMenuRequested -= ActiveEditor_ContextMenuRequested;
            closing.Close();
            closing.Dispose();
        }

        private sealed class TitleTextBox : TextBox
        {
            public bool PassThrough { get; set; }
            public Action<Point> ContextMenuRequested { get; set; }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x007B)
                {
                    ContextMenuRequested?.Invoke(message.LParam == new IntPtr(-1) ? MousePosition : new Point(unchecked((int)message.LParam.ToInt64())));
                    return;
                }
                if (message.Msg == WM_NCHITTEST && PassThrough)
                {
                    message.Result = new IntPtr(HTTRANSPARENT);
                    return;
                }

                base.WndProc(ref message);
            }
        }
    }
}
