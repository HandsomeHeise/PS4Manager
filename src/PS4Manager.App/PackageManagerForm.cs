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
using FileManager.App.Dialogs;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Packages;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;

namespace PS4Manager.App
{
    internal sealed class PackageManagerForm : AppWindow
    {
        private readonly ManagerSessionController _sessions;
        private readonly ToolUiDispatcher _ui;
        private readonly ToolSessionBinding _binding;
        private readonly UiPanel _toolbar = new UiPanel { Dock = DockStyle.Top };
        private readonly UiButton _refresh = ToolbarButton("Refresh");
        private readonly UiButton _upload = ToolbarButton("Upload PKG");
        private readonly UiButton _download = ToolbarButton("Download");
        private readonly UiButton _delete = ToolbarButton("Delete");
        private readonly UiLabel _targetLabel = new UiLabel { Text = "Target", Dock = DockStyle.Left, AutoSize = false, Width = 52 };
        private readonly ComboBox _target = new ComboBox { Dock = DockStyle.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly UiLabel _status = new UiLabel { Dock = DockStyle.Fill, AutoSize = false };
        private readonly AppGrid _grid = new AppGrid { Dock = DockStyle.Fill };
        private readonly List<PackageRow> _rows = new List<PackageRow>();
        private readonly ListDataSource<PackageRow> _source;
        private ManagerSession _session;
        private CancellationTokenSource _refreshCts;

        public PackageManagerForm(ManagerSessionController sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            _source = new ListDataSource<PackageRow>(_rows);
            _source.Map("Name", r => r.Name);
            _source.Map("Location", r => r.Location);
            _source.Map("Size", r => r.Size);
            _source.Map("Modified", r => r.Modified);
            _source.Map("Path", r => r.Path);

            BuildUi();
            _binding = new ToolSessionBinding(_sessions, AttachSession, DetachSession);
            UpdateButtons();
        }

        private void BuildUi()
        {
            Text = "PKG Manager";
            ClientSize = new Size(920, 500);
            MinimumSize = new Size(680, 360);
            StartPosition = FormStartPosition.CenterParent;
            _toolbar.Height = 34;
            _status.TextAlignment = ContentAlignment.MiddleLeft;
            _targetLabel.TextAlignment = ContentAlignment.MiddleRight;

            foreach (var location in PackageRepository.DefaultLocations)
                _target.Items.Add(new TargetItem(location));
            if (_target.Items.Count > 0) _target.SelectedIndex = 0;

            _grid.Columns.Add(new GridColumn("Name", "Package") { Width = 260, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Location", "Location") { Width = 90, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Size", "Size") { Width = 90, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Modified", "Modified") { Width = 135, ReadOnly = true });
            _grid.Columns.Add(new GridColumn("Path", "Remote path") { Width = 320, ReadOnly = true });
            _grid.DataSource = _source;
            _grid.Selection.Changed += (s, e) => UpdateButtons();

            _toolbar.Controls.Add(_status);
            _toolbar.Controls.Add(_delete);
            _toolbar.Controls.Add(_download);
            _toolbar.Controls.Add(_upload);
            _toolbar.Controls.Add(_target);
            _toolbar.Controls.Add(_targetLabel);
            _toolbar.Controls.Add(_refresh);
            Controls.Add(_grid);
            Controls.Add(_toolbar);

            _refresh.Click += async (s, e) => await RefreshAsync();
            _upload.Click += async (s, e) => await UploadAsync();
            _download.Click += async (s, e) => await DownloadAsync();
            _delete.Click += async (s, e) => await DeleteAsync();
        }

        private static UiButton ToolbarButton(string text)
        {
            return new UiButton
            {
                Text = text,
                Dock = DockStyle.Left,
                AutoSize = false,
                Width = string.Equals(text, "Upload PKG", StringComparison.Ordinal) ? 92 : 78
            };
        }

        private void AttachSession(ManagerSession session)
        {
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed) return;
                    _session = session;
                    _status.Text = "Connected to " + session.Endpoint.Host + ".";
                    UpdateButtons();
                    var _ = RefreshAsync();
                });
            }
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
                    if (_refreshCts != null) _refreshCts.Cancel();
                    _session = null;
                    _rows.Clear();
                    Rebind();
                    _status.Text = "No FTP session.";
                    UpdateButtons();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private async Task RefreshAsync()
        {
            var session = _session;
            if (session == null)
            {
                _status.Text = "Connect the dashboard first.";
                return;
            }

            if (_refreshCts != null) _refreshCts.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken);
            _refreshCts = cts;
            _refresh.Enabled = false;
            _status.Text = "Scanning PKG locations...";
            try
            {
                var packages = await new PackageRepository(session.Explorer.Ftp).DiscoverAsync(cts.Token)
                    .ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _rows.Clear();
                    foreach (var package in packages)
                        _rows.Add(new PackageRow(package));
                    Rebind();
                    _status.Text = _rows.Count == 0
                        ? "No PKG files found in /data/pkg or USB roots."
                        : _rows.Count + " package(s) found.";
                    UpdateButtons();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _status.Text = "PKG scan failed: " + Ui.Describe(ex);
                    UpdateButtons();
                });
            }
            finally
            {
                _ui.Dispatch(() =>
                {
                    if (IsCurrent(cts)) _refresh.Enabled = _session != null;
                });
            }
        }

        private async Task UploadAsync()
        {
            var session = _session;
            var target = SelectedTarget();
            if (session == null || target == null) return;
            using (var dialog = new OpenFileDialog
            {
                Title = "Select PKG files",
                Filter = "PKG files (*.pkg)|*.pkg|All files (*.*)|*.*",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var packages = dialog.FileNames.Where(PackageRepository.IsPackageFile).ToList();
                if (packages.Count == 0)
                {
                    _status.Text = "Select at least one .pkg file.";
                    return;
                }

                try
                {
                    var job = await session.Explorer.Operations.PlanUploadAsync(packages, target.Path,
                        session.CancellationToken).ConfigureAwait(true);
                    if (job == null) return;
                    session.Explorer.Queue.Enqueue(job);
                    _status.Text = "Upload queued to " + target.Path + ".";
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _status.Text = "Upload failed: " + Ui.Describe(ex);
                }
            }
        }

        private async Task DownloadAsync()
        {
            var row = Selected();
            var session = _session;
            if (row == null || session == null) return;
            using (var dialog = new FolderBrowserDialog { Description = "Download PKG to folder" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var job = await session.Explorer.Operations.PlanDownloadAsync(
                        new[] { row.ToRemoteItem() }, dialog.SelectedPath, session.CancellationToken)
                        .ConfigureAwait(true);
                    session.Explorer.Queue.Enqueue(job);
                    _status.Text = "Download queued for " + row.Name + ".";
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _status.Text = "Download failed: " + Ui.Describe(ex);
                }
            }
        }

        private async Task DeleteAsync()
        {
            var row = Selected();
            var session = _session;
            if (row == null || session == null) return;
            if (!MessageDialog.Ask(this, "Delete PKG",
                    "Delete " + row.Name + " from " + row.Location + "?", "Delete", "Cancel"))
                return;

            try
            {
                var job = await session.Explorer.Operations.PlanDeleteAsync(
                    new[] { row.ToRemoteItem() }, session.CancellationToken).ConfigureAwait(true);
                if (job == null) return;
                session.Explorer.Queue.Enqueue(job);
                _status.Text = "Delete queued for " + row.Name + ".";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _status.Text = "Delete failed: " + Ui.Describe(ex);
            }
        }

        private PackageRow Selected()
        {
            var index = _grid.Selection.CurrentRow;
            return index >= 0 && index < _rows.Count ? _rows[index] : null;
        }

        private PackageLocation SelectedTarget()
        {
            var item = _target.SelectedItem as TargetItem;
            return item == null ? null : item.Location;
        }

        private bool IsCurrent(CancellationTokenSource cts)
        {
            return !IsDisposed && ReferenceEquals(_refreshCts, cts) && !cts.IsCancellationRequested;
        }

        private void UpdateButtons()
        {
            var connected = _session != null;
            var selected = Selected() != null;
            _refresh.Enabled = connected;
            _upload.Enabled = connected && SelectedTarget() != null;
            _download.Enabled = connected && selected;
            _delete.Enabled = connected && selected;
            _target.Enabled = connected;
        }

        private void Rebind()
        {
            _grid.DataSource = null;
            _grid.DataSource = _source;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refreshCts != null) _refreshCts.Cancel();
                if (_binding != null) _binding.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class TargetItem
        {
            public TargetItem(PackageLocation location)
            {
                Location = location;
            }

            public PackageLocation Location { get; }
            public override string ToString() { return Location.Name; }
        }

        private sealed class PackageRow
        {
            private readonly RemotePackage _package;

            public PackageRow(RemotePackage package)
            {
                _package = package;
            }

            public string Name { get { return _package.Name; } }
            public string Location { get { return _package.Location; } }
            public string Path { get { return _package.Path; } }
            public string Size { get { return SizeFormat.Bytes(_package.Size); } }
            public string Modified
            {
                get
                {
                    return _package.Modified == DateTime.MinValue
                        ? ""
                        : _package.Modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                }
            }

            public RemoteItem ToRemoteItem()
            {
                return new RemoteItem(_package.Path, false, _package.Size, _package.Modified);
            }
        }
    }
}
