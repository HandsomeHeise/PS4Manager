using PS4Manager.UI;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        private readonly AppPopup _itemMenu = new AppPopup();
        private readonly AppPopup _backgroundMenu = new AppPopup();
        private readonly AppPopup _nodeMenu = new AppPopup();

        partial void WireMenus()
        {
            _grid.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _session == null) return;
                int row = _grid.GetDropTarget(e.Location);
                if (row >= 0 && !_grid.Selection.IsSelected(row)) _grid.Selection.Select(row);
                if (row >= 0) ShowItemMenu(_grid, e.Location);
                else ShowBackgroundMenu(_grid, e.Location);
            };
            _gallery.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _session == null) return;
                // Die Gallery wählt beim Rechtsklick ein nicht angehaktes Element selbst an.
                if (_gallery.Gallery.CheckedItems.Any()) ShowItemMenu(_gallery, e.Location);
                else ShowBackgroundMenu(_gallery, e.Location);
            };
            _tree.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || _session == null) return;
                var node = _tree.HitTest(e.Location);
                var path = node == null ? null : node.Tag as string;
                if (path != null) ShowNodeMenu(path, e.Location);
            };

            // AppPopup ist IDisposable (MenuController) — Muster Gallery-MainForm.
            FormClosed += (s, e) =>
            {
                _itemMenu.Dispose();
                _backgroundMenu.Dispose();
                _nodeMenu.Dispose();
            };
        }

        private void ShowItemMenu(Control owner, Point location)
        {
            UpdateCommands();
            var selection = CurrentSelection();
            var single = selection.Count == 1 ? selection[0] : null;

            _itemMenu.Items.Clear();
            Add(_itemMenu, "Open", single != null && (single.IsFolder || single.IsLink || _commands.CanOpen), () => Activate(single));
            Add(_itemMenu, "Download to …", _commands.CanDownload, DownloadSelection);
            _itemMenu.Items.Add(MenuEntry.Separator());
            Add(_itemMenu, "Cut", _commands.CanCut, () => { var _ = CopyAsync(ClipMode.Cut); });
            Add(_itemMenu, "Copy", _commands.CanCopy, () => { var _ = CopyAsync(ClipMode.Copy); });
            _itemMenu.Items.Add(MenuEntry.Separator());
            Add(_itemMenu, "Rename", _commands.CanRename, RenameSelection);
            Add(_itemMenu, "Delete", _commands.CanDelete, DeleteSelection);
            _itemMenu.Items.Add(MenuEntry.Separator());
            Add(_itemMenu, "Copy path", single != null, () => { var _ = CopyPathAsync(single.Item.Path); });
            if (single != null && single.IsFolder)
            {
                var path = single.Item.Path;
                Add(_itemMenu, IsPinned(path) ? "Remove from Quick access" : "Pin to Quick access", true, () => TogglePin(path));
            }
            _itemMenu.Show(owner, owner.PointToScreen(location));
        }

        private void ShowBackgroundMenu(Control owner, Point location)
        {
            UpdateCommands();
            _backgroundMenu.Items.Clear();
            Add(_backgroundMenu, "Paste", _commands.CanPaste, () => { var _ = PasteAsync(); });
            Add(_backgroundMenu, "New folder", _commands.CanNewFolder, NewFolder);
            _backgroundMenu.Items.Add(MenuEntry.Separator());
            Add(_backgroundMenu, "Upload …", _commands.CanUpload, UploadFiles);
            Add(_backgroundMenu, "Upload folder …", _commands.CanUpload, UploadFolder);
            _backgroundMenu.Items.Add(MenuEntry.Separator());
            Add(_backgroundMenu, "Refresh", _currentPath != null, () => { var _ = RefreshAsync(false); });
            _backgroundMenu.Show(owner, owner.PointToScreen(location));
        }

        private void ShowNodeMenu(string path, Point location)
        {
            _nodeMenu.Items.Clear();
            Add(_nodeMenu, "Open", true, () => NavigateTo(path));
            Add(_nodeMenu, "Copy path", true, () => { var _ = CopyPathAsync(path); });
            Add(_nodeMenu, IsPinned(path) ? "Remove from Quick access" : "Pin to Quick access", path != RemotePath.Root, () => TogglePin(path));
            _nodeMenu.Show(_tree, _tree.PointToScreen(location));
        }

        private static void Add(AppPopup menu, string text, bool enabled, Action action)
        {
            var entry = new MenuEntry(text) { Enabled = enabled };
            entry.Click += (s, e) => action();
            menu.Items.Add(entry);
        }

        private bool IsPinned(string path)
        {
            return _settings.PinnedFolders.Contains(RemotePath.Normalize(path));
        }

        /// <summary>Angeheftete Ordner (Spec „Schnellzugriff … dazu angeheftete Ordner des Anwenders").</summary>
        private void TogglePin(string path)
        {
            var normalized = RemotePath.Normalize(path);
            if (!_settings.PinnedFolders.Remove(normalized)) _settings.PinnedFolders.Add(normalized);
            // Geschrieben wird vom Dashboard (es besitzt die Einstellungen).
            var managerSession = _managerSession;
            if (managerSession != null)
            {
                var _ = LoadQuickAccessAsync(managerSession, managerSession.CancellationToken);
            }
        }

        private async System.Threading.Tasks.Task CopyPathAsync(string path)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            try
            {
                var data = _clipData;
                if (data != null)
                {
                    await _publisher.InvokeAsync(() =>
                    {
                        data.CloseOpenStreams();
                        return true;
                    });
                    if (!IsCurrentSession(managerSession, token)) return;
                }
                Clipboard.SetText(path);
                if (data != null) await _publisher.OwnsClipboardAsync(data);
                if (!IsCurrentSession(managerSession, token)) return;
                _clipData = null;
                _clipItems = null;
                _clipMode = ClipMode.None;
                SetStatus("Path copied: " + path);
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token))
                    ShowError(ex);                                // Zwischenablage gerade von einem anderen Programm belegt
            }
        }
    }
}
