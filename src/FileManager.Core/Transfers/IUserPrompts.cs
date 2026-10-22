using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Zones;

namespace FileManager.Core.Transfers
{
    public sealed class DeleteSummary
    {
        public DeleteSummary(int fileCount, int folderCount, long totalBytes, Zone zone, IReadOnlyList<string> topLevelNames)
        {
            FileCount = fileCount;
            FolderCount = folderCount;
            TotalBytes = totalBytes;
            Zone = zone;
            TopLevelNames = topLevelNames;
        }

        public int FileCount { get; }
        public int FolderCount { get; }
        public long TotalBytes { get; }

        /// <summary>Warn → der Löschdialog trägt zugleich die Zonenwarnung (ein Dialog, nicht zwei).</summary>
        public Zone Zone { get; }

        public IReadOnlyList<string> TopLevelNames { get; }
    }

    /// <summary>Die Rückfragen, die der Kern stellt; die App zeigt sie als Dialoge.</summary>
    public interface IUserPrompts
    {
        /// <summary>Warnzone: eine Frage pro Auftrag. true = fortfahren.</summary>
        Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct);

        /// <summary>„endgültig löschen: 37 Dateien, 1,2 GB" — die PS4 hat keinen Papierkorb.</summary>
        Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct);
    }
}
