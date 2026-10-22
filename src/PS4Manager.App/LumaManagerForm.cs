using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma;
using PS4Manager.Core.Session;

namespace PS4Manager.App
{
    internal sealed class LumaManagerForm : AppWindow
    {
        private readonly ManagerSettings _settings;
        private readonly Action _saveSettings;
        private readonly LumaClient _client;
        private readonly ToolUiDispatcher _ui;
        private readonly UiPanel _toolbar = new UiPanel { Dock = DockStyle.Top };
        private readonly UiComboBox _game = new UiComboBox { Dock = DockStyle.Left };
        private readonly UiTextBox _host = new UiTextBox { PlaceholderText = "PS4 IP" };
        private readonly UiTextBox _port = new UiTextBox { Text = LumaEndpoint.GtaPort.ToString(CultureInfo.InvariantCulture) };
        private readonly UiTextBox _token = new UiTextBox { PlaceholderText = "Token" };
        private readonly UiButton _connect = new UiButton { Text = "Connect", AutoSize = true, Dock = DockStyle.Left };
        private readonly UiButton _disconnect = new UiButton { Text = "Disconnect", AutoSize = true, Dock = DockStyle.Left };
        private readonly UiButton _hello = new UiButton { Text = "Hello", AutoSize = true, Dock = DockStyle.Left };
        private readonly UiButton _reload = new UiButton { Text = "Reload Scripts", AutoSize = true, Dock = DockStyle.Left };
        private readonly UiButton _resources = new UiButton { Text = "Resources", AutoSize = true, Dock = DockStyle.Left };
        private readonly UiLabel _status = new UiLabel { Text = "Disconnected.", Dock = DockStyle.Fill, AutoSize = false };
        private readonly UiSplit _split = new UiSplit
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 360,
            Panel2MinSize = 260
        };
        private readonly UiTextBox _script = new UiTextBox { Dock = DockStyle.Fill, Multiline = true };
        private readonly UiTextBox _output = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiButton _execute = new UiButton { Text = "Run Current Script", AutoSize = true };
        private readonly AppGrid _resourceGrid = new AppGrid { Dock = DockStyle.Fill };
        private readonly List<ResourceRow> _resourceRows = new List<ResourceRow>();
        private readonly ListDataSource<ResourceRow> _resourceSource;
        private CancellationTokenSource _requestCts;
        private bool _loading;

        public LumaManagerForm(ManagerSettings settings, Action saveSettings)
            : this(settings, saveSettings, new LumaClient())
        {
        }

        internal LumaManagerForm(ManagerSettings settings, Action saveSettings, LumaClient client)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _saveSettings = saveSettings ?? (() => { });
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            if (_settings.Luma == null) _settings.Luma = new LumaSettings();

            _resourceSource = new ListDataSource<ResourceRow>(_resourceRows);
            _resourceSource.Map("Name", r => r.Name);
            _resourceSource.Map("State", r => r.State);

