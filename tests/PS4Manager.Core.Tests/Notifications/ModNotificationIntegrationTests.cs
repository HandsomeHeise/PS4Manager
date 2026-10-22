using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using FileManager.Core.Transfers;
using PS4Manager.App;
using PS4Manager.App.Notifications;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    public sealed class ModNotificationIntegrationTests
    {
        [Theory][InlineData(true,false,0)][InlineData(true,true,1)][InlineData(false,false,1)]
        public async Task Modal_plugin_outcome_does_not_duplicate_general_job_result(bool plugin,bool fail,int noticeCount)
        {
            using(var s=new ModScenario()) {
                if(plugin){s.Ftp.Parent("/data/GoldHEN/plugins/Luma.prx");s.Package("plugin","/data/GoldHEN/plugins/Luma.prx");}
                var plan=await s.Prepare(); var receipt=s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId);
                var shown=new List<DesktopNotification>();
                using(var coordinator=new TransferNotificationCoordinator(shown.Add,()=>{},a=>a(),()=>{},n=>{})) {
                    coordinator.Attach(s.Queue);
                    var job=new TransferJob("Install",s.Planner.CreateInstall(plan,true,true),plan.Targets,CancellationToken.None);
                    ModLibraryForm.ClaimCompletion(coordinator,job,receipt,"install");
                    if(fail)s.Ftp.BeforeMutation=p=>throw new IOException("offline");
                    s.Queue.Enqueue(job); int prompted=0;
                    var result=await ModActivationPrompt.OfferResultAsync(job,"install",()=>true,()=>s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId),r=>{prompted++;return Task.FromResult(false);},id=>throw new Exception("Unexpected activation"));
                    await s.Queue.WhenIdle(); Assert.Equal(noticeCount,shown.Count);
                    Assert.Equal(plugin&&!fail?1:0,prompted); Assert.Equal(plugin&&!fail,result!=null);
                    if(fail)Assert.NotEqual(MessageKind.Success,shown[0].Kind);
                }
            }
        }
        [Fact] public void Removed_old_prx_is_not_an_installable_plugin_payload()
        {
            var receipt=new ModReceipt();receipt.Files.Add(new ModReceiptFile{Target="/data/GoldHEN/plugins/old.prx",Source=null});
            receipt.Files.Add(new ModReceiptFile{Target="/data/new.lua",Source="payload/new.lua"});
            Assert.False(ModLibraryForm.HasPluginPayload(receipt));
        }
        [Fact] public void Activation_warning_does_not_depend_on_english_copy()
        { Assert.Equal(MessageKind.Warning,ModLibraryForm.CompletionKind(new ModActivationOutcome(ModActivationOutcomeKind.ActivationFailed,"Unrelated localized text"))); }
    }
}
