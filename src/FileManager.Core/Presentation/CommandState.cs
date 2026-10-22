using System;
using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Zones;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Welche Befehle gerade gehen (Spec „in gesperrten Ordnern sind
    /// Schreibbefehle ausgegraut"). Nur Ausgrauen — durchgesetzt wird im Kern.
    /// </summary>
    public sealed class CommandState
    {
        public bool CanUpload { get; private set; }
        public bool CanNewFolder { get; private set; }
        public bool CanPaste { get; private set; }
        public bool CanDownload { get; private set; }
        public bool CanCopy { get; private set; }
        public bool CanOpen { get; private set; }
        public bool CanRename { get; private set; }
        public bool CanDelete { get; private set; }
        public bool CanCut { get; private set; }

        public static CommandState For(bool connected, Zone folderZone, IReadOnlyList<FolderRow> selection, Func<string, Zone> zoneOf)
        {
            if (zoneOf == null) throw new ArgumentNullException(nameof(zoneOf));
            var rows = selection ?? new FolderRow[0];
            bool writableFolder = connected && folderZone != Zone.Locked;
            bool any = connected && rows.Count > 0;
            bool single = connected && rows.Count == 1;
            bool noneLocked = rows.All(r => zoneOf(r.Item.Path) != Zone.Locked);

            return new CommandState
            {
                CanUpload = writableFolder,
                CanNewFolder = writableFolder,
                CanPaste = writableFolder,
                CanDownload = any && rows.All(r => !r.IsLink),
                CanCopy = any && rows.All(r => !r.IsLink),
                CanOpen = single && !rows[0].IsFolder && !rows[0].IsLink,
                CanRename = single && !rows[0].IsTitleFolder && noneLocked,
                CanDelete = any && noneLocked,
                CanCut = any && noneLocked && rows.All(r => !r.IsLink)
            };
        }
    }
}
