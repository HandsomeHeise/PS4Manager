using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;
using DevExpress.XtraTreeList;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.UI;
using ScintillaNET;

namespace PS4Manager.App
{
    internal sealed partial class LumaStudioForm : AppWindow
    {
        private sealed class Buffer
        {
            public StudioDocument Document;
            public Scintilla Editor;
            public XtraTabPage Page;
            public UiLabel Breadcrumb;
            public LuaCheckResult Check;
            public long CheckedVersion = -1;
            public long AssistanceVersion = -1;
            public int AssistanceCaret = -1;
            public string AssistanceProfile;
        }
        private readonly ManagerSettings settings;
        private readonly Action saveSettings;
        private readonly StudioConnection connection;
        private readonly ILuaSyntaxChecker checker;
        private readonly Dictionary<Guid, Buffer> buffers = new Dictionary<Guid, Buffer>();
        private readonly Dictionary<string, UiButton> commands = new Dictionary<string, UiButton>();
        private readonly XtraTabControl documents = new XtraTabControl { Dock = DockStyle.Fill, ClosePageButtonShowMode = ClosePageButtonShowMode.InActiveTabPageHeader };
        private readonly TreeList project = new TreeList { Dock = DockStyle.Fill };
        private readonly ListBoxControl diagnostics = new ListBoxControl { Dock = DockStyle.Fill };
        private readonly MemoEdit console = new MemoEdit { Dock = DockStyle.Fill };
        private readonly MemoEdit help = new MemoEdit { Dock = DockStyle.Fill };
        private readonly TextEdit find = new TextEdit { Width = 200 };
        private readonly ComboBoxEdit game = new ComboBoxEdit { Width = 165 };
        private readonly ComboBoxEdit templates = new ComboBoxEdit { Width = 170 };
        private readonly TextEdit host = new TextEdit { Width = 130 };
        private readonly TextEdit port = new TextEdit { Width = 60 };
        private readonly TextEdit token = new TextEdit { Width = 120 };
        private readonly UiLabel status = new UiLabel { Dock = DockStyle.Fill, AutoSize = false };
        private readonly System.Windows.Forms.Timer checkTimer = new System.Windows.Forms.Timer { Interval = 600 };
        private readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Queue<string> logLines = new Queue<string>();
        private bool busy, checking, closing, discardAll, loading;
        private StudioExecution execution;
        private string projectRoot;

        public LumaStudioForm(ManagerSettings settings, Action saveSettings)
            : this(settings, saveSettings, new StudioConnection(), new LuaSyntaxChecker()) { }
        internal LumaStudioForm(ManagerSettings settings, Action saveSettings, StudioConnection connection, ILuaSyntaxChecker checker)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.saveSettings = saveSettings ?? (() => { });
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.checker = checker ?? throw new ArgumentNullException(nameof(checker));
            if (settings.Luma == null) settings.Luma = new LumaSettings();
            Text = "Luma Studio — PS4"; ClientSize = new Size(1380, 850); MinimumSize = new Size(1040, 680);
            StartPosition = FormStartPosition.CenterParent;
            BuildStudio(); LoadConnectionSettings(); RefreshProfile();
            AddDocument(new StudioDocument("hello.lua", StudioTemplates.ForProfile(Profile)[0].Source));
            documents.SelectedPageChanged += (s, e) => { ShowDiagnostics(); RefreshEditorChrome(); UpdateCommands(); };
            documents.CloseButtonClick += (s, e) => { if (ActiveBuffer != null) TryCloseDocument(ActiveDocument.Id, ConfirmClose); };
            diagnostics.DoubleClick += (s, e) => NavigateDiagnostic(diagnostics.SelectedIndex);
            checkTimer.Tick += async (s, e) => { checkTimer.Stop(); await CheckDocumentAsync(); };
            pollTimer.Tick += async (s, e) => { if (!busy && !closing && connection.Connected) await RunRemoteAsync("Logs", ct => connection.PollAsync(ct), true); };
            connection.LogReceived += OnStudioLog;
            AppTheme.Changed += OnThemeChanged;
            UpdateCommands();
        }
        private Buffer ActiveBuffer => documents.SelectedTabPage?.Tag as Buffer;
        internal Scintilla ActiveEditor => ActiveBuffer?.Editor;
        internal StudioDocument ActiveDocument => ActiveBuffer?.Document;
        internal int DocumentCount => buffers.Count;
        internal bool StudioConnected => connection.Connected;
        internal IEnumerable<string> CommandNames => commands.Keys;
        internal LuaApiCatalog ApiCatalog => LuaApiCatalog.ForProfile(Profile);
        private string Profile => game.SelectedIndex == 1 ? "rdr2" : "gta5";
        internal IReadOnlyList<LuaDiagnostic> CurrentDiagnostics => ActiveBuffer?.Check?.Diagnostics ?? Array.Empty<LuaDiagnostic>();
        internal void DiscardAllForTest() { discardAll = true; }

