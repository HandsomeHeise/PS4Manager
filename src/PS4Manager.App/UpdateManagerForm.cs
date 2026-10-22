using PS4Manager.UI;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using FileManager.App.Dialogs;
using PS4Manager.Core.Config;
using PS4Manager.Core.Updates;

namespace PS4Manager.App
{
    internal sealed class UpdateManagerForm : AppWindow
    {
        private readonly ManagerSettings _settings;
        private readonly Action _saveSettings;
        private readonly UpdateService _service;
        private readonly UiPanel _toolbar = new UiPanel { Dock = DockStyle.Top };
        private readonly UiButton _check = new UiButton { Text = "Check", Dock = DockStyle.Left, AutoSize = false, Width = 76 };
        private readonly UiButton _install = new UiButton { Text = "Download + Install", Dock = DockStyle.Left, AutoSize = false, Width = 136 };
        private readonly UiLabel _status = new UiLabel { Dock = DockStyle.Fill, AutoSize = false };
        private readonly UiTextBox _manifestUrl = new UiTextBox();
        private readonly ComboBox _channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly UiTextBox _notes = new UiTextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill };
        private readonly UiPanel _settingsPanel = new UiPanel { Dock = DockStyle.Top };
        private CancellationTokenSource _requestCts;
        private UpdateCheckResult _lastResult;

        public UpdateManagerForm(ManagerSettings settings, Action saveSettings)
            : this(settings, saveSettings, new UpdateService())
        {
        }

        internal UpdateManagerForm(ManagerSettings settings, Action saveSettings, UpdateService service)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            if (_settings.Updates == null) _settings.Updates = new UpdateSettings();
            BuildUi();
            LoadSettings();
            UpdateButtons();
        }

        private void BuildUi()
        {
            Text = "Updates";
            ClientSize = new Size(720, 360);
            MinimumSize = new Size(560, 300);
            StartPosition = FormStartPosition.CenterParent;
            WindowIcon.Apply(this);

            _status.TextAlignment = ContentAlignment.MiddleLeft;
            _toolbar.Height = 34;
            _toolbar.Controls.Add(_status);
            _toolbar.Controls.Add(_install);
            _toolbar.Controls.Add(_check);

            _channel.Items.Add("stable");
            _channel.Items.Add("preview");

            _settingsPanel.Height = 76;
            _settingsPanel.Controls.Add(new UiLabel { Text = "Manifest URL", Location = new Point(12, 12), AutoSize = true });
            _settingsPanel.Controls.Add(new UiLabel { Text = "Channel", Location = new Point(12, 44), AutoSize = true });
            _settingsPanel.Controls.Add(_manifestUrl);
            _settingsPanel.Controls.Add(_channel);
            _manifestUrl.SetBounds(110, 8, 570, 26);
            _channel.SetBounds(110, 40, 140, 26);

            Controls.Add(_notes);
            Controls.Add(_settingsPanel);
            Controls.Add(_toolbar);

            _check.Click += async (s, e) => await CheckAsync();
            _install.Click += async (s, e) => await InstallAsync();
            _manifestUrl.TextChanged += (s, e) => StoreSettings();
            _channel.SelectedIndexChanged += (s, e) => StoreSettings();
        }

        private void LoadSettings()
        {
            _manifestUrl.Text = _settings.Updates.ManifestUrl;
            var channel = string.IsNullOrWhiteSpace(_settings.Updates.Channel) ? "stable" : _settings.Updates.Channel;
            _channel.SelectedItem = channel;
            if (_channel.SelectedIndex < 0) _channel.SelectedIndex = 0;
            _status.Text = "Current version: " + _service.CurrentVersion;
            _notes.Text = "Host a manifest JSON on HTTPS, then paste its URL here.";
        }

        private void StoreSettings()
        {
            _settings.Updates.ManifestUrl = _manifestUrl.Text.Trim();
            _settings.Updates.Channel = (_channel.SelectedItem as string) ?? "stable";
            _saveSettings();
            UpdateButtons();
        }

        private async Task CheckAsync()
        {
            StoreSettings();
            var cts = BeginRequest("Checking for updates...");
            try
            {
                var result = await _service.CheckAsync(_settings.Updates, cts.Token).ConfigureAwait(true);
                if (!IsCurrent(cts)) return;
                _lastResult = result;
                _status.Text = result.Message;
                _notes.Text = Describe(result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _status.Text = "Update check failed: " + Ui.Describe(ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task InstallAsync()
        {
            var result = _lastResult;
            if (result == null || !result.IsUpdateAvailable) return;
            if (!MessageDialog.Ask(this, "Install update",
                    "Download and install PS4Manager " + result.Manifest.Version + "? The app will restart.",
                    "Install", "Cancel"))
                return;

            var cts = BeginRequest("Downloading update...");
            try
            {
                var package = await _service.DownloadPackageAsync(result.Manifest, cts.Token).ConfigureAwait(true);
                if (!IsCurrent(cts)) return;
                _service.LaunchUpdater(package);
                Application.Exit();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _status.Text = "Install failed: " + Ui.Describe(ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private CancellationTokenSource BeginRequest(string text)
        {
            if (_requestCts != null) _requestCts.Cancel();
            _requestCts = new CancellationTokenSource();
            _status.Text = text;
            _check.Enabled = false;
            _install.Enabled = false;
            return _requestCts;
        }

        private void FinishRequest(CancellationTokenSource cts)
        {
            if (IsCurrent(cts)) UpdateButtons();
        }

        private bool IsCurrent(CancellationTokenSource cts)
        {
            return !IsDisposed && ReferenceEquals(_requestCts, cts) && !cts.IsCancellationRequested;
        }

        private void UpdateButtons()
        {
            _check.Enabled = !string.IsNullOrWhiteSpace(_manifestUrl.Text);
            _install.Enabled = _lastResult != null && _lastResult.IsUpdateAvailable;
        }

        private static string Describe(UpdateCheckResult result)
        {
            if (result == null) return "";
            if (result.Manifest == null) return result.Message;
            var text = "Version: " + result.Manifest.Version + Environment.NewLine
                       + "Channel: " + result.Manifest.Channel + Environment.NewLine
                       + "Package: " + result.Manifest.Url + Environment.NewLine
                       + "SHA-256: " + result.Manifest.Sha256 + Environment.NewLine;
            if (!string.IsNullOrWhiteSpace(result.Manifest.Notes))
                text += Environment.NewLine + result.Manifest.Notes;
            return text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _requestCts != null) _requestCts.Cancel();
            base.Dispose(disposing);
        }
    }
}
