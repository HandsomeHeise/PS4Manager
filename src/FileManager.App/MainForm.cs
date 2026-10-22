using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Shell;
using FileManager.App.Views;
using FileManager.Core.Config;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using PS4Manager.Core.Config;
using PS4Manager.Core.Presentation;
using PS4Manager.Core.Session;
using TreeNode = PS4Manager.UI.NavigationNode;

namespace FileManager.App
{
    /// <summary>
    /// Das Hauptfenster (Spec „Oberfläche"): Ribbon, Navigationszeile, links
    /// Baum, rechts Details oder Kacheln, unten Übertragungen und Status. Die
    /// Logik steckt im Kern (FileManager.Core.Presentation); die Teilklassen
    /// MainForm.*.cs binden je ein Thema an, eingehängt über die partiellen
    /// Wire*-Haken und die Ereignisse unten.
    /// </summary>
    public sealed partial class FileManagerForm : AppForm, ILeaveWarningSource
    {
        private const int TileImageLogical = 64;

        // ---- Zustand ---------------------------------------------------------------
        private FileManagerViewSettings _settings;
        private ManagerSettings _managerSettings;
        private ToolSessionBinding _sessionBinding;
        private readonly ToolUiDispatcher _uiDispatcher = new ToolUiDispatcher(
            SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        private volatile ManagerSession _requestedManagerSession;
        private ManagerSession _managerSession;
        private ExplorerSession _session;
        private string _currentPath;
        private CommandState _commands = CommandState.For(false, Zone.Locked, new FolderRow[0], p => Zone.Locked);
        private readonly List<FolderRow> _rows = new List<FolderRow>();
        private ListDataSource<FolderRow> _list;
        private FilteredSource _filtered;
        private SortedSource _sorted;
        private string _sortKey = "Name";
        private SortDirection _sortDirection = SortDirection.Ascending;
        private string _filterText = "";
        private readonly ShellFileInfo _shell = new ShellFileInfo();
        private readonly Dictionary<string, Image> _titleIcons = new Dictionary<string, Image>(StringComparer.Ordinal);

        // ---- Controls ----------------------------------------------------------------
        private readonly AppRibbon _ribbon = new AppRibbon { Dock = DockStyle.Top };
        private readonly NavigationStrip _nav = new NavigationStrip();
        private readonly UiSplit _split = new UiSplit
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 160,
            Panel2MinSize = 300
        };
        private readonly AppTree _tree = new AppTree { Dock = DockStyle.Fill };
        private readonly TreeNode _quickRoot = new TreeNode("Quick access");
        private readonly TreeNode _consoleRoot = new TreeNode("PS4");
        private readonly AppGrid _grid = new AppGrid { Dock = DockStyle.Fill };
        private readonly AppGallery _gallery = new AppGallery { Dock = DockStyle.Fill, Visible = false };
        private readonly UiPanel _statusBar = new UiPanel { Dock = DockStyle.Bottom };
        private readonly UiLabel _status = new UiLabel { Dock = DockStyle.Fill, AutoSize = false, Text = "Disconnected" };

        // ---- Ribbon (Spec: „Start" und „Ansicht"; Plan-Entscheidungen 2 und 3) ------
        private readonly RibbonItem _connectItem = Small("Connect");
        private readonly RibbonItem _targetItem = Small("Select console …");
        private readonly RibbonItem _uploadItem = Small("Upload …");
        private readonly RibbonItem _uploadFolderItem = Small("Upload folder …");
        private readonly RibbonItem _downloadItem = Small("Download …");
        private readonly RibbonItem _newFolderItem = Small("New folder");
        private readonly RibbonItem _renameItem = Small("Rename");
        private readonly RibbonItem _deleteItem = Small("Delete");
        private readonly RibbonItem _detailsItem = Toggle("Details");
        private readonly RibbonItem _tilesItem = Toggle("Tiles");
        private readonly RibbonItem _expertItem = Toggle("Expert Mode");
        private readonly RibbonItem _lightItem = Toggle("Light theme");

        // ---- Ereignisse für die Teilklassen -------------------------------------------
        // (SessionStarted, FolderShown und FileActivated deklariert Task 15 dort,
        // wo sie ausgelöst werden — ein nie ausgelöstes Ereignis wäre Warnung CS0067.)
        /// <summary>Vor dem Entsorgen einer Sitzung (Trennen, neu Verbinden, Beenden).</summary>
        private event Action<ExplorerSession> SessionEnding;
        /// <summary>
        /// Beim Schließen: jeder Teil trägt ein, was dagegen spräche. Das
        /// Dashboard holt das über <c>CollectLeaveWarnings</c>, bevor es die
        /// Anwendung beendet — dieses Fenster fragt selbst nichts mehr.
        /// </summary>
        private event Action<List<string>> CollectingCloseWarnings;

