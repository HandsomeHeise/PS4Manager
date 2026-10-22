using System;

namespace FileManager.Core.Editing
{
    /// <summary>Eine geöffnete Datei: wo sie auf der PS4 liegt, wo die Arbeitskopie liegt, und womit verglichen wird.</summary>
    public sealed class EditSession
    {
        internal EditSession(string remotePath, string localPath, long baseSize, DateTime baseModified)
        {
            RemotePath = remotePath;
            LocalPath = localPath;
            BaseSize = baseSize;
            BaseModified = baseModified;
        }

        public string RemotePath { get; }
        public string LocalPath { get; }
        public long BaseSize { get; internal set; }
        public DateTime BaseModified { get; internal set; }
        public bool HasPendingChange { get; internal set; }
        internal byte[] BaseContentHash { get; set; }
        internal bool BaseRemoteExists { get; set; }
        internal byte[] ObservedRemoteHash { get; set; }
        internal bool ObservedRemoteExists { get; set; }
        internal long ObservedRemoteSize { get; set; }
        internal DateTime ObservedRemoteModified { get; set; }

        internal DateTime? LastChangeUtc { get; set; }

        /// <summary>Zuletzt gemeldeter lokaler Stand: unveränderte Watcher-Ereignisse werden ignoriert.</summary>
        internal DateTime LocalWriteUtc { get; set; }
        internal long LocalLength { get; set; }
    }
}
