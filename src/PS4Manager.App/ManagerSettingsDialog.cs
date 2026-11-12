using System;
using System.Drawing;
using System.Windows.Forms;
using PS4Manager.Core.Config;
using PS4Manager.UI;

namespace PS4Manager.App
{
    internal sealed class ManagerSettingsDialog : AppWindow
    {
        public ManagerSettingsDialog(ManagerSettings settings)
        {
            Text = "Settings"; ClientSize = new Size(440, 230); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var rows = new TableLayoutPanel {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 4, Padding = new Padding(24), Margin = Padding.Empty
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var theme = new UiComboBox { Width = 250, MinimumSize = new Size(200, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            theme.Items.AddRange(new object[] { "Dark", "Light" }); theme.SelectedIndex = AppTheme.IsLight ? 1 : 0;
            var frame4 = new UiCheckBox { Text = "Enable Frame4 debugger and trainers", AutoSize = true, Anchor = AnchorStyles.Left, Checked = settings.Frame4.Enabled, Margin = new Padding(3, 16, 3, 8) };
            var note = new UiLabel { Text = "Refresh an open tool after changing Frame4.", Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 16) };
            var save = new UiButton { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new UiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, WrapContents = false, Margin = Padding.Empty };
            actions.Controls.AddRange(new Control[] { save, cancel });
            rows.Controls.Add(new UiLabel { Text = "Appearance", Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 16, 3) }, 0, 0);
            rows.Controls.Add(theme, 1, 0);
            rows.Controls.Add(frame4, 0, 1); rows.SetColumnSpan(frame4, 2);
            rows.Controls.Add(note, 0, 2); rows.SetColumnSpan(note, 2);
            rows.Controls.Add(actions, 0, 3); rows.SetColumnSpan(actions, 2);
            Controls.Add(rows);
            bool fitting = false;
            Action fit = () => {
                if (fitting || IsDisposed) return;
                fitting = true;
                try {
                    // A docked percent-column table and Form.AutoSize otherwise
                    // feed the current (shrinking) width back into each other.
                    var preferred = rows.GetPreferredSize(Size.Empty);
                    var size = new Size(Math.Max(DpiScale.Scale(440, DeviceDpi), preferred.Width), preferred.Height);
                    if (ClientSize != size) ClientSize = size;
                }
                finally { fitting = false; }
            };
            rows.Layout += (s, e) => fit();
            FontChanged += (s, e) => fit();
            Shown += (s, e) => fit();
            fit();
            AcceptButton = save; CancelButton = cancel;
            save.Click += (s, e) => { settings.Frame4.Enabled = frame4.Checked; AppTheme.Current = theme.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark; settings.Theme = theme.SelectedIndex == 1 ? "Light" : "Dark"; };
        }
    }
}
