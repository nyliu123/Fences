using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NoFences
{
    public partial class EditDialog : Form
    {
        private readonly int selectionLength;

        public EditDialog(string oldName, bool isFile)
        {
            InitializeComponent();
            tbName.Text = oldName;
            selectionLength = isFile ? System.IO.Path.GetFileNameWithoutExtension(oldName).Length : oldName.Length;
            Shown += EditDialog_Shown;
        }

        public string NewName => tbName.Text;

        private void btnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbName.Text))
            {
                MessageBox.Show(this, "名称不能为空。", "无法重命名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tbName.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void EditDialog_Shown(object sender, EventArgs e)
        {
            tbName.Focus();
            tbName.Select(0, selectionLength);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }
}
