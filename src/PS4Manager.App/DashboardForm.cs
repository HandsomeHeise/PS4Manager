using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using PS4Manager.App.Notifications;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Config;
using PS4Manager.Core.Presentation;
using PS4Manager.Core.Session;
using PS4Manager.Gallery.App;

namespace PS4Manager.App
{
    /// <summary>
    /// Das Dashboard (Spec „Dashboard"): Es besitzt die Verbindung und öffnet
    /// jedes Werkzeug als eigenes Fenster. Ein Fenster je Werkzeug, eine
    /// Warteschlange für alle, ein Endpunkt (Spec „Gemeinsame Verbindung").
    ///
    /// Die Werkzeuge bauen keine Verbindung mehr — sie lassen sich vom
    /// veröffentlichten <see cref="ManagerSession"/> bedienen. Ein Fenster
    /// schließen trennt deshalb nichts: Es verschwindet nur.
    /// </summary>
    internal sealed partial class DashboardForm : AppWindow
    {
        private const string FilesKey = "files";
        private const string MediaKey = "media";
        private const string GoldHenKey = "goldhen";
        private const string Frame4Key = "frame4";
        private const string HealthKey = "health";
        private const string PrxKey = "prx";
        private const string BackupKey = "backup";
        private const string PackageKey = "packages";
        private const string LumaKey = "luma";
        private const string UpdatesKey = "updates";
        private const string ModsKey = "mods";
        private const string GamesKey = "games";
        private readonly PS4Manager.Core.Games.ITrainerTransport _trainerTransport = new PS4Manager.Core.Games.Frame4TrainerTransport(
            new PS4Manager.Core.Frame4.Frame4Client(), new PS4Manager.Core.Frame4.Frame4PacketClient());

        private static readonly ToolDefinition[] Tools =
        {
            new ToolDefinition(FilesKey, "File Manager", true),
            new ToolDefinition(MediaKey, "Screenshots & Videos", true),
            new ToolDefinition(GoldHenKey, "GoldHEN Manager", true),
            new ToolDefinition(HealthKey, "PS4 Health", false),
            new ToolDefinition(Frame4Key, "Frame4 Debugger", false),
            new ToolDefinition(PrxKey, "PRX Manager", false),
            new ToolDefinition(BackupKey, "Backup Timeline", false),
            new ToolDefinition(PackageKey, "PKG Manager", false),
            new ToolDefinition(LumaKey, "Luma Script Manager", false),
            new ToolDefinition(ModsKey, "Mod Library", false),
            new ToolDefinition(GamesKey, "Games", false)
        };

        private readonly ManagerSettings _settings;
        private readonly ManagerSessionController _sessions;
        private readonly ToolWindowRegistry _tools;
        private readonly ToolUiDispatcher _ui;
        private readonly DesktopNotificationService _notifications;
        private readonly TransferNotificationCoordinator _notificationCoordinator;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        // Ribbon
        private readonly AppRibbon _ribbon = new AppRibbon { Dock = DockStyle.Top };
        private readonly RibbonItem _connectItem = Small("Connect");
        private readonly RibbonItem _transfersItem = Small("Transfers");
        private readonly RibbonItem _updatesItem = Small("Check for updates");
        private readonly Dictionary<string, RibbonItem> _toolItems = new Dictionary<string, RibbonItem>(StringComparer.Ordinal);

        // Verbindungskarte
        private readonly UiPanel _connectionCard = new UiPanel { Dock = DockStyle.Top };
        private readonly UiPanel _headline = new UiPanel { Dock = DockStyle.Top };
        private readonly UiLabel _profile = new UiLabel { Text = "Console" };
        private readonly UiTextBox _ip = new UiTextBox();
        private readonly UiLabel _portLabel = new UiLabel { Text = "Port" };
        private readonly UiTextBox _port = new UiTextBox();
        private readonly UiButton _connect = new UiButton { Text = "Connect" };
        private readonly UiLabel _state = new UiLabel { Dock = DockStyle.Fill, AutoSize = false };

        // Übertragungen
        private readonly UiPanel _transferBar = new UiPanel { Dock = DockStyle.Bottom };
        private readonly UiLabel _transferSummary = new UiLabel { Dock = DockStyle.Fill, AutoSize = false };
        private readonly UiButton _showTransfers = new UiButton { Text = "Transfers …", Dock = DockStyle.Right };

