using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FileManager.App;
using PS4Manager.Core.GoldHen;
using Ps4.Core.Titles;

namespace PS4Manager.App
{
    internal sealed class GoldHenPluginTargetDialog : AppForm
    {
        private const string Other = "Other title ID...";

        private readonly UiComboBox _targets = new UiComboBox();
        private readonly UiTextBox _custom = new UiTextBox();
        private readonly UiLabel _message = new UiLabel { Text = "Enable this plugin for:" };
        private readonly UiLabel _error = new UiLabel { AutoSize = true };
        private readonly UiButton _ok = new UiButton { Text = "OK" };
        private readonly UiButton _cancel = new UiButton { Text = "Cancel" };

        public GoldHenPluginTargetDialog(IReadOnlyList<GoldHenPluginTarget> targets, string currentTitleId)
        {
            Text = "Choose Title ID";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            foreach (var target in targets ?? new GoldHenPluginTarget[0])
                _targets.Items.Add(target);
            _targets.Items.Add(Other);
            SelectInitial(currentTitleId);

            _custom.Text = "";
            _custom.Enabled = IsOtherSelected;
            _targets.SelectedIndexChanged += (s, e) => _custom.Enabled = IsOtherSelected;

            _ok.Click += (s, e) => Accept();
            _cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.Add(_message);
            Controls.Add(_targets);
            Controls.Add(_custom);
            Controls.Add(_error);
            Controls.Add(_ok);
            Controls.Add(_cancel);
        }

        public string SelectedTitleId { get; private set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int margin = Px(16);
            int gap = Px(8);
            _message.Location = new Point(margin, margin);
            _message.Size = _message.GetPreferredSize(Size.Empty);
            _targets.SetBounds(margin, _message.Bottom + gap, Px(280), Px(28));
            _custom.SetBounds(margin, _targets.Bottom + gap, Px(280), Px(28));
            _error.Location = new Point(margin, _custom.Bottom + gap);
            _error.Size = new Size(Px(280), Px(28));
            _ok.Size = _ok.GetPreferredSize(Size.Empty);
            _cancel.Size = _cancel.GetPreferredSize(Size.Empty);
            _cancel.Location = new Point(margin + Px(280) - _cancel.Width, _error.Bottom + gap);
            _ok.Location = new Point(_cancel.Left - gap - _ok.Width, _cancel.Top);
            ClientSize = new Size(Px(312), _cancel.Bottom + margin);
            ActiveControl = _targets;
            if (Owner != null) CenterToParent(); else CenterToScreen();
        }

        private bool IsOtherSelected
        {
            get { return string.Equals(_targets.SelectedItem as string, Other, StringComparison.Ordinal); }
        }

        private void SelectInitial(string currentTitleId)
        {
            var normalized = string.IsNullOrEmpty(currentTitleId) ? null : currentTitleId;
            for (int i = 0; i < _targets.Items.Count; i++)
            {
                var target = _targets.Items[i] as GoldHenPluginTarget;
                if (target != null && string.Equals(target.TitleId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _targets.SelectedIndex = i;
                    return;
                }
            }
            _targets.SelectedIndex = _targets.Items.Count > 0 ? 0 : -1;
        }

        private void Accept()
        {
            var target = _targets.SelectedItem as GoldHenPluginTarget;
            if (target != null)
            {
                SelectedTitleId = target.TitleId;
                DialogResult = DialogResult.OK;
                return;
            }

            var custom = (_custom.Text ?? "").Trim().ToUpperInvariant();
            if (!TitleId.IsMatch(custom))
            {
                _error.Text = "Enter a Title ID like CUSA00411.";
                return;
            }
            SelectedTitleId = custom;
            DialogResult = DialogResult.OK;
        }
    }
}
