using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;

namespace FileManager.Core.Presentation
{
    public enum ClipMode { None, Copy, Cut }

    public enum PasteAction { Nothing, UploadFiles, MoveItems, RefuseCopyWithinPs4 }

    public enum DropAction { None, Upload, Move }

    /// <summary>
    /// Was Strg+V und Ablegen bedeuten (Spec „Drag &amp; Drop / Zwischenablage"):
    /// Wer die Zwischenablage zuletzt gefüllt hat, gewinnt — wie im Explorer.
    /// Kopieren innerhalb der PS4 gibt es in v1 nicht.
    /// </summary>
    public static class PastePlanner
    {
        public static PasteAction Decide(ClipMode ourMode, bool weOwnClipboard, bool clipboardHasFiles)
        {
            if (weOwnClipboard && ourMode == ClipMode.Cut) return PasteAction.MoveItems;
            if (weOwnClipboard && ourMode == ClipMode.Copy) return PasteAction.RefuseCopyWithinPs4;
            if (clipboardHasFiles) return PasteAction.UploadFiles;
            return PasteAction.Nothing;
        }

        public static DropAction ForDrop(bool internalDrag, bool hasFileDrop, Zone targetZone)
        {
            if (targetZone == Zone.Locked) return DropAction.None;
            if (internalDrag) return DropAction.Move;
            if (hasFileDrop) return DropAction.Upload;
            return DropAction.None;
        }

        /// <summary>Ein Ordner unter dem Zeiger ist das Ziel, sonst der offene Ordner. Links und Dateien sind kein Ziel.</summary>
        public static string DropFolder(FolderRow row, string currentFolder)
        {
            return row != null && row.IsFolder ? row.Item.Path : currentFolder;
        }

        public static bool IsUsefulMove(IReadOnlyList<RemoteItem> items, string targetFolder)
        {
            var target = RemotePath.Normalize(targetFolder);
            if (items.Any(i => i.IsDirectory && RemotePath.IsSameOrBelow(target, i.Path))) return false;
            return items.Any(i => RemotePath.Parent(i.Path) != target);
        }
    }
}
