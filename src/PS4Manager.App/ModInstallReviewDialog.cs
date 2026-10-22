using PS4Manager.UI;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PS4Manager.Core.Mods;

namespace PS4Manager.App
{
    internal sealed class ModInstallReviewDialog : AppWindow
    {
        public ModInstallReviewDialog(string title, string[] targets, string[] replacements, string backupRoot, bool warn, bool scripts, string action = "Install")
        {
            Text = action + " — " + title; ClientSize = new Size(800, 570); MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.Controls.Add(new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = "Only the listed files will change. Existing files are backed up before writing.\r\n" + (scripts ? "Script reload is optional after successful installation." : "Close the game now. After successful PRX installation, you can choose to enable the plugin in GoldHEN, or do so later in GoldHEN Manager. Start the game afterwards. Undeclared Lua files are not created.") + (warn ? "\r\nWarning: this package changes protected user/game directories." : "") }, 0, 0);
            layout.Controls.Add(new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = string.Join("\r\n\r\n", targets.Select(p => (replacements.Contains(p) ? "Replace / restore: " : "Add / restore absence: ") + p)) }, 0, 1);
            layout.Controls.Add(new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = "Local backups (retained until explicit cleanup):\r\n" + backupRoot }, 0, 2);
            var check = new UiCheckBox { AutoSize = true, Anchor = AnchorStyles.Left, Text = "The game is closed; I approve the listed file changes." }; layout.Controls.Add(check, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; var accept = new UiButton { Text = action, Width = 150, Height = 34, Enabled = false }; var cancel = new UiButton { Text = "Cancel", Width = 120, Height = 34 };
            check.CheckedChanged += (s, e) => accept.Enabled = check.Checked; accept.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); }; cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); }; buttons.Controls.Add(accept); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 4); Controls.Add(layout);
        }
    }
}