        partial void WireConnection();
        partial void WireBrowsing();
        partial void WireTitles();
        partial void AddTransferPanel();
        partial void WireTransfers();
        partial void WireOperations();
        partial void WireClipboard();
        partial void WireMenus();
        partial void WireEditing();

        public event EventHandler ConnectionRequested;

        public FileManagerForm(ManagerSessionController sessions, ManagerSettings settings)
        {
            if (sessions == null) throw new ArgumentNullException(nameof(sessions));
            _managerSettings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (_managerSettings.FileManager == null) _managerSettings.FileManager = new FileManagerViewSettings();
            InitializeUi(_managerSettings.FileManager);
            _sessionBinding = new ToolSessionBinding(sessions, AttachSession, DetachSession);
        }

        private void InitializeUi(FileManagerViewSettings settings)
        {
            Text = "PS4 File Manager";
            ClientSize = new Size(1200, 760);
            StartPosition = FormStartPosition.CenterScreen;

            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _list = new ListDataSource<FolderRow>(_rows);
            _list.Map("Name", r => r.NameCell);
            _list.Map("Modified", r => r.ModifiedCell);
            _list.Map("Type", r => r.TypeCell);
            _list.Map("Size", r => r.SizeCell);
            _list.Map("TitleId", r => r.TitleIdCell);
            _list.Map("Row", r => r);
            _list.Map("Rank", r => r.GroupRank);
            _list.MapImage("Name", r => IconFor(r, false));
            _filtered = new FilteredSource(_list, (source, i) => Matches((FolderRow)source.GetValue(i, "Row")));
            _sorted = new SortedSource(_filtered);

            BuildRibbon();
            BuildGrid();
            BuildGallery();
            BuildTree();

            _split.SplitterDistance = _settings.TreeWidth;
            _split.Panel1.Controls.Add(_tree);
            _split.Panel2.Controls.Add(_grid);
            _split.Panel2.Controls.Add(_gallery);
            _statusBar.Controls.Add(_status);

            // Zuletzt Hinzugefügtes dockt außen: Fill zuerst, dann die Übertragungen
            // (innen unten), die Statusleiste (ganz unten), die Navigation und
            // zuletzt das Ribbon (ganz oben).
            Controls.Add(_split);
            AddTransferPanel();
            Controls.Add(_statusBar);
            Controls.Add(_nav);
            Controls.Add(_ribbon);

            _grid.Selection.Changed += (s, e) => { UpdateCommands(); UpdateStatus(); };
            _gallery.Gallery.SelectionChanged += (s, e) => { UpdateCommands(); UpdateStatus(); };

            WireConnection();
            WireBrowsing();
            WireTitles();
            WireTransfers();
            WireOperations();
            WireClipboard();
            WireMenus();
            WireEditing();

            SetViewMode(_settings.ViewMode);
            _expertItem.Checked = _managerSettings.ExpertMode;
            _lightItem.Checked = AppTheme.IsLight;   // Theme gilt für die ganze Anwendung
            UpdateTargetText();
            UpdateCommands();
            UpdateStatus();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ClientSize = new Size(Px(1200), Px(760));
            CenterToScreen();

            // Höhe am Inhalt messen statt fest vorgeben: Der Skin gibt einem
            // Panel 8 px Padding und 1 px Rahmen je Seite (zusammen 18 px). Bei
            // festen Px(28) blieben dem eingedockten Text nur 10 px und
            // TextRenderer schnitt die Zeile oben und unten ab. Muster wie
            // TransferPanel.Relayout: Innenabstand des Panels + Textbedarf.
            int statusChrome = _statusBar.Height - _statusBar.DisplayRectangle.Height;
            _statusBar.Height = _status.GetPreferredSize(Size.Empty).Height + statusChrome;
        }

        // ---- Aufbau --------------------------------------------------------------------

        private static RibbonItem Small(string text)
        {
            return new RibbonItem(text) { Size = RibbonItemSize.Small };
        }

        private static RibbonItem Toggle(string text)
        {
            return new RibbonItem(text) { Size = RibbonItemSize.Small, Kind = RibbonItemKind.ToggleButton };
        }

