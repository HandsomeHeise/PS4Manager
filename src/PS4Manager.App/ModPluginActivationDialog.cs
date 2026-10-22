using PS4Manager.UI;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PS4Manager.Core.Mods;

namespace PS4Manager.App
{
    internal sealed class ModPluginActivationDialog : AppWindow
    {
        private readonly UiButton later = new UiButton { Text = "Later", Width = 140, Height = 34 };
        public ModPluginActivationDialog(ModReceipt receipt, IReadOnlyList<ModGame> games)
        {
            Text = "Installation successful";
            ClientSize = new Size(660, 370); MinimumSize = new Size(570, 330);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            var game = games?.FirstOrDefault(g => g.TitleId == receipt.TitleId);
            var message = receipt.Title + " was installed successfully.\r\n\r\n" +
                "Enable the installed plugin(s) in GoldHEN for:\r\n" +
                (game?.Name ?? receipt.TitleId) + " (" + receipt.TitleId + ")\r\n\r\n" +
                string.Join("\r\n", ModPluginActivation.PluginPaths(receipt)) + "\r\n\r\n" +
                "Your GoldHEN configuration will be backed up first.\r\n" +
                "Then start (or restart) the game to load the plugin.\r\n" +
                "Choose Later to activate it yourself in GoldHEN Manager.";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.Controls.Add(new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = message }, 0, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var activate = new UiButton { Text = "Activate now", Width = 150, Height = 34 };
            activate.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            later.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(activate); buttons.Controls.Add(later); layout.Controls.Add(buttons, 0, 1); Controls.Add(layout);
            ActiveControl = later;
        }
        protected override void OnLoad(System.EventArgs e)
        {
            base.OnLoad(e);
            ActiveControl = later;
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            if (keyData == Keys.Enter) { (ActiveControl as UiButton ?? later).PerformClick(); return true; }
            return base.ProcessDialogKey(keyData);
        }
    }
}
