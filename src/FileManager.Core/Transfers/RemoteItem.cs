using System;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Ein ausgewählter Eintrag auf der Konsole — mit vollem Pfad statt nur
    /// Namen und mit seiner Eintragsart: ein Link ist weder Datei noch Ordner
    /// (Plan-1-Übergabe „Links"; Zonen-Umgehung über /data/x -> /system).
    /// </summary>
    public sealed class RemoteItem
    {
        public RemoteItem(string path, bool isDirectory, long size, DateTime modified)
            : this(path, isDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, size, modified, null)
        {
        }

        public RemoteItem(string path, FtpEntryKind kind, long size, DateTime modified, string linkTarget)
        {
            Path = RemotePath.Normalize(path);
            Kind = kind;
            Size = size;
            Modified = modified;
            LinkTarget = kind == FtpEntryKind.Link ? linkTarget : null;
        }

        public string Path { get; }
        public string Name { get { return RemotePath.Name(Path); } }
        public FtpEntryKind Kind { get; }
        public bool IsDirectory { get { return Kind == FtpEntryKind.Directory; } }
        public bool IsLink { get { return Kind == FtpEntryKind.Link; } }
        public string LinkTarget { get; }
        public long Size { get; }
        public DateTime Modified { get; }

        public static RemoteItem From(string directory, FtpEntry entry)
        {
            return new RemoteItem(RemotePath.Combine(directory, entry.Name), entry.Kind, entry.Size, entry.Modified, entry.LinkTarget);
        }
    }
}
