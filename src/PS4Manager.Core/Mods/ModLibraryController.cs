using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.Mods
{
    public sealed class ModLibraryController:IDisposable
    {
        private readonly object sync=new object();
        private readonly ModInstallPlanner planner;
        private readonly ModReceiptStore store;
        private readonly ModConsoleProfile profile;
        private readonly TransferQueue queue;
        private readonly CancellationTokenSource lifetime;
        private readonly CancellationTokenRegistration disconnect;
        private readonly Func<CancellationToken,Task<IReadOnlyList<ModGame>>> games;
        private readonly Func<ModGame,int,CancellationToken,Task<ModCatalogPage>> catalog;
        private readonly Func<Guid,CancellationToken,Task<ModDetail>> detail;
        private readonly Func<Guid,CancellationToken,Task<ModRelease>> release;
        private CancellationTokenSource request;
        private int generation;
        private bool disposed;
        private ModLibraryState state=new ModLibraryState();
        public event EventHandler Changed;
        public ModLibraryState State {get{lock(sync)return state;}}
        public ModLibraryController(ModCatalogClient catalog,ModGameDiscovery discovery,ModInstallPlanner planner,ModReceiptStore store,ModConsoleProfile profile,TransferQueue queue,CancellationToken sessionToken)
            :this(planner,store,profile,queue,sessionToken,discovery.DiscoverAsync,(g,p,ct)=>catalog.CatalogAsync(g.TitleId,g.Version,p,ct),catalog.DetailAsync,catalog.ReleaseAsync){}
        internal ModLibraryController(ModInstallPlanner planner,ModReceiptStore store,ModConsoleProfile profile,TransferQueue queue,CancellationToken sessionToken,
            Func<CancellationToken,Task<IReadOnlyList<ModGame>>> games,Func<ModGame,int,CancellationToken,Task<ModCatalogPage>> catalog,
            Func<Guid,CancellationToken,Task<ModDetail>> detail,Func<Guid,CancellationToken,Task<ModRelease>> release)
        {
            this.planner=planner;this.store=store;this.profile=profile;this.queue=queue;this.games=games;this.catalog=catalog;this.detail=detail;this.release=release;
            lifetime=CancellationTokenSource.CreateLinkedTokenSource(sessionToken);disconnect=lifetime.Token.Register(Disconnected);queue.JobChanged+=JobChanged;
        }
        public Task RefreshGamesAsync(){return Run("Reading console games",async (n,ct)=>{var list=await games(ct).ConfigureAwait(false);Publish(n,s=>{s.Games=list;s.Game=null;s.Catalog=null;s.Release=null;s.Detail=null;s.Status=list.Count==0?"No installed games found.":"Choose a game to browse compatible mods.";});});}
        public Task SelectGameAsync(ModGame game,int page=1)
        {
            return Run("Loading compatible mods",async (n,ct)=>{
                Publish(n,s=>{s.Game=game;s.Catalog=null;s.Release=null;s.Detail=null;});
                if(game?.Version==null){Publish(n,s=>s.Status=game?.InvalidReason??"Select a game.");return;}
                var result=await catalog(game,page,ct).ConfigureAwait(false);
                Publish(n,s=>{s.Catalog=result;s.Status=result.IsOffline?"Offline catalog — "+result.FetchedUtc.ToLocalTime().ToString("g")+". New installs disabled.":result.Items.Count==0?"No mods for this exact title and game version.":"Select a mod to view details.";});
            });
        }
        public Task SelectModAsync(ModCatalogItem item)
        {
            if(item==null||State.Catalog==null||!State.Catalog.Items.Any(i=>i.ReleaseId==item.ReleaseId))throw new InvalidOperationException("Select a release from the current catalog.");
            return Run("Loading release details",async (n,ct)=>{
                Publish(n,s=>{s.Release=null;s.Detail=null;});
                var d=await detail(item.ModId,ct).ConfigureAwait(false);var r=await release(item.ReleaseId,ct).ConfigureAwait(false);
                if(r.ModId!=item.ModId||r.ReleaseId!=item.ReleaseId)throw new InvalidOperationException("Release response does not match selection.");
                Publish(n,s=>{s.Detail=d;s.Release=r;s.Status=s.Catalog.IsOffline?"Offline catalog — installation disabled.":r.State=="withdrawn"?"Release withdrawn: "+r.WithdrawalReason:"Review the package before installing. The game must be closed.";});
            });
        }
        public async Task<ModInstallPlan> PrepareInstallAsync()
        {
            var selected=State;if(!selected.CanPrepare)throw new InvalidOperationException("Current selection cannot be installed.");ModInstallPlan plan=null;
            try{
                await Run("Downloading, verifying and backing up targets",async(n,ct)=>{
                    plan=await planner.PrepareAsync(selected.Release,selected.Game,ct).ConfigureAwait(false);
                    Publish(n,s=>{s.Plan=plan;s.Status="Package verified. Review targets and confirm installation.";});
                }).ConfigureAwait(false);return plan;
            }catch{if(plan!=null){Change(s=>{if(ReferenceEquals(s.Plan,plan))s.Plan=null;});planner.Abandon(plan);}throw;}
        }
        public void DeclinePlan(){var p=State.Plan;if(p==null)return;planner.Abandon(p);Change(s=>{s.Plan=null;s.Status="Installation cancelled before console writes.";});}
        public TransferJob ConfirmAndEnqueue(ModInstallPlan plan,bool gameClosed,bool approveZones,Action<TransferJob> beforeEnqueue=null)
        {
            TransferJob job;
            lock(sync){
                lifetime.Token.ThrowIfCancellationRequested();if(disposed||!state.CanInstall||!ReferenceEquals(state.Plan,plan))throw new InvalidOperationException("Installation approval is stale.");
                job=new TransferJob("Mod Library: "+plan.Title,planner.CreateInstall(plan,gameClosed,approveZones),plan.Targets,lifetime.Token);
                var next=state.Copy();next.Plan=null;next.Job=job;next.Status="Installation queued.";state=next;generation++;
                RegisterPresentation(beforeEnqueue,job);queue.Enqueue(job);
            }
            Notify();return job;
        }
        public TransferJob Recover(Guid id,string action,bool gameClosed,bool approveZones,Action<TransferJob> beforeEnqueue=null)
        {
            lock(sync){
                lifetime.Token.ThrowIfCancellationRequested();if(disposed||state.Busy||state.JobActive||state.Plan!=null)throw new InvalidOperationException("Finish the current action first.");
                var r=store.LoadTransaction(profile.Id,id);var recovery=new ModRecovery(planner);
                IQueuedTransaction body=action=="resume"?recovery.CreateResume(id,gameClosed,approveZones):action=="rollback"?recovery.CreateRollback(id,gameClosed,approveZones):action=="uninstall"?recovery.CreateUninstall(id,gameClosed,approveZones):throw new ArgumentException("Unknown recovery action.");
                var job=new TransferJob("Mod "+action+": "+r.Title,body,r.Files.Select(f=>f.Target),lifetime.Token);var next=state.Copy();next.Job=job;next.Status="Recovery queued.";state=next;generation++;RegisterPresentation(beforeEnqueue,job);queue.Enqueue(job);Notify();return job;
            }
        }
        public void RefreshReceipts(){var receipts=store.Load(profile.Id);Change(s=>s.Receipts=receipts);}
        public TransferJob ActivatePlugins(Guid id,GoldHen.GoldHenBackupWriter backups,Action<TransferJob> beforeEnqueue=null)
        {
            lock(sync){
                lifetime.Token.ThrowIfCancellationRequested();if(disposed||state.Busy||state.JobActive||state.Plan!=null)throw new InvalidOperationException("Finish the current action first.");
                planner.Confirmed();var receipt=store.LoadTransaction(profile.Id,id);
                if(ModPluginActivation.PluginPaths(receipt).Count==0)throw new InvalidOperationException("No installed PRX plugin is available for activation.");
                var job=new TransferJob("GoldHEN activation: "+receipt.Title,new ModPluginActivation(planner,id,backups),new[]{ModPluginActivation.ConfigurationPath},lifetime.Token);
                var next=state.Copy();next.Job=job;next.Status="GoldHEN activation queued. Mod installation is complete.";state=next;generation++;RegisterPresentation(beforeEnqueue,job);queue.Enqueue(job);Notify();return job;
            }
        }
        private static void RegisterPresentation(Action<TransferJob> register,TransferJob job)
        {try{register?.Invoke(job);}catch(Exception ex){System.Diagnostics.Trace.TraceError("Presentation registration failed: {0}",ex.Message);}}
        public void CancelRequest(){CancellationTokenSource current;lock(sync)current=request;current?.Cancel();}
        public Task CancelJobAsync(){var job=State.Job;return job==null?Task.CompletedTask:queue.CancelAsync(job);}
        private async Task Run(string status,Func<int,CancellationToken,Task> body)
        {
            int n;CancellationTokenSource current,previous;
            lock(sync){lifetime.Token.ThrowIfCancellationRequested();if(disposed||state.JobActive||state.Plan!=null)throw new InvalidOperationException("Finish or decline the current action first.");
                n=++generation;previous=request;current=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);request=current;var next=state.Copy();next.Busy=true;next.Status=status;state=next;}
            previous?.Cancel();Notify();
            try{await body(n,current.Token).ConfigureAwait(false);current.Token.ThrowIfCancellationRequested();}
            catch(Exception ex){if(IsCurrent(n))Publish(n,s=>{s.Plan=null;s.Status=ex is OperationCanceledException?"Cancelled.":ex.Message;},true);throw;}
            finally{if(IsCurrent(n))Publish(n,s=>s.Busy=false,true);}
        }
        private bool IsCurrent(int n){lock(sync)return !disposed&&!lifetime.IsCancellationRequested&&generation==n;}
        private void Publish(int n,Action<ModLibraryState> update,bool finishing=false){lock(sync){if(!IsCurrent(n)||(!finishing&&request.IsCancellationRequested))throw new OperationCanceledException();var next=state.Copy();update(next);state=next;}Notify();}
        private void Change(Action<ModLibraryState> update){lock(sync){if(disposed)return;var next=state.Copy();update(next);state=next;}Notify();}
        private void Disconnected(){lock(sync){generation++;var next=state.Copy();next.Connected=false;next.Busy=false;next.Plan=null;next.Status="Console disconnected. Unfinished transactions remain available for recovery.";state=next;}Notify();}
        private void JobChanged(object sender,TransferJob job)
        {
            if(!ReferenceEquals(State.Job,job))return;
            Change(s=>{s.Status=job.State==JobState.Completed?(job.Phase??"Installed/recovered successfully. PRX files require game restart and GoldHEN activation."):job.Summary;});
            if(job.State!=JobState.Running&&job.State!=JobState.Waiting){try{RefreshReceipts();}catch(Exception ex){Change(s=>s.Status="Receipt inspection required: "+ex.Message);}}
        }
        private void Notify(){var handlers=Changed;if(handlers==null)return;foreach(EventHandler h in handlers.GetInvocationList())try{h(this,EventArgs.Empty);}catch{/* UI subscribers cannot break state or queue progress. */}}
        public void Dispose(){lock(sync){if(disposed)return;disposed=true;generation++;}queue.JobChanged-=JobChanged;lifetime.Cancel();disconnect.Dispose();}
    }
}
