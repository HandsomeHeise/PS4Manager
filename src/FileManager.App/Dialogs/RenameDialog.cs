using PS4Manager.UI;
using System;
using System.Drawing;
using System.Windows.Forms;
using FileManager.App.Views;

namespace FileManager.App.Dialogs
{
    /// <summary>Umbenennen in der Kachelansicht — dort gibt es keine Zellbearbeitung (Spec „Umbenennen").</summary>
    internal sealed class RenameDialog : AppForm
    {
        private readonly UiPanel _body = new UiPanel { Dock = DockStyle.Fill };
        private readonly UiLabel _label;
        private readonly EditBox _name = new EditBox();
        private readonly UiButton _ok = new UiButton { Text = "Rename" };
        private readonly UiButton _cancel = new UiButton { Text = "Cancel" };

        public RenameDialog(string currentName)
        {
            Text = "Rename";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            _label = new UiLabel { Text = "New name for “" + currentName + "”:" };
            _name.Text = currentName;
            _ok.Click += (s, e) => Confirm();
            _cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            foreach (Control c in new Control[] { _label, _name, _ok, _cancel }) _body.Controls.Add(c);
            Controls.Add(_body);
        }

        public string NewName { get; private set; }

        private void Confirm()
        {
            NewName = _name.Text;
            DialogResult = DialogResult.OK;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int margin = Px(16);
            int gap = Px(8);
            _label.Size = _label.GetPreferredSize(Size.Empty);
            _ok.Size = _ok.GetPreferredSize(Size.Empty);
            _cancel.Size = _cancel.GetPreferredSize(Size.Empty);
            int width = Math.Max(Px(320), _label.Width);

            int y = margin;
            _label.Location = new Point(margin, y); y += _label.Height + Px(4);
            _name.SetBounds(margin, y, width, _name.Height); y += _name.Height + margin;
            _cancel.Location = new Point(margin + width - _cancel.Width, y);
            _ok.Location = new Point(_cancel.Left - gap - _ok.Width, y);
            ClientSize = new Size(width + 2 * margin, y + _ok.Height + margin);
            if (Owner != null) CenterToParent(); else CenterToScreen();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _name.FocusText();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter) { Confirm(); return true; }
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessDialogKey(keyData);
        }
    }
}
