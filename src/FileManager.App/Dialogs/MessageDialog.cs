using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FileManager.App.Dialogs
{
    /// <summary>Shared, bounded skinned dialog. Closing always retains the cancel choice.</summary>
    public sealed class MessageDialog : AppForm
    {
        private readonly UiPanel body = new UiPanel { Dock = DockStyle.Fill };
        private readonly UiLabel heading;
        private readonly MessageTextBox content;
        private readonly List<UiButton> buttons = new List<UiButton>();
        private readonly UiCheckBox check;
        private readonly int defaultIndex, cancelIndex;
        private bool ready, arranging;

        public MessageDialog(string title, string message, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex, string checkText)
            : this(title, message, buttons, defaultIndex, cancelIndex, checkText, MessageKind.Information, null) { }

        public MessageDialog(string title, string message, IReadOnlyList<string> choices, int defaultIndex, int cancelIndex, string checkText, MessageKind kind, string details)
        {
            if (choices == null || choices.Count == 0) throw new ArgumentException("At least one button is required.", nameof(choices));
            if (defaultIndex < 0 || defaultIndex >= choices.Count) throw new ArgumentOutOfRangeException(nameof(defaultIndex));
            if (cancelIndex < 0 || cancelIndex >= choices.Count) throw new ArgumentOutOfRangeException(nameof(cancelIndex));
            this.defaultIndex = defaultIndex; this.cancelIndex = cancelIndex; Choice = cancelIndex;
            Text = title; ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            heading = new UiLabel { Text = MessageKinds.Caption(kind), AutoSize = false };
            content = new MessageTextBox { Text = (message ?? "") + (string.IsNullOrEmpty(details) ? "" : "\r\n\r\n" + details), AccessibleName = "Message details" };
            body.Controls.Add(heading); body.Controls.Add(content);
            if (checkText != null) { check = new UiCheckBox { Text = checkText }; body.Controls.Add(check); }
            for (int i = 0; i < choices.Count; i++) {
                int index = i;
                var button = new UiButton { Text = choices[i], AccessibleName = choices[i] };
                button.Click += (s, e) => { Choice = index; DialogResult = index == cancelIndex ? DialogResult.Cancel : DialogResult.OK; };
                this.buttons.Add(button); body.Controls.Add(button);
            }
            Controls.Add(body);
        }

        public int Choice { get; private set; }
        public bool Checked => check != null && check.Checked;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var measured = TextRenderer.MeasureText(content.Text, Font, new Size(Px(510), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            ClientSize = new Size(Px(560), Math.Min(Px(420), Math.Max(Px(200), measured.Height + Px(check == null ? 130 : 170))));
            ready = true; Arrange();
            if (Owner != null) CenterToParent(); else CenterToScreen();
            Arrange(); ActiveControl = buttons[defaultIndex];
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (ready) Arrange(); }
        private void Arrange()
        {
            if (arranging) return;
            arranging = true;
            try {
                var area = Screen.FromControl(Owner ?? this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
                int margin = Px(16), gap = Px(8), width = Math.Max(1, ClientSize.Width - margin * 2);
                heading.SetBounds(margin, margin, width, Px(28));
                int buttonHeight = Px(34), x = margin + width, bottom = ClientSize.Height - margin;
                foreach (var button in buttons.AsEnumerable().Reverse()) {
                    int w = Math.Min(width, Math.Max(Px(90), button.GetPreferredSize(Size.Empty).Width));
                    if (x - w < margin) { bottom -= buttonHeight + gap; x = margin + width; }
                    button.SetBounds(x - w, bottom - buttonHeight, w, buttonHeight); x -= w + gap;
                }
                int textBottom = bottom - buttonHeight - gap;
                if (check != null) { check.SetBounds(margin, textBottom - Px(28), width, Px(28)); textBottom -= Px(36); }
                int top = margin + Px(36);
                content.SetBounds(margin, top, width, Math.Max(1, textBottom - top));
            } finally { arranging = false; }
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Choice = cancelIndex; DialogResult = DialogResult.Cancel; return true; }
            if (keyData == Keys.Enter) { (buttons.FirstOrDefault(b => b.Focused) ?? buttons[defaultIndex]).PerformClick(); return true; }
            return base.ProcessDialogKey(keyData);
        }
        public static int Choose(IWin32Window owner, string title, string message, params string[] buttons)
        {
            using (var dialog = new MessageDialog(title, message, buttons, 0, buttons.Length - 1, null)) { dialog.ShowDialog(owner); return dialog.Choice; }
        }
        public static void Info(IWin32Window owner, string title, string message) { ShowMessage(owner, title, message, MessageKind.Information); }
        public static void ShowMessage(IWin32Window owner, string title, string message, MessageKind kind, string details = null)
        {
            using (var dialog = new MessageDialog(title, message, new[] { "OK" }, 0, 0, null, kind, details)) dialog.ShowDialog(owner);
        }
        public static bool Ask(IWin32Window owner, string title, string message, string yes, string no) { return Choose(owner, title, message, yes, no) == 0; }
    }

    internal sealed class MessageTextBox : UiTextBox
    {
        public MessageTextBox()
        {
            Multiline = true; ReadOnly = true;
            InnerTextBox.ScrollBars = ScrollBars.Vertical;
            InnerTextBox.WordWrap = true;
        }
    }
}
