using System.Threading;
using System.Threading.Tasks;

namespace FileManager.Core.Transfers
{
    public enum ConflictChoice { Replace, Skip, KeepBoth }

    public sealed class ConflictInfo
    {
        public ConflictInfo(string targetPath, bool targetIsRemote, long existingSize, long newSize, string keepBothName)
        {
            TargetPath = targetPath;
            TargetIsRemote = targetIsRemote;
            ExistingSize = existingSize;
            NewSize = newSize;
            KeepBothName = keepBothName;
        }

        public string TargetPath { get; }
        public bool TargetIsRemote { get; }
        public long ExistingSize { get; }
        public long NewSize { get; }

        /// <summary>Der Name, den „Beide behalten" vergäbe — der Dialog zeigt ihn an.</summary>
        public string KeepBothName { get; }
    }

    public sealed class ConflictDecision
    {
        public ConflictDecision(ConflictChoice choice, bool applyToAll)
        {
            Choice = choice;
            ApplyToAll = applyToAll;
        }

        public ConflictChoice Choice { get; }

        /// <summary>„Für alle weiteren Konflikte dieses Auftrags übernehmen".</summary>
        public bool ApplyToAll { get; }
    }

    /// <summary>Die App zeigt den Konfliktdialog; die Warteschlange wartet so lange.</summary>
    public interface IConflictResolver
    {
        Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct);
    }
}
