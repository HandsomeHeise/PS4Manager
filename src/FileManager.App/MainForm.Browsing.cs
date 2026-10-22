using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using Ps4.Core.Ftp;
using TreeNode = PS4Manager.UI.NavigationNode;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        private readonly Dictionary<Keys, Func<bool>> _shortcuts = new Dictionary<Keys, Func<bool>>();
        private readonly HashSet<Keys> _listOnlyKeys = new HashSet<Keys>();
        private readonly HashSet<TreeNode> _loadedFolderNodes = new HashSet<TreeNode>();
        private CancellationTokenSource _navCts;
        private bool _clearingFilter;

        /// <summary>Ein Ordner wurde neu gezeigt (Navigation, Aktualisieren).</summary>
        private event Action FolderShown;

        /// <summary>Doppelklick/Enter auf eine Datei — Öffnen mit Rückschreiben (Task 21).</summary>
        private event Action<FolderRow> FileActivated;

        /// <summary>Task 16 setzt hier Spielname und -symbol eines Ordnerknotens.</summary>
        partial void DecorateFolderNode(TreeNode node);

        partial void WireBrowsing()
        {
            // „_ =": Go ist async Task — ein verworfener Aufruf wäre Warnung CS4014.
            _nav.Back.Click += (s, e) => { if (_session != null && _session.Browser.CanGoBack) _ = Go((session, ct) => session.Browser.BackAsync(ct)); };
            _nav.Forward.Click += (s, e) => { if (_session != null && _session.Browser.CanGoForward) _ = Go((session, ct) => session.Browser.ForwardAsync(ct)); };
            _nav.Up.Click += (s, e) => { if (_session != null && _session.Browser.CanGoUp) _ = Go((session, ct) => session.Browser.UpAsync(ct)); };
            _nav.Reload.Click += async (s, e) => await RefreshAsync(true);

            _nav.Address.SegmentClicked += (s, e) => NavigateTo(e.Path);
            _nav.Address.PathSubmitted += (s, e) => NavigateTo(e.Path);
            _nav.Filter.TextChanged += (s, e) =>
            {
                if (_clearingFilter) return;
                _filterText = _nav.Filter.Text.Trim();
                Rebind();
                UpdateCommands();
                UpdateStatus();
            };

            _grid.RowActivated += (s, row) => Activate(RowAt(row));
            _gallery.Gallery.ItemDoubleClick += (s, e) => Activate((FolderRow)e.Item.Tag);

            _tree.NodeExpanding += async (s, e) => await ExpandNodeAsync(e.Node);
            _tree.SelectedNodeChanged += (s, e) =>
            {
                var path = _tree.SelectedNode == null ? null : _tree.SelectedNode.Tag as string;
                if (path != null) NavigateTo(path);
            };

            SessionEnding += session =>
            {
                if (_navCts != null) _navCts.Cancel();
            };

            AddShortcut(Keys.Alt | Keys.Left, () => { _nav.Back.PerformClick(); return true; }, false);
            AddShortcut(Keys.Alt | Keys.Right, () => { _nav.Forward.PerformClick(); return true; }, false);
            AddShortcut(Keys.Alt | Keys.Up, () => { _nav.Up.PerformClick(); return true; }, false);
            AddShortcut(Keys.Back, () => { _nav.Up.PerformClick(); return true; }, true);
            AddShortcut(Keys.F5, () => { var _ = RefreshAsync(true); return true; }, false);
            AddShortcut(Keys.Control | Keys.L, () => { _nav.Address.BeginEdit(); return true; }, false);
            AddShortcut(Keys.Enter, () =>
            {
                // Details: das Grid meldet Enter selbst als RowActivated.
                if (!IsTilesView || !_gallery.ContainsFocus) return false;
                var selection = CurrentSelection();
                if (selection.Count == 1) Activate(selection[0]);
                return true;
            }, true);
        }

        // ---- Tasten ------------------------------------------------------------------------

        private void AddShortcut(Keys keys, Func<bool> action, bool listOnly)
        {
            _shortcuts[keys] = action;
            if (listOnly) _listOnlyKeys.Add(keys);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Func<bool> action;
            if (_shortcuts.TryGetValue(keyData, out action)
                && !(_listOnlyKeys.Contains(keyData) && Ui.IsTextInputFocused())
                && action())
            {
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- Navigation ----------------------------------------------------------------------

        private Task NavigateToAsync(string path)
        {
            return Go((session, ct) => session.Browser.NavigateAsync(path, ct));
        }

        private void NavigateTo(string path)
        {
            var _ = NavigateToAsync(path);
        }

        /// <summary>Aktualisieren; everything (F5, Spec) verwirft vorher den ganzen Verzeichnis-Cache.</summary>
        private Task RefreshAsync(bool everything)
        {
            if (_session == null || _currentPath == null) return Task.FromResult(0);
            if (everything)
            {
                _session.Cache.Clear();
                ReloadTreeFolders(LoadedFolderNodes(_tree.Nodes).Select(n => (string)n.Tag).ToList());
            }
            return Go((session, ct) => session.Browser.RefreshAsync(ct));
        }

        /// <summary>Eine Navigation; eine neuere bricht die ältere ab, eine alte Antwort zeigt nichts mehr an.</summary>
        private async Task Go(Func<ExplorerSession, CancellationToken, Task<FolderView>> navigation)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var session = managerSession.Explorer;
            var token = managerSession.CancellationToken;
            if (_navCts != null) _navCts.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _navCts = cts;
            SetStatus("Loading …");
            try
            {
                var view = await navigation(session, cts.Token);
                if (cts != _navCts || !IsCurrentSession(managerSession, token)) return;
                ShowFolder(view);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // Spec „Fehlerbehandlung": Listenfehler beim Durchsuchen → Status mit Klartext.
                if (cts == _navCts && IsCurrentSession(managerSession, token))
                {
                    SetStatus("Error: " + Ui.Describe(ex));
                    _nav.Address.Path = _currentPath;
                    UpdateCommands();
                }
            }
        }

        private void ShowFolder(FolderView view)
        {
            _currentPath = view.Path;
            _settings.LastPath = view.Path;
            _nav.Address.Path = view.Path;
            ClearFilter();

            _rows.Clear();
            _rows.AddRange(view.Entries.Select(e => new FolderRow(view.Path, e, TypeNameFor(e))));
            Rebind();

            var shown = FolderShown;
            if (shown != null) shown();
            UpdateCommands();
            UpdateStatus();
        }

        private void ClearFilter()
        {
            _clearingFilter = true;
            _nav.Filter.Text = "";
            _clearingFilter = false;
            _filterText = "";
        }

        private string TypeNameFor(FtpEntry entry)
        {
            if (entry.Kind == FtpEntryKind.Link) return "Shortcut → " + entry.LinkTarget;
            return _shell.TypeName(entry.Name, entry.IsDirectory);
        }

        /// <summary>Doppelklick/Enter (Spec): Ordner und Verknüpfungen öffnen, Dateien gehen an FileActivated.</summary>
        private void Activate(FolderRow row)
        {
            if (row == null) return;
            if (row.IsFolder || row.IsLink)
            {
                NavigateTo(row.Item.Path);                    // Links löst FolderBrowser über PathResolver auf
                return;
            }
            var activated = FileActivated;
            if (activated != null) activated(row);
        }

        private void ClearView()
        {
            if (_navCts != null) _navCts.Cancel();
            _currentPath = null;
            _rows.Clear();
            ClearFilter();
            Rebind();
            _nav.Address.Path = "/";
            _quickRoot.Children.Clear();
            _quickRoot.HasChildrenHint = false;
            _consoleRoot.Children.ReplaceWith(new TreeNode[0]);   // beendet auch einen hängenden Ladevorgang
            _consoleRoot.HasChildrenHint = true;
            _loadedFolderNodes.Clear();
        }

        // ---- Baum ------------------------------------------------------------------------------

        private TreeNode FolderNode(string label, string path)
        {
            var node = new TreeNode(label)
            {
                Tag = RemotePath.Normalize(path),
                HasChildrenHint = true,
                Image = _shell.SmallIcon("", true)
            };
            DecorateFolderNode(node);
            return node;
        }

        /// <summary>Schon geladene Baumknoten dieser Ordner neu lesen (Spec „Verzeichnis-Cache": Baum und Liste teilen ihn).</summary>
        private void ReloadTreeFolders(IEnumerable<string> folders)
        {
            var wanted = new HashSet<string>(folders.Select(RemotePath.Canonical), StringComparer.Ordinal);
            foreach (var node in LoadedFolderNodes(_tree.Nodes).ToList())
            {
                if (wanted.Contains(RemotePath.Canonical((string)node.Tag)))
                {
                    var _ = ExpandNodeAsync(node);
                }
            }
        }

        private IEnumerable<TreeNode> LoadedFolderNodes(IEnumerable<TreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Tag is string && _loadedFolderNodes.Contains(node)) yield return node;
                foreach (var child in LoadedFolderNodes(node.Children)) yield return child;
            }
        }

        private void ForgetLoadedNodes(IEnumerable<TreeNode> nodes)
        {
            foreach (var node in nodes.ToList())
            {
                ForgetLoadedNodes(node.Children);
                _loadedFolderNodes.Remove(node);
            }
        }

        /// <summary>Kinder erst beim Aufklappen laden (Spec) — über den gemeinsamen Verzeichnis-Cache, Links aufgelöst, nur Ordner.</summary>
        private async Task ExpandNodeAsync(TreeNode node)
        {
            var path = node.Tag as string;
            var managerSession = _managerSession;
            if (path == null || managerSession == null)
            {
                ForgetLoadedNodes(node.Children);
                _loadedFolderNodes.Remove(node);
                node.Children.ReplaceWith(new TreeNode[0]);
                return;
            }

            var session = managerSession.Explorer;
            var token = managerSession.CancellationToken;
            node.BeginLoading();
            try
            {
                var resolved = await PathResolver.ResolveAsync(session.Cache, path, token);
                var entries = await session.Cache.ListAsync(resolved, token);
                if (!IsCurrentSession(managerSession, token)) return;
                ForgetLoadedNodes(node.Children);
                node.Children.ReplaceWith(entries
                    .Where(x => x.IsDirectory)
                    .OrderBy(x => DisplayNames.For(x.Name), StringComparer.CurrentCultureIgnoreCase)
                    .Select(x => FolderNode(DisplayNames.For(x.Name), RemotePath.Combine(resolved, x.Name)))
                    .ToList());
                _loadedFolderNodes.Add(node);
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token) && !(ex is OperationCanceledException))
                    node.EndLoadingWithError(Ui.Describe(ex));
            }
        }
    }
}