        private void BuildRibbon()
        {
            var connection = new RibbonGroup("Connection");
            connection.Items.Add(_connectItem);
            connection.Items.Add(_targetItem);

            var transfer = new RibbonGroup("Transfer");
            transfer.Items.Add(_uploadItem);
            transfer.Items.Add(_uploadFolderItem);
            transfer.Items.Add(_downloadItem);

            var organize = new RibbonGroup("Organize");
            organize.Items.Add(_newFolderItem);
            organize.Items.Add(_renameItem);
            organize.Items.Add(_deleteItem);

            var start = new RibbonTab("Home");
            start.Groups.Add(connection);
            start.Groups.Add(transfer);
            start.Groups.Add(organize);

            var layout = new RibbonGroup("Layout");
            layout.Items.Add(_detailsItem);
            layout.Items.Add(_tilesItem);

            var protection = new RibbonGroup("Protection");
            protection.Items.Add(_expertItem);

            var design = new RibbonGroup("Theme");
            design.Items.Add(_lightItem);

            var view = new RibbonTab("View");
            view.Groups.Add(layout);
            view.Groups.Add(protection);
            view.Groups.Add(design);

            _ribbon.Tabs.Add(start);
            _ribbon.Tabs.Add(view);

            // ToggleButton: Checked steht beim Click schon auf dem NEUEN Zustand.
            _detailsItem.Click += (s, e) => SetViewMode(FileManagerViewSettings.DetailsView);
            _tilesItem.Click += (s, e) => SetViewMode(FileManagerViewSettings.TilesView);
            _expertItem.Click += (s, e) => SetExpertMode(_expertItem.Checked);
            _lightItem.Click += (s, e) => AppTheme.Current = _lightItem.Checked ? AppTheme.Light : AppTheme.Dark;
        }

