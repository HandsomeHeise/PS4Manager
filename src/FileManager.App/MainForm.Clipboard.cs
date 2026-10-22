using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Presentation;
using FileManager.Core.Shell;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        private readonly ShellPublisher _publisher = new ShellPublisher();
        private VirtualFileDataObject _clipData;
        private List<RemoteItem> _clipItems;
        private ClipMode _clipMode = ClipMode.None;
        private List<RemoteItem> _internalDrag;

        partial void WireClipboard()
        {
            FormClosed += (s, e) => _publisher.Dispose();
            SessionEnding += session =>
            {
                _clipMode = ClipMode.None;
                _clipItems = null;
                _clipData = null;
            };

            _grid.AllowItemDrag = true;
            _grid.AllowDrop = true;
            _grid.ItemDrag += (s, e) => StartDrag(e.Rows.Select(RowAt).Where(r => r != null).ToList());
            _grid.DragEnter += OnGridDrag;
            _grid.DragOver += OnGridDrag;
            _grid.DragLeave += (s, e) => _grid.DropHighlight = -1;
            _grid.DragDrop += (s, e) =>
            {
                var folder = PastePlanner.DropFolder(RowAt(_grid.GetDropTarget(_grid.PointToClient(new Point(e.X, e.Y)))), _currentPath);
                _grid.DropHighlight = -1;
                Drop(e, folder);
            };

            _gallery.AllowItemDrag = true;
            _gallery.AllowDrop = true;
            _gallery.ItemDrag += (s, e) => StartDrag(e.Items.Select(i => (FolderRow)i.Tag).ToList());
            _gallery.DragEnter += OnGalleryDrag;
            _gallery.DragOver += OnGalleryDrag;
            _gallery.DragLeave += (s, e) => _gallery.DropHighlight = null;
            _gallery.DragDrop += (s, e) =>
            {
                var item = _gallery.GetDropTarget(_gallery.PointToClient(new Point(e.X, e.Y)));
                var folder = PastePlanner.DropFolder(item == null ? null : (FolderRow)item.Tag, _currentPath);
                _gallery.DropHighlight = null;
                Drop(e, folder);
            };

            _tree.AllowItemDrag = true;
            _tree.AllowDrop = true;
            _tree.ItemDrag += (s, e) =>
            {
                var path = e.Node.Tag as string;
                if (path == null || path == "/" || _session == null) return;
                StartDrag(_session, new List<RemoteItem> { new RemoteItem(path, true, 0, DateTime.MinValue) });
            };
            _tree.DragEnter += OnTreeDrag;
            _tree.DragOver += OnTreeDrag;
            _tree.DragLeave += (s, e) => _tree.DropHighlight = null;
            _tree.DragDrop += (s, e) =>
            {
                var node = _tree.GetDropTarget(_tree.PointToClient(new Point(e.X, e.Y)));
                _tree.DropHighlight = null;
                Drop(e, node == null ? null : node.Tag as string);
            };

            AddShortcut(Keys.Control | Keys.C, () => { if (!ListHasFocus()) return false; var _ = CopyAsync(ClipMode.Copy); return true; }, true);
            AddShortcut(Keys.Control | Keys.X, () => { if (!ListHasFocus()) return false; var _ = CopyAsync(ClipMode.Cut); return true; }, true);
            AddShortcut(Keys.Control | Keys.V, () => { if (!ListHasFocus() && !_tree.ContainsFocus) return false; var _ = PasteAsync(); return true; }, true);
            AddShortcut(Keys.Control | Keys.A, () => { if (!ListHasFocus()) return false; SelectAll(); return true; }, true);
        }

        // ---- Hinaus --------------------------------------------------------------------------

        private void StartDrag(IReadOnlyList<FolderRow> rows)
        {
            var session = _session;
            var items = rows.Where(r => !r.IsLink).Select(r => r.Item).ToList();
            if (session == null || items.Count == 0) return;
            StartDrag(session, items);
        }

        /// <summary>
        /// Virtuelle Dateien über den Shell-Thread (Übergabe Plan 1): Explorers
        /// Rückrufe laufen dort, nicht auf dem UI-Thread. _internalDrag markiert
        /// das eigene Ziehen für die eigenen Ablageziele.
        /// </summary>
        private void StartDrag(ExplorerSession session, List<RemoteItem> items)
        {
            _internalDrag = items;
            try
            {
                var data = _publisher.Create(session.Ftp, items);
                _publisher.DoDragDrop(data, ShellInterop.DropEffectCopy | ShellInterop.DropEffectMove);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _internalDrag = null;
            }
        }

        // ---- Hinein --------------------------------------------------------------------------

        private DropAction DropActionFor(DragEventArgs e, string folder)
        {
            if (_session == null || folder == null) return DropAction.None;
            var zone = FolderBrowser.ContentsZone(_session.Policy, folder);
            var action = PastePlanner.ForDrop(_internalDrag != null, e.Data.GetDataPresent(DataFormats.FileDrop), zone);
            if (action == DropAction.Move && !PastePlanner.IsUsefulMove(_internalDrag, folder)) return DropAction.None;
            return action;
        }

        private static DragDropEffects EffectFor(DropAction action)
        {
            if (action == DropAction.Move) return DragDropEffects.Move;
            if (action == DropAction.Upload) return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        private void OnGridDrag(object sender, DragEventArgs e)
        {
            int row = _grid.GetDropTarget(_grid.PointToClient(new Point(e.X, e.Y)));
            var target = RowAt(row);
            var action = DropActionFor(e, PastePlanner.DropFolder(target, _currentPath));
            e.Effect = EffectFor(action);
            _grid.DropHighlight = action != DropAction.None && target != null && target.IsFolder ? row : -1;
        }

        private void OnGalleryDrag(object sender, DragEventArgs e)
        {
            var item = _gallery.GetDropTarget(_gallery.PointToClient(new Point(e.X, e.Y)));
            var target = item == null ? null : (FolderRow)item.Tag;
            var action = DropActionFor(e, PastePlanner.DropFolder(target, _currentPath));
            e.Effect = EffectFor(action);
            _gallery.DropHighlight = action != DropAction.None && target != null && target.IsFolder ? item : null;
        }

        private void OnTreeDrag(object sender, DragEventArgs e)
        {
            var node = _tree.GetDropTarget(_tree.PointToClient(new Point(e.X, e.Y)));
            var action = DropActionFor(e, node == null ? null : node.Tag as string);
            e.Effect = EffectFor(action);
            _tree.DropHighlight = action != DropAction.None ? node : null;
        }

        private void Drop(DragEventArgs e, string folder)
        {
            var action = DropActionFor(e, folder);
            if (action == DropAction.Upload) UploadInto((string[])e.Data.GetData(DataFormats.FileDrop), folder);
            else if (action == DropAction.Move) { var _ = MoveInto(_internalDrag.ToList(), folder); }
        }

        // ---- Zwischenablage ------------------------------------------------------------------

        private async Task CopyAsync(ClipMode mode)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var session = managerSession.Explorer;
            var items = CurrentSelection().Where(r => !r.IsLink).Select(r => r.Item).ToList();
            if (items.Count == 0) return;
            if (mode == ClipMode.Cut && !_commands.CanCut) return;
            try
            {
                var data = _publisher.Create(session.Ftp, items);
                await _publisher.SetClipboardAsync(data);
                if (!IsCurrentSession(managerSession, token)) return;
                _clipData = data;
                _clipItems = items;
                _clipMode = mode;
                SetStatus(StatusText.Items(items.Count) + (mode == ClipMode.Cut
                    ? " cut — press Ctrl+V to move it to another folder."
                    : " copied — press Ctrl+V in Explorer to paste it."));
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token)) ShowError(ex);
            }
        }

        private async Task PasteAsync()
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var session = managerSession.Explorer;
            var folder = _currentPath;
            if (folder == null || !_commands.CanPaste) return;

            bool ours = _clipData != null && await _publisher.OwnsClipboardAsync(_clipData);
            if (!IsCurrentSession(managerSession, token)) return;
            bool files = !ours && Clipboard.ContainsFileDropList();
            switch (PastePlanner.Decide(_clipMode, ours, files))
            {
                case PasteAction.MoveItems:
                    if (await MoveInto(_clipItems, folder))
                    {
                        if (!IsCurrentSession(managerSession, token)) return;
                        _clipMode = ClipMode.None;           // erst nach Annahme des Auftrags einmalig verbraucht
                    }
                    break;
                case PasteAction.RefuseCopyWithinPs4:
                    try { session.Operations.PlanCopyWithinPs4(); }
                    catch (NotSupportedException ex) { MessageDialog.Info(this, "Paste", ex.Message); }
                    break;
                case PasteAction.UploadFiles:
                    UploadInto(Clipboard.GetFileDropList().Cast<string>().ToList(), folder);
                    break;
                default:
                    SetStatus("Nothing to paste.");
                    break;
            }
        }

        private void SelectAll()
        {
            if (IsTilesView)
            {
                foreach (var item in _gallery.Gallery.Groups.SelectMany(g => g.Items)) item.Checked = true;
            }
            else if (_sorted.RowCount > 0)
            {
                _grid.Selection.Select(0);
                _grid.Selection.ExtendTo(_sorted.RowCount - 1);
            }
            UpdateCommands();
            UpdateStatus();
        }
    }
}
