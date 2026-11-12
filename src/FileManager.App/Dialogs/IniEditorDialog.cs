using PS4Manager.UI;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Views;

namespace FileManager.App.Dialogs
{
    /// <summary>
    /// Der eingebaute Editor für Textdateien (Spec „integrierter Editor").
    /// Öffentlich, damit auch der GoldHEN-Manager Konfigurationen damit öffnet;
    /// das Speichern bleibt beim Aufrufer (Rückruf) und damit bei der
    /// gemeinsamen Warteschlange.
    /// </summary>
    public sealed class IniEditorDialog : AppForm
    {
        private readonly MultilineEditBox _editor = new MultilineEditBox();
        private readonly UiLabel _status = new UiLabel { AutoSize = false };
        private readonly UiButton _save = new UiButton { Text = "Save" };
        private readonly UiButton _saveClose = new UiButton { Text = "Save and close" };
        private readonly UiButton _cancel = new UiButton { Text = "Cancel" };
        private readonly Func<string, Task<bool>> _saveText;
        private readonly string _lineEnding;
        private bool _saving;

        public IniEditorDialog(string fileName, string text, Func<string, Task<bool>> saveText)
        {
            if (saveText == null) throw new ArgumentNullException(nameof(saveText));
            Text = "Edit " + fileName;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(520, 320);
            _saveText = saveText;
            _lineEnding = DetectLineEnding(text ?? "");
            _status.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.NoWrap;
            _status.Appearance.TextOptions.Trimming = DevExpress.Utils.Trimming.EllipsisCharacter;
            _status.TextChanged += (s, e) => _status.ToolTip = _status.Text;

            _editor.Multiline = true;
            _editor.ReadOnly = false;
            _editor.ScrollBars = ScrollBars.Both;
            _editor.WordWrap = false;
            _editor.AcceptsTab = true;
            _editor.Text = ToEditorText(text ?? "");
            _editor.Dock = DockStyle.Fill;

            _save.Click += async (s, e) => await SaveAsync(false);
            _saveClose.Click += async (s, e) => await SaveAsync(true);
            _cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            var buttons = new UiToolbar { Dock = DockStyle.Bottom };
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_saveClose);
            buttons.Controls.Add(_save);
            buttons.Controls.Add(_status);
            buttons.Layout += (s, e) => LayoutButtons();

            var body = new UiPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            body.Controls.Add(_editor);
            Controls.Add(body);
            Controls.Add(buttons);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutButtons();
            if (Owner != null) CenterToParent(); else CenterToScreen();
            _editor.FocusText();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                var _ = SaveAsync(false);
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (_saving) return true;
                DialogResult = DialogResult.Cancel;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LayoutButtons()
        {
            if (!IsHandleCreated) return;
            int gap = Px(8);
            var area = _cancel.Parent.DisplayRectangle;
            int right = area.Right;
            int y = area.Top;
            _cancel.Size = _cancel.GetPreferredSize(Size.Empty);
            _saveClose.Size = _saveClose.GetPreferredSize(Size.Empty);
            _save.Size = _save.GetPreferredSize(Size.Empty);
            _cancel.Location = new Point(right - _cancel.Width, y);
            right = _cancel.Left - gap;
            _saveClose.Location = new Point(right - _saveClose.Width, y);
            right = _saveClose.Left - gap;
            _save.Location = new Point(right - _save.Width, y);
            _status.SetBounds(area.Left, area.Top, Math.Max(1, _save.Left - gap - area.Left), area.Height);
        }

        private async Task SaveAsync(bool closeAfter)
        {
            if (_saving) return;
            var snapshot = _editor.Text;
            var saveText = FromEditorText(snapshot);
            _saving = true;
            _editor.Enabled = false;
            _save.Enabled = false;
            _saveClose.Enabled = false;
            _cancel.Enabled = false;
            _status.Text = "Saving …";
            try
            {
                if (await _saveText(saveText).ConfigureAwait(true))
                {
                    bool changedAgain = _editor.Text != snapshot;
                    _status.Text = changedAgain ? "Saved. Further changes are not saved yet." : "Saved.";
                    if (closeAfter && !changedAgain) DialogResult = DialogResult.OK;
                }
                else
                {
                    _status.Text = "Save failed.";
                }
            }
            catch (Exception ex)
            {
                _status.Text = "Error: " + ex.Message;
            }
            finally
            {
                _saving = false;
                _editor.Enabled = true;
                _save.Enabled = true;
                _saveClose.Enabled = true;
                _cancel.Enabled = true;
            }
        }

        private string ToEditorText(string text)
        {
            return NormalizeLineEndings(text, "\r\n");
        }

        private string FromEditorText(string text)
        {
            return NormalizeLineEndings(text, _lineEnding);
        }

        private static string DetectLineEnding(string text)
        {
            var crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (crlf >= 0) return "\r\n";
            if (text.IndexOf("\n", StringComparison.Ordinal) >= 0) return "\n";
            if (text.IndexOf("\r", StringComparison.Ordinal) >= 0) return "\r";
            return Environment.NewLine;
        }

        private static string NormalizeLineEndings(string text, string lineEnding)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", lineEnding);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_saving)
            {
                e.Cancel = true;
                _status.Text = "Please wait until saving has finished.";
            }
            base.OnFormClosing(e);
        }
    }
}
