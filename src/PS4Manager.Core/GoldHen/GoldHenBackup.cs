namespace PS4Manager.Core.GoldHen
{
    /// <summary>Was eine Sicherung angelegt hat — Pfade für die Anzeige, Hash für die Prüfung.</summary>
    public sealed class GoldHenBackup
    {
        public GoldHenBackup(string directory, string payloadPath, string manifestPath, string sha256)
        {
            Directory = directory;
            PayloadPath = payloadPath;
            ManifestPath = manifestPath;
            Sha256 = sha256;
        }

        public string Directory { get; }
        public string PayloadPath { get; }
        public string ManifestPath { get; }
        public string Sha256 { get; }
    }
}
