using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using PS4Manager.Core.Mods;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModActivationPromptTests
    {
        [Theory][InlineData(false,false,ModActivationOutcomeKind.Deferred)][InlineData(true,false,ModActivationOutcomeKind.Enabled)][InlineData(true,true,ModActivationOutcomeKind.ActivationFailed)]
        public async Task Typed_outcome_keeps_activation_failure_separate(bool consent,bool fail,ModActivationOutcomeKind expected)
        {
            using(var s=new ModScenario()) {
                s.Ftp.Parent("/data/GoldHEN/plugins/Luma.prx"); s.Package("plugin","/data/GoldHEN/plugins/Luma.prx"); var plan=await s.Prepare();
                var job=new TransferJob("Install",s.Planner.CreateInstall(plan,true,true),plan.Targets,CancellationToken.None); s.Queue.Enqueue(job);
                var result=await ModActivationPrompt.OfferResultAsync(job,"install",()=>true,()=>s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId),
                    r=>Task.FromResult(consent),id=>fail?Task.FromException(new IOException("offline")):Task.CompletedTask);
                Assert.Equal(expected,result.Kind); Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId).State);
            }
        }
        [Theory][InlineData("install",true)][InlineData("resume",true)][InlineData("install",false)]
        public async Task Successful_install_or_resume_asks_once_and_only_consent_activates(string action,bool consent)
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent("/data/GoldHEN/plugins/Luma.prx");s.Package("plugin","/data/GoldHEN/plugins/Luma.prx");var plan=await s.Prepare();
                var job=new TransferJob("Install",s.Planner.CreateInstall(plan,true,true),plan.Targets,CancellationToken.None);
                int asked=0,enabled=0;
                var pending=ModActivationPrompt.OfferAsync(job,action,()=>true,()=>s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId),
                    r=>{asked++;Assert.Equal(ModInstallState.Installed,r.State);return Task.FromResult(consent);},id=>{enabled++;return Task.CompletedTask;});
                Assert.Equal(0,asked);s.Queue.Enqueue(job);var result=await pending;
                Assert.Equal(1,asked);Assert.Equal(consent?1:0,enabled);Assert.NotNull(result);
                Assert.False(s.Ftp.Files.ContainsKey(ModPluginActivation.ConfigurationPath));
            }
        }
        [Theory][InlineData("rollback",true)][InlineData("uninstall",true)][InlineData("install",false)]
        public async Task Other_actions_or_stale_session_never_prompt(string action,bool current)
        {
            using(var s=new ModScenario()){
                var job=new TransferJob(TransferKind.CreateFolder,"No work",new TransferStep[0]);s.Queue.Enqueue(job);await job.Completion;
                Assert.Null(await ModActivationPrompt.OfferAsync(job,action,()=>current,()=>throw new Exception("Must not load another session's receipt"),r=>throw new Exception("Must not prompt"),id=>throw new Exception("Must not activate")));
            }
        }
        [Fact] public async Task Activation_failure_is_separate_from_successful_installation()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent("/data/GoldHEN/plugins/Luma.prx");s.Package("plugin","/data/GoldHEN/plugins/Luma.prx");var plan=await s.Prepare();
                var job=new TransferJob("Install",s.Planner.CreateInstall(plan,true,true),plan.Targets,CancellationToken.None);s.Queue.Enqueue(job);
                var message=await ModActivationPrompt.OfferAsync(job,"install",()=>true,()=>s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId),r=>Task.FromResult(true),id=>throw new IOException("offline"));
                Assert.Contains("activation failed",message);Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId).State);
            }
        }
        [Fact] public async Task Failed_install_never_offers_activation()
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();s.Ftp.BeforeMutation=p=>throw new IOException("offline");
                var job=new TransferJob("Install",s.Planner.CreateInstall(plan,true,true),plan.Targets,CancellationToken.None);s.Queue.Enqueue(job);
                Assert.Null(await ModActivationPrompt.OfferAsync(job,"install",()=>true,()=>throw new Exception("Must not load"),r=>throw new Exception("Must not prompt"),id=>throw new Exception("Must not activate")));
            }
        }
        [Fact] public async Task Session_change_while_confirmation_is_open_prevents_activation()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent("/data/GoldHEN/plugins/Luma.prx");s.Package("plugin","/data/GoldHEN/plugins/Luma.prx");var plan=await s.Prepare();
                var job=new TransferJob("Install",s.Planner.CreateInstall(plan,true,true),plan.Targets,CancellationToken.None);s.Queue.Enqueue(job);bool current=true;
                Assert.Null(await ModActivationPrompt.OfferAsync(job,"install",()=>current,()=>s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId),r=>{current=false;return Task.FromResult(true);},id=>throw new Exception("Wrong session")));
            }
        }
    }
}
