using System;

namespace FileManager.Core.Shell
{
    /// <summary>Ein Eintrag, wie Explorer ihn sieht: relativer Windows-Name, Größe, Datum — und woher die Bytes kommen.</summary>
    internal sealed class VirtualFile
    {
        public VirtualFile(string relativeName, bool isDirectory, long size, DateTime modified, string remotePath)
        {
            RelativeName = relativeName;
            IsDirectory = isDirectory;
            Size = size;
            Modified = modified;
            RemotePath = remotePath;
        }

        public string RelativeName { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
        public DateTime Modified { get; }
        public string RemotePath { get; }
    }
}
