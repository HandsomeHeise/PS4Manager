using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using FileManager.App.Dialogs;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Session;

namespace PS4Manager.App
{
    /// <summary>
    /// Der GoldHEN-Manager (Spec „GoldHEN Manager"): Er zeigt, was auf der
    /// Konsole liegt, schaltet Plugins über die gefundene Konfiguration und
    /// verwaltet Plugin-, Cheat- und Patch-Dateien. Geschrieben wird
    /// ausschließlich über die gemeinsame Warteschlange, und jeder Änderung
    /// geht eine lokale Sicherung voraus — scheitert sie, unterbleibt sie.
    ///
    /// Der Aktivierungszustand wird NICHT geraten: Eine Plugin-Zeile gilt nur
    /// dann als erkannt, wenn sie auf eine vorhandene Plugin-Datei verweist;
    /// ohne erkennbare Konfiguration steht „unbekannt" da und der Schalter ist
    /// gesperrt (dann bleibt der Editor).
    /// </summary>
    internal sealed class GoldHenForm : AppWindow
    {
        private readonly ManagerSessionController _sessions;
        private readonly ToolUiDispatcher _ui;
        private readonly ToolSessionBinding _binding;

        private readonly UiPanel _toolbar = new UiPanel { Dock = DockStyle.Top };
        private readonly UiLabel _status = new UiLabel
            { Text = "Nicht verbunden.", AutoSize = false, Dock = DockStyle.Fill };
        private readonly UiButton _refresh = new UiButton
            { Text = "Aktualisieren", Enabled = false, AutoSize = true, Dock = DockStyle.Left };
        private readonly UiButton _upload = new UiButton
            { Text = "Hochladen …", Enabled = false, AutoSize = true, Dock = DockStyle.Right };
        private readonly UiButton _replace = new UiButton
            { Text = "Ersetzen …", Enabled = false, AutoSize = true, Dock = DockStyle.Right };
        private readonly UiButton _download = new UiButton
            { Text = "Herunterladen …", Enabled = false, AutoSize = true, Dock = DockStyle.Right };
        private readonly UiButton _delete = new UiButton
            { Text = "Löschen", Enabled = false, AutoSize = true, Dock = DockStyle.Right };
        private readonly UiButton _edit = new UiButton
            { Text = "Im Editor öffnen", Enabled = false, AutoSize = true, Dock = DockStyle.Right };
        private readonly UiButton _toggle = new UiButton
            { Text = "Aktivieren", Enabled = false, AutoSize = true, Dock = DockStyle.Right };

        private readonly UiSplit _split = new UiSplit
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 160,
            Panel2MinSize = 320
        };

        private readonly AppGrid _sections = new AppGrid { Dock = DockStyle.Fill };
        private readonly AppGrid _pluginsGrid = new AppGrid { Dock = DockStyle.Fill };
        private readonly AppGrid _filesGrid = new AppGrid { Dock = DockStyle.Fill };
        private readonly AppGrid _configGrid = new AppGrid { Dock = DockStyle.Fill };

        private readonly List<SectionRow> _sectionRows = new List<SectionRow>();
        private readonly List<ContentRow> _contentRows = new List<ContentRow>();
        private readonly ListDataSource<SectionRow> _sectionSource;
        private readonly ListDataSource<ContentRow> _pluginsSource;
        private readonly ListDataSource<ContentRow> _filesSource;
        private readonly ListDataSource<ContentRow> _configSource;
        private readonly GoldHenRequestGate _requests = new GoldHenRequestGate();

        private ManagerSession _session;
        private GoldHenDiscovery _discovery;
        private GoldHenSection _section = GoldHenSection.Plugins;
        private TransferQueue _queue;
        private ManagerSession _busySession;

        public GoldHenForm(ManagerSessionController sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());

            _sectionSource = new ListDataSource<SectionRow>(_sectionRows);
            _sectionSource.Map("Section", r => r.Title);
            _sectionSource.Map("State", r => r.State);
            _sectionSource.Map("Row", r => r);

