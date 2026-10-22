using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;

namespace FileManager.Core.Tests.Support
{
    /// <summary>Antwortet der Reihe nach; ist Hold gesetzt, wartet jede Antwort darauf.</summary>
    internal sealed class ScriptedResolver : IConflictResolver
    {
        public readonly Queue<ConflictDecision> Answers = new Queue<ConflictDecision>();
        public readonly List<ConflictInfo> Asked = new List<ConflictInfo>();
        public TaskCompletionSource<bool> Hold;

        public async Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
        {
            lock (Asked) Asked.Add(conflict);
            var hold = Hold;
            if (hold != null) await hold.Task.ConfigureAwait(false);
            lock (Answers) return Answers.Dequeue();
        }

        public ScriptedResolver Then(ConflictChoice choice, bool applyToAll = false)
        {
            Answers.Enqueue(new ConflictDecision(choice, applyToAll));
            return this;
        }
    }
}
