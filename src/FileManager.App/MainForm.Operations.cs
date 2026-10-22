using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        private const string TitleFolderText = "Game folders must keep their Title ID — the PS4 can only find a game under this name.";

        private string _lastLocalFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        partial void WireOperations()
        {
            // Nicht direkt aus SetValue heraus: ein Dialog mitten im CommitEdit
            // löste über den Fokusverlust des Editors ein zweites Commit aus.
            _list.MapSet("Name", (row, value) => BeginInvoke((Action)(() => RenameRow(row, value as string))));

            _newFolderItem.Click += (s, e) => NewFolder();
            _renameItem.Click += (s, e) => RenameSelection();
            _deleteItem.Click += (s, e) => DeleteSelection();
            _uploadItem.Click += (s, e) => UploadFiles();
            _uploadFolderItem.Click += (s, e) => UploadFolder();
            _downloadItem.Click += (s, e) => DownloadSelection();

            AddShortcut(Keys.Control | Keys.Shift | Keys.N, () => { NewFolder(); return true; }, false);
            AddShortcut(Keys.Delete, () =>
            {
                if (!ListHasFocus()) return false;
                DeleteSelection();
                return true;
            }, true);
            AddShortcut(Keys.F2, () =>
            {
                // Beide Ansichten über RenameSelection: prüft CanRename und Spielordner, dann Zelle
                // bzw. Dialog. ProcessCmdKey läuft vor dem OnKeyDown des Grids.
                if (!ListHasFocus()) return false;
                RenameSelection();
                return true;
            }, true);
        }

        private bool ListHasFocus()
        {
            return IsTilesView ? _gallery.ContainsFocus : _grid.ContainsFocus;
        }

        /// <summary>
        /// Plant einen Auftrag (Phase „wird ermittelt …", Spec) und reiht ihn ein.
        /// null heißt: der Anwender hat eine Rückfrage abgelehnt.
        /// </summary>
        private async Task<bool> RunPlanAsync(string busyText, Func<ExplorerSession, CancellationToken, Task<TransferJob>> plan)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return false;
            var session = managerSession.Explorer;
            var token = managerSession.CancellationToken;
            SetStatus(busyText + " — preparing …");
            try
            {
                var job = await plan(session, token);
                if (!IsCurrentSession(managerSession, token)) return false;
                if (job == null)
                {
                    SetStatus("Cancelled.");
                    return false;
                }
                session.Queue.Enqueue(job);
                SetStatus(job.Title + " — queued.");
                return true;
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token)) ShowError(ex);
                return false;
            }
        }

        private void UploadInto(IReadOnlyList<string> localPaths, string folder)
        {
            if (localPaths == null || localPaths.Count == 0 || folder == null) return;
            // Auch Ziele aus dem Schnellzugriff laufen durch PathResolver (Plan-Entscheidung 5).
            var _ = RunPlanAsync("Upload", async (s, token) => await s.Operations.PlanUploadAsync(localPaths,
                await PathResolver.ResolveAsync(s.Cache, folder, token), token));
        }

        private Task<bool> MoveInto(IReadOnlyList<RemoteItem> items, string folder)
        {
            if (items == null || items.Count == 0 || folder == null) return Task.FromResult(false);
            if (!PastePlanner.IsUsefulMove(items, folder)) return Task.FromResult(false);
            return RunPlanAsync("Move", async (s, token) => await s.Operations.PlanMoveAsync(items,
                await PathResolver.ResolveAsync(s.Cache, folder, token), token));
        }

        private void NewFolder()
        {
            if (!_commands.CanNewFolder) return;
            var folder = _currentPath;
            var _ = RunPlanAsync("New folder", (s, token) => s.Operations.PlanCreateFolderAsync(folder, token));
        }

        private void RenameSelection()
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var selection = CurrentSelection();
            if (selection.Count == 1 && selection[0].IsTitleFolder)
            {
                MessageDialog.Info(this, "Rename", TitleFolderText);
                return;
            }
            if (!_commands.CanRename || selection.Count != 1) return;

            if (!IsTilesView)
            {
                int nameColumn = _grid.Columns.IndexOf(_grid.Columns.First(c => c.Key == "Name"));   // Spalten lassen sich umordnen
                _grid.BeginEdit(_grid.Selection.CurrentRow, nameColumn);
                return;
            }
            using (var dialog = new RenameDialog(selection[0].DisplayName))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && IsCurrentSession(managerSession, token))
                    RenameRow(selection[0], dialog.NewName);
            }
        }

        private void RenameRow(FolderRow row, string newName)
        {
            if (row == null || newName == null || !_rows.Contains(row)) return;
            if (row.IsTitleFolder)
            {
                MessageDialog.Info(this, "Rename", TitleFolderText);
                return;
            }
            var name = newName.Trim();
            if (name == row.DisplayName) return;
            var item = row.Item;
            var _ = RunPlanAsync("Rename", (s, token) => s.Operations.PlanRenameAsync(item, name, token));
        }

        private void DeleteSelection()
        {
            var items = CurrentSelection().Select(r => r.Item).ToList();
            if (!_commands.CanDelete || items.Count == 0) return;
            var _ = RunPlanAsync("Delete", (s, token) => s.Operations.PlanDeleteAsync(items, token));
        }

        private void UploadFiles()
        {
            if (!_commands.CanUpload) return;
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Upload files to the PS4", InitialDirectory = _lastLocalFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!IsCurrentSession(managerSession, token)) return;
                _lastLocalFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                UploadInto(dialog.FileNames.ToList(), _currentPath);
            }
        }

        private void UploadFolder()
        {
            if (!_commands.CanUpload) return;
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            using (var dialog = new FolderBrowserDialog { Description = "Upload a folder to the PS4", SelectedPath = _lastLocalFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!IsCurrentSession(managerSession, token)) return;
                _lastLocalFolder = dialog.SelectedPath;
                UploadInto(new[] { dialog.SelectedPath }, _currentPath);
            }
        }

        private void DownloadSelection()
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var items = CurrentSelection().Where(r => !r.IsLink).Select(r => r.Item).ToList();
            if (!_commands.CanDownload || items.Count == 0) return;
            using (var dialog = new FolderBrowserDialog { Description = "Download to …", SelectedPath = _lastLocalFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!IsCurrentSession(managerSession, token)) return;
                _lastLocalFolder = dialog.SelectedPath;
                var target = dialog.SelectedPath;
                var _ = RunPlanAsync("Download", (s, ct) => s.Operations.PlanDownloadAsync(items, target, ct));
            }
        }
    }
}
