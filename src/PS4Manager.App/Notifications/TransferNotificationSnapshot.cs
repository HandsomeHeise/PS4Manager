using System.Linq;
using FileManager.App.Dialogs;
using FileManager.Core.Transfers;

namespace PS4Manager.App.Notifications
{
    internal sealed class TransferNotificationSnapshot
    {
        private TransferNotificationSnapshot() { }
        public int Attempt { get; private set; }
        public JobState State { get; private set; }
        public string Title { get; private set; }
        public string Message { get; private set; }
        public string Details { get; private set; }
        public MessageKind Kind { get; private set; }
        public bool Reportable => State == JobState.Completed || State == JobState.CompletedWithErrors || State == JobState.Interrupted;
        public bool Successful => State == JobState.Completed && Kind == MessageKind.Success;
        public static TransferNotificationSnapshot Capture(TransferJob job)
        {
            var attempt = job.Attempt;
            var state = job.State;
            var failures = job.Failures.Select(f => (f.RemotePath ?? f.LocalPath ?? "File") + ": " + f.Error).ToArray();
            var reason = job.InterruptReason;
            var snapshot = new TransferNotificationSnapshot {
                Attempt = attempt, State = state, Title = job.Title,
                Message = state == JobState.Interrupted ? "Interrupted. Open Transfers to inspect or resume." : job.Summary,
                Details = string.Join("\r\n", new[] { reason }.Where(s => !string.IsNullOrEmpty(s)).Concat(failures)),
                Kind = failures.Length > 0 || state == JobState.CompletedWithErrors ? MessageKind.Error : state == JobState.Interrupted ? MessageKind.Warning : MessageKind.Success
            };
            // Resume can run while result text is copied. Never report a mixed attempt;
            // its own terminal event will supply a consistent snapshot later.
            if (job.Attempt != attempt) snapshot.State = JobState.Waiting;
            return snapshot;
        }
    }
}
