using PS4Manager.UI;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using Ps4.Core.Titles;
using TreeNode = PS4Manager.UI.NavigationNode;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        partial void WireTitles()
        {
            FolderShown += () =>
            {
                var managerSession = _managerSession;
                if (managerSession == null) return;
                var ct = _navCts != null ? _navCts.Token : managerSession.CancellationToken;
                foreach (var row in _rows.Where(r => r.IsTitleFolder).ToList())
                {
                    var _ = ApplyTitleAsync(managerSession, row, ct);
                }
            };
        }

        partial void DecorateFolderNode(TreeNode node)
        {
            var name = RemotePath.Name(node.Tag as string ?? "");
            if (!TitleId.IsMatch(name)) return;
            var _ = DecorateNodeAsync(node, name);
        }

        private async Task ApplyTitleAsync(PS4Manager.Core.Session.ManagerSession managerSession,
            FolderRow row, CancellationToken ct)
        {
            var info = await LoadTitleAsync(managerSession, row.TitleId, ct);
            if (ct.IsCancellationRequested || !IsCurrentSession(managerSession, managerSession.CancellationToken)
                || info == null || !_rows.Contains(row)) return;      // inzwischen weg navigiert

            row.TitleName = info.HasRealName ? info.Name : null;
            _grid.Invalidate();
            var item = _gallery.Gallery.Groups.SelectMany(g => g.Items).FirstOrDefault(i => ReferenceEquals(i.Tag, row));
            if (item != null)
            {
                item.Caption = row.ShownName;
                item.Image = IconFor(row, true);
            }
        }

        private async Task DecorateNodeAsync(TreeNode node, string titleId)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var info = await LoadTitleAsync(managerSession, titleId, token);
            if (!IsCurrentSession(managerSession, token) || info == null) return;
            if (info.HasRealName) node.Text = info.Name;
            Image icon;
            if (_titleIcons.TryGetValue(titleId, out icon)) node.Image = icon;
        }

        /// <summary>
        /// Titel über den Katalog der Sitzung (Browse-Tor, höchstens zwei
        /// gleichzeitig; Ergebnisse für die Sitzung gemerkt). Das Icon wird einmal
        /// je Spiel dekodiert und gehört _titleIcons.
        /// </summary>
        private async Task<TitleInfo> LoadTitleAsync(PS4Manager.Core.Session.ManagerSession managerSession,
            string titleId, CancellationToken ct)
        {
            if (!IsCurrentSession(managerSession, managerSession.CancellationToken)) return null;
            var session = managerSession.Explorer;
            var sessionToken = managerSession.CancellationToken;

            TitleInfo info;
            if (!session.Titles.TryGet(titleId, out info))
            {
                try
                {
                    info = await session.Titles.GetAsync(titleId, ct);
                }
                catch (Exception)
                {
                    return null;                                   // Abbruch oder Verbindung — der Name bleibt die ID
                }
                if (ct.IsCancellationRequested || !IsCurrentSession(managerSession, sessionToken)) return null;
            }

            if (ct.IsCancellationRequested || !IsCurrentSession(managerSession, sessionToken)) return null;
            if (info.IconPng != null && !_titleIcons.ContainsKey(titleId))
            {
                var image = Decode(info.IconPng);
                if (image != null) _titleIcons[titleId] = image;
            }
            return info;
        }

        private static Image Decode(byte[] png)
        {
            try
            {
                using (var stream = new MemoryStream(png))
                using (var decoded = Image.FromStream(stream))
                {
                    return new Bitmap(decoded);
                }
            }
            catch (Exception)
            {
                return null;                                       // kaputtes Icon — Ordnersymbol bleibt
            }
        }

        /// <summary>
        /// Gehört der Sitzung. Der Aufrufer entfernt vorher alle Zeilen und
        /// Baumknoten, damit kein Control ein bereits entsorgtes Bild behält.
        /// </summary>
        private void ClearTitleIcons()
        {
            foreach (var image in _titleIcons.Values) image.Dispose();
            _titleIcons.Clear();
        }
    }
}