        private void BuildStudio()
        {
            var fileBar = Bar();
            AddButton(fileBar, "New", () => AddDocument(new StudioDocument("untitled.lua")));
            AddButton(fileBar, "Open", OpenFiles); AddButton(fileBar, "Open project", OpenProject);
            AddButton(fileBar, "Save", () => SaveDocument(ActiveBuffer, false));
            AddButton(fileBar, "Save as", () => SaveDocument(ActiveBuffer, true));
            AddButton(fileBar, "Check", async () => await CheckDocumentAsync());
            AddButton(fileBar, "Execute", async () => await ExecuteDocumentAsync());
            AddButton(fileBar, "Upload", async () => await UploadDocumentAsync());
            AddButton(fileBar, "Reload", async () => await RunRemoteAsync("Reload", ct => connection.ReloadAsync(ct)));
            fileBar.Controls.Add(templates); AddButton(fileBar, "Template", () => { var t = templates.SelectedItem as StudioTemplate; if (t != null) AddDocument(new StudioDocument(t.FileName, t.Source)); });

            var connectionBar = Bar();
            game.Properties.Items.AddRange(new object[] { "GTA V · Luma", "RDR2 · Luma" });
            game.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            templates.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            host.Properties.NullValuePrompt = "PS4 address"; token.Properties.NullValuePrompt = "Auth token"; token.Properties.UseSystemPasswordChar = true;
            connectionBar.Controls.AddRange(new Control[] { game, host, port, token });
            AddButton(connectionBar, "Connect", async () => await ConnectStudioAsync());
            AddButton(connectionBar, "Disconnect", DisconnectStudio);
            AddButton(connectionBar, "Find", FocusSearch);
            game.SelectedIndexChanged += (s, e) => { if (!loading) { DisconnectStudio(); port.Text = Profile == "gta5" ? "9615" : "9616"; RefreshProfile(); } };
            find.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; } };

            var workspace = BuildReferenceWorkspace();
            var projectBar = BuildProjectBar();
            var footer = BuildReferenceFooter();
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
            for (int row = 0; row < 3; row++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.Controls.Add(fileBar, 0, 0); top.Controls.Add(connectionBar, 0, 1); top.Controls.Add(projectBar, 0, 2);
            Controls.Add(workspace); Controls.Add(footer); Controls.Add(top);
            ApplyStudioTheme();
            status.Text = "Ready · offline";
        }
        private static FlowLayoutPanel Bar() => new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(4, 1, 4, 1) };
        private void AddButton(Control bar, string name, Action action)
        { var button = new StudioButton { Text = name, AutoSize = true, Height = 30, AccessibleName = name }; button.Click += (s, e) => { if (!closing) action(); }; commands.Add(name, button); bar.Controls.Add(button); }
        private void UpdateCommands()
        {
            bool editor = ActiveBuffer != null;
            foreach (string name in new[] { "Save", "Save as", "Check", "Next", "Find" }) commands[name].Enabled = editor;
            commands["Save"].Enabled = commands["Save as"].Enabled = editor && !busy;
            foreach (string name in new[] { "Execute", "Upload", "Reload" }) commands[name].Enabled = connection.Connected && !busy && (name == "Reload" || editor);
            commands["Connect"].Enabled = !busy && !connection.Connected;
            commands["Disconnect"].Enabled = busy || connection.Connected;
            game.Enabled = host.Enabled = port.Enabled = token.Enabled = !busy && !connection.Connected;
            UpdateProjectCommands();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { SaveDocument(ActiveBuffer, false); return true; }
            if (keyData == (Keys.Control | Keys.N)) { AddDocument(new StudioDocument("untitled.lua")); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenFiles(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { FocusSearch(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.O)) { OpenProject(); return true; }
            if (keyData == (Keys.Control | Keys.W) && ActiveBuffer != null) { TryCloseDocument(ActiveDocument.Id, ConfirmClose); return true; }
            if (keyData == Keys.F5) { _ = CheckDocumentAsync(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
