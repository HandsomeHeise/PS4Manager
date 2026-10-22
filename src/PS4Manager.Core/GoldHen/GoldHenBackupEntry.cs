using System;

namespace PS4Manager.Core.GoldHen
{
    public sealed class GoldHenBackupEntry
    {
        public GoldHenBackupEntry(string directory, string payloadPath, string manifestPath, string source,
            string operation, DateTime createdUtc, long size, string sha256)
        {
            Directory = directory;
            PayloadPath = payloadPath;
            ManifestPath = manifestPath;
            Source = source;
            Operation = operation;
            CreatedUtc = createdUtc;
            Size = size;
            Sha256 = sha256;
        }

        public string Directory { get; private set; }
        public string PayloadPath { get; private set; }
        public string ManifestPath { get; private set; }
        public string Source { get; private set; }
        public string Operation { get; private set; }
        public DateTime CreatedUtc { get; private set; }
        public long Size { get; private set; }
        public string Sha256 { get; private set; }
    }
}
