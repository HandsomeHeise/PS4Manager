using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;

namespace FileManager.Core.Transfers
{
    public enum TransferKind { Upload, Download, Delete, CreateFolder, Rename, Move, Transaction }

    /// <summary>
    /// Die Phase „wird ermittelt…" (Spec) ist die Laufzeit von
    /// FileOperations.Plan*Async — erst danach existiert ein Auftrag.
    /// </summary>
    public enum JobState { Waiting, Running, Completed, CompletedWithErrors, Cancelled, Interrupted }

    public sealed class TransferJob
    {
        private readonly TaskCompletionSource<TransferJob> completion = new TaskCompletionSource<TransferJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string[] transactionTargets = new string[0];
        public TransferJob(string title,IQueuedTransaction transaction,IEnumerable<string> targets,CancellationToken sessionToken)
            :this(TransferKind.Transaction,title,new[]{new TransferStep(StepKind.Transaction,null,null,null,0)})
        {Transaction=transaction??throw new ArgumentNullException(nameof(transaction));transactionTargets=targets.ToArray();SessionToken=sessionToken;}
        internal IQueuedTransaction Transaction {get;}
        internal CancellationToken SessionToken {get;}
        public Task<TransferJob> Completion {get{return completion.Task;}}
        internal void CompleteAttempt(){completion.TrySetResult(this);}
        public string Phase {get;internal set;}
        public TransferJob(TransferKind kind, string title, IReadOnlyList<TransferStep> steps)
        {
            Kind = kind;
            Title = title;
            Steps = steps;
            State = JobState.Waiting;
        }

        public TransferKind Kind { get; }
        public string Title { get; }
        public IReadOnlyList<TransferStep> Steps { get; }

        public JobState State { get; internal set; }
        /// <summary>Monotonic enqueue/resume identity; presentation must not infer attempts from timing.</summary>
        private int attempt;
        public int Attempt { get { return Volatile.Read(ref attempt); } internal set { Volatile.Write(ref attempt, value); } }
        public bool CanResume { get { return State == JobState.Interrupted && Transaction == null; } }
        public int CurrentIndex { get; internal set; }
        public string InterruptReason { get; internal set; }
        public double BytesPerSecond { get; internal set; }

        internal ConflictChoice? RememberedChoice { get; set; }
        internal CancellationTokenSource Cancellation { get; set; }

        public long TotalBytes
        {
            get { return Steps.Where(s => s.IsTransfer).Sum(s => s.Size); }
        }

        public long DoneBytes
        {
            get
            {
                return Steps.Where(s => s.IsTransfer)
                    .Sum(s => s.State == StepState.Done ? s.Size : s.Transferred);
            }
        }

        public TimeSpan? Remaining
        {
            get
            {
                if (BytesPerSecond <= 0) return null;
                return TimeSpan.FromSeconds(Math.Max(0, TotalBytes - DoneBytes) / BytesPerSecond);
            }
        }

        public IReadOnlyList<TransferStep> Failures
        {
            get { return Steps.Where(s => s.State == StepState.Failed).ToList(); }
        }

        public bool IsFinished
        {
            get
            {
                return State == JobState.Completed || State == JobState.CompletedWithErrors || State == JobState.Cancelled;
            }
        }

        /// <summary>
        /// Bindet eine spätere Remote-Mutation an genau den Inhalt, der zuvor
        /// gesichert oder vom Anwender bestätigt wurde.
        /// </summary>
        public void ProtectRemoteFile(string remotePath, byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(content);
            ProtectRemoteTarget(remotePath, true, hash);
        }

        /// <summary>Verhindert, dass ein nach der Planung erschienenes Ziel kommentarlos ersetzt wird.</summary>
        public void ProtectRemoteAbsence(string remotePath)
        {
            ProtectRemoteTarget(remotePath, false, null);
        }

        private void ProtectRemoteTarget(string remotePath, bool exists, byte[] hash)
        {
            var path = RemotePath.Normalize(remotePath);
            var matches = Steps.Where(s =>
                (s.Kind == StepKind.UploadFile || s.Kind == StepKind.DeleteRemoteFile) &&
                string.Equals(RemotePath.Normalize(s.RemotePath), path, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
                throw new ArgumentException("The job does not mutate the remote file: " + path, nameof(remotePath));

            foreach (var step in matches)
            {
                step.TargetIdentityRequired = true;
                step.ExpectedTargetExists = exists;
                step.ExpectedTargetHash = hash == null ? null : hash.ToArray();
            }
        }

        /// <summary>Ordner, deren Listing nach dem Auftrag veraltet ist (DirectoryCache.Invalidate).</summary>
        public IReadOnlyCollection<string> RemoteFoldersToRefresh
        {
            get
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach(var target in transactionTargets)result.Add(RemotePath.Parent(target));
                foreach (var step in Steps)
                {
                    switch (step.Kind)
                    {
                        case StepKind.UploadFile:
                        case StepKind.CreateRemoteFolder:
                        case StepKind.DeleteRemoteFile:
                        case StepKind.RemoveRemoteFolder:
                            result.Add(RemotePath.Parent(step.RemotePath));
                            break;
                        case StepKind.RenameRemote:
                            result.Add(RemotePath.Parent(step.RemotePath));
                            result.Add(RemotePath.Parent(step.TargetRemotePath));
                            break;
                    }
                }
                return result;
            }
        }

        /// <summary>Teilbäume, die es danach nicht mehr gibt — gelöschte oder verschobene Ordner (DirectoryCache.InvalidateBelow).</summary>
        public IReadOnlyCollection<string> RemoteSubtreesGone
        {
            get
            {
                return Steps
                    .Where(s => s.Kind == StepKind.RemoveRemoteFolder || (s.Kind == StepKind.RenameRemote && s.IsDirectory))
                    .Select(s => RemotePath.Normalize(s.RemotePath))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        /// <summary>„118 von 120 übertragen, 2 fehlgeschlagen" (Spec „Fehler").</summary>
        public string Summary
        {
            get
            {
                if(Transaction!=null)return Phase??State.ToString();
                var counted = Steps.Where(s => s.CountsAsItem).ToList();
                if (counted.Count == 0) counted = Steps.ToList();

                int done = counted.Count(s => s.State == StepState.Done);
                int failed = counted.Count(s => s.State == StepState.Failed);
                int skipped = counted.Count(s => s.State == StepState.Skipped);

                string verb;
                switch (Kind)
                {
                    case TransferKind.Upload:
                    case TransferKind.Download: verb = "transferred"; break;
                    case TransferKind.Delete: verb = "deleted"; break;
                    default: verb = "completed"; break;
                }

                var text = done + " of " + counted.Count + " " + verb;
                if (failed > 0) text += ", " + failed + " failed";
                if (skipped > 0) text += ", " + skipped + " skipped";
                return text;
            }
        }
    }
}