        private CancellationTokenSource _connectCts;
        private TransferQueue _queue;
        private string _lastError;
        private bool _closing;

        public DashboardForm(ManagerSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (_settings.FileManager == null) _settings.FileManager = new FileManagerViewSettings();

            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            _sessions = new ManagerSessionController(new ManagerSessionFactory(ActivePromptOwner));
            _tools = new ToolWindowRegistry(CreateTool);

            WindowIcon.Apply(this);
            BuildUi();
            _notifications = new DesktopNotificationService(this, ShowTransfers);
            _notificationCoordinator = new TransferNotificationCoordinator(_notifications, _ui.Dispatch, ShowTransfers);
            _sessions.SessionChanged += OnSessionChanged;
            UpdateConnectionState();
            UpdateTransferSummary();
        }

        // ---- Aufbau --------------------------------------------------------------------

        private void BuildUi()
        {
            Text = "PS4 Manager";
            ClientSize = new Size(1440, 900);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1080, 720);

            _ip.Text = _settings.Ip;
            _port.Text = _settings.Port.ToString();
            _state.TextAlignment = ContentAlignment.MiddleLeft;
            _transferSummary.TextAlignment = ContentAlignment.MiddleLeft;

            BuildRibbon();
            _headline.Controls.Add(_connect);
            _headline.Controls.Add(_port);
            _headline.Controls.Add(_portLabel);
            _headline.Controls.Add(_ip);
            _headline.Controls.Add(_profile);
            _connectionCard.Controls.Add(_state);
            _connectionCard.Controls.Add(_headline);

            _transferBar.Controls.Add(_transferSummary);
            _transferBar.Controls.Add(_showTransfers);

            // Zuletzt Hinzugefügtes dockt außen: unten die Übertragungsleiste,
            // dann die Verbindungskarte, zuletzt das Ribbon ganz oben.
            Controls.Add(BuildDashboard());
            Controls.Add(_transferBar);
            Controls.Add(_ribbon);
            AppTheme.Changed += OnDashboardThemeChanged;
            ApplyDashboardTheme();

            _connect.Click += async (s, e) =>
            {
                if (_sessions.Current != null) await DisconnectAsync(true);
                else await ConnectAsync();
            };
            _connectItem.Click += async (s, e) =>
            {
                if (_sessions.Current != null) await DisconnectAsync(true);
                else await ConnectAsync();
            };
            _transfersItem.Click += (s, e) => ShowTransfers();
            _updatesItem.Click += (s, e) => OpenTool(UpdatesKey);
            _showTransfers.Click += (s, e) => ShowTransfers();
            _ip.KeyDown += OnEndpointKeyDown;
            _port.KeyDown += OnEndpointKeyDown;
        }

