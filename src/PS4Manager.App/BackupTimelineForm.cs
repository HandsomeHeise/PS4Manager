using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using FileManager.App.Dialogs;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Session;

namespace PS4Manager.App
{
    internal sealed class BackupTimelineForm : AppWindow
    {
        private readonly ManagerSessionController _sessions;
        private readonly string _root;
        private readonly ToolUiDispatcher _ui;
        private readonly ToolSessionBinding _binding;
        private readonly UiPanel _toolbar = new UiToolbar { Dock = DockStyle.Top };
        private readonly UiButton _refresh = new UiButton { Text = "Refresh", Dock = DockStyle.Left, AutoSize = true };
        private readonly UiButton _restore = new UiButton { Text = "Restore", Dock = DockStyle.Left, AutoSize = true };
        private readonly UiLabel _status = new UiLabel { Dock = DockStyle.Fill, AutoSize = false };
        private readonly AppGrid _grid = new AppGrid { Dock = DockStyle.Fill };
        private readonly List<BackupRow> _rows = new List<BackupRow>();
        private readonly ListDataSource<BackupRow> _source;
        private ManagerSession _session;

        public BackupTimelineForm(ManagerSessionController sessions)
            : this(sessions, AppPaths.GoldHenBackupRoot)
        {
        }

        internal BackupTimelineForm(ManagerSessionController sessions, string root)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            _source = new ListDataSource<BackupRow>(_rows);
            _source.Map("Created", r => r.Created);
            _source.Map("Operation", r => r.Operation);
            _source.Map("Source", r => r.Source);
            _source.Map("Size", r => r.Size);
            _source.Map("Hash", r => r.Hash);

            Text = "Backup Timeline";
            ClientSize = new System.Drawing.Size(880, 460);
            MinimumSize = new System.Drawing.Size(640, 340);
            StartPosition = FormStartPosition.CenterParent;
            _status.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;

            _grid.Columns.Add(new GridColumn("Created", "Created") { Width = 135, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Operation", "Operation") { Width = 130, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Source", "Source") { Width = 330, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Size", "Size") { Width = 90, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Hash", "SHA-256") { Width = 180, ReadOnly = true });
            _grid.DataSource = _source;
            _grid.Selection.Changed += (s, e) => UpdateButtons();

            _toolbar.Controls.Add(_status);
            _toolbar.Controls.Add(_restore);
            _toolbar.Controls.Add(_refresh);
            Controls.Add(_grid);
            Controls.Add(_toolbar);

            _refresh.Click += (s, e) => LoadBackups();
            _restore.Click += async (s, e) => await RestoreAsync();
            _binding = new ToolSessionBinding(_sessions, AttachSession, DetachSession);
            LoadBackups();
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

        private void LoadBackups()
        {
            _rows.Clear();
            foreach (var entry in GoldHenBackupTimeline.Load(_root))
                _rows.Add(new BackupRow(entry));
            Rebind();
            _status.Text = _rows.Count == 0
                ? "No GoldHEN backups found."
                : _rows.Count + " backup(s) found.";
            UpdateButtons();
        }

        private async Task RestoreAsync()
        {
            var row = Selected();
            var session = _session;
            if (row == null || session == null) return;
            if (!GoldHenBackupTimeline.VerifyPayload(row.Entry))
            {
                _status.Text = "Restore blocked: backup hash does not match the payload.";
                return;
            }
            if (!MessageDialog.Ask(this, "Restore backup",
                    "Restore " + row.Entry.Source + " from the selected backup?",
                    "Restore", "Cancel"))
                return;

            var token = session.CancellationToken;
            try
            {
                var snapshot = await new GoldHenMutationSafety(session.Explorer.Ftp, session.Explorer.Gates,
                    new GoldHenBackupWriter(AppPaths.GoldHenBackupRoot, SystemClock.Instance))
                    .BackupCurrentAsync(row.Entry.Source, "restore-backup", token).ConfigureAwait(true);
                var job = await session.Explorer.Operations.PlanUploadAsync(
                    new[] { row.Entry.PayloadPath }, RemotePath.Parent(row.Entry.Source), token).ConfigureAwait(true);
                if (job == null) return;
                snapshot.Protect(job);
                session.Explorer.Queue.Enqueue(job);
                _status.Text = "Restore queued for " + RemotePath.Name(row.Entry.Source) + ".";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _status.Text = "Restore failed: " + Ui.Describe(ex);
            }
        }

        private BackupRow Selected()
        {
            var index = _grid.Selection.CurrentRow;
            return index >= 0 && index < _rows.Count ? _rows[index] : null;
        }

        private void UpdateButtons()
        {
            _restore.Enabled = _session != null && Selected() != null;
        }

        private void Rebind()
        {
            _grid.DataSource = null;
            _grid.DataSource = _source;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _binding != null) _binding.Dispose();
            base.Dispose(disposing);
        }

        private sealed class BackupRow
        {
            public BackupRow(GoldHenBackupEntry entry)
            {
                Entry = entry;
            }

            public GoldHenBackupEntry Entry { get; private set; }
            public string Created { get { return Entry.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture); } }
            public string Operation { get { return Entry.Operation; } }
            public string Source { get { return Entry.Source; } }
            public string Size { get { return SizeFormat.Bytes(Entry.Size); } }
            public string Hash { get { return Entry.Sha256.Length > 16 ? Entry.Sha256.Substring(0, 16) : Entry.Sha256; } }
        }
    }
}
