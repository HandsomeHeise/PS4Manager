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
            var theme = new UiComboBox { Left = 150, Top = 25, Width = 250 }; theme.Items.AddRange(new object[] { "Dark", "Light" }); theme.SelectedIndex = AppTheme.IsLight ? 1 : 0;
            var frame4 = new UiCheckBox { Text = "Enable Frame4 debugger and trainers", Left = 25, Top = 82, Width = 390, Height = 34, Checked = settings.Frame4.Enabled };
            var note = new UiLabel { Text = "Refresh an open tool after changing Frame4.", Left = 25, Top = 123, Width = 390, Height = 28, AutoSize = false };
            var save = new UiButton { Text = "Save", Left = 155, Top = 175, Width = 115, DialogResult = DialogResult.OK };
            var cancel = new UiButton { Text = "Cancel", Left = 285, Top = 175, Width = 115, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { new UiLabel { Text = "Appearance", Left = 25, Top = 33 }, theme, frame4, note, save, cancel });
            AcceptButton = save; CancelButton = cancel;
            save.Click += (s, e) => { settings.Frame4.Enabled = frame4.Checked; AppTheme.Current = theme.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark; settings.Theme = theme.SelectedIndex == 1 ? "Light" : "Dark"; };
        }
    }
}
