using System;
using System.Collections.Generic;
using System.Linq;
using FileManager.App.Dialogs;

namespace FileManager.App.Notifications
{
    /// <summary>UI-thread model; pending messages never age and storage stays bounded.</summary>
    public sealed class NotificationQueue
    {
        private sealed class Entry
        {
            public Entry(DesktopNotification notice) { Notice = notice; }
            public DesktopNotification Notice;
            public TimeSpan Elapsed;
        }
        private readonly List<Entry> visible = new List<Entry>(), pending = new List<Entry>();
        private readonly Action openOverflow;
        private Entry overflow;
        private int capacity;
        public NotificationQueue(Action openOverflowDetails) { openOverflow = openOverflowDetails; }
        public IReadOnlyList<DesktopNotification> Visible => visible.Select(e => e.Notice).ToArray();
        public IReadOnlyList<DesktopNotification> Pending => pending.Select(e => e.Notice).ToArray();
        public int OverflowCount { get; private set; }

        public void Add(DesktopNotification notice)
        {
            if (notice == null) throw new ArgumentNullException(nameof(notice));
            if (visible.Any(e => e.Notice.Id == notice.Id) || pending.Any(e => e.Notice.Id == notice.Id)) return;
            pending.Add(new Entry(notice)); Promote(); BoundPending();
        }
        public void Dismiss(Guid id)
        {
            visible.RemoveAll(e => e.Notice.Id == id); pending.RemoveAll(e => e.Notice.Id == id);
            if (overflow != null && overflow.Notice.Id == id) { overflow = null; OverflowCount = 0; }
            Promote();
        }
        public void Advance(TimeSpan elapsed, ISet<Guid> paused)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            foreach (var entry in visible.ToArray()) {
                if (entry.Notice.Kind != MessageKind.Information && entry.Notice.Kind != MessageKind.Success) continue;
                if (paused != null && paused.Contains(entry.Notice.Id)) continue;
                entry.Elapsed += elapsed;
                if (entry.Elapsed >= TimeSpan.FromSeconds(6)) visible.Remove(entry);
            }
            Promote();
        }
        public void SetVisibleCapacity(int count)
        {
            capacity = Math.Max(0, Math.Min(3, count));
            if (visible.Count > capacity) {
                var displaced = visible.Skip(capacity).Where(e => e != overflow).ToArray();
                pending.InsertRange(0, displaced); visible.RemoveRange(capacity, visible.Count - capacity);
            }
            Promote(); BoundPending();
        }
        public void Clear() { visible.Clear(); pending.Clear(); overflow = null; OverflowCount = 0; }
        private void Promote()
        {
            while (visible.Count < capacity && pending.Count > 0) { visible.Add(pending[0]); pending.RemoveAt(0); }
            if (visible.Count < capacity && overflow != null && !visible.Contains(overflow)) visible.Add(overflow);
        }
        private void BoundPending()
        {
            if (pending.Count <= 50) return;
            OverflowCount = (int)Math.Min(int.MaxValue, (long)OverflowCount + pending.Count - 50);
            pending.RemoveRange(50, pending.Count - 50);
            var notice = new DesktopNotification(overflow?.Notice.Id ?? Guid.NewGuid(), "More results",
                OverflowCount + " additional results. Open Transfers for details.", MessageKind.Warning, null, openOverflow);
            if (overflow == null) overflow = new Entry(notice); else overflow.Notice = notice;
            Promote();
        }
    }
}
