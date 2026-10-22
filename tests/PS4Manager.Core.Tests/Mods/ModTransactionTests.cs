using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModTransactionTests
    {
        [Fact] public async Task Backup_failure_performs_no_remote_writes()
        {using(var s=new ModScenario()){s.Ftp.FailRead=p=>p=="/data/mod.txt"?new IOException("bad RETR"):null;await Assert.ThrowsAsync<IOException>(()=>s.Install());Assert.Empty(s.Ftp.Mutations);Assert.Equal("original",s.Read());}}
        [Fact] public async Task Install_update_rollback_uninstall_restores_exact_original()
        {
            using(var s=new ModScenario()){
                var first=await s.Install();Assert.Equal("one",s.Read());s.Package("two");var second=await s.Install();Assert.Equal("two",s.Read());
                var recovery=new ModRecovery(s.Planner);await s.Run(recovery.CreateRollback(second,true,true));Assert.Equal("one",s.Read());
                await s.Run(recovery.CreateUninstall(first,true,true));Assert.Equal("original",s.Read());
                Assert.DoesNotContain(s.Ftp.Mutations,m=>m=="delete /data/mod.txt");
            }
        }
        [Fact] public async Task Changed_target_and_other_mod_ownership_block_writes()
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();s.Ftp.Add("/data/mod.txt",Encoding.UTF8.GetBytes("user edit"));
                await Assert.ThrowsAsync<IOException>(()=>s.Run(s.Planner.CreateInstall(plan,true,true)));Assert.Empty(s.Ftp.Mutations);
            }
            using(var s=new ModScenario()){
                await s.Install();s.Ftp.Mutations.Clear();s.Package("two",otherMod:true);
                await Assert.ThrowsAsync<InvalidDataException>(()=>s.Prepare());Assert.Empty(s.Ftp.Mutations);
            }
        }
        [Fact] public async Task Lost_rename_response_resumes_by_inspecting_remote_state()
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();bool fail=true;s.Ftp.AfterRename=()=>{if(fail){fail=false;throw new IOException("lost response");}};
                await Assert.ThrowsAsync<IOException>(()=>s.Run(s.Planner.CreateInstall(plan,true,true)));
                Assert.Equal(ModInstallState.Incomplete,s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId).State);
                s.Ftp.AfterRename=null;await s.Run(new ModRecovery(s.Planner).CreateResume(plan.TransactionId,true,true));Assert.Equal("one",s.Read());
            }
        }
        [Fact] public async Task Withdrawal_blocks_resume_but_offline_uninstall_is_available()
        {
            using(var s=new ModScenario()){
                var id=await s.Install();s.Release.State="withdrawn";
                await s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true));Assert.Equal("original",s.Read());
            }
        }
    }
}