        private void BuildRibbon()
        {
            var console = new RibbonGroup("Console");
            console.Items.Add(_connectItem);

            var transfers = new RibbonGroup("Transfers");
            transfers.Items.Add(_transfersItem);

            var files = new RibbonGroup("Files & media");
            var modding = new RibbonGroup("Modding");
            var games = new RibbonGroup("Games & mods");
            var tools = new RibbonGroup("Tools");
            foreach (var definition in Tools)
            {
                var item = Small(definition.Title);
                item.Size = RibbonItemSize.Large;
                item.Image = DashboardArtwork.Icon(definition.Key);
                var key = definition.Key;
                item.Click += (s, e) => OpenTool(key);
                _toolItems[key] = item;
                if (key == FilesKey || key == MediaKey || key == PackageKey) files.Items.Add(item);
                else if (key == GoldHenKey || key == PrxKey || key == LumaKey) modding.Items.Add(item);
                else if (key == GamesKey || key == ModsKey) games.Items.Add(item);
                else tools.Items.Add(item);
            }

            var updates = new RibbonGroup("Updates");
            updates.Items.Add(_updatesItem);

            _connectItem.Size = RibbonItemSize.Large; _connectItem.Image = DashboardArtwork.Icon("connect");
            _transfersItem.Size = RibbonItemSize.Large; _transfersItem.Image = DashboardArtwork.Icon("transfers");
            _updatesItem.Size = RibbonItemSize.Large; _updatesItem.Image = DashboardArtwork.Icon("updates");
            var settings = new RibbonGroup("Configuration");
            var settingsItem = new RibbonItem("Settings") { Size = RibbonItemSize.Large, Image = DashboardArtwork.Icon("settings") };
            settingsItem.Click += (s, e) => ShowSettings(); settings.Items.Add(settingsItem);
            var home = new RibbonTab("Start");
            home.Groups.Add(console);
            home.Groups.Add(files);
            home.Groups.Add(transfers);
            home.Groups.Add(settings);
            home.Groups.Add(updates);
            _ribbon.Tabs.Add(home);
            var moddingTab = new RibbonTab("PS4 / Modding"); moddingTab.Groups.Add(modding); _ribbon.Tabs.Add(moddingTab);
            var gamesTab = new RibbonTab("Games"); gamesTab.Groups.Add(games); _ribbon.Tabs.Add(gamesTab);
            var toolsTab = new RibbonTab("Tools"); toolsTab.Groups.Add(tools); _ribbon.Tabs.Add(toolsTab);
            var viewTab = new RibbonTab("View"); var theme = new RibbonGroup("Appearance");
            var dark = new RibbonItem("Dark theme") { Size = RibbonItemSize.Large, Image = DashboardArtwork.Icon("dark") };
            var light = new RibbonItem("Light theme") { Size = RibbonItemSize.Large, Image = DashboardArtwork.Icon("light") };
            dark.Click += (s, e) => { AppTheme.Current = AppTheme.Dark; SaveSettings(); };
            light.Click += (s, e) => { AppTheme.Current = AppTheme.Light; SaveSettings(); };
            theme.Items.Add(dark); theme.Items.Add(light); viewTab.Groups.Add(theme); _ribbon.Tabs.Add(viewTab);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ClientSize = new Size(Px(1440), Px(900));
            CenterToScreen();
            LayoutConnectionCard();
            LayoutTransferBar();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_headline != null && IsHandleCreated) LayoutConnectionCard();
        }

        /// <summary>
        /// Maße erst ab OnLoad: Die automatische DPI-Skalierung ist zu diesem
        /// Zeitpunkt schon gelaufen, also rechnet jede Länge hier über Px
        /// (Muster wie AppForm/FileManagerForm).
        /// </summary>
        private void LayoutConnectionCard()
        {
            int pad = Px(8);
            int fieldHeight = _connect.GetPreferredSize(Size.Empty).Height;
            int width = Math.Max(Px(280), _headline.ClientSize.Width - pad * 2);
            _profile.SetBounds(pad, pad + Px(6), Px(64), fieldHeight);
            _ip.SetBounds(pad + Px(70), pad, Math.Max(Px(120), width - Px(78)), fieldHeight);
            int second = pad + fieldHeight + Px(8);
            _portLabel.SetBounds(pad, second + Px(6), Px(64), fieldHeight);
            _port.SetBounds(pad + Px(70), second, Px(70), fieldHeight);
            _connect.SetBounds(Math.Max(pad + Px(152), _headline.ClientSize.Width - Px(125)), second, Px(115), fieldHeight);
            _headline.Height = fieldHeight * 2 + pad * 3;
        }

        private void LayoutTransferBar()
        {
            int chrome = _transferBar.Height - _transferBar.DisplayRectangle.Height;
            _transferBar.Height = _showTransfers.GetPreferredSize(Size.Empty).Height + chrome;
        }

        // ---- Verbindung ----------------------------------------------------------------

        /// <summary>
        /// Das Besitzerfenster für Rückfragen und Konfliktdialoge: das gerade
        /// aktive, sonst das Dashboard. Sonst verschwände ein Dialog hinter dem
        /// Werkzeugfenster, aus dem er kommt.
        /// </summary>
        private Form ActivePromptOwner()
        {
            var active = Form.ActiveForm;
            return active != null && !active.IsDisposed && active.Visible ? active : this;
        }

        private void OnEndpointKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            var _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            ManagerEndpoint endpoint;
            var error = ReadEndpoint(out endpoint);
            if (error != null)
            {
                _lastError = error;
                UpdateConnectionState();
                _ip.Focus();
                return;
            }

            // Neuverbinden bei laufender Sitzung: Erst die Aufträge abbrechen,
            // auf sie warten und die alte Sitzung entsorgen (wie die Einzel-App
            // vor dem Neuverbinden) — die Warnung oben sagt genau das zu, und
            // `ManagerSession.Dispose` allein ließe die Warteschlange weiterlaufen.
            if (_sessions.Current != null)
            {
                if (!ConfirmLeave("Reconnect")) return;
                await EndSessionAsync();
            }

            if (_connectCts != null) _connectCts.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _connectCts = cts;
            _lastError = null;

            var attempt = _sessions.ConnectAsync(endpoint, _settings.ExpertMode, cts.Token);
            UpdateConnectionState();                 // zeigt „Connecting …", Knopf gesperrt
            try
            {
                await attempt;
                if (cts != _connectCts) return;      // ein neuerer Versuch führt

                // Gewählten Endpunkt erst nach erfolgreicher Verbindung merken
                // (Plan Task 6, Schritt 3) — aus der veröffentlichten Sitzung,
                // nicht aus dem Textfeld.
                var session = _sessions.Current;
                if (session != null)
                {
                    _settings.Ip = session.Endpoint.Host;
                    _settings.Port = session.Endpoint.Port;
                }
                SaveSettings();
            }
            catch (Exception ex)
            {
                if (cts == _connectCts && !(ex is OperationCanceledException)) _lastError = Ui.Describe(ex);
            }
            finally
            {
                if (cts == _connectCts) UpdateConnectionState();
            }
        }

        private async Task DisconnectAsync(bool confirm)
        {
            if (confirm && !ConfirmLeave("Disconnect")) return;
            await EndSessionAsync();
        }

        /// <summary>
        /// Aufträge abbrechen, auf sie warten, dann die Sitzung entsorgen — die
        /// Reihenfolge der Einzel-App (MainForm.Transfers), nur einmal für alle
        /// Werkzeuge.
        /// </summary>
        private async Task EndSessionAsync()
        {
            var session = _sessions.Current;
            if (session == null) return;
            var token = session.CancellationToken;
            foreach (var job in session.Queue.Jobs.Where(j => !j.IsFinished).ToList())
            {
                await session.Queue.CancelAsync(job);
                if (token.IsCancellationRequested) break;
            }
            await session.Queue.WhenIdle();
            await _sessions.DisconnectAsync();
        }

        private string ReadEndpoint(out ManagerEndpoint endpoint)
        {
            endpoint = null;
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port)) return "Enter the FTP port as a number (1–65535).";
            try
            {
                endpoint = new ManagerEndpoint(_ip.Text, port);
            }
            catch (ArgumentException ex)
            {
                // ManagerEndpoint prüft Rechner und Port und nennt den Grund.
                return ex.Message;
            }
            return null;
        }

        private void OnSessionChanged(object sender, ManagerSessionChangedEventArgs args)
        {
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed || _closing) return;
                    AttachQueue(_sessions.Current);
                    UpdateConnectionState();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>Ein Werkzeug bittet um Verbinden oder Trennen — führen tut es nur das Dashboard.</summary>
        private async void OnConnectionRequested(object sender, EventArgs e)
        {
            if (_sessions.Current != null) await DisconnectAsync(true);
            else await ConnectAsync();
        }

        private void UpdateConnectionState()
        {
            var session = _sessions.Current;
            var connecting = _sessions.State == ConnectionState.Connecting;
            _connect.Text = session != null ? "Disconnect" : "Connect";
            _connect.Enabled = !connecting;
            _connectItem.Text = session != null ? "Disconnect" : "Connect";
            _connectItem.Enabled = !connecting;
            _transfersItem.Enabled = session != null;
            foreach (var definition in Tools)
            {
                RibbonItem item;
                if (_toolItems.TryGetValue(definition.Key, out item))
                    item.Enabled = !definition.NeedsConnection || session != null;
            }
            _ribbon.Invalidate();
            UpdateDashboardState();

            if (session != null)
            {
                var endpoint = session.Endpoint;
                _ip.Text = endpoint.Host;
                _port.Text = endpoint.Port.ToString();
                var connected = "Connected to " + endpoint.Host + ":" + endpoint.Port
                                + (_settings.ExpertMode ? " · expert mode" : "") + ".";
                SetState(_lastError == null ? connected : "Error: " + _lastError);
                Text = "PS4 Manager — " + endpoint.Host;
            }
            else
            {
                var text = connecting ? "Connecting …" : "Disconnected. Enter the PS4 address and connect.";
                SetState(_lastError == null ? text : "Error: " + _lastError);
                Text = "PS4 Manager";
            }
        }

        private void SetState(string text)
        {
            _state.Text = text;
        }

        // ---- Werkzeuge -----------------------------------------------------------------

        private void OpenTool(string key)
        {
            var form = _tools.Open(key);
            if (!form.Visible) form.Show(this);
            form.Activate();
        }

        /// <summary>
        /// Die Werkzeugfenster der ersten Ausbaustufe: File Manager und Gallery
        /// konsumieren die gemeinsame Sitzung; die drei übrigen Module melden
        /// ihren Stand, bis ihr eigener Plan sie baut. Alle gehören dem Dashboard
        /// (Spec „Dashboard": Schließen des Dashboards schließt die Anwendung).
        /// </summary>
        private Form CreateTool(string key)
        {
            var form = BuildTool(key);
            form.Owner = this;
            WindowIcon.Apply(form);
            return form;
        }

        private Form BuildTool(string key)
        {
            switch (key)
            {
                case FilesKey:
                    var files = new FileManagerForm(_sessions, _settings);
                    files.ConnectionRequested += OnConnectionRequested;
                    return files;
                case MediaKey:
                    return new MediaGalleryForm(_sessions);
                case GoldHenKey:
                    return new GoldHenForm(_sessions);
                case HealthKey:
                    return new Ps4HealthForm(_sessions, _settings);
                case Frame4Key:
                    return new Frame4Form(_settings);
                case PrxKey:
                    return new PrxManagerForm(_settings, _sessions);
                case BackupKey:
                    return new BackupTimelineForm(_sessions);
                case PackageKey:
                    return new PackageManagerForm(_sessions);
                case LumaKey:
                    return new LumaManagerForm(_settings, SaveSettings);
                case ModsKey:
                    return new ModLibraryForm(_sessions,_settings,SaveSettings,()=>OpenTool(GoldHenKey),ShowTransfers,()=>_tools.OpenForms.Any(f=>f is LumaManagerForm),_notificationCoordinator);
                case GamesKey:
                    return new GamesForm(_sessions, _settings, _notifications, _trainerTransport);
                case UpdatesKey:
                    return new UpdateManagerForm(_settings, SaveSettings);
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), "Unknown tool “" + key + "”.");
            }
        }

        private static string TitleOf(string key)
        {
            return Tools.First(t => string.Equals(t.Key, key, StringComparison.Ordinal)).Title;
        }

        private void ShowTransfers()
        {
            var files = _tools.Open(FilesKey) as FileManagerForm;
            if (files == null) return;
            if (!files.Visible) files.Show(this);
            files.Activate();
            files.ShowTransfers();
        }

        // ---- Übertragungen -------------------------------------------------------------

        /// <summary>Die Warteschlange gehört der Sitzung — nach jedem Wechsel neu abonnieren.</summary>
        private void AttachQueue(ManagerSession session)
        {
            var queue = session == null ? null : session.Queue;
            if (ReferenceEquals(_queue, queue)) return;
            if (_queue != null) _queue.JobChanged -= OnQueueChanged;
            _queue = queue;
            _notificationCoordinator.Attach(queue);
            if (_queue != null) _queue.JobChanged += OnQueueChanged;
            UpdateTransferSummary();
        }

        private void OnQueueChanged(object sender, TransferJob job)
        {
            var queue = (TransferQueue)sender;
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed || _closing || !ReferenceEquals(_queue, queue)) return;
                    UpdateTransferSummary();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void UpdateTransferSummary()
        {
            var session = _sessions.Current;
            if (session == null)
            {
                _transferSummary.Text = "No connection.";
                _showTransfers.Enabled = false;
                _transfersItem.Enabled = false;
                _ribbon.Invalidate();
                return;
            }

            var jobs = session.Queue.Jobs;
            int open = jobs.Count(j => !j.IsFinished);
            var text = TransferText.Summary(jobs);
            if (open > 0) text += "  ·  " + open + (open == 1 ? " job in the queue" : " jobs in the queue");
            _transferSummary.Text = text;
            _showTransfers.Enabled = true;
            _transfersItem.Enabled = true;
            _ribbon.Invalidate();
        }

        // ---- Beenden -------------------------------------------------------------------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closing)
            {
                base.OnFormClosing(e);
                return;
            }

            if (!e.Cancel && _tools.OpenForms.OfType<GamesForm>().Any(g => !g.ApproveClosing())) e.Cancel = true;
            if (!e.Cancel && !ConfirmLeave("Exit"))
            {
                e.Cancel = true;
            }

            if (!e.Cancel)
            {
                foreach (var games in _tools.OpenForms.OfType<GamesForm>()) games.CommitCloseApproval();
                // FormClosing ist synchron: erst asynchron aufräumen (Aufträge
                // abbrechen, Werkzeugfenster schließen), dann erneut schließen.
                e.Cancel = true;
                _closing = true;
                BeginInvoke((Action)(() => { var _ = ShutdownAsync(); }));
            }
            base.OnFormClosing(e);
        }

        /// <summary>Eine Warnung für alles, was das Schließen kostet (Spec „Sicherheit").</summary>
        private bool ConfirmLeave(string action)
        {
            var warnings = CollectLeaveWarnings();
            return warnings.Count == 0
                || MessageDialog.Ask(this, action, string.Join("\n", warnings) + "\n\nContinue anyway?", "Continue", "Cancel");
        }

        private IReadOnlyList<string> CollectLeaveWarnings()
        {
            var warnings = new List<string>();

            // Übertragungen und offene Änderungen hängen an der Sitzung, nicht am
            // Fenster: Der File Manager kann längst geschlossen sein. Wortlaut
            // wie in MainForm.Editing, damit die Sammelwarnung sie nicht doppelt
            // nennt (die Werkzeuge melden ihr Eigenes über ILeaveWarningSource).
            var session = _sessions.Current;
            if (session != null)
            {
                int open = session.Queue.Jobs.Count(j => !j.IsFinished);
                if (open == 1) warnings.Add("1 transfer is still active and will be cancelled.");
                else if (open > 1) warnings.Add(open + " transfers are still active and will be cancelled.");

                int pending = session.Explorer.Editing.PendingSessions.Count;
                if (pending == 1) warnings.Add("1 changed file has not been written back to the PS4 yet.");
                else if (pending > 1) warnings.Add(pending + " changed files have not been written back to the PS4 yet.");
            }

            foreach (var form in _tools.OpenForms)
            {
                var source = form as ILeaveWarningSource;
                if (source == null) continue;
                foreach (var warning in source.CollectLeaveWarnings())
                {
                    if (!warnings.Contains(warning)) warnings.Add(warning);
                }
            }
            return warnings;
        }

        private async Task ShutdownAsync()
        {
            _lifetime.Cancel();                      // laufende Verbindungsversuche verwerfen
            try
            {
                await EndSessionAsync();
            }
            catch (Exception ex)
            {
                _lastError = Ui.Describe(ex);
            }
            _tools.CloseAll();
            SaveSettings();
            Close();
        }

        private void SaveSettings()
        {
            _settings.Theme = AppTheme.IsLight ? "Light" : "Dark";
            try
            {
                ManagerSettingsStore.Save(AppPaths.Settings, _settings);
            }
            catch (Exception ex)
            {
                _lastError = "Settings could not be saved: " + Ui.Describe(ex);
                UpdateConnectionState();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppTheme.Changed -= OnDashboardThemeChanged;
                _notificationCoordinator?.Dispose();
                _notifications?.Dispose();
                if (_queue != null) _queue.JobChanged -= OnQueueChanged;
                _sessions.SessionChanged -= OnSessionChanged;
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }

        private int Px(int logical)
        {
            return DpiScale.Scale(logical, DeviceDpi);
        }

        private static RibbonItem Small(string text)
        {
            return new RibbonItem(text) { Size = RibbonItemSize.Small };
        }

        private sealed class ToolDefinition
        {
            public ToolDefinition(string key, string title, bool needsConnection)
            {
                Key = key;
                Title = title;
                NeedsConnection = needsConnection;
            }

            public string Key { get; }
            public string Title { get; }
            public bool NeedsConnection { get; }
        }
    }
}
