using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using FileManager.Core.Paths;
using PS4Manager.Core.Config;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Session;

namespace PS4Manager.App
{
    internal sealed class PrxManagerForm : AppWindow
    {
        private readonly ManagerSettings _settings;
        private readonly ManagerSessionController _sessions;
        private readonly Frame4Client _client;
        private readonly Frame4PacketClient _packetClient;
        private readonly ToolUiDispatcher _ui;
        private readonly ToolSessionBinding _binding;
        private readonly TableLayoutPanel _toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, Padding = new Padding(6, 4, 6, 4) };
        private readonly UiButton _refresh = new UiButton { Text = "Refresh Processes", AutoSize = true };
        private readonly UiLabel _status = new UiLabel { Text = "Not loaded.", Dock = DockStyle.Fill, AutoSize = false };
        private readonly UiSplit _split = new UiSplit
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 200,
            Panel1MinSize = 156,
            Panel2MinSize = 406
        };
        private readonly AppGrid _processes = new AppGrid { Dock = DockStyle.Fill };
        private readonly AppGrid _modules = new AppGrid { Dock = DockStyle.Fill };
        private readonly UiTextBox _processName = new UiTextBox { PlaceholderText = "eboot.bin" };
        private readonly UiTextBox _remotePath = new UiTextBox { PlaceholderText = "/data/GoldHEN/plugins/plugin.prx" };
        private readonly UiTextBox _localPrx = new UiTextBox { PlaceholderText = "Local .prx" };
        private readonly UiTextBox _handle = new UiTextBox { PlaceholderText = "0x0" };
        private readonly UiButton _list = new UiButton { Text = "List Modules", AutoSize = true };
        private readonly UiButton _browse = new UiButton { Text = "Browse", AutoSize = true };
        private readonly UiButton _upload = new UiButton { Text = "Upload", AutoSize = true };
        private readonly UiButton _deployLoad = new UiButton { Text = "Deploy + Load", AutoSize = true };
        private readonly UiButton _load = new UiButton { Text = "Load PRX", AutoSize = true };
        private readonly UiButton _unload = new UiButton { Text = "Unload", AutoSize = true };
        private readonly List<ProcessRow> _processRows = new List<ProcessRow>();
        private readonly List<ModuleRow> _moduleRows = new List<ModuleRow>();
        private readonly ListDataSource<ProcessRow> _processSource;
        private readonly ListDataSource<ModuleRow> _moduleSource;
        private CancellationTokenSource _requestCts;
        private ManagerSession _session;

        public PrxManagerForm(ManagerSettings settings, ManagerSessionController sessions)
            : this(settings, sessions, new Frame4Client(), new Frame4PacketClient())
        {
        }

        internal PrxManagerForm(ManagerSettings settings, ManagerSessionController sessions, Frame4Client client,
            Frame4PacketClient packetClient)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _sessions = sessions;
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _packetClient = packetClient ?? throw new ArgumentNullException(nameof(packetClient));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());

            _processSource = new ListDataSource<ProcessRow>(_processRows);
            _processSource.Map("Pid", r => r.Pid.ToString(CultureInfo.InvariantCulture));
            _processSource.Map("Name", r => r.Name);
            _moduleSource = new ListDataSource<ModuleRow>(_moduleRows);
            _moduleSource.Map("Handle", r => Hex(r.Handle));
            _moduleSource.Map("Name", r => r.Name);
            _moduleSource.Map("Text", r => Hex(r.TextAddress) + " +" + Hex(r.TextSize));

            BuildUi();
            if (_sessions != null)
                _binding = new ToolSessionBinding(_sessions, AttachSession, DetachSession);
        }

        private void BuildUi()
        {
            Text = "PRX Manager";
            ClientSize = new Size(860, 520);
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterParent;
            _status.TextAlignment = ContentAlignment.MiddleLeft;

            _processes.Columns.Add(new GridColumn("Pid", "PID") { Width = 70, ReadOnly = true });
            _processes.Columns.Add(new GridColumn("Name", "Process") { Width = 220, ReadOnly = true });
            _processes.View.OptionsView.ColumnAutoWidth = true;
            _processes.DataSource = _processSource;
            _processes.Selection.Changed += (s, e) => UseSelectedProcess();

            _modules.Columns.Add(new GridColumn("Handle", "Handle") { Width = 90, ReadOnly = true });
            _modules.Columns.Add(new GridColumn("Name", "Name") { Width = 300, ReadOnly = true });
            _modules.Columns.Add(new GridColumn("Text", "Text") { Width = 190, ReadOnly = true });
            _modules.View.OptionsView.ColumnAutoWidth = true;
            _modules.DataSource = _moduleSource;
            _modules.Selection.Changed += (s, e) => UseSelectedModule();

            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _toolbar.Controls.Add(_refresh, 0, 0);
            _toolbar.Controls.Add(_status, 1, 0);
            _toolbar.Controls.Add(new UiLabel { Text = "Frame4 · " + _settings.Frame4.HttpPort, Margin = new Padding(10, 3, 3, 3) }, 2, 0);
            UiLayout.SizeCommandColumns(_toolbar);
            _split.Panel1.Controls.Add(_processes);
            _split.Panel2.Controls.Add(RightPanel());
            Controls.Add(_split);
            Controls.Add(_toolbar);

            _refresh.Click += async (s, e) => await RefreshProcessesAsync();
            _list.Click += async (s, e) => await ListModulesAsync();
            _browse.Click += (s, e) => BrowseLocalPrx();
            _upload.Click += async (s, e) => await UploadPrxAsync(false);
            _deployLoad.Click += async (s, e) => await UploadPrxAsync(true);
            _load.Click += async (s, e) => await LoadPrxAsync();
            _unload.Click += async (s, e) => await UnloadPrxAsync();
            _processName.TextChanged += (s, e) => UpdateButtons();
            _remotePath.TextChanged += (s, e) => UpdateButtons();
            _localPrx.TextChanged += (s, e) => UpdateButtons();
            _handle.TextChanged += (s, e) => UpdateButtons();
            UpdateButtons();
        }

        private void AttachSession(ManagerSession session)
        {
            try { _ui.Dispatch(() => { if (!IsDisposed) { _session = session; UpdateButtons(); } }); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void DetachSession(ManagerSession session)
        {
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed || !ReferenceEquals(_session, session)) return;
                    _session = null;
                    UpdateButtons();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private Control RightPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int row = 0; row < 5; row++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            AddInputRow(panel, 0, "Process", _processName, _list);
            AddInputRow(panel, 1, "Console path", _remotePath);
            AddInputRow(panel, 2, "Local file", _localPrx, _browse);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
            foreach (var button in new[] { _upload, _deployLoad, _load })
            {
                button.Margin = new Padding(3, 2, 3, 2);
                actions.Controls.Add(button);
            }
            panel.Controls.Add(actions, 0, 3);
            panel.SetColumnSpan(actions, 3);
            AddInputRow(panel, 4, "Handle", _handle, _unload);
            panel.Controls.Add(_modules, 0, 5);
            panel.SetColumnSpan(_modules, 3);
            UiLayout.SizeCommandColumns(panel);
            // Measure the action flow at the actual viewport width, not its
            // unwrapped preferred width. The module grid gets the space left.
            panel.Layout += (s, e) =>
            {
                int width = panel.ClientSize.Width - panel.Padding.Horizontal;
                if (width <= 0) return;
                var row = panel.RowStyles[3];
                row.SizeType = SizeType.Absolute;
                int height = actions.GetPreferredSize(new Size(width, 0)).Height;
                if (row.Height != height) row.Height = height;
            };
            return panel;
        }

        private static void AddInputRow(TableLayoutPanel panel, int row, string caption, UiTextBox editor, UiButton action = null)
        {
            var margin = new Padding(3, 2, 3, 2);
            panel.Controls.Add(new UiLabel { Text = caption, Margin = margin }, 0, row);
            editor.Dock = DockStyle.Fill;
            editor.Margin = margin;
            panel.Controls.Add(editor, 1, row);
            if (action == null) panel.SetColumnSpan(editor, 2);
            else
            {
                action.Dock = DockStyle.Fill;
                action.Margin = margin;
                panel.Controls.Add(action, 2, row);
            }
        }

        private void BrowseLocalPrx()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Select PRX",
                Filter = "PRX files (*.prx)|*.prx|All files (*.*)|*.*",
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _localPrx.Text = dialog.FileName;
                _remotePath.Text = RemotePath.Combine(GoldHenDiscoveryService.PluginsPath,
                    Path.GetFileName(dialog.FileName));
            }
        }

        private async Task RefreshProcessesAsync()
        {
            var cts = BeginRequest("Loading processes...");
            try
            {
                var processes = await _client.GetProcessesAsync(Endpoint(), cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _processRows.Clear();
                    foreach (var process in processes)
                        _processRows.Add(new ProcessRow(process.Pid, process.Name));
                    Rebind(_processes, _processSource);
                    _status.Text = "Loaded " + _processRows.Count + " process(es).";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private async Task ListModulesAsync()
        {
            var row = SelectedProcess();
            if (row == null) return;
            var cts = BeginRequest("Loading modules...");
            try
            {
                var modules = await _packetClient.GetPrxModulesAsync(Endpoint(), row.Pid, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _moduleRows.Clear();
                    foreach (var module in modules)
                        _moduleRows.Add(new ModuleRow(module.Handle, module.Name, module.TextAddress,
                            module.TextSize));
                    Rebind(_modules, _moduleSource);
                    _status.Text = "Loaded " + _moduleRows.Count + " module(s).";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private async Task LoadPrxAsync()
        {
            var processName = _processName.Text.Trim();
            var path = _remotePath.Text.Trim();
            var cts = BeginRequest("Loading PRX...");
            try
            {
                var handle = await _packetClient.LoadPrxAsync(Endpoint(), processName, path, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _handle.Text = Hex(handle);
                    _status.Text = "Loaded PRX handle " + Hex(handle) + ".";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private async Task UploadPrxAsync(bool loadAfterUpload)
        {
            var session = _session;
            if (session == null) return;
            var local = _localPrx.Text.Trim();
            if (!File.Exists(local))
            {
                _status.Text = "Select a local PRX first.";
                return;
            }

            var remote = _remotePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(remote))
                remote = RemotePath.Combine(GoldHenDiscoveryService.PluginsPath, Path.GetFileName(local));

            var token = session.CancellationToken;
            var cts = BeginRequest(loadAfterUpload ? "Deploying PRX..." : "Uploading PRX...");
            try
            {
                var job = await session.Explorer.Operations.PlanUploadAsync(new[] { local },
                    RemotePath.Parent(remote), token).ConfigureAwait(true);
                if (job == null) return;
                session.Explorer.Queue.Enqueue(job);
                await session.Explorer.Queue.WhenIdle().ConfigureAwait(true);
                if (job.State != FileManager.Core.Transfers.JobState.Completed)
                {
                    _status.Text = job.Title + ": " + job.Summary;
                    return;
                }

                _remotePath.Text = RemotePath.Combine(RemotePath.Parent(remote), Path.GetFileName(local));
                _status.Text = "Uploaded " + Path.GetFileName(local) + ".";
                if (loadAfterUpload)
                    await LoadPrxAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _status.Text = "Upload failed: " + Ui.Describe(ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task UnloadPrxAsync()
        {
            var processName = _processName.Text.Trim();
            uint handle;
            if (!TryUInt(_handle.Text, out handle))
            {
                _status.Text = "Handle must be a valid hex/decimal value.";
                return;
            }

            var cts = BeginRequest("Unloading PRX...");
            try
            {
                await _packetClient.UnloadPrxAsync(Endpoint(), processName, handle, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (IsCurrent(cts)) _status.Text = "Unloaded " + Hex(handle) + ".";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(cts, ex); }
            finally { FinishRequest(cts); }
        }

        private void UseSelectedProcess()
        {
            var row = SelectedProcess();
            if (row != null) _processName.Text = row.Name;
            UpdateButtons();
        }

        private void UseSelectedModule()
        {
            var row = SelectedModule();
            if (row != null) _handle.Text = Hex(row.Handle);
        }

        private ProcessRow SelectedProcess()
        {
            var index = _processes.Selection.CurrentRow;
            return index >= 0 && index < _processRows.Count ? _processRows[index] : null;
        }

        private ModuleRow SelectedModule()
        {
            var index = _modules.Selection.CurrentRow;
            return index >= 0 && index < _moduleRows.Count ? _moduleRows[index] : null;
        }

        private CancellationTokenSource BeginRequest(string text)
        {
            if (_requestCts != null) _requestCts.Cancel();
            _requestCts = new CancellationTokenSource();
            _status.Text = text;
            _refresh.Enabled = false;
            _list.Enabled = false;
            _browse.Enabled = false;
            _upload.Enabled = false;
            _deployLoad.Enabled = false;
            _load.Enabled = false;
            _unload.Enabled = false;
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
                _status.Text = "Frame4 error: " + Ui.Describe(ex);
                UpdateButtons();
            });
        }

        private void UpdateButtons()
        {
            var hasProcess = !string.IsNullOrWhiteSpace(_processName.Text) || SelectedProcess() != null;
            var hasLocal = File.Exists(_localPrx.Text.Trim());
            var hasFtp = _session != null;
            _refresh.Enabled = true;
            _list.Enabled = SelectedProcess() != null;
            _browse.Enabled = true;
            _upload.Enabled = hasFtp && hasLocal;
            _deployLoad.Enabled = hasFtp && hasLocal && hasProcess;
            _load.Enabled = hasProcess && !string.IsNullOrWhiteSpace(_remotePath.Text);
            _unload.Enabled = hasProcess && !string.IsNullOrWhiteSpace(_handle.Text);
        }

        private Frame4Endpoint Endpoint()
        {
            return new Frame4Endpoint(_settings.Ip, _settings.Frame4.HttpPort, _settings.Frame4.UartPort);
        }

        private static void Rebind<T>(AppGrid grid, ListDataSource<T> source)
        {
            grid.DataSource = null;
            grid.DataSource = source;
        }

        private static string Hex(ulong value)
        {
            return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
        }

        private static bool TryUInt(string value, out uint result)
        {
            var text = (value ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            ulong parsed;
            if (ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed) &&
                parsed <= uint.MaxValue)
            {
                result = (uint)parsed;
                return true;
            }
            result = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_requestCts != null) _requestCts.Cancel();
                if (_binding != null) _binding.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class ProcessRow
        {
            public ProcessRow(int pid, string name)
            {
                Pid = pid;
                Name = name;
            }

            public int Pid { get; private set; }
            public string Name { get; private set; }
        }

        private sealed class ModuleRow
        {
            public ModuleRow(uint handle, string name, ulong textAddress, uint textSize)
            {
                Handle = handle;
                Name = name;
                TextAddress = textAddress;
                TextSize = textSize;
            }

            public uint Handle { get; private set; }
            public string Name { get; private set; }
            public ulong TextAddress { get; private set; }
            public uint TextSize { get; private set; }
        }
    }
}
