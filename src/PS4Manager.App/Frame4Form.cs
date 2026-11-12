using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Session;

namespace PS4Manager.App
{
    internal sealed class Frame4Form : AppWindow
    {
        private readonly ManagerSettings _settings;
        private readonly Frame4Client _client;
        private readonly Frame4PacketClient _packetClient;
        private readonly Frame4UartClient _uartClient;
        private readonly ToolUiDispatcher _ui;

        private readonly TableLayoutPanel _toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, Padding = new Padding(6) };
        private readonly UiButton _refresh = new UiButton { Text = "Refresh", AutoSize = true };
        private readonly UiButton _loadDetails = new UiButton { Text = "Load Details", AutoSize = true };
        private readonly UiLabel _status = new UiLabel { Text = "Not loaded.", Dock = DockStyle.Fill, AutoSize = false };

        private readonly UiSplit _split = new UiSplit
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 220,
            Panel2MinSize = 360
        };

        private readonly AppGrid _processes = new AppGrid { Dock = DockStyle.Fill };
        private readonly AppGrid _maps = new AppGrid { Dock = DockStyle.Fill };
        private readonly UiTabs _tabs = new UiTabs { Dock = DockStyle.Fill };
        private readonly UiTextBox _processInfo = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiTextBox _address = new UiTextBox { PlaceholderText = "0x0000000000000000" };
        private readonly UiTextBox _length = new UiTextBox { Text = "0x40" };
        private readonly UiButton _previous = new UiButton { Text = "Prev", AutoSize = true };
        private readonly UiButton _read = new UiButton { Text = "Read", AutoSize = true };
        private readonly UiButton _next = new UiButton { Text = "Next", AutoSize = true };
        private readonly UiTextBox _memory = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiTextBox _notifyType = new UiTextBox { Text = "222" };
        private readonly UiTextBox _notifyMessage = new UiTextBox { Text = "Frame4 connected" };
        private readonly UiButton _notify = new UiButton { Text = "Notify", AutoSize = true };
        private readonly UiButton _loadConsole = new UiButton { Text = "Load Console", AutoSize = true };
        private readonly UiTextBox _consoleInfo = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiTextBox _aobPattern = new UiTextBox { Text = "48 8B ?? ??" };
        private readonly UiButton _findAob = new UiButton { Text = "Find AOB", AutoSize = true };
        private readonly UiTextBox _aobResult = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiButton _loadPrx = new UiButton { Text = "Load PRX List", AutoSize = true };
        private readonly AppGrid _prxModules = new AppGrid { Dock = DockStyle.Fill };
        private readonly UiTextBox _writeAddress = new UiTextBox { PlaceholderText = "0x0000000000000000" };
        private readonly UiTextBox _writeBytes = new UiTextBox { PlaceholderText = "90 90" };
        private readonly UiButton _writeMemory = new UiButton { Text = "Write", AutoSize = true };
        private readonly UiTextBox _allocLength = new UiTextBox { Text = "0x1000" };
        private readonly UiButton _allocate = new UiButton { Text = "Alloc", AutoSize = true };
        private readonly UiTextBox _freeAddress = new UiTextBox { PlaceholderText = "0x0000000000000000" };
        private readonly UiTextBox _freeLength = new UiTextBox { Text = "0x1000" };
        private readonly UiButton _free = new UiButton { Text = "Free", AutoSize = true };
        private readonly UiTextBox _prxProcessName = new UiTextBox { PlaceholderText = "eboot.bin" };
        private readonly UiTextBox _prxPath = new UiTextBox { PlaceholderText = "/data/plugin.prx" };
        private readonly UiTextBox _prxHandle = new UiTextBox { PlaceholderText = "0x0" };
        private readonly UiButton _loadPrxModule = new UiButton { Text = "Load PRX", AutoSize = true };
        private readonly UiButton _unloadPrxModule = new UiButton { Text = "Unload PRX", AutoSize = true };
        private readonly UiTextBox _rpcStub = new UiTextBox { PlaceholderText = "0x0" };
        private readonly UiTextBox _rpcRip = new UiTextBox { PlaceholderText = "0x0" };
        private readonly UiTextBox _rpcRdi = new UiTextBox { Text = "0x0" };
        private readonly UiTextBox _rpcRsi = new UiTextBox { Text = "0x0" };
        private readonly UiTextBox _rpcRdx = new UiTextBox { Text = "0x0" };
        private readonly UiTextBox _rpcRcx = new UiTextBox { Text = "0x0" };
        private readonly UiTextBox _rpcR8 = new UiTextBox { Text = "0x0" };
        private readonly UiTextBox _rpcR9 = new UiTextBox { Text = "0x0" };
        private readonly UiButton _callRpc = new UiButton { Text = "Call RPC", AutoSize = true };
        private readonly UiTextBox _expertOutput = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private readonly UiButton _startLog = new UiButton { Text = "Start Log", AutoSize = true };
        private readonly UiButton _stopLog = new UiButton { Text = "Stop Log", AutoSize = true };
        private readonly UiButton _clearLog = new UiButton { Text = "Clear Log", AutoSize = true };
        private readonly UiTextBox _uartLog = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };

        private readonly List<ProcessRow> _processRows = new List<ProcessRow>();
        private readonly List<MapRow> _mapRows = new List<MapRow>();
        private readonly List<PrxRow> _prxRows = new List<PrxRow>();
        private readonly ListDataSource<ProcessRow> _processSource;
        private readonly ListDataSource<MapRow> _mapSource;
        private readonly ListDataSource<PrxRow> _prxSource;
        private CancellationTokenSource _loadCts;
        private CancellationTokenSource _logCts;
        private IFrame4UartConnection _logConnection;

        public Frame4Form(ManagerSettings settings)
            : this(settings, new Frame4Client(), new Frame4PacketClient(), new Frame4UartClient())
        {
        }

        internal Frame4Form(ManagerSettings settings, Frame4Client client)
            : this(settings, client, new Frame4PacketClient(), new Frame4UartClient())
        {
        }

        internal Frame4Form(ManagerSettings settings, Frame4Client client, Frame4PacketClient packetClient)
            : this(settings, client, packetClient, new Frame4UartClient())
        {
        }

        internal Frame4Form(ManagerSettings settings, Frame4Client client, Frame4PacketClient packetClient,
            Frame4UartClient uartClient)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _packetClient = packetClient ?? throw new ArgumentNullException(nameof(packetClient));
            _uartClient = uartClient ?? throw new ArgumentNullException(nameof(uartClient));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());

            _processSource = new ListDataSource<ProcessRow>(_processRows);
            _processSource.Map("Pid", r => r.Pid.ToString(CultureInfo.InvariantCulture));
            _processSource.Map("Name", r => r.Name);

            _mapSource = new ListDataSource<MapRow>(_mapRows);
            _mapSource.Map("Name", r => r.Name);
            _mapSource.Map("Start", r => Hex(r.Start));
            _mapSource.Map("End", r => Hex(r.End));
            _mapSource.Map("Size", r => Hex(r.Size));
            _mapSource.Map("Prot", r => r.Protection.ToString(CultureInfo.InvariantCulture));

            _prxSource = new ListDataSource<PrxRow>(_prxRows);
            _prxSource.Map("Handle", r => Hex(r.Handle));
            _prxSource.Map("Name", r => r.Name);
            _prxSource.Map("Text", r => Hex(r.TextAddress) + " +" + Hex(r.TextSize));
            _prxSource.Map("Data", r => Hex(r.DataAddress) + " +" + Hex(r.DataSize));

            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Frame4 Debugger";
            ClientSize = new Size(980, 620);
            MinimumSize = new Size(680, 420);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            _status.TextAlignment = ContentAlignment.MiddleLeft;

            _processes.Columns.Add(new GridColumn("Pid", "PID") { Width = 70, ReadOnly = true });
            _processes.Columns.Add(new GridColumn("Name", "Process") { Width = 240, ReadOnly = true });
            _processes.DataSource = _processSource;
            _processes.Selection.Changed += async (s, e) => await LoadSelectedProcessAsync();

            _maps.Columns.Add(new GridColumn("Name", "Name") { Width = 180, ReadOnly = true });
            _maps.Columns.Add(new GridColumn("Start", "Start") { Width = 135, ReadOnly = true });
            _maps.Columns.Add(new GridColumn("End", "End") { Width = 135, ReadOnly = true });
            _maps.Columns.Add(new GridColumn("Size", "Size") { Width = 90, ReadOnly = true });
            _maps.Columns.Add(new GridColumn("Prot", "Prot") { Width = 60, ReadOnly = true });
            _maps.DataSource = _mapSource;
            _maps.Selection.Changed += (s, e) => UseSelectedMapAddress();

            _prxModules.Columns.Add(new GridColumn("Handle", "Handle") { Width = 80, ReadOnly = true });
            _prxModules.Columns.Add(new GridColumn("Name", "Name") { Width = 240, ReadOnly = true });
            _prxModules.Columns.Add(new GridColumn("Text", "Text") { Width = 190, ReadOnly = true });
            _prxModules.Columns.Add(new GridColumn("Data", "Data") { Width = 190, ReadOnly = true });
            _prxModules.DataSource = _prxSource;

            _tabs.AddTab("Process", _processInfo);
            _tabs.AddTab("Maps", _maps);
            _tabs.AddTab("Memory", MemoryPanel());
            _tabs.AddTab("Search", SearchPanel());
            _tabs.AddTab("PRX", PrxPanel());
            _tabs.AddTab("Console", ConsolePanel());
            _tabs.AddTab("Log", LogPanel());
            _tabs.AddTab("Notify", NotifyPanel());
            _tabs.AddTab("Expert", ExpertPanel());

            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _toolbar.Controls.Add(_refresh, 0, 0);
            _toolbar.Controls.Add(_loadDetails, 1, 0);
            _toolbar.Controls.Add(_status, 2, 0);

            _split.Panel1.Controls.Add(_processes);
            _split.Panel2.Controls.Add(_tabs);
            Controls.Add(_split);
            Controls.Add(_toolbar);

            _refresh.Click += async (s, e) => await RefreshAsync();
            _loadDetails.Click += async (s, e) => await LoadSelectedProcessAsync();
            _previous.Click += async (s, e) => await StepMemoryAsync(-1);
            _read.Click += async (s, e) => await ReadMemoryAsync();
            _next.Click += async (s, e) => await StepMemoryAsync(1);
            _findAob.Click += async (s, e) => await FindAobAsync();
            _loadPrx.Click += async (s, e) => await LoadPrxAsync();
            _loadConsole.Click += async (s, e) => await LoadConsoleAsync();
            _notify.Click += async (s, e) => await NotifyAsync();
            _writeMemory.Click += async (s, e) => await WriteMemoryAsync();
            _allocate.Click += async (s, e) => await AllocateMemoryAsync();
            _free.Click += async (s, e) => await FreeMemoryAsync();
            _loadPrxModule.Click += async (s, e) => await LoadPrxModuleAsync();
            _unloadPrxModule.Click += async (s, e) => await UnloadPrxModuleAsync();
            _callRpc.Click += async (s, e) => await CallRpcAsync();
            _startLog.Click += (s, e) => StartLog();
            _stopLog.Click += (s, e) => StopLog();
            _clearLog.Click += (s, e) => _uartLog.Text = "";
            UpdateActionButtons();
        }

        private Control MemoryPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(new UiLabel { Text = "Address", TextAlignment = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_address, 1, 0);
            panel.Controls.Add(new UiLabel { Text = "Length", TextAlignment = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(_length, 3, 0);
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Anchor = AnchorStyles.Left, FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.Add(_previous);
            buttons.Controls.Add(_read);
            buttons.Controls.Add(_next);
            panel.Controls.Add(buttons, 4, 0);
            panel.Controls.Add(_memory, 0, 1);
            panel.SetColumnSpan(_memory, 5);
            return CommandPanel(panel, 640);
        }

        private Control SearchPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(new UiLabel { Text = "AOB", TextAlignment = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_aobPattern, 1, 0);
            panel.Controls.Add(new UiLabel { Text = "Selected map", TextAlignment = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(_findAob, 3, 0);
            panel.Controls.Add(_aobResult, 0, 1);
            panel.SetColumnSpan(_aobResult, 4);
            return CommandPanel(panel, 560);
        }

        private Control ConsolePanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(_loadConsole, 0, 0);
            panel.Controls.Add(new UiLabel { Text = "Socket 2811", TextAlignment = ContentAlignment.MiddleLeft }, 1, 0);
            panel.Controls.Add(_consoleInfo, 0, 1);
            panel.SetColumnSpan(_consoleInfo, 2);
            return CommandPanel(panel, 420);
        }

        private Control PrxPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(_loadPrx, 0, 0);
            panel.Controls.Add(new UiLabel { Text = "Selected process modules", TextAlignment = ContentAlignment.MiddleLeft }, 1, 0);
            panel.Controls.Add(_prxModules, 0, 1);
            panel.SetColumnSpan(_prxModules, 2);
            return CommandPanel(panel, 420);
        }


        private Control LogPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(_startLog, 0, 0);
            panel.Controls.Add(_stopLog, 1, 0);
            panel.Controls.Add(_clearLog, 2, 0);
            panel.Controls.Add(new UiLabel { Text = "UART " + _settings.Frame4.UartPort, TextAlignment = ContentAlignment.MiddleLeft }, 3, 0);
            panel.Controls.Add(_uartLog, 0, 1);
            panel.SetColumnSpan(_uartLog, 4);
            return CommandPanel(panel, 480);
        }

        private void StartLog()
        {
            if (_logCts != null) return;
            _logCts = new CancellationTokenSource();
            _status.Text = "Opening UART log...";
            UpdateActionButtons();
            var _ = ReadLogLoopAsync(_logCts);
        }

        private void StopLog()
        {
            if (_logCts == null) return;
            _logCts.Cancel();
            if (_logConnection != null) _logConnection.Dispose();
            _logConnection = null;
            _logCts = null;
            _status.Text = "UART log stopped.";
            UpdateActionButtons();
        }

        private async Task ReadLogLoopAsync(CancellationTokenSource cts)
        {
            var buffer = new byte[4096];
            try
            {
                using (var connection = await _uartClient.ConnectAsync(Endpoint(), cts.Token).ConfigureAwait(false))
                {
                    _logConnection = connection;
                    _ui.Dispatch(() =>
                    {
                        if (ReferenceEquals(_logCts, cts)) _status.Text = "UART log connected.";
                    });
                    while (!cts.IsCancellationRequested)
                    {
                        var read = await connection.Read.ReadAsync(buffer, 0, buffer.Length, cts.Token)
                            .ConfigureAwait(false);
                        if (read <= 0) break;
                        var text = Frame4UartClient.Decode(buffer, read);
                        _ui.Dispatch(() =>
                        {
                            if (!ReferenceEquals(_logCts, cts)) return;
                            _uartLog.Text += text;
                        });
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _ui.Dispatch(() =>
                {
                    if (ReferenceEquals(_logCts, cts)) _status.Text = "UART error: " + Ui.Describe(ex);
                });
            }
            finally
            {
                _ui.Dispatch(() =>
                {
                    if (!ReferenceEquals(_logCts, cts)) return;
                    _logConnection = null;
                    _logCts = null;
                    UpdateActionButtons();
                });
            }
        }

        private Control NotifyPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(new UiLabel { Text = "Type", TextAlignment = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_notifyType, 1, 0);
            panel.Controls.Add(new UiLabel { Text = "Message", TextAlignment = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(_notifyMessage, 3, 0);
            panel.Controls.Add(_notify, 4, 0);
            return CommandPanel(panel, 540);
        }

        private Control ExpertPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 10,
                BackColor = Color.Transparent
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var row = 0; row < 9; row++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(new UiLabel { Text = "Address", TextAlignment = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_writeAddress, 1, 0);
            panel.Controls.Add(new UiLabel { Text = "Bytes", TextAlignment = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(_writeBytes, 3, 0);
            panel.Controls.Add(_writeMemory, 4, 0);

            panel.Controls.Add(new UiLabel { Text = "Length", TextAlignment = ContentAlignment.MiddleLeft }, 0, 1);
            panel.Controls.Add(_allocLength, 1, 1);
            panel.Controls.Add(_allocate, 4, 1);

            panel.Controls.Add(new UiLabel { Text = "Free Addr", TextAlignment = ContentAlignment.MiddleLeft }, 0, 2);
            panel.Controls.Add(_freeAddress, 1, 2);
            panel.Controls.Add(new UiLabel { Text = "Length", TextAlignment = ContentAlignment.MiddleLeft }, 2, 2);
            panel.Controls.Add(_freeLength, 3, 2);
            panel.Controls.Add(_free, 4, 2);

            panel.Controls.Add(new UiLabel { Text = "Process", TextAlignment = ContentAlignment.MiddleLeft }, 0, 3);
            panel.Controls.Add(_prxProcessName, 1, 3);
            panel.Controls.Add(new UiLabel { Text = "PRX Path", TextAlignment = ContentAlignment.MiddleLeft }, 2, 3);
            panel.Controls.Add(_prxPath, 3, 3);
            panel.Controls.Add(_loadPrxModule, 4, 3);

            panel.Controls.Add(new UiLabel { Text = "Handle", TextAlignment = ContentAlignment.MiddleLeft }, 0, 4);
            panel.Controls.Add(_prxHandle, 1, 4);
            panel.Controls.Add(_unloadPrxModule, 4, 4);

            panel.Controls.Add(new UiLabel { Text = "RPC Stub", TextAlignment = ContentAlignment.MiddleLeft }, 0, 5);
            panel.Controls.Add(_rpcStub, 1, 5);
            panel.Controls.Add(new UiLabel { Text = "RIP", TextAlignment = ContentAlignment.MiddleLeft }, 2, 5);
            panel.Controls.Add(_rpcRip, 3, 5);
            panel.Controls.Add(_callRpc, 4, 5);

            panel.Controls.Add(new UiLabel { Text = "RDI", TextAlignment = ContentAlignment.MiddleLeft }, 0, 6);
            panel.Controls.Add(_rpcRdi, 1, 6);
            panel.Controls.Add(new UiLabel { Text = "RSI", TextAlignment = ContentAlignment.MiddleLeft }, 2, 6);
            panel.Controls.Add(_rpcRsi, 3, 6);

            panel.Controls.Add(new UiLabel { Text = "RDX", TextAlignment = ContentAlignment.MiddleLeft }, 0, 7);
            panel.Controls.Add(_rpcRdx, 1, 7);
            panel.Controls.Add(new UiLabel { Text = "RCX", TextAlignment = ContentAlignment.MiddleLeft }, 2, 7);
            panel.Controls.Add(_rpcRcx, 3, 7);

            panel.Controls.Add(new UiLabel { Text = "R8", TextAlignment = ContentAlignment.MiddleLeft }, 0, 8);
            panel.Controls.Add(_rpcR8, 1, 8);
            panel.Controls.Add(new UiLabel { Text = "R9", TextAlignment = ContentAlignment.MiddleLeft }, 2, 8);
            panel.Controls.Add(_rpcR9, 3, 8);

            panel.Controls.Add(_expertOutput, 0, 9);
            panel.SetColumnSpan(_expertOutput, 5);
            return CommandPanel(panel, 640);
        }

        private Control CommandPanel(TableLayoutPanel panel, int minimumWidth)
        {
            // Command cells follow skin/font metrics; only the output row consumes remaining space.
            foreach (RowStyle row in panel.RowStyles)
                if (row.SizeType == SizeType.Absolute) row.SizeType = SizeType.AutoSize;
            foreach (ColumnStyle column in panel.ColumnStyles)
                if (column.SizeType == SizeType.Absolute) column.SizeType = SizeType.AutoSize;
            UiLayout.SizeCommandColumns(panel);
            panel.Dock = DockStyle.None;
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            host.Controls.Add(panel);
            host.Layout += (s, e) =>
            {
                var minimumHeight = 0;
                for (var row = 0; row < panel.RowCount; row++)
                {
                    if (panel.RowStyles[row].SizeType == SizeType.Percent) { minimumHeight += 100; continue; }
                    var children = panel.Controls.Cast<Control>().Where(c => panel.GetRow(c) == row).ToArray();
                    minimumHeight += children.Length == 0 ? 0 : children.Max(c => c.GetPreferredSize(Size.Empty).Height + c.Margin.Vertical);
                }
                panel.Size = new Size(Math.Max(host.ClientSize.Width, (int)Math.Ceiling(DpiScale.Scale(minimumWidth, DeviceDpi) * Font.Size / 10)),
                    Math.Max(host.ClientSize.Height, minimumHeight));
            };
            return host;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _split.SplitterDistance = Math.Max(260, ClientSize.Width / 3);
            var _ = RefreshAsync();
        }

        private Frame4Endpoint Endpoint()
        {
            return new Frame4Endpoint(_settings.Ip, _settings.Frame4.HttpPort, _settings.Frame4.UartPort);
        }

        private async Task RefreshAsync()
        {
            var cts = BeginRequest();
            SetBusy("Loading frame4 process list...");
            try
            {
                var endpoint = Endpoint();
                var processes = await _client.GetProcessesAsync(endpoint, cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _processRows.Clear();
                    foreach (var process in processes) _processRows.Add(new ProcessRow(process.Pid, process.Name));
                    Rebind(_processes, _processSource);
                    _processInfo.Text = "";
                    _mapRows.Clear();
                    Rebind(_maps, _mapSource);
                    _prxRows.Clear();
                    Rebind(_prxModules, _prxSource);
                    _status.Text = _processRows.Count + " processes from " + endpoint.HttpBaseUri.Host + ":" + endpoint.HttpPort + ".";
                    if (_processRows.Count > 0) _processes.Selection.Select(0);
                    UpdateActionButtons();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task LoadSelectedProcessAsync()
        {
            var row = SelectedProcess();
            if (row == null) { UpdateActionButtons(); return; }
            var cts = BeginRequest();
            SetBusy("Loading " + row.Name + "...");
            try
            {
                var endpoint = Endpoint();
                var infoTask = _client.GetProcessInfoAsync(endpoint, row.Pid, cts.Token);
                var mapsTask = _client.GetProcessMapsAsync(endpoint, row.Pid, cts.Token);
                await Task.WhenAll(infoTask, mapsTask).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    ShowInfo(infoTask.Result);
                    _mapRows.Clear();
                    foreach (var map in mapsTask.Result)
                        _mapRows.Add(new MapRow(map.Name, map.Start, map.End, map.Offset, map.Protection));
                    Rebind(_maps, _mapSource);
                    _prxRows.Clear();
                    Rebind(_prxModules, _prxSource);
                    if (_mapRows.Count > 0) _maps.Selection.Select(0);
                    _status.Text = "Loaded " + row.Name + " (" + _mapRows.Count + " maps).";
                    UpdateActionButtons();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task ReadMemoryAsync()
        {
            var row = SelectedProcess();
            if (row == null) return;
            ulong address;
            uint length;
            if (!TryHex(_address.Text, out address) || !TryUInt(_length.Text, out length) || length == 0)
            {
                _status.Text = "Address and length must be valid hex/decimal values.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Reading memory...");
            try
            {
                var bytes = await _client.ReadMemoryAsync(Endpoint(), row.Pid, address, length, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _memory.Text = Frame4HexDump.Format(address, bytes);
                    _status.Text = "Read " + bytes.Length + " bytes from " + row.Name + ".";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task StepMemoryAsync(int direction)
        {
            ulong address;
            uint length;
            if (!TryHex(_address.Text, out address) || !TryUInt(_length.Text, out length) || length == 0)
            {
                _status.Text = "Address and length must be valid hex/decimal values.";
                return;
            }

            var delta = (ulong)length;
            _address.Text = direction < 0
                ? Hex(address > delta ? address - delta : 0)
                : Hex(address + delta);
            await ReadMemoryAsync();
        }

        private async Task LoadConsoleAsync()
        {
            var cts = BeginRequest();
            SetBusy("Loading console info...");
            try
            {
                var endpoint = Endpoint();
                var versionTask = _packetClient.GetVersionAsync(endpoint, cts.Token);
                var infoTask = _packetClient.GetConsoleInfoAsync(endpoint, cts.Token);
                await Task.WhenAll(versionTask, infoTask).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    ShowConsole(versionTask.Result, infoTask.Result);
                    _status.Text = "Loaded frame4 console info.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task FindAobAsync()
        {
            var row = SelectedProcess();
            if (row == null) return;
            Frame4AobPattern pattern;
            try
            {
                pattern = Frame4AobPattern.Parse(_aobPattern.Text);
            }
            catch (ArgumentException ex)
            {
                _status.Text = ex.Message;
                return;
            }

            var map = SelectedMap();
            ulong start;
            uint length;
            if (map != null)
            {
                start = map.Start;
                length = map.Size > uint.MaxValue ? uint.MaxValue : (uint)map.Size;
            }
            else if (!TryHex(_address.Text, out start) || !TryUInt(_length.Text, out length) || length == 0)
            {
                _status.Text = "Select a map or enter address and length.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Searching AOB...");
            try
            {
                var result = await _packetClient.FindAobAsync(Endpoint(), row.Pid, start, length, pattern, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _aobResult.Text = result == 0
                        ? "No match in " + Hex(start) + " - " + Hex(start + length) + "."
                        : "Match: " + Hex(result);
                    _status.Text = result == 0 ? "AOB search finished without a match." : "AOB match at " + Hex(result) + ".";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task LoadPrxAsync()
        {
            var row = SelectedProcess();
            if (row == null) return;
            var cts = BeginRequest();
            SetBusy("Loading PRX modules...");
            try
            {
                var modules = await _packetClient.GetPrxModulesAsync(Endpoint(), row.Pid, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _prxRows.Clear();
                    foreach (var module in modules)
                        _prxRows.Add(new PrxRow(module.Handle, module.Name, module.TextAddress, module.TextSize,
                            module.DataAddress, module.DataSize));
                    Rebind(_prxModules, _prxSource);
                    _status.Text = "Loaded " + _prxRows.Count + " PRX modules from " + row.Name + ".";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task NotifyAsync()
        {
            int type;
            if (!int.TryParse(_notifyType.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out type))
            {
                _status.Text = "Notify type must be a decimal number.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Sending notification...");
            try
            {
                await _client.NotifyAsync(Endpoint(), type, _notifyMessage.Text, cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (IsCurrent(cts)) _status.Text = "Notification sent.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task WriteMemoryAsync()
        {
            var row = SelectedProcess();
            if (!_settings.ExpertMode || row == null) return;
            ulong address;
            if (!TryHex(_writeAddress.Text, out address))
            {
                _status.Text = "Write address must be valid.";
                return;
            }

            byte[] bytes;
            if (!TryBytes(_writeBytes.Text, out bytes) || bytes.Length == 0)
            {
                _status.Text = "Write bytes must be hex bytes like 90 90.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Writing memory...");
            try
            {
                await _client.WriteMemoryAsync(Endpoint(), row.Pid, address, bytes, cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _expertOutput.Text = "Wrote " + bytes.Length + " bytes to " + Hex(address) + ".";
                    _status.Text = "Memory write finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task AllocateMemoryAsync()
        {
            var row = SelectedProcess();
            if (!_settings.ExpertMode || row == null) return;
            uint length;
            if (!TryUInt(_allocLength.Text, out length) || length == 0)
            {
                _status.Text = "Alloc length must be valid.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Allocating memory...");
            try
            {
                var address = await _client.AllocateMemoryAsync(Endpoint(), row.Pid, length, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _freeAddress.Text = Hex(address);
                    _freeLength.Text = Hex(length);
                    _expertOutput.Text = "Allocated " + Hex(length) + " at " + Hex(address) + ".";
                    _status.Text = "Memory allocation finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task FreeMemoryAsync()
        {
            var row = SelectedProcess();
            if (!_settings.ExpertMode || row == null) return;
            ulong address;
            uint length;
            if (!TryHex(_freeAddress.Text, out address) || !TryUInt(_freeLength.Text, out length) || length == 0)
            {
                _status.Text = "Free address and length must be valid.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Freeing memory...");
            try
            {
                await _client.FreeMemoryAsync(Endpoint(), row.Pid, address, length, cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _expertOutput.Text = "Freed " + Hex(length) + " at " + Hex(address) + ".";
                    _status.Text = "Memory free finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task LoadPrxModuleAsync()
        {
            if (!_settings.ExpertMode) return;
            var cts = BeginRequest();
            SetBusy("Loading PRX...");
            try
            {
                var handle = await _packetClient.LoadPrxAsync(Endpoint(), _prxProcessName.Text, _prxPath.Text,
                    cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _prxHandle.Text = Hex(handle);
                    _expertOutput.Text = "Loaded PRX handle " + Hex(handle) + ".";
                    _status.Text = "PRX load finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task UnloadPrxModuleAsync()
        {
            if (!_settings.ExpertMode) return;
            uint handle;
            if (!TryUInt(_prxHandle.Text, out handle))
            {
                _status.Text = "PRX handle must be valid.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Unloading PRX...");
            try
            {
                await _packetClient.UnloadPrxAsync(Endpoint(), _prxProcessName.Text, handle, cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _expertOutput.Text = "Unloaded PRX handle " + Hex(handle) + ".";
                    _status.Text = "PRX unload finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private async Task CallRpcAsync()
        {
            var row = SelectedProcess();
            if (!_settings.ExpertMode || row == null) return;
            ulong rpcStub;
            ulong rip;
            ulong rdi;
            ulong rsi;
            ulong rdx;
            ulong rcx;
            ulong r8;
            ulong r9;
            if (!TryHex(_rpcStub.Text, out rpcStub) || !TryHex(_rpcRip.Text, out rip) ||
                !TryHex(_rpcRdi.Text, out rdi) || !TryHex(_rpcRsi.Text, out rsi) ||
                !TryHex(_rpcRdx.Text, out rdx) || !TryHex(_rpcRcx.Text, out rcx) ||
                !TryHex(_rpcR8.Text, out r8) || !TryHex(_rpcR9.Text, out r9))
            {
                _status.Text = "RPC values must be valid hex/decimal values.";
                return;
            }

            var cts = BeginRequest();
            SetBusy("Calling RPC...");
            try
            {
                var result = await _packetClient.CallRpcAsync(Endpoint(),
                    new Frame4RpcCall(row.Pid, rpcStub, rip, rdi, rsi, rdx, rcx, r8, r9), cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _expertOutput.Text = "RPC result for PID " + result.Pid + ": RAX " + Hex(result.Rax) + ".";
                    _status.Text = "RPC call finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetError(cts, ex);
            }
            finally
            {
                FinishRequest(cts);
            }
        }

        private CancellationTokenSource BeginRequest()
        {
            if (_loadCts != null) _loadCts.Cancel();
            _loadCts = new CancellationTokenSource();
            UpdateActionButtons();
            return _loadCts;
        }

        private bool IsCurrent(CancellationTokenSource cts)
        {
            return !IsDisposed && ReferenceEquals(_loadCts, cts) && !cts.IsCancellationRequested;
        }

        private void SetBusy(string text)
        {
            _refresh.Enabled = false;
            _loadDetails.Enabled = false;
            _read.Enabled = false;
            _previous.Enabled = false;
            _next.Enabled = false;
            _notify.Enabled = false;
            _loadConsole.Enabled = false;
            _findAob.Enabled = false;
            _loadPrx.Enabled = false;
            _startLog.Enabled = false;
            _stopLog.Enabled = false;
            _clearLog.Enabled = false;
            _writeMemory.Enabled = false;
            _allocate.Enabled = false;
            _free.Enabled = false;
            _loadPrxModule.Enabled = false;
            _unloadPrxModule.Enabled = false;
            _callRpc.Enabled = false;
            _status.Text = text;
        }

        private void FinishRequest(CancellationTokenSource cts)
        {
            _ui.Dispatch(() =>
            {
                if (IsCurrent(cts)) UpdateActionButtons();
            });
        }

        private void SetError(CancellationTokenSource cts, Exception ex)
        {
            _ui.Dispatch(() =>
            {
                if (!IsCurrent(cts)) return;
                _status.Text = "Frame4 error: " + Ui.Describe(ex);
                UpdateActionButtons();
            });
        }

        private ProcessRow SelectedProcess()
        {
            var row = _processes.Selection.CurrentRow;
            return row >= 0 && row < _processRows.Count ? _processRows[row] : null;
        }

        private MapRow SelectedMap()
        {
            var row = _maps.Selection.CurrentRow;
            return row >= 0 && row < _mapRows.Count ? _mapRows[row] : null;
        }

        private void UseSelectedMapAddress()
        {
            var map = SelectedMap();
            if (map != null)
            {
                _address.Text = Hex(map.Start);
                _writeAddress.Text = Hex(map.Start);
            }
            var process = SelectedProcess();
            if (process != null && string.IsNullOrWhiteSpace(_prxProcessName.Text))
                _prxProcessName.Text = process.Name;
        }

        private void ShowInfo(Frame4ProcessInfo info)
        {
            _processInfo.Text =
                "PID: " + info.Pid + Environment.NewLine +
                "Name: " + info.Name + Environment.NewLine +
                "Path: " + info.Path + Environment.NewLine +
                "Title ID: " + info.TitleId + Environment.NewLine +
                "Content ID: " + info.ContentId;
        }

        private void ShowConsole(string version, Frame4ConsoleInfo info)
        {
            _consoleInfo.Text =
                "Frame4: " + version + Environment.NewLine +
                "PSID: " + info.PsidHex + Environment.NewLine +
                "Update Version: " + Hex(info.UpdateVersion) + Environment.NewLine +
                "SDK Version: " + Hex(info.SdkVersion) + Environment.NewLine +
                "OS Type: " + info.KernelOsType + Environment.NewLine +
                "OS Release: " + info.KernelOsRelease + Environment.NewLine +
                "OS Revision: " + info.KernelOsRevision + Environment.NewLine +
                "Kernel: " + info.KernelVersion + Environment.NewLine +
                "Model: " + info.HardwareModel + Environment.NewLine +
                "CPUs: " + info.CpuCount;
        }

        private void UpdateActionButtons()
        {
            var hasProcess = SelectedProcess() != null;
            _refresh.Enabled = true;
            _loadDetails.Enabled = hasProcess;
            _read.Enabled = hasProcess;
            _previous.Enabled = hasProcess;
            _next.Enabled = hasProcess;
            _findAob.Enabled = hasProcess;
            _loadPrx.Enabled = hasProcess;
            _notify.Enabled = true;
            _loadConsole.Enabled = true;
            _startLog.Enabled = _logCts == null;
            _stopLog.Enabled = _logCts != null;
            _clearLog.Enabled = true;
            _writeMemory.Enabled = _settings.ExpertMode && hasProcess;
            _allocate.Enabled = _settings.ExpertMode && hasProcess;
            _free.Enabled = _settings.ExpertMode && hasProcess;
            _loadPrxModule.Enabled = _settings.ExpertMode;
            _unloadPrxModule.Enabled = _settings.ExpertMode;
            _callRpc.Enabled = _settings.ExpertMode && hasProcess;
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

        private static bool TryHex(string value, out ulong result)
        {
            var text = (value ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryUInt(string value, out uint result)
        {
            ulong parsed;
            if (TryHex(value, out parsed) && parsed <= uint.MaxValue)
            {
                result = (uint)parsed;
                return true;
            }
            result = 0;
            return false;
        }

        private static bool TryBytes(string value, out byte[] bytes)
        {
            var tokens = (value ?? "").Split(new[] { ' ', '\t', '\r', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries);
            var parsed = new List<byte>();
            foreach (var token in tokens)
            {
                if (token.Length != 2)
                {
                    bytes = new byte[0];
                    return false;
                }
                byte valueByte;
                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out valueByte))
                {
                    bytes = new byte[0];
                    return false;
                }
                parsed.Add(valueByte);
            }
            bytes = parsed.ToArray();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_loadCts != null) _loadCts.Cancel();
                StopLog();
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

        private sealed class MapRow
        {
            public MapRow(string name, ulong start, ulong end, ulong offset, int protection)
            {
                Name = name;
                Start = start;
                End = end;
                Offset = offset;
                Protection = protection;
            }

            public string Name { get; private set; }
            public ulong Start { get; private set; }
            public ulong End { get; private set; }
            public ulong Offset { get; private set; }
            public int Protection { get; private set; }
            public ulong Size { get { return End > Start ? End - Start : 0; } }
        }

        private sealed class PrxRow
        {
            public PrxRow(uint handle, string name, ulong textAddress, uint textSize, ulong dataAddress,
                uint dataSize)
            {
                Handle = handle;
                Name = name;
                TextAddress = textAddress;
                TextSize = textSize;
                DataAddress = dataAddress;
                DataSize = dataSize;
            }

            public uint Handle { get; private set; }
            public string Name { get; private set; }
            public ulong TextAddress { get; private set; }
            public uint TextSize { get; private set; }
            public ulong DataAddress { get; private set; }
            public uint DataSize { get; private set; }
        }
    }
}