            BuildUi();
            LoadSettings();
            UpdateButtons();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutToolbar();
        }

        private void BuildUi()
        {
            Text = "Luma Script Manager";
            ClientSize = new Size(940, 560);
            MinimumSize = new Size(720, 420);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            _game.Items.Add("GTA V");
            _game.Items.Add("Red Dead Redemption 2");
            _game.Width = Px(170);
            _host.Width = Px(140);
            _port.Width = Px(70);
            _token.Width = Px(130);
            _status.TextAlignment = ContentAlignment.MiddleLeft;

            LayoutToolbar();
            _toolbar.Controls.Add(_status);
            _toolbar.Controls.Add(_resources);
            _toolbar.Controls.Add(_reload);
            _toolbar.Controls.Add(_hello);
            _toolbar.Controls.Add(_disconnect);
            _toolbar.Controls.Add(_connect);

            _resourceGrid.Columns.Add(new GridColumn("Name", "Resource") { Width = 190, ReadOnly = true });
            _resourceGrid.Columns.Add(new GridColumn("State", "State") { Width = 90, ReadOnly = true });
            _resourceGrid.DataSource = _resourceSource;

            _split.Panel1.Controls.Add(LeftPanel());
            _split.Panel2.Controls.Add(_resourceGrid);
            Controls.Add(_split);
            Controls.Add(_toolbar);

            _connect.Click += async (s, e) => await ConnectAsync();
            _disconnect.Click += (s, e) => Disconnect();
            _hello.Click += async (s, e) => await HelloAsync();
            _reload.Click += async (s, e) => await ReloadAsync();
            _resources.Click += async (s, e) => await RefreshResourcesAsync();
            _execute.Click += async (s, e) => await ExecuteAsync();
            _game.SelectedIndexChanged += (s, e) => GameChanged();
            _host.TextChanged += (s, e) => SaveCurrentSettings();
            _port.TextChanged += (s, e) => SaveCurrentSettings();
            _token.TextChanged += (s, e) => SaveCurrentSettings();
        }

        private Control LeftPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(70)));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(70)));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(34)));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(34)));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(38)));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(new UiLabel { Text = "Game", TextAlignment = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_game, 1, 0);
            panel.Controls.Add(new UiLabel { Text = "Host", TextAlignment = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(_host, 3, 0);
            panel.Controls.Add(new UiLabel { Text = "Port", TextAlignment = ContentAlignment.MiddleLeft }, 0, 1);
            panel.Controls.Add(_port, 1, 1);
            panel.Controls.Add(new UiLabel { Text = "Token", TextAlignment = ContentAlignment.MiddleLeft }, 2, 1);
            panel.Controls.Add(_token, 3, 1);
            panel.Controls.Add(_execute, 0, 2);
            panel.SetColumnSpan(_execute, 4);
            panel.Controls.Add(_script, 0, 3);
            panel.SetColumnSpan(_script, 2);
            panel.Controls.Add(_output, 2, 3);
            panel.SetColumnSpan(_output, 2);
            return panel;
        }

        private void LoadSettings()
        {
            _loading = true;
            try
            {
                _game.SelectedIndex = _settings.Luma.Game == LumaSettings.Rdr2 ? 1 : 0;
                _host.Text = string.IsNullOrWhiteSpace(_settings.Ip) ? "" : _settings.Ip;
                _port.Text = _settings.Luma.Port.ToString(CultureInfo.InvariantCulture);
                _token.Text = _settings.Luma.Token ?? "";
                _script.Text = "print('hello from PS4Manager')";
            }
            finally
            {
                _loading = false;
            }
        }

        private void GameChanged()
        {
            if (_game.SelectedIndex == 1)
                _port.Text = LumaEndpoint.Rdr2Port.ToString(CultureInfo.InvariantCulture);
            else
                _port.Text = LumaEndpoint.GtaPort.ToString(CultureInfo.InvariantCulture);
            SaveCurrentSettings();
        }

        private void SaveCurrentSettings()
        {
            if (_loading) return;
            _settings.Luma.Game = _game.SelectedIndex == 1 ? LumaSettings.Rdr2 : LumaSettings.Gta5;
            int port;
            if (int.TryParse(_port.Text.Trim(), out port)) _settings.Luma.Port = port;
            _settings.Luma.Token = _token.Text ?? "";
            if (!string.IsNullOrWhiteSpace(_host.Text)) _settings.Ip = _host.Text.Trim();
            _saveSettings();
        }

        private async Task ConnectAsync()
        {
            SaveCurrentSettings();
            var endpoint = ReadEndpoint();
            if (endpoint == null) return;
            var cts = BeginRequest("Connecting to Luma...");
            try
            {
                _client.LogReceived -= OnLogReceived;
                _client.LogReceived += OnLogReceived;
                await _client.ConnectAsync(endpoint, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(_settings.Luma.Token))
                    await _client.HelloAsync(_settings.Luma.Token, cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (IsCurrent(cts))
                    {
                        _status.Text = "Connected to Luma on " + endpoint.Host + ":" + endpoint.Port + ".";
                        UpdateButtons();
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private void Disconnect()
        {
            if (_requestCts != null) _requestCts.Cancel();
            _client.LogReceived -= OnLogReceived;
            _client.Disconnect();
            _status.Text = "Disconnected.";
            UpdateButtons();
        }

        private async Task HelloAsync()
        {
            await RunTextCommandAsync("Sending hello...", ct => _client.HelloAsync(_settings.Luma.Token, ct));
        }

        private async Task ReloadAsync()
        {
            await RunTextCommandAsync("Reloading scripts...", ct => _client.ReloadAsync(ct));
        }

        private async Task ExecuteAsync()
        {
            await RunTextCommandAsync("Running Lua...", ct => _client.ExecuteAsync(_script.Text, ct));
        }

        private async Task RunTextCommandAsync(string status, Func<CancellationToken, Task<string>> command)
        {
            var cts = BeginRequest(status);
            try
            {
                var text = await command(cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    AppendOutput(text);
                    _status.Text = "Luma command completed.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private async Task RefreshResourcesAsync()
        {
            var cts = BeginRequest("Loading resources...");
            try
            {
                var resources = await _client.GetResourcesAsync(cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _resourceRows.Clear();
                    foreach (var entry in resources.Entries)
                        _resourceRows.Add(new ResourceRow(entry.Name, entry.State));
                    RebindResources();
                    _status.Text = resources.Truncated
                        ? "Resource list loaded, but truncated."
                        : "Loaded " + _resourceRows.Count + " resource(s).";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private LumaEndpoint ReadEndpoint()
        {
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port))
            {
                _status.Text = "Port must be a number.";
                return null;
            }

            try
            {
                return new LumaEndpoint(string.IsNullOrWhiteSpace(_host.Text) ? _settings.Ip : _host.Text, port);
            }
            catch (Exception ex)
            {
                _status.Text = Ui.Describe(ex);
                return null;
            }
        }

        private CancellationTokenSource BeginRequest(string text)
        {
            if (_requestCts != null) _requestCts.Cancel();
            _requestCts = new CancellationTokenSource();
            _status.Text = text;
            UpdateButtons(false);
            return _requestCts;
        }

        private void FinishRequest(CancellationTokenSource cts)
        {
            _ui.Dispatch(() =>
            {
                if (IsCurrent(cts)) UpdateButtons();
            });
        }

        private bool IsCurrent(CancellationTokenSource cts)
        {
            return !IsDisposed && ReferenceEquals(_requestCts, cts) && !cts.IsCancellationRequested;
        }

        private void SetError(CancellationTokenSource cts, Exception ex)
        {
            _ui.Dispatch(() =>
            {
                if (!IsCurrent(cts)) return;
                _status.Text = "Luma error: " + Ui.Describe(ex);
                AppendOutput(_status.Text);
            });
        }

        private void UpdateButtons()
        {
            UpdateButtons(true);
        }

        private void UpdateButtons(bool idle)
        {
            var connected = _client.Connected;
            _connect.Enabled = idle && !connected;
            _disconnect.Enabled = connected;
            _hello.Enabled = idle && connected;
            _reload.Enabled = idle && connected;
            _resources.Enabled = idle && connected;
            _execute.Enabled = idle && connected;
        }

        private void ConfigureToolbarButton(UiButton button)
        {
            button.AutoSize = false;
            button.Width = Math.Max(Px(88), button.GetPreferredSize(Size.Empty).Width + Px(12));
            button.Height = Px(34);
            button.MinimumSize = new Size(Px(88), Px(34));
        }

        private void LayoutToolbar()
        {
            _toolbar.Height = Px(40);
            _toolbar.MinimumSize = new Size(0, Px(40));
            ConfigureToolbarButton(_connect);
            ConfigureToolbarButton(_disconnect);
            ConfigureToolbarButton(_hello);
            ConfigureToolbarButton(_reload);
            ConfigureToolbarButton(_resources);
        }

        private void OnLogReceived(object sender, LumaLogEventArgs e)
        {
            try
            {
                _ui.Dispatch(() => AppendOutput("[LOG] " + e.Text));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _output.Text = string.IsNullOrEmpty(_output.Text)
                ? text
                : _output.Text + Environment.NewLine + text;
        }

        private void RebindResources()
        {
            _resourceGrid.DataSource = null;
            _resourceGrid.DataSource = _resourceSource;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_requestCts != null) _requestCts.Cancel();
                _client.Dispose();
            }
            base.Dispose(disposing);
        }

        private int Px(int logical)
        {
            return DpiScale.Scale(logical, DeviceDpi);
        }

        private sealed class ResourceRow
        {
            public ResourceRow(string name, string state)
            {
                Name = name;
                State = state;
            }

            public string Name { get; private set; }
            public string State { get; private set; }
        }
    }
}