        private void BuildGrid()
        {
            _grid.Columns.Add(new GridColumn("Name", "Name") { Width = 320, ShowImage = true, EditorFactory = () => new UiTextBox() });
            _grid.Columns.Add(new GridColumn("Modified", "Modified") { Width = 100, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Type", "Type") { Width = 160, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Size", "Size") { Width = 100, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("TitleId", "Title ID") { Width = 100, ReadOnly = true });
            foreach (var column in _grid.Columns)
            {
                int width;
                if (_settings.ColumnWidths.TryGetValue(column.Key, out width)) column.Width = width;
            }

            _grid.EditTriggers = GridEditTrigger.F2;         // Doppelklick/Enter öffnen (Spec)
            _grid.TypeAheadColumnKey = "Name";
            _grid.DataSource = _sorted;

            _grid.HeaderClick += (s, column) =>
            {
                string key = _grid.Columns[column].Key;
                _sortDirection = key == _sortKey && _sortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
                _sortKey = key;
                ApplySort();
            };
        }

        private void BuildGallery()
        {
            _gallery.Gallery.ImageSize = new Size(TileImageLogical, TileImageLogical);
            _gallery.Gallery.ItemCheckMode = ItemCheckMode.Multiple;
            _gallery.Gallery.ShowGroupCaption = false;
        }

        private void BuildTree()
        {
            _quickRoot.HasChildrenHint = false;
            _consoleRoot.Tag = "/";
            _consoleRoot.HasChildrenHint = true;
            _consoleRoot.Image = _shell.SmallIcon("", true);
            _tree.Nodes.Add(_quickRoot);
            _tree.Nodes.Add(_consoleRoot);
        }

        // ---- Liste ---------------------------------------------------------------------

        private bool IsTilesView { get { return _gallery.Visible; } }

        private bool Matches(FolderRow row)
        {
            return _filterText.Length == 0
                || row.ShownName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0
                || row.DisplayName.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Nach Neubefüllen oder Filtern werden Zeilen und Auswahl neu gebunden.
        /// </summary>
        private void Rebind()
        {
            _grid.DataSource = null;
            _filtered.Refresh();
            ApplySort();
            _grid.DataSource = _sorted;
            FillGallery();
        }

        private void ApplySort()
        {
            _sorted.Sort(_sortKey, _sortDirection, i => (int)_filtered.GetValue(i, "Rank"));
            _grid.Selection.Clear();
            _grid.Invalidate();
        }

        private FolderRow RowAt(int row)
        {
            return row < 0 || row >= _sorted.RowCount ? null : (FolderRow)_sorted.GetValue(row, "Row");
        }

        private List<FolderRow> CurrentSelection()
        {
            if (IsTilesView) return _gallery.Gallery.CheckedItems.Select(i => (FolderRow)i.Tag).ToList();
            return _grid.Selection.SelectedRows.Select(RowAt).Where(r => r != null).ToList();
        }

        private Zone CurrentZone()
        {
            return _session != null && _currentPath != null
                ? FolderBrowser.ContentsZone(_session.Policy, _currentPath)
                : Zone.Locked;
        }

        private void FillGallery()
        {
            _gallery.Gallery.Groups.Clear();
            var group = new GalleryItemGroup();
            var visible = Enumerable.Range(0, _filtered.RowCount)
                .Select(i => (FolderRow)_filtered.GetValue(i, "Row"))
                .OrderBy(r => r.GroupRank)
                .ThenBy(r => r.ShownName, StringComparer.CurrentCultureIgnoreCase);
            foreach (var row in visible)
            {
                group.Items.Add(new GalleryItem
                {
                    Caption = row.ShownName,
                    Description = row.IsFolder || row.IsLink ? "" : SizeFormat.Bytes(row.Entry.Size),
                    Image = IconFor(row, true),
                    Tag = row
                });
            }
            if (group.Items.Count > 0) _gallery.Gallery.Groups.Add(group);
        }

        private Image IconFor(FolderRow row, bool large)
        {
            Image title;
            if (row.TitleId != null && _titleIcons.TryGetValue(row.TitleId, out title)) return title;
            if (row.IsLink) return _shell.LinkIcon();
            return large ? _shell.LargeIcon(row.Entry.Name, row.IsFolder) : _shell.SmallIcon(row.Entry.Name, row.IsFolder);
        }

        // ---- Zustand anzeigen ------------------------------------------------------------

        private void UpdateCommands()
        {
            bool connected = _session != null && _currentPath != null;
            Func<string, Zone> zoneOf = _session != null ? (Func<string, Zone>)_session.Policy.Effective : p => Zone.Locked;
            _commands = CommandState.For(connected, CurrentZone(), CurrentSelection(), zoneOf);

            _uploadItem.Enabled = _commands.CanUpload;
            _uploadFolderItem.Enabled = _commands.CanUpload;
            _downloadItem.Enabled = _commands.CanDownload;
            _newFolderItem.Enabled = _commands.CanNewFolder;
            _renameItem.Enabled = _commands.CanRename;
            _deleteItem.Enabled = _commands.CanDelete;
            _connectItem.Text = _session != null ? "Disconnect" : "Connect";
            _ribbon.Invalidate();                       // RibbonItem meldet Änderungen nicht selbst

            _nav.Back.Enabled = _session != null && _session.Browser.CanGoBack;
            _nav.Forward.Enabled = _session != null && _session.Browser.CanGoForward;
            _nav.Up.Enabled = _session != null && _session.Browser.CanGoUp;
            _nav.Reload.Enabled = connected;
        }

        private void UpdateStatus()
        {
            _status.Text = StatusText.Line(_session != null, _filtered.RowCount, CurrentSelection(), CurrentZone());
        }

        private void UpdateTargetText()
        {
            var endpoint = _managerSession == null ? null : _managerSession.Endpoint;
            var host = endpoint == null ? _managerSettings.Ip : endpoint.Host;
            var port = endpoint == null ? _managerSettings.Port : endpoint.Port;
            _targetItem.Text = string.IsNullOrWhiteSpace(host) ? "Select console …" : host + ":" + port;
            _ribbon.Invalidate();
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        /// <summary>
        /// Fehler einer Anwenderaktion: Zonen-, Namens- und „geht nicht"-Fehler
        /// als Meldung (Spec „Locked → Meldung mit Pfad und Grund"), alles
        /// andere (Verbindung, Server) als Status wie in der Gallery.
        /// </summary>
        private void ShowError(Exception ex)
        {
            if (ex is OperationCanceledException) return;
            if (ex is ZoneViolationException || ex is NotSupportedException || ex is InvalidOperationException || ex is ArgumentException)
            {
                Dialogs.MessageDialog.Info(this, "PS4 File Manager", Ui.Describe(ex));
                return;
            }
                SetStatus("Error: " + Ui.Describe(ex));
        }

        private void SetViewMode(string mode)
        {
            bool tiles = mode == FileManagerViewSettings.TilesView;
            _settings.ViewMode = tiles ? FileManagerViewSettings.TilesView : FileManagerViewSettings.DetailsView;
            _gallery.Visible = tiles;
            _grid.Visible = !tiles;
            _detailsItem.Checked = !tiles;
            _tilesItem.Checked = tiles;
            _ribbon.Invalidate();
            UpdateCommands();
            UpdateStatus();
        }

        private void SetExpertMode(bool on)
        {
            _managerSettings.ExpertMode = on;
            _expertItem.Checked = on;
            if (_session != null) _session.ExpertMode = on;
            UpdateCommands();
            UpdateStatus();
        }

        // ---- Schließen -------------------------------------------------------------------

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _settings.TreeWidth = _split.SplitterDistance;
            foreach (var column in _grid.Columns) _settings.ColumnWidths[column.Key] = column.Width;
            if (_sessionBinding != null) _sessionBinding.Dispose();

            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            // Die Bindung zuerst lösen: so werden Queue-/Editing-Ereignisse auch
            // bei direktem Dispose abgemeldet. Detach leert die Ansichten, bevor
            // deren sitzungseigene Bilder freigegeben werden.
            if (disposing && _sessionBinding != null) _sessionBinding.Dispose();
            base.Dispose(disposing);
            if (disposing)
            {
                _shell.Dispose();
                ClearTitleIcons();
            }
        }
    }
}
