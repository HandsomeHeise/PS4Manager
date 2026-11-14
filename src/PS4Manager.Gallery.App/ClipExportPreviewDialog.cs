using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.Gallery.Core.Clips;
using PS4Manager.UI;

namespace PS4Manager.Gallery.App
{
    internal sealed class ClipExportPreviewDialog : AppWindow
    {
        private readonly VideoView _player = new VideoView { Dock = DockStyle.Fill, Visible = false };
        private readonly UiTextBox _status = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, TabStop = false };
        private readonly UiButton _save = new UiButton { Text = "Als MP4 speichern …", AutoSize = true, Enabled = false };
        private readonly UiButton _close = new UiButton { Text = "Abbrechen", AutoSize = true };
        private readonly string _encoder, _source, _rendered;
        private readonly ClipRange _range;
        private readonly CancellationTokenSource _lifetime;
        private bool _busy, _ready, _closing, _disposed;

        public ClipExportPreviewDialog(string encoder, string source, ClipRange range, CancellationToken sessionToken)
        {
            _encoder = encoder;
            _source = source;
            _range = range;
            _rendered = Path.Combine(Path.GetTempPath(), "ps4-clip-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);

            Text = "Ausschnitt-Vorschau";
            ClientSize = new Size(860, 540);
            MinimumSize = new Size(490, 340);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
            actions.Controls.AddRange(new Control[] { _save, _close });
            layout.Controls.Add(_player, 0, 0);
            layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(actions, 0, 2);
            Controls.Add(layout);

            _status.Text = "Ausschnitt wird erstellt …";
            _player.Failed += (s, message) => _status.Text = "Vorschau konnte nicht abgespielt werden: " + message;
            _save.Click += async (s, e) => await ChooseDestinationAsync();
            _close.Click += (s, e) => Close();
            Shown += async (s, e) => await RenderAsync();
        }

        private Task RenderAsync() => RunAsync("Ausschnitt wird erstellt …", async token =>
        {
            await ClipExport.EncodeAsync(_encoder, _source, _range, _rendered, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || _closing) return;
            _ready = true;
            _status.Text = "Vorschau bereit. Beim Speichern wird genau diese MP4 übernommen.";
            _player.Visible = true;
            _player.Play(_rendered);
        });

        private async Task ChooseDestinationAsync()
        {
            if (!_ready || _busy || _closing || _lifetime.IsCancellationRequested) return;
            _player.Pause();
            using (var dialog = new SaveFileDialog
            {
                Title = "Ausschnitt als MP4 speichern", Filter = "MP4-Video (*.mp4)|*.mp4",
                DefaultExt = "mp4", AddExtension = true, OverwritePrompt = false, RestoreDirectory = true,
                FileName = Path.GetFileNameWithoutExtension(_source) + " - Ausschnitt.mp4"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || _disposed || _closing) return;
                await RunAsync("MP4 wird gespeichert …", async token =>
                {
                    await ClipExport.SaveAsync(_rendered, dialog.FileName, token);
                    if (!_disposed && !_closing) _status.Text = "Gespeichert: " + dialog.FileName;
                });
            }
        }

        private async Task RunAsync(string status, Func<CancellationToken, Task> operation)
        {
            if (_disposed || _busy) return;
            _busy = true;
            _save.Enabled = false;
            _close.Text = "Abbrechen";
            _status.Text = status;
            try
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                await operation(_lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { _closing = true; }
            catch (Exception ex)
            {
                if (!_disposed && !_closing) _status.Text = ex.Message;
            }
            finally
            {
                _busy = false;
                if (_disposed) Cleanup();
                else if (_closing || _lifetime.IsCancellationRequested) Close();
                else { _save.Enabled = _ready; _close.Text = "Schließen"; }
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!_busy) return;
            _closing = true;
            _lifetime.Cancel();
            // A direct close waits for cleanup. An owner or application close
            // must proceed; Dispose/RunAsync still clean up the stopped encode.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _close.Enabled = false;
                _status.Text = "Wird abgebrochen …";
            }
        }

        protected override void Dispose(bool disposing)
        {
            bool first = disposing && !_disposed;
            if (first) { _disposed = true; _lifetime.Cancel(); }
            base.Dispose(disposing);
            if (first && !_busy) Cleanup();
        }

        private void Cleanup()
        {
            _lifetime.Dispose();
            try { File.Delete(_rendered); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
