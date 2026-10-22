using System;
using System.Collections.Generic;
using System.Linq;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    public sealed class NotificationQueueTests
    {
        [Theory][InlineData(MessageKind.Information,true)][InlineData(MessageKind.Success,true)][InlineData(MessageKind.Warning,false)][InlineData(MessageKind.Error,false)]
        public void Only_transient_messages_expire_after_six_visible_seconds(MessageKind kind, bool expires)
        {
            var q = new NotificationQueue(null); q.SetVisibleCapacity(3);
            var n = new DesktopNotification("Title", "Message",kind); q.Add(n);
            q.Advance(TimeSpan.FromMilliseconds(5999),new HashSet<Guid>()); Assert.Single(q.Visible);
            q.Advance(TimeSpan.FromSeconds(30),new HashSet<Guid>{n.Id}); Assert.Single(q.Visible);
            q.Advance(TimeSpan.FromMilliseconds(1),new HashSet<Guid>()); Assert.Equal(expires ? 0 : 1,q.Visible.Count);
        }
        [Fact] public void Pending_items_do_not_age_and_promote_in_order()
        {
            var q = new NotificationQueue(null); q.SetVisibleCapacity(1);
            var first = new DesktopNotification("First","",MessageKind.Error);
            var next = new DesktopNotification("Next","",MessageKind.Success);
            q.Add(first); q.Add(next); q.Advance(TimeSpan.FromHours(2),new HashSet<Guid>());
            q.Dismiss(first.Id); Assert.Equal(next.Id,Assert.Single(q.Visible).Id);
            q.Advance(TimeSpan.FromSeconds(5),new HashSet<Guid>()); Assert.Single(q.Visible);
        }
        [Fact] public void Flood_is_bounded_and_overflow_is_a_persistent_summary()
        {
            int opened=0; var q = new NotificationQueue(()=>opened++); q.SetVisibleCapacity(3);
            for(int i=0;i<1000;i++) q.Add(new DesktopNotification("Item "+i,"",MessageKind.Success));
            Assert.Equal(3,q.Visible.Count); Assert.Equal(50,q.Pending.Count); Assert.Equal(947,q.OverflowCount);
            for(int i=0;i<53;i++) q.Dismiss(q.Visible.First().Id);
            var summary=Assert.Single(q.Visible); Assert.Equal(MessageKind.Warning,summary.Kind);
            summary.OpenDetails(); Assert.Equal(1,opened);
            q.Advance(TimeSpan.FromHours(1),new HashSet<Guid>()); Assert.Single(q.Visible);
            q.Dismiss(summary.Id); Assert.Equal(0,q.OverflowCount); Assert.Empty(q.Visible);
        }
        [Fact] public void Capacity_reduction_preserves_remaining_lifetime_and_clear_removes_all()
        {
            var q=new NotificationQueue(null); q.SetVisibleCapacity(3);
            var notices=Enumerable.Range(0,3).Select(i=>new DesktopNotification(i.ToString(),"",MessageKind.Success)).ToArray();
            foreach(var n in notices) q.Add(n);
            q.Advance(TimeSpan.FromSeconds(5),new HashSet<Guid>()); q.SetVisibleCapacity(0);
            Assert.Empty(q.Visible); q.Advance(TimeSpan.FromHours(1),new HashSet<Guid>());
            q.SetVisibleCapacity(1); Assert.Equal(notices[0].Id,Assert.Single(q.Visible).Id);
            q.Advance(TimeSpan.FromSeconds(1),new HashSet<Guid>()); Assert.Equal(notices[1].Id,Assert.Single(q.Visible).Id);
            q.Clear(); Assert.Empty(q.Visible); Assert.Empty(q.Pending); Assert.Equal(0,q.OverflowCount);
        }
    }
}
