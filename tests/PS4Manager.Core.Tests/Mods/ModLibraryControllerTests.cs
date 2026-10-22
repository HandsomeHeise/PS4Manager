using System;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModLibraryControllerTests
    {
        [Theory][InlineData(false)][InlineData(true)]
        public async Task Registration_precedes_every_job_event_and_cannot_fail_install(bool throws)
        {
            using(var s=new ModScenario()) using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,CancellationToken.None,
                ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),
                (g,p,ct)=>Task.FromResult(new ModCatalogPage{Items=new[]{s.Release}}),
                (id,ct)=>Task.FromResult(new ModDetail()),(id,ct)=>Task.FromResult(s.Release))) {
                await c.SelectGameAsync(new ModGame{TitleId="CUSA00411",Version="1.57"}); await c.SelectModAsync(s.Release);
                var plan=await c.PrepareInstallAsync(); bool registered=false,tooEarly=false;
                s.Queue.JobChanged+=(sender,job)=>{ if(!registered)tooEarly=true; };
                var result=c.ConfirmAndEnqueue(plan,true,true,job=>{registered=true;if(throws)throw new InvalidOperationException("UI only");});
                await result.Completion; Assert.True(registered);Assert.False(tooEarly);Assert.Equal(FileManager.Core.Transfers.JobState.Completed,result.State);
            }
        }
        [Fact] public async Task Explicit_activation_is_tracked_as_a_separate_shared_queue_job()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent("/data/GoldHEN/plugins/Luma.prx");s.Package("plugin","/data/GoldHEN/plugins/Luma.prx");var id=await s.Install();
                using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,CancellationToken.None,
                    ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),(g,p,ct)=>Task.FromResult(new ModCatalogPage()),
                    (mod,ct)=>Task.FromResult(new ModDetail()),(release,ct)=>Task.FromResult(s.Release))){
                    var job=c.ActivatePlugins(id,new PS4Manager.Core.GoldHen.GoldHenBackupWriter(System.IO.Path.Combine(s.Root,"goldhen"),FileManager.Core.Transfers.SystemClock.Instance));
                    Assert.Same(job,c.State.Job);await job.Completion;
                    Assert.Equal(FileManager.Core.Transfers.JobState.Completed,job.State);Assert.Equal(2,s.Queue.Jobs.Count);
                    Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,id).State);
                }
            }
        }
        [Fact] public async Task Cancellation_at_plan_publication_abandons_decision_and_allows_navigation()
        {
            using(var s=new ModScenario())using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,CancellationToken.None,
                ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),
                (g,p,ct)=>Task.FromResult(new ModCatalogPage{Items=new[]{s.Release}}),
                (id,ct)=>Task.FromResult(new ModDetail()),(id,ct)=>Task.FromResult(s.Release))){
                await c.SelectGameAsync(new ModGame{TitleId="CUSA00411",Version="1.57"});await c.SelectModAsync(s.Release);
                c.Changed+=(o,e)=>{if(c.State.Plan!=null)c.CancelRequest();};
                await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>c.PrepareInstallAsync());
                Assert.Null(c.State.Plan);Assert.False(c.State.CanInstall);Assert.Empty(s.Ftp.Mutations);
                Assert.Equal(ModInstallState.RolledBack,Assert.Single(s.Store.Load(s.Profile.Id)).State);
                await c.RefreshGamesAsync();
            }
        }
        [Fact] public async Task Cancelled_late_catalog_response_cannot_publish_content()
        {
            using(var s=new ModScenario()){
                var late=new TaskCompletionSource<ModCatalogPage>();
                using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,CancellationToken.None,
                    ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),(g,p,ct)=>late.Task,
                    (id,ct)=>Task.FromResult(new ModDetail()),(id,ct)=>Task.FromResult(s.Release))){
                    var pending=c.SelectGameAsync(new ModGame{TitleId="CUSA00411",Version="1.57"});c.CancelRequest();late.SetResult(new ModCatalogPage{Items=new[]{s.Release}});
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);Assert.Null(c.State.Catalog);Assert.False(c.State.Busy);
                }
            }
        }
        [Fact] public async Task Late_catalog_response_cannot_replace_new_game_selection()
        {
            using(var s=new ModScenario()){
                var late=new TaskCompletionSource<ModCatalogPage>();
                using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,CancellationToken.None,
                    ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),
                    (g,p,ct)=>g.TitleId=="CUSA00411"?late.Task:Task.FromResult(new ModCatalogPage{Items=new ModCatalogItem[0]}),
                    (id,ct)=>Task.FromResult(new ModDetail()),(id,ct)=>Task.FromResult(s.Release))){
                    var old=c.SelectGameAsync(new ModGame{TitleId="CUSA00411",Version="1.57"});
                    await c.SelectGameAsync(new ModGame{TitleId="CUSA03041",Version="1.32"});late.SetResult(new ModCatalogPage{Items=new[]{s.Release}});
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>old);
                    Assert.Equal("CUSA03041",c.State.Game.TitleId);Assert.Empty(c.State.Catalog.Items);Assert.False(c.State.CanPrepare);
                }
            }
        }
        [Fact] public async Task Offline_catalog_and_cancelled_session_cannot_enable_install()
        {
            using(var s=new ModScenario())using(var cancel=new CancellationTokenSource()){
                using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,cancel.Token,
                    ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),
                    (g,p,ct)=>Task.FromResult(new ModCatalogPage{Items=new[]{s.Release},IsOffline=true}),
                    (id,ct)=>Task.FromResult(new ModDetail()),(id,ct)=>Task.FromResult(s.Release))){
                    await c.SelectGameAsync(new ModGame{TitleId="CUSA00411",Version="1.57"});await c.SelectModAsync(s.Release);
                    Assert.False(c.State.CanPrepare);await Assert.ThrowsAsync<InvalidOperationException>(()=>c.PrepareInstallAsync());
                    cancel.Cancel();Assert.False(c.State.CanInstall);
                }
            }
        }
        [Fact] public async Task Double_confirmation_cannot_enqueue_twice_and_recovery_works_without_catalog()
        {
            using(var s=new ModScenario()){
                bool offline=false;
                using(var c=new ModLibraryController(s.Planner,s.Store,s.Profile,s.Queue,CancellationToken.None,
                    ct=>Task.FromResult<System.Collections.Generic.IReadOnlyList<ModGame>>(new ModGame[0]),
                    (g,p,ct)=>offline?throw new System.IO.IOException("offline"):Task.FromResult(new ModCatalogPage{Items=new[]{s.Release}}),
                    (id,ct)=>Task.FromResult(new ModDetail()),(id,ct)=>Task.FromResult(s.Release))){
                    await c.SelectGameAsync(new ModGame{TitleId="CUSA00411",Version="1.57"});await c.SelectModAsync(s.Release);
                    var plan=await c.PrepareInstallAsync();var job=c.ConfirmAndEnqueue(plan,true,true);
                    Assert.Throws<InvalidOperationException>(()=>c.ConfirmAndEnqueue(plan,true,true));await job.Completion;
                    Assert.Equal("one",s.Read());offline=true;await c.Recover(plan.TransactionId,"uninstall",true,true).Completion;
                    Assert.Equal("original",s.Read());Assert.Equal(2,s.Queue.Jobs.Count);
                }
            }
        }
    }
}
