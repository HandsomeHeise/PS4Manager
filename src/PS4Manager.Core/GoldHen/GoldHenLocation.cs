using System.Collections.Generic;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>Ein geprüfter Ort auf der Konsole. Fehlt er, ist Exists false — kein Fehler.</summary>
    public sealed class GoldHenLocation
    {
        public GoldHenLocation(GoldHenSection section, string path, bool exists, IReadOnlyList<RemoteItem> entries)
        {
            Section = section;
            Path = path;
            Exists = exists;
            Entries = entries ?? new RemoteItem[0];
        }

        public GoldHenSection Section { get; }
        public string Path { get; }
        public bool Exists { get; }
        public IReadOnlyList<RemoteItem> Entries { get; }
    }
}
