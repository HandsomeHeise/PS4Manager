using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FileManager.Core.Transfers;

namespace PS4Manager.App.Notifications
{
    internal sealed class TransferNotificationTracker
    {
        private sealed class Seen { public int Attempt = -1; }
        private readonly ConditionalWeakTable<TransferJob, Seen> attempts = new ConditionalWeakTable<TransferJob, Seen>();
        public bool Observe(TransferJob job, TransferNotificationSnapshot snapshot)
        {
            if (!snapshot.Reportable) return false;
            var seen = attempts.GetOrCreateValue(job);
            if (seen.Attempt == snapshot.Attempt) return false;
            seen.Attempt = snapshot.Attempt; return true;
        }
        public void Seed(IEnumerable<TransferJob> jobs)
        { foreach (var job in jobs) Observe(job, TransferNotificationSnapshot.Capture(job)); }
    }
}
