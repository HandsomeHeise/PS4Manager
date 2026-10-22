using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PS4Manager.App
{
    /// <summary>
    /// Fenster eines Moduls, das noch nicht gebaut ist (Plan Task 6, Schritt 2).
    /// Es sagt klar, was das Modul können wird und dass es noch fehlt — ein
    /// Knopf, der nichts tut, wäre die schlechtere Antwort.
    /// </summary>
    internal sealed class PhaseStatusForm : AppWindow
    {
        private readonly UiPanel _body = new UiPanel { Dock = DockStyle.Fill };
        private readonly List<UiLabel> _lines = new List<UiLabel>();
        private readonly UiButton _close = new UiButton { Text = "Close" };

        public PhaseStatusForm(string title, IReadOnlyList<string> lines)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            foreach (var line in lines)
            {
                var label = new UiLabel { Text = line };
                _lines.Add(label);
                _body.Controls.Add(label);
            }

            _close.Click += (s, e) => Close();
            _body.Controls.Add(_close);
            Controls.Add(_body);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            const int margin = 20;
            int y = margin;
            int width = 420;

            foreach (var label in _lines)
            {
                label.Size = label.GetPreferredSize(Size.Empty);
                label.Location = new Point(margin, y);
                y += label.Height + 4;
                width = Math.Max(width, label.Width);
            }

            _close.Size = _close.GetPreferredSize(Size.Empty);
            _close.Location = new Point(margin + width - _close.Width, y + margin);
            ClientSize = new Size(width + 2 * margin, y + margin + _close.Height + margin);
            if (Owner != null) CenterToParent(); else CenterToScreen();
        }
    }
}
