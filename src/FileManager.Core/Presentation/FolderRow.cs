using System;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Eine Zeile der Liste (Details wie Kacheln): Draht-Eintrag, Anzeigename,
    /// Typname (liefert die App aus der Shell), Spiel-ID und — sobald geladen —
    /// Spielname (Spec „Titelnamen": Name statt ID, ID in eigener Spalte).
    /// TitleName setzt nur der UI-Thread.
    /// </summary>
    public sealed class FolderRow
    {
        public FolderRow(string directory, FtpEntry entry, string typeName)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            Entry = entry;
            Item = RemoteItem.From(directory, entry);
            DisplayName = DisplayNames.For(entry.Name);
            TypeName = typeName ?? "";
            TitleId = entry.IsDirectory && global::Ps4.Core.Titles.TitleId.IsMatch(entry.Name) ? entry.Name : null;
        }

        public FtpEntry Entry { get; }
        public RemoteItem Item { get; }
        public string DisplayName { get; }
        public string TypeName { get; }
        public string TitleId { get; }
        public string TitleName { get; set; }

        public bool IsFolder { get { return Entry.IsDirectory; } }
        public bool IsLink { get { return Entry.Kind == FtpEntryKind.Link; } }
        public bool IsTitleFolder { get { return TitleId != null; } }
        public string ShownName { get { return string.IsNullOrEmpty(TitleName) ? DisplayName : TitleName; } }
        public int GroupRank { get { return IsFolder ? 0 : 1; } }

        public SortValue NameCell { get { return new SortValue(ShownName, ShownName); } }
        public SortValue ModifiedCell { get { return new SortValue(SizeFormat.Date(Entry.Modified), Entry.Modified); } }
        public SortValue TypeCell { get { return new SortValue(TypeName, TypeName); } }
        public SortValue SizeCell { get { return IsFolder ? new SortValue("", -1L) : new SortValue(SizeFormat.Bytes(Entry.Size), Entry.Size); } }
        public SortValue TitleIdCell { get { return new SortValue(TitleId ?? "", TitleId ?? ""); } }
    }
}
