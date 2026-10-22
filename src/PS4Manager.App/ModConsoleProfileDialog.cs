using PS4Manager.UI;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using PS4Manager.Core.Config;

namespace PS4Manager.App
{
    internal sealed class ModConsoleProfileDialog : AppWindow
    {
        public ModProfileSettings Selected { get; private set; }
        public ModConsoleProfileDialog(ModSettings settings, string host)
        {
            Text = "Confirm console profile"; ClientSize = new Size(610, 280); MinimumSize = new Size(550, 280); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.Controls.Add(new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = "Connected to " + host + ".\r\nSelect the physical console whose mod backups belong here. An IP address does not prove console identity. Confirm again after each connection." }, 0, 0);
            var choices = new UiComboBox { Dock = DockStyle.Fill }; choices.Items.Add("Create a new console profile"); foreach (var p in settings.Profiles) choices.Items.Add(p);
            choices.SelectedIndex = 0; for (int i = 1; i < choices.Items.Count; i++) if (((ModProfileSettings)choices.Items[i]).Id == settings.SelectedProfileId) choices.SelectedIndex = i;
            var name = new UiTextBox { Dock = DockStyle.Fill, PlaceholderText = "New console name" }; layout.Controls.Add(choices, 0, 1); layout.Controls.Add(name, 0, 2);
            var confirm = new UiButton { Text = "This is the selected console", Dock = DockStyle.Fill }; var cancel = new UiButton { Text = "Cancel", Dock = DockStyle.Fill };
            confirm.Click += (s, e) => { Selected = choices.SelectedItem as ModProfileSettings; if (Selected == null) { if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Trim().Length > 80) { MessageDialog.ShowMessage(this,"Console profile", "Enter a console name (1–80 characters).",MessageKind.Warning); return; } Selected = new ModProfileSettings { Id = Guid.NewGuid(), Name = name.Text.Trim() }; } DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); }; layout.Controls.Add(confirm, 0, 3); layout.Controls.Add(cancel, 0, 4); Controls.Add(layout);
        }
    }
}
