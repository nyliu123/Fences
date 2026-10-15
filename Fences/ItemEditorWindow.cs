using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NoFences
{
    internal sealed class ItemEditorWindow : Form
    {
        private readonly TextBox editor;

        public event EventHandler CommitRequested;
        public event EventHandler CancelRequested;

        public string EditedName => editor.Text;

        public ItemEditorWindow(string name, bool isFile, Font font, Rectangle bounds)
        {
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
                Text = name,
                TextAlign = HorizontalAlignment.Center
            };
            editor.KeyDown += Editor_KeyDown;
            Controls.Add(editor);
            var editorSize = new Size(bounds.Width, editor.PreferredHeight);
            MinimumSize = editorSize;
            MaximumSize = editorSize;
            Bounds = new Rectangle(bounds.Location, editorSize);
            Deactivate += (sender, e) =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && !ContainsFocus)
                            CommitRequested?.Invoke(this, EventArgs.Empty);
                    }));
            };

            var selectionLength = isFile ? Path.GetFileNameWithoutExtension(name).Length : name.Length;
            Shown += (sender, e) =>
            {
                editor.Focus();
                editor.Select(0, selectionLength);
            };
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
