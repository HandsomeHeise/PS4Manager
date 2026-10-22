using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using FileManager.Core.Transfers;
using PS4Manager.App.Notifications;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    public sealed class TransferNotificationTests
    {
        private static void Set(object target,string name,object value) { target.GetType().GetProperty(name).SetValue(target,value); }
        [Fact] public async Task Resume_during_snapshot_cannot_consume_the_new_attempt()
        {
            using(var scenario=new ModScenario()) {
                var step=new TransferStep(StepKind.UploadFile,"/data/a",null,null,1);
                Set(step,"State",StepState.Done);
                var steps=new EnumeratingSteps(new[]{step});
                var job=new TransferJob(TransferKind.Upload,"Upload",steps);
                scenario.Queue.Enqueue(job); await scenario.Queue.WhenIdle();
                Set(job,"State",JobState.Interrupted);
                steps.OnEnumerate=()=>scenario.Queue.Resume(job);
                var tracker=new TransferNotificationTracker();
                var mixed=TransferNotificationSnapshot.Capture(job);
                Assert.False(tracker.Observe(job,mixed));
                await scenario.Queue.WhenIdle();
                Assert.Equal(2,job.Attempt);
                Assert.True(tracker.Observe(job,TransferNotificationSnapshot.Capture(job)));
            }
        }
        private sealed class EnumeratingSteps : IReadOnlyList<TransferStep>
        {
            private readonly TransferStep[] items;
            public Action OnEnumerate;
            public EnumeratingSteps(TransferStep[] items) { this.items=items; }
            public int Count=>items.Length;
            public TransferStep this[int index]=>items[index];
            public IEnumerator<TransferStep> GetEnumerator() {
                Interlocked.Exchange(ref OnEnumerate,null)?.Invoke();
                return ((IEnumerable<TransferStep>)items).GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [Fact] public void Snapshot_preserves_failure_after_mutable_job_changes()
        {
            var step=new TransferStep(StepKind.UploadFile,"/data/a",null,null,1);
            Set(step,"State",StepState.Failed); Set(step,"Error","Write failed");
            var job=new TransferJob(TransferKind.Upload,"Upload",new[]{step}); Set(job,"State",JobState.CompletedWithErrors);
            var snapshot=TransferNotificationSnapshot.Capture(job);
            Set(step,"State",StepState.Done); Set(step,"Error",null); Set(job,"State",JobState.Running);
            Assert.Equal(MessageKind.Error,snapshot.Kind); Assert.Contains("Write failed",snapshot.Details); Assert.True(snapshot.Reportable);
        }
        [Fact] public void Fast_resume_does_not_need_a_visible_running_event()
        {
            var job=new TransferJob(TransferKind.Upload,"Upload",new TransferStep[0]);
            var tracker=new TransferNotificationTracker();
            Set(job,"State",JobState.Interrupted); Set(job,"Attempt",1);
            Assert.True(tracker.Observe(job,TransferNotificationSnapshot.Capture(job)));
            Assert.False(tracker.Observe(job,TransferNotificationSnapshot.Capture(job)));
            Set(job,"Attempt",2); Set(job,"State",JobState.Completed);
            Assert.True(tracker.Observe(job,TransferNotificationSnapshot.Capture(job)));
            Assert.False(tracker.Observe(job,TransferNotificationSnapshot.Capture(job)));
        }
        [Theory][InlineData(JobState.Waiting,false)][InlineData(JobState.Running,false)][InlineData(JobState.Cancelled,false)][InlineData(JobState.Completed,true)][InlineData(JobState.Interrupted,true)][InlineData(JobState.CompletedWithErrors,true)]
        public void Only_results_are_reported(JobState state,bool reportable)
        {
            var job=new TransferJob(TransferKind.Upload,"Upload",new TransferStep[0]); Set(job,"State",state);
            Assert.Equal(reportable,TransferNotificationSnapshot.Capture(job).Reportable);
        }
        [Theory][InlineData(TransferKind.Upload)][InlineData(TransferKind.Delete)]
        public async Task Multiple_manager_job_kinds_share_one_notification_path(TransferKind kind)
        {
            using(var scenario=new ModScenario()) {
                var shown=new List<DesktopNotification>();
                using(var coordinator=new TransferNotificationCoordinator(shown.Add,()=>{},a=>a(),()=>{},n=>{})) {
                    coordinator.Attach(scenario.Queue);
                    var job=new TransferJob(kind,"Job",new TransferStep[0]); scenario.Queue.Enqueue(job); await scenario.Queue.WhenIdle();
                    Assert.Equal(MessageKind.Success,Assert.Single(shown).Kind);
                }
            }
        }
        [Fact] public async Task Old_queued_results_are_dropped_and_old_details_do_not_navigate()
        {
            using(var first=new ModScenario()) using(var second=new ModScenario()) {
                var posted=new List<Action>(); var shown=new List<DesktopNotification>(); int navigated=0, archived=0;
                using(var coordinator=new TransferNotificationCoordinator(shown.Add,()=>shown.Clear(),a=>{lock(posted)posted.Add(a);},()=>navigated++,n=>archived++)) {
                    coordinator.Attach(first.Queue); Drain(posted);
                    first.Queue.Enqueue(new TransferJob(TransferKind.Upload,"Old",new TransferStep[0])); await first.Queue.WhenIdle(); Drain(posted);
                    var old=Assert.Single(shown);
                    first.Queue.Enqueue(new TransferJob(TransferKind.Upload,"Late",new TransferStep[0])); await first.Queue.WhenIdle();
                    coordinator.Attach(second.Queue); Drain(posted); Assert.Empty(shown);
                    old.OpenDetails(); Assert.Equal(0,navigated); Assert.Equal(1,archived);
                    coordinator.Dispose(); coordinator.ShowForQueue(second.Queue,new DesktopNotification("Late","",MessageKind.Error)); Drain(posted); Assert.Empty(shown);
                }
            }
        }
        [Fact] public async Task Historical_results_are_not_replayed_and_claim_is_registered_before_start()
        {
            using(var scenario=new ModScenario()) {
                scenario.Queue.Enqueue(new TransferJob(TransferKind.Upload,"Historical",new TransferStep[0])); await scenario.Queue.WhenIdle();
                var shown=new List<DesktopNotification>();
                using(var coordinator=new TransferNotificationCoordinator(shown.Add,()=>{},a=>a(),()=>{},n=>{})) {
                    coordinator.Attach(scenario.Queue); Assert.Empty(shown);
                    var job=new TransferJob(TransferKind.Upload,"Owned",new TransferStep[0]); coordinator.Claim(job,true);
                    scenario.Queue.Enqueue(job); await scenario.Queue.WhenIdle(); Assert.Empty(shown);
                }
            }
        }
        [Theory][InlineData(false)][InlineData(true)]
        public async Task Details_keeps_all_full_paths_after_clear_or_resume(bool resume)
        {
            using(var scenario=new ModScenario()) {
                var shown=new List<DesktopNotification>(); var details=new List<DesktopNotification>(); int navigated=0;
                using(var coordinator=new TransferNotificationCoordinator(shown.Add,()=>{},a=>a(),()=>navigated++,details.Add)) {
                    coordinator.Attach(scenario.Queue);
                    var steps=Enumerable.Range(0,25).Select(i=> {
                        var step=new TransferStep(StepKind.UploadFile,"/data/"+new string('a',1200)+"/file"+i,null,null,1);
                        Set(step,"State",StepState.Failed); Set(step,"Error","original error "+i); return step;
                    }).ToArray();
                    var job=new TransferJob(TransferKind.Upload,"Upload",steps);
                    scenario.Queue.Enqueue(job); await scenario.Queue.WhenIdle();
                    var notice=Assert.Single(shown);
                    if(resume) {
                        Set(job,"State",JobState.Interrupted);
                        foreach(var step in steps) { Set(step,"State",StepState.Done); Set(step,"Error",null); }
                        scenario.Queue.Resume(job); await scenario.Queue.WhenIdle();
                    } else scenario.Queue.ClearFinished();
                    notice.OpenDetails();
                    var saved=Assert.Single(details);
                    Assert.Equal(0,navigated);
                    foreach(var step in steps) Assert.Contains(step.RemotePath,saved.Details);
                    Assert.Contains("original error 24",saved.Details);
                }
            }
        }
        [Fact] public async Task Details_navigation_is_explicit_and_rechecks_session_after_dialog()
        {
            using(var first=new ModScenario()) using(var second=new ModScenario()) {
                var shown=new List<DesktopNotification>(); Action navigate=null; int navigated=0;
                using(var coordinator=new TransferNotificationCoordinator(shown.Add,()=>{},a=>a(),()=>navigated++,n=>{},
                    (notice,open)=>navigate=open)) {
                    coordinator.Attach(first.Queue);
                    first.Queue.Enqueue(new TransferJob(TransferKind.Upload,"Result",new TransferStep[0])); await first.Queue.WhenIdle();
                    Assert.Single(shown).OpenDetails();
                    Assert.Equal(0,navigated); Assert.NotNull(navigate);
                    navigate(); Assert.Equal(1,navigated);
                    coordinator.Attach(second.Queue);
                    navigate(); Assert.Equal(1,navigated);
                }
            }
        }
        [Fact] public async Task Presentation_exception_cannot_fail_transfer()
        {
            using(var scenario=new ModScenario()) using(var coordinator=new TransferNotificationCoordinator(n=>throw new InvalidOperationException(),()=>{},a=>a(),()=>{},n=>{})) {
                coordinator.Attach(scenario.Queue); var job=new TransferJob(TransferKind.Upload,"Job",new TransferStep[0]);
                scenario.Queue.Enqueue(job); await scenario.Queue.WhenIdle(); Assert.Equal(JobState.Completed,job.State);
            }
        }
        private static void Drain(List<Action> posted) { Action[] actions; lock(posted) { actions=posted.ToArray(); posted.Clear(); } foreach(var action in actions)action(); }
    }
}