            _pluginsSource = Source("Name", "Datei", "Zustand", "Pfad");
            _filesSource = Source("Spiel", "Titel-ID", "Datei", "Größe", "Geändert");
            _configSource = Source("Datei", "Größe", "Geändert");

            InitializeUi();
            _binding = new ToolSessionBinding(_sessions, AttachSession, DetachSession);
        }

        private ListDataSource<ContentRow> Source(params string[] columns)
        {
            var source = new ListDataSource<ContentRow>(_contentRows);
            foreach (var column in columns) source.Map(column, r => r.Cells[Array.IndexOf(columns, column)]);
            source.Map("Row", r => r);
            return source;
        }

        // ---- Aufbau --------------------------------------------------------------------

        private void InitializeUi()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "GoldHEN Manager";
            ClientSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;

            _status.TextAlignment = ContentAlignment.MiddleLeft;

            _sections.Columns.Add(new GridColumn("Section", "Abschnitt") { Width = 150, ReadOnly = true });
            _sections.Columns.Add(new GridColumn("State", "Zustand") { Width = 130, ReadOnly = true });
            _sections.DataSource = _sectionSource;

            _pluginsGrid.Columns.Add(new GridColumn("Name", "Plugin") { Width = 200, ReadOnly = true });
            _pluginsGrid.Columns.Add(new GridColumn("Datei", "Datei") { Width = 70, ReadOnly = true });
            _pluginsGrid.Columns.Add(new GridColumn("Zustand", "Zustand") { Width = 100, ReadOnly = true });
            _pluginsGrid.Columns.Add(new GridColumn("Pfad", "Pfad") { Width = 340, ReadOnly = true });
            _pluginsGrid.DataSource = _pluginsSource;

            _filesGrid.Columns.Add(new GridColumn("Spiel", "Spiel") { Width = 220, ReadOnly = true });
            _filesGrid.Columns.Add(new GridColumn("Titel-ID", "Titel-ID") { Width = 100, ReadOnly = true });
            _filesGrid.Columns.Add(new GridColumn("Datei", "Datei") { Width = 200, ReadOnly = true });
            _filesGrid.Columns.Add(new GridColumn("Größe", "Größe") { Width = 90, ReadOnly = true });
            _filesGrid.Columns.Add(new GridColumn("Geändert", "Geändert") { Width = 130, ReadOnly = true });
            _filesGrid.DataSource = _filesSource;

            _configGrid.Columns.Add(new GridColumn("Datei", "Datei") { Width = 220, ReadOnly = true });
            _configGrid.Columns.Add(new GridColumn("Größe", "Größe") { Width = 90, ReadOnly = true });
            _configGrid.Columns.Add(new GridColumn("Geändert", "Geändert") { Width = 150, ReadOnly = true });
            _configGrid.DataSource = _configSource;

            // Zuletzt Hinzugefügtes dockt außen: Text füllt, Knöpfe rechts, Umschalter links.
            _toolbar.Controls.Add(_status);
            _toolbar.Controls.Add(_toggle);
            _toolbar.Controls.Add(_edit);
            _toolbar.Controls.Add(_delete);
            _toolbar.Controls.Add(_download);
            _toolbar.Controls.Add(_replace);
            _toolbar.Controls.Add(_upload);
            _toolbar.Controls.Add(_refresh);

            _split.Panel1.Controls.Add(_sections);
            _split.Panel2.Controls.Add(_filesGrid);
            _split.Panel2.Controls.Add(_configGrid);
            _split.Panel2.Controls.Add(_pluginsGrid);

            Controls.Add(_split);
            Controls.Add(_toolbar);

            _refresh.Click += async (s, e) => await RefreshAsync();
            _upload.Click += async (s, e) => await UploadAsync();
            _replace.Click += async (s, e) => await ReplaceAsync();
            _download.Click += async (s, e) => await DownloadAsync();
            _delete.Click += async (s, e) => await DeleteAsync();
            _edit.Click += async (s, e) => await EditAsync();
            _toggle.Click += async (s, e) => await ToggleAsync();
            _sections.Selection.Changed += (s, e) => OnSectionChanged();
            _pluginsGrid.Selection.Changed += (s, e) => UpdateButtons();
            _filesGrid.Selection.Changed += (s, e) => UpdateButtons();
            _configGrid.Selection.Changed += (s, e) => UpdateButtons();

            ShowContentGrid();
            UpdateButtons();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ClientSize = new Size(Px(1000), Px(640));
            CenterToScreen();

            // Höhe am Inhalt messen (Muster: Statusleiste des File Managers —
            // der Skin gibt einem Panel 8 px Padding und 1 px Rahmen je Seite).
            int chrome = _toolbar.Height - _toolbar.DisplayRectangle.Height;
            _toolbar.Height = _refresh.GetPreferredSize(Size.Empty).Height + chrome;
            _split.SplitterDistance = Px(240);
        }

        private int Px(int logical)
        {
            return DpiScale.Scale(logical, DeviceDpi);
        }

        // ---- Sitzung -------------------------------------------------------------------

        private void AttachSession(ManagerSession session)
        {
            try
            {
                _ui.Dispatch(() => { if (!IsDisposed) AttachSessionOnUi(session); });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void DetachSession(ManagerSession session)
        {
            try
            {
                _ui.Dispatch(() => { if (!IsDisposed) DetachSessionOnUi(session); });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void AttachSessionOnUi(ManagerSession session)
        {
            if (session.CancellationToken.IsCancellationRequested) return;
            _requests.BeginRefresh();
            _session = session;
            if (_queue != null) _queue.JobChanged -= OnQueueChanged;
            _queue = session.Queue;
            _queue.JobChanged += OnQueueChanged;
            _refresh.Enabled = true;
            Text = "GoldHEN Manager — " + session.Endpoint.Host;
            SetStatus("Wird gesucht …");
            var _ = RefreshAsync();
        }

        private void DetachSessionOnUi(ManagerSession session)
        {
            if (!ReferenceEquals(_session, session)) return;
            _requests.BeginRefresh();
            if (_queue != null) _queue.JobChanged -= OnQueueChanged;
            _queue = null;
            _session = null;
            if (ReferenceEquals(_busySession, session)) _busySession = null;
            _discovery = null;
            _sectionRows.Clear();
            _contentRows.Clear();
            _sections.DataSource = null;
            _sections.DataSource = _sectionSource;
            _refresh.Enabled = false;
            Text = "GoldHEN Manager";
            SetStatus("Nicht verbunden.");
            ShowContentGrid();
            UpdateButtons();
        }

        private bool IsCurrent(ManagerSession session, CancellationToken token)
        {
            return !token.IsCancellationRequested && ReferenceEquals(session, _session);
        }

        // ---- Erkennung -----------------------------------------------------------------

        private async Task RefreshAsync()
        {
            var session = _session;
            if (session == null) return;
            var token = session.CancellationToken;
            var explorer = session.Explorer;
            var request = _requests.BeginRefresh();
            _busySession = session;
            UpdateButtons();
            try
            {
                var discovery = await new GoldHenDiscoveryService(explorer.Cache, explorer.Ftp, explorer.Gates)
                    .DiscoverAsync(token).ConfigureAwait(true);
                if (!IsCurrent(session, token) || !_requests.IsCurrentRefresh(request)) return;
                _discovery = discovery;
                FillSections();
                var section = _section;
                var contentRequest = _requests.BeginContent(section);
                await FillContentAsync(session, token, discovery, section, contentRequest).ConfigureAwait(true);
                if (!IsCurrent(session, token) || !_requests.IsCurrentRefresh(request)) return;
                SetStatus(discovery.RootExists
                    ? "Gefunden: " + Describe(discovery)
                    : "Kein GoldHEN-Verzeichnis auf dieser Konsole gefunden.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
            finally
            {
                if (_requests.IsCurrentRefresh(request))
                {
                    if (ReferenceEquals(_busySession, session)) _busySession = null;
                    if (!IsDisposed) UpdateButtons();
                }
            }
        }

        /// <summary>Kurzfassung für die Statuszeile — was da ist, in einem Satz.</summary>
        private static string Describe(GoldHenDiscovery discovery)
        {
            var parts = new List<string>();
            foreach (var location in discovery.Locations)
            {
                if (location.Section == GoldHenSection.Configuration) continue;
                parts.Add(SectionName(location.Section) + (location.Exists ? " " + location.Entries.Count : " —"));
            }
            parts.Add("Konfiguration " + (discovery.ConfigurationPath == null ? "—" : RemotePath.Name(discovery.ConfigurationPath)));
            return string.Join(" · ", parts);
        }

        private static string SectionName(GoldHenSection section)
        {
            switch (section)
            {
                case GoldHenSection.Plugins: return "Plugins";
                case GoldHenSection.Cheats: return "Cheats";
                case GoldHenSection.Patches: return "Patches";
                default: return "Konfiguration";
            }
        }

        private void FillSections()
        {
            _sectionRows.Clear();
            foreach (var location in _discovery.Locations)
            {
                _sectionRows.Add(new SectionRow(location.Section, SectionName(location.Section),
                    location.Section == GoldHenSection.Configuration
                        ? (location.Exists ? _discovery.ConfigurationFiles.Count + " Datei(en)" : "nicht vorhanden")
                        : (location.Exists ? location.Entries.Count + " Datei(en)" : "nicht vorhanden")));
            }
            // Das Raster neu binden: sonst bleiben Bildlauf und Auswahl am alten Bestand
            // Die native Tabelle erhält eine neue Bindung für den neuen Inhalt.
            _sections.DataSource = null;
            _sections.DataSource = _sectionSource;
            if (_sectionRows.Count > 0 && _sections.Selection.CurrentRow < 0) _sections.Selection.Select(0);
        }

        private void OnSectionChanged()
        {
            var row = _sections.Selection.CurrentRow;
            if (row < 0 || row >= _sectionRows.Count) return;
            _section = _sectionRows[row].Section;
            ShowContentGrid();
            var session = _session;
            var discovery = _discovery;
            if (session != null && discovery != null)
            {
                var section = _section;
                var request = _requests.BeginContent(section);
                _ = FillContentAsync(session, session.CancellationToken, discovery, section, request);
            }
        }

        private void ShowContentGrid()
        {
            _pluginsGrid.Visible = _section == GoldHenSection.Plugins;
            _filesGrid.Visible = _section == GoldHenSection.Cheats || _section == GoldHenSection.Patches;
            _configGrid.Visible = _section == GoldHenSection.Configuration;
        }

        private async Task FillContentAsync(ManagerSession session, CancellationToken token,
            GoldHenDiscovery discovery, GoldHenSection section, int request)
        {
            var rows = new List<ContentRow>();

            if (section == GoldHenSection.Plugins)
            {
                foreach (var plugin in discovery.Plugins)
                {
                    rows.Add(new ContentRow(
                        new[] { plugin.Name, plugin.FilePresent ? "ja" : "fehlt", Activation(plugin.Activation), plugin.Path },
                        FileOf(discovery, plugin.Path), plugin));
                }
            }
            else if (section == GoldHenSection.Cheats || section == GoldHenSection.Patches)
            {
                var location = discovery.Location(section);
                var groups = await GoldHenInventory.GroupAsync(location.Entries, Resolver(session), token)
                    .ConfigureAwait(true);
                if (!IsCurrent(session, token) || !_requests.IsCurrentContent(request, section)) return;
                foreach (var group in groups)
                {
                    foreach (var file in group.Files)
                    {
                        rows.Add(new ContentRow(new[]
                        {
                            group.Label,
                            group.TitleId ?? "",
                            RemotePath.Name(file.Path),
                            SizeFormat.Bytes(file.Size),
                            FileTime(file.Modified)
                        }, file, null));
                    }
                }
            }
            else
            {
                var location = discovery.Location(GoldHenSection.Configuration);
                foreach (var row in GoldHenConfigurationRows.Build(location.Entries,
                    discovery.ConfigurationPath, discovery.Plugins))
                {
                    rows.Add(new ContentRow(row.Cells, row.Item, null));
                }
            }

            if (!IsCurrent(session, token) || !_requests.IsCurrentContent(request, section)) return;
            _contentRows.Clear();
            _contentRows.AddRange(rows);
            if (section == GoldHenSection.Plugins) Rebind(_pluginsGrid, _pluginsSource);
            else if (section == GoldHenSection.Configuration) Rebind(_configGrid, _configSource);
            else Rebind(_filesGrid, _filesSource);
            UpdateButtons();
        }

        private static RemoteItem FileOf(GoldHenDiscovery discovery, string path)
        {
            return discovery.Location(GoldHenSection.Plugins).Entries
                .FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        private static string Activation(GoldHenActivation activation)
        {
            switch (activation)
            {
                case GoldHenActivation.Enabled: return "aktiv";
                case GoldHenActivation.Disabled: return "inaktiv";
                default: return "unbekannt";
            }
        }

        private static string FileTime(DateTime modified)
        {
            return modified == DateTime.MinValue ? "" : modified.ToString("g");
        }

        /// <summary>Spielnamen über den vorhandenen Titel-Katalog; ohne Namen bleibt die ID.</summary>
        private static Func<string, CancellationToken, Task<string>> Resolver(ManagerSession session)
        {
            return async (titleId, ct) =>
            {
                var info = await session.Explorer.Titles.GetAsync(titleId, ct).ConfigureAwait(false);
                return info != null && info.HasRealName ? info.Name : null;
            };
        }

        private static void Rebind(AppGrid grid, ListDataSource<ContentRow> source)
        {
            grid.DataSource = null;
            grid.DataSource = source;
        }

        // ---- Auswahl und Knöpfe --------------------------------------------------------

        private AppGrid ActiveGrid()
        {
            if (_section == GoldHenSection.Plugins) return _pluginsGrid;
            return _section == GoldHenSection.Configuration ? _configGrid : _filesGrid;
        }

        private IReadOnlyList<ContentRow> Selected()
        {
            var grid = ActiveGrid();
            return grid.Selection.SelectedRows
                .Where(i => i >= 0 && i < _contentRows.Count)
                .Select(i => _contentRows[i])
                .ToList();
        }

        private void UpdateButtons()
        {
            var selected = Selected();
            var connected = _session != null && !ReferenceEquals(_busySession, _session);
            var items = selected.Where(r => r.Item != null).ToList();
            var plugin = selected.Count == 1 ? selected[0].Plugin : null;

            _refresh.Enabled = connected || _session != null;
            _upload.Enabled = connected && _discovery != null;
            _download.Enabled = connected && items.Count > 0;
            _delete.Enabled = connected && items.Count > 0;
            _replace.Enabled = connected && items.Count == 1;
            _edit.Enabled = connected && selected.Count == 1 && CanEdit(selected[0]);
            _toggle.Enabled = connected && plugin != null && _discovery.ConfigurationPath != null
                              && plugin.Activation != GoldHenActivation.Unknown;
            _toggle.Text = plugin != null && plugin.Activation == GoldHenActivation.Enabled
                ? "Deaktivieren" : "Aktivieren";
        }

        /// <summary>Der Editor öffnet die gefundene Konfiguration immer, sonst nur, was die Richtlinie kennt.</summary>
        private bool CanEdit(ContentRow row)
        {
            if (row.Path == null) return false;
            if (_section == GoldHenSection.Configuration) return true;
            return IniFilePolicy.IsEditable(row.Path);
        }

        private string SectionFolder()
        {
            switch (_section)
            {
                case GoldHenSection.Plugins: return GoldHenDiscoveryService.PluginsPath;
                case GoldHenSection.Cheats: return GoldHenDiscoveryService.CheatsPath;
                case GoldHenSection.Patches: return GoldHenDiscoveryService.PatchesPath;
                default: return GoldHenDiscoveryService.Root;
            }
        }

        // ---- Änderungen (immer: Sicherung zuerst) ---------------------------------------

        private async Task ToggleAsync()
        {
            var session = _session;
            var row = Selected().FirstOrDefault(r => r.Plugin != null);
            if (session == null || row == null || _discovery == null) return;
            var token = session.CancellationToken;
            var configuration = _discovery.ConfigurationPath;
            var enabled = row.Plugin.Activation != GoldHenActivation.Enabled;
            try
            {
                string targetTitleId = row.Plugin.TitleId;
                if (enabled && !ChoosePluginTarget(row.Plugin, out targetTitleId)) return;

                var bytes = await FtpGates.RunAsync(session.Explorer.Gates.Browse,
                    () => session.Explorer.Ftp.DownloadAsync(configuration, token), token).ConfigureAwait(true);
                if (!IsCurrent(session, token)) return;
                var document = TextFileContent.FromBytes(bytes);
                var text = IniPluginEditor.SetEnabled(document.Text, row.Plugin, enabled, targetTitleId);
                if (text == document.Text)
                {
                    SetStatus(row.Plugin.Name + " ist bereits " + (enabled ? "aktiv." : "inaktiv."));
                    return;
                }
                var item = _discovery.Location(GoldHenSection.Configuration).Entries
                    .First(e => string.Equals(e.Path, configuration, StringComparison.Ordinal));
                var local = session.Explorer.Editing.PrepareLocalPath(configuration);
                document.Save(local, document.Text);
                var edit = await session.Explorer.Editing.RegisterAsync(configuration, local,
                    bytes.LongLength, item.Modified, token).ConfigureAwait(true);
                if (!IsCurrent(session, token)) return;
                if (await WriteTextAsync(session, edit, document, text,
                    enabled ? "toggle-plugin-on" : "toggle-plugin-off", token).ConfigureAwait(true))
                    await RefreshAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        private bool ChoosePluginTarget(PluginEntry plugin, out string titleId)
        {
            titleId = null;
            var targets = GoldHenPluginTargets.FromDiscovery(_discovery);
            using (var dialog = new GoldHenPluginTargetDialog(targets, plugin.TitleId))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                titleId = dialog.SelectedTitleId;
                return true;
            }
        }

        private async Task EditAsync()
        {
            var session = _session;
            var rows = Selected();
            if (session == null || rows.Count != 1 || rows[0].Item == null) return;
            var item = rows[0].Item;
            var token = session.CancellationToken;
            try
            {
                var local = session.Explorer.Editing.PrepareLocalPath(item.Path);
                var download = session.Explorer.Operations.PlanOpenForEdit(item, local);
                session.Explorer.Queue.Enqueue(download);
                SetStatus(RemotePath.Name(item.Path) + " — wird geladen …");
                await session.Explorer.Queue.WhenIdle().ConfigureAwait(true);
                if (!IsCurrent(session, token)) return;
                if (download.State != JobState.Completed)
                {
                    SetStatus(download.Title + ": " + download.Summary);
                    return;
                }

                var edit = await session.Explorer.Editing.RegisterAsync(item.Path, local, item.Size, item.Modified, token)
                    .ConfigureAwait(true);
                if (!IsCurrent(session, token)) return;
                var document = TextFileContent.Load(local);
                using (var dialog = new IniEditorDialog(RemotePath.Name(item.Path), document.Text,
                    value => SaveEditorAsync(session, edit, document, value, token)))
                {
                    dialog.ShowDialog(this);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        private async Task<bool> SaveEditorAsync(ManagerSession session, EditSession edit,
            TextFileContent document, string text, CancellationToken token)
        {
            if (!IsCurrent(session, token)) return false;
            return await WriteTextAsync(session, edit, document, text, "edit-configuration", token)
                .ConfigureAwait(true);
        }

        /// <summary>
        /// Sichert und schreibt zurück. Die Sicherung läuft ZUERST: schlägt sie
        /// fehl, unterbleibt die Änderung (Spec „GoldHEN Manager").
        /// </summary>
        private async Task<bool> WriteTextAsync(ManagerSession session, EditSession edit,
            TextFileContent document, string text, string operation, CancellationToken token)
        {
            if (!IsCurrent(session, token)) return false;
            var snapshot = await BackupCurrentAsync(session, edit.RemotePath, operation, token).ConfigureAwait(true);
            if (snapshot == null || !IsCurrent(session, token)) return false;

            if (session.Explorer.Editing.RemoteSnapshotChanged(edit, snapshot.Content))
            {
                if (!MessageDialog.Ask(this, "Zurückschreiben",
                        "„" + RemotePath.Name(edit.RemotePath) + "“ hat sich auf der PS4 inzwischen geändert.\nTrotzdem mit der PC-Fassung überschreiben?",
                        "Überschreiben", "Abbrechen"))
                    return false;
            }
            session.Explorer.Editing.AcceptRemoteSnapshot(edit, snapshot.Content);
            document.Save(edit.LocalPath, text);
            session.Explorer.Editing.MarkLocalSavePending(edit);

            var job = await session.Explorer.Operations.PlanWriteBackAsync(edit.LocalPath, edit.RemotePath, token).ConfigureAwait(true);
            if (!IsCurrent(session, token) || job == null) return false;
            session.Explorer.Editing.ProtectWriteBack(job, edit);
            var completion = session.Explorer.Editing.TrackWriteBackAsync(job, edit);
            session.Explorer.Queue.Enqueue(job);
            var state = await completion.ConfigureAwait(true);
            if (!IsCurrent(session, token)) return false;
            SetStatus(state == JobState.Completed
                ? RemotePath.Name(edit.RemotePath) + " wurde auf die PS4 geschrieben."
                : job.Title + ": " + job.Summary);
            return state == JobState.Completed;
        }

        private GoldHenMutationSafety Safety(ManagerSession session)
        {
            return new GoldHenMutationSafety(session.Explorer.Ftp, session.Explorer.Gates,
                new GoldHenBackupWriter(AppPaths.GoldHenBackupRoot, SystemClock.Instance));
        }

        /// <summary>Sichert den aktuellen Remote-Stand; null bedeutet: Mutation abbrechen.</summary>
        private async Task<GoldHenRemoteSnapshot> BackupCurrentAsync(ManagerSession session,
            string remotePath, string operation, CancellationToken token)
        {
            try
            {
                var snapshot = await Safety(session).BackupCurrentAsync(remotePath, operation, token)
                    .ConfigureAwait(true);
                if (IsCurrent(session, token)) SetStatus("Sicherung: " + snapshot.Backup.Directory);
                return snapshot;
            }
            catch (Exception ex)
            {
                if (IsCurrent(session, token))
                    SetStatus("Sicherung fehlgeschlagen, Änderung unterblieben: " + Ui.Describe(ex));
                return null;
            }
        }

        private async Task UploadAsync()
        {
            var session = _session;
            if (session == null || _discovery == null) return;
            var token = session.CancellationToken;
            string[] files;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Zum GoldHEN-Ordner hochladen" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                files = dialog.FileNames;
            }
            try
            {
                var job = await session.Explorer.Operations.PlanUploadAsync(files, SectionFolder(), token).ConfigureAwait(true);
                if (job == null || !IsCurrent(session, token)) return;
                try
                {
                    await Safety(session).ProtectUploadAsync(job, "upload-replace", token).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    if (IsCurrent(session, token))
                        SetStatus("Sicherung fehlgeschlagen, Änderung unterblieben: " + Ui.Describe(ex));
                    return;
                }
                if (!IsCurrent(session, token)) return;
                session.Explorer.Queue.Enqueue(job);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        private async Task ReplaceAsync()
        {
            var session = _session;
            var rows = Selected().Where(r => r.Item != null).ToList();
            if (session == null || rows.Count != 1) return;
            var item = rows[0].Item;
            var token = session.CancellationToken;
            try
            {
                string chosen;
                using (var dialog = new OpenFileDialog { Multiselect = false, Title = "Diese Datei ersetzt " + RemotePath.Name(item.Path) })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    chosen = dialog.FileName;
                }

                var snapshot = await BackupCurrentAsync(session, item.Path, "replace", token).ConfigureAwait(true);
                if (snapshot == null || !IsCurrent(session, token)) return;

                // Unter dem ZIELnamen in die Arbeitskopie legen: der Upload trägt
                // diesen Namen, überschreibt also genau den ausgewählten Eintrag.
                var staged = session.Explorer.Editing.PrepareLocalPath(item.Path);
                File.Copy(chosen, staged, true);
                var job = await session.Explorer.Operations.PlanUploadAsync(new[] { staged },
                    RemotePath.Parent(item.Path), token).ConfigureAwait(true);
                if (job == null || !IsCurrent(session, token)) return;
                snapshot.Protect(job);
                session.Explorer.Queue.Enqueue(job);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        private async Task DownloadAsync()
        {
            var session = _session;
            var items = Selected().Where(r => r.Item != null).Select(r => r.Item).ToList();
            if (session == null || items.Count == 0) return;
            var token = session.CancellationToken;
            string target;
            using (var dialog = new FolderBrowserDialog { Description = "Herunterladen nach …" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                target = dialog.SelectedPath;
            }
            try
            {
                var job = await session.Explorer.Operations.PlanDownloadAsync(items, target, token).ConfigureAwait(true);
                if (job == null || !IsCurrent(session, token)) return;
                session.Explorer.Queue.Enqueue(job);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        private async Task DeleteAsync()
        {
            var session = _session;
            var items = Selected().Where(r => r.Item != null).Select(r => r.Item).ToList();
            if (session == null || items.Count == 0) return;
            var token = session.CancellationToken;
            try
            {
                var job = await session.Explorer.Operations.PlanDeleteAsync(items, token).ConfigureAwait(true);
                if (job == null || !IsCurrent(session, token)) return;

                var snapshots = new List<GoldHenRemoteSnapshot>();
                foreach (var item in items)
                {
                    var snapshot = await BackupCurrentAsync(session, item.Path, "delete", token).ConfigureAwait(true);
                    if (snapshot == null || !IsCurrent(session, token)) return;
                    snapshots.Add(snapshot);
                }
                foreach (var snapshot in snapshots) snapshot.Protect(job);
                session.Explorer.Queue.Enqueue(job);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrent(session, token)) SetStatus("Fehler: " + Ui.Describe(ex));
            }
        }

        // ---- Übertragungen und Status --------------------------------------------------

        private void OnQueueChanged(object sender, TransferJob job)
        {
            var queue = (TransferQueue)sender;
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed || !ReferenceEquals(_queue, queue)) return;
                    SetStatus(TransferText.Summary(queue.Jobs));
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_binding != null) _binding.Dispose();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _binding != null) _binding.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Eine Zeile der Abschnittsliste links.</summary>
        private sealed class SectionRow
        {
            public SectionRow(GoldHenSection section, string title, string state)
            {
                Section = section;
                Title = title;
                State = state;
            }

            public GoldHenSection Section { get; }
            public string Title { get; }
            public string State { get; }
        }

        /// <summary>Eine Zeile im Inhaltsraster: Anzeigetexte plus, was Aktionen brauchen.</summary>
        private sealed class ContentRow
        {
            public ContentRow(string[] cells, RemoteItem item, PluginEntry plugin)
            {
                Cells = cells;
                Item = item;
                Plugin = plugin;
            }

            public string[] Cells { get; }
            public RemoteItem Item { get; }
            public PluginEntry Plugin { get; }
            public string Path { get { return Item != null ? Item.Path : Plugin != null ? Plugin.Path : null; } }
        }
    }
}
