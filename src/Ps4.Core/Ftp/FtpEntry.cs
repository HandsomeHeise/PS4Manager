using System;

namespace Ps4.Core.Ftp
{
    public enum FtpEntryKind { File, Directory, Link }

    /// <summary>
    /// Ein Eintrag aus einem LIST-Ergebnis. Modified trägt bei GoldHEN nur das
    /// Datum (die Konsole liefert keine Uhrzeit, Spec „LIST-Format");
    /// DateTime.MinValue heißt „nicht lesbar".
    /// </summary>
    public sealed class FtpEntry
    {
        public FtpEntry(string name, bool isDirectory, long size)
            : this(name, isDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, size, DateTime.MinValue, null)
        {
        }

        public FtpEntry(string name, FtpEntryKind kind, long size, DateTime modified, string linkTarget)
        {
            Name = name;
            Kind = kind;
            Size = size;
            Modified = modified;
            LinkTarget = kind == FtpEntryKind.Link ? linkTarget : null;
        }

        public string Name { get; }
        public FtpEntryKind Kind { get; }
        public bool IsDirectory { get { return Kind == FtpEntryKind.Directory; } }
        public long Size { get; }
        public DateTime Modified { get; }
        public string LinkTarget { get; }
    }
}
