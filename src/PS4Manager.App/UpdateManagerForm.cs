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
        private readonly UiButton _check = new UiButton { Text = "Check", AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly UiButton _install = new UiButton { Text = "Download + Install", AutoSize = true, Anchor = AnchorStyles.Left };
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
            var commands = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commands.Controls.Add(_check, 0, 0);
            commands.Controls.Add(_install, 1, 0);
            commands.Controls.Add(_status, 2, 0);
            _toolbar.Controls.Add(commands);
            FitPanelToContent(_toolbar, commands);

            _channel.Items.Add("stable");
            _channel.Items.Add("preview");

            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new UiLabel { Text = "Manifest URL", Anchor = AnchorStyles.Left }, 0, 0);
            fields.Controls.Add(new UiLabel { Text = "Channel", Anchor = AnchorStyles.Left }, 0, 1);
            _manifestUrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _channel.Anchor = AnchorStyles.Left;
            _channel.Width = 140;
            fields.Controls.Add(_manifestUrl, 1, 0);
            fields.Controls.Add(_channel, 1, 1);
            _settingsPanel.Controls.Add(fields);
            FitPanelToContent(_settingsPanel, fields);

            Controls.Add(_notes);
            Controls.Add(_settingsPanel);
            Controls.Add(_toolbar);

            _check.Click += async (s, e) => await CheckAsync();
            _install.Click += async (s, e) => await InstallAsync();
            _manifestUrl.TextChanged += (s, e) => StoreSettings();
            _channel.SelectedIndexChanged += (s, e) => StoreSettings();
        }

        private static void FitPanelToContent(UiPanel panel, Control content)
        {
            // WXI adds its own border in addition to the panel's padding.
            panel.Layout += (s, e) => {
                int chrome = panel.Height - panel.DisplayRectangle.Height;
                int height = content.GetPreferredSize(new Size(panel.DisplayRectangle.Width, 0)).Height + chrome;
                if (panel.Height != height) panel.Height = height;
            };
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
