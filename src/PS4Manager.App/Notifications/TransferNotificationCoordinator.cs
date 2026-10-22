using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using FileManager.Core.Transfers;

namespace PS4Manager.App.Notifications
{
    /// <summary>Presentation-only adapter. Claims must be registered before a job can run.</summary>
    internal sealed class TransferNotificationCoordinator : IDisposable
    {
        private sealed class ClaimRule { public bool SuccessOnly; }
        private readonly object sync = new object();
        private readonly Action<DesktopNotification> show, archived;
        private readonly Action<DesktopNotification, Action> currentDetails;
        private readonly Action clear, openTransfers;
        private readonly Action<Action> dispatch;
        private readonly ConditionalWeakTable<TransferJob, ClaimRule> claims = new ConditionalWeakTable<TransferJob, ClaimRule>();
        private TransferNotificationTracker tracker = new TransferNotificationTracker();
        private TransferQueue queue;
        private int generation;
        private bool disposed;
        public TransferNotificationCoordinator(DesktopNotificationService service, Action<Action> dispatch, Action openTransfers)
            : this(service.Show, service.Clear, dispatch, openTransfers,
                  n => MessageDialog.ShowMessage(null, n.Title, n.Message, n.Kind, n.Details), ShowResultDetails) { }
        internal TransferNotificationCoordinator(Action<DesktopNotification> show, Action clear, Action<Action> dispatch, Action openTransfers, Action<DesktopNotification> archived,
            Action<DesktopNotification, Action> currentDetails = null)
        { this.show = show; this.clear = clear; this.dispatch = dispatch; this.openTransfers = openTransfers; this.archived = archived; this.currentDetails = currentDetails ?? ((n, navigate) => archived(n)); }
        private static void ShowResultDetails(DesktopNotification notice, Action navigate)
        {
            using (var dialog = new MessageDialog(notice.Title, notice.Message,
                new[] { "Open Transfers", "Close" }, 1, 1, null, notice.Kind, notice.Details)) {
                dialog.ShowDialog();
                if (dialog.Choice == 0) navigate();
            }
        }
        public void Attach(TransferQueue next)
        {
            int current;
            lock (sync) {
                if (disposed || ReferenceEquals(queue,next)) return;
                if (queue != null) queue.JobChanged -= Changed;
                queue = next; current = ++generation; tracker = new TransferNotificationTracker();
                if (queue != null) { tracker.Seed(queue.Jobs); queue.JobChanged += Changed; }
            }
            Post(current, clear);
        }
        public void Claim(TransferJob job, bool successOnly)
        { lock(sync) { if (disposed) return; claims.Remove(job); claims.Add(job, new ClaimRule { SuccessOnly = successOnly }); } }
        public void ShowForQueue(TransferQueue expectedQueue, DesktopNotification notice)
        {
            int current;
            lock(sync) { if (disposed || expectedQueue == null || !ReferenceEquals(queue,expectedQueue)) return; current = generation; }
            Post(current, () => show(notice));
        }
        private void Changed(object sender, TransferJob job)
        {
            try {
                int current; TransferNotificationSnapshot snapshot;
                lock(sync) {
                    if (disposed || !ReferenceEquals(sender,queue)) return;
                    snapshot = TransferNotificationSnapshot.Capture(job);
                    if (!tracker.Observe(job,snapshot)) return;
                    ClaimRule claim;
                    if (claims.TryGetValue(job,out claim) && (!claim.SuccessOnly || snapshot.Successful)) return;
                    current = generation;
                }
                DesktopNotification notice = null;
                notice = new DesktopNotification(snapshot.Title,snapshot.Message,snapshot.Kind,snapshot.Details,() => {
                    bool same;
                    lock(sync) { if (disposed) return; same = current == generation; }
                    if (same) currentDetails(notice, () => {
                        // The details dialog has its own message loop: the session may
                        // have changed while it was open. Never navigate a replacement.
                        lock(sync) { if (disposed || current != generation) return; }
                        openTransfers();
                    }); else archived(notice);
                });
                Post(current, () => show(notice));
            } catch(Exception ex) { Trace.TraceError("Transfer notification failed: {0}",ex.Message); }
        }
        private void Post(int current, Action action)
        {
            try { dispatch(() => {
                lock(sync) { if(disposed || current != generation) return; }
                try { action(); } catch(Exception ex) { Trace.TraceError("Transfer notification display failed: {0}",ex.Message); }
            }); } catch(Exception ex) { Trace.TraceError("Transfer notification dispatch failed: {0}",ex.Message); }
        }
        public void Dispose()
        { lock(sync) { if(disposed)return; disposed=true; generation++; if(queue!=null)queue.JobChanged-=Changed; queue=null; } }
    }
}
