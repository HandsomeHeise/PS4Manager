using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModRecoveryTests
    {
        [Fact] public async Task Missing_retained_original_never_falls_back_to_a_local_readback()
        {
            using(var s=new ModScenario()){
                var id=await s.Install();var receipt=s.Store.LoadTransaction(s.Profile.Id,id);
                s.Ftp.Files.Remove(receipt.Files[0].RemoteOriginalPath);s.Ftp.Mutations.Clear();
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true)));
                Assert.Empty(s.Ftp.Mutations);Assert.Equal("one",s.Read());
            }
        }
        [Fact] public async Task Validation_extractions_are_disposable_not_cumulative()
        {
            using(var s=new ModScenario()){
                var id=await s.Install();var path=s.Store.StagingPath(s.Profile.Id,id);
                Assert.DoesNotContain(Directory.GetDirectories(path),p=>Path.GetFileName(p)!="validated");
                await s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true));
                Assert.DoesNotContain(Directory.GetDirectories(path),p=>Path.GetFileName(p)!="validated");
            }
        }
        [Theory][InlineData(false)][InlineData(true)]
        public async Task Repeatably_wrong_original_RETR_never_becomes_the_restored_original(bool removeInUpdate)
        {
            using(var s=new ModScenario()){
                s.Ftp.ReadTransform=(path,bytes)=>Encoding.UTF8.GetString(bytes)=="original"?Encoding.UTF8.GetBytes("corrupt!"):bytes;
                await s.Install();s.Package("two",removeInUpdate?"/data/new.txt":"/data/mod.txt");var id=await s.Install();
                if(removeInUpdate)Assert.Equal("original",s.Read());
                await s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true));Assert.Equal("original",s.Read());
            }
        }
        [Fact] public async Task Rollback_of_removed_target_preserves_original_for_predecessor_uninstall()
        {
            using(var s=new ModScenario()){
                var first=await s.Install();s.Package("two","/data/new.txt");var second=await s.Install();
                await s.Run(new ModRecovery(s.Planner).CreateRollback(second,true,true));Assert.Equal("one",s.Read());
                await s.Run(new ModRecovery(s.Planner).CreateUninstall(first,true,true));Assert.Equal("original",s.Read());
            }
        }
        [Theory]
        [InlineData(1)][InlineData(2)][InlineData(3)]
        public async Task Every_install_mutation_can_fail_then_rollback_without_losing_original(int boundary)
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();int n=0;s.Ftp.BeforeMutation=p=>{if(++n==boundary)throw new OperationCanceledException("stopped");};
                await Assert.ThrowsAsync<IOException>(()=>s.Run(s.Planner.CreateInstall(plan,true,true)));
                s.Ftp.BeforeMutation=null;await s.Run(new ModRecovery(s.Planner).CreateRollback(plan.TransactionId,true,true));Assert.Equal("original",s.Read());
            }
        }
        [Theory]
        [InlineData(1)][InlineData(2)]
        public async Task Lost_restore_rename_response_can_be_reconciled(int boundary)
        {
            using(var s=new ModScenario()){
                var id=await s.Install();int n=0;s.Ftp.AfterRename=()=>{if(++n==boundary)throw new IOException("lost");};var recovery=new ModRecovery(s.Planner);
                await Assert.ThrowsAsync<IOException>(()=>s.Run(recovery.CreateUninstall(id,true,true)));
                s.Ftp.AfterRename=null;await s.Run(recovery.CreateUninstall(id,true,true));Assert.Equal("original",s.Read());
            }
        }
        [Fact] public async Task Modified_target_or_tampered_backup_blocks_uninstall_without_writes()
        {
            using(var s=new ModScenario()){
                var id=await s.Install();s.Ftp.Add("/data/mod.txt",Encoding.UTF8.GetBytes("user edit"));s.Ftp.Mutations.Clear();
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true)));Assert.Empty(s.Ftp.Mutations);
            }
            using(var s=new ModScenario()){
                var id=await s.Install();var r=s.Store.LoadTransaction(s.Profile.Id,id);File.WriteAllText(s.Store.BackupPath(s.Profile.Id,r.Files[0].OriginalHash),"bad");s.Ftp.Mutations.Clear();
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true)));Assert.Empty(s.Ftp.Mutations);
            }
        }
        [Fact] public async Task Removed_update_target_is_restored_and_owned_addition_is_removed_on_uninstall()
        {
            using(var s=new ModScenario()){
                await s.Install();s.Package("new file","/data/new.txt");var id=await s.Install();Assert.Equal("original",s.Read());Assert.True(s.Ftp.Files.ContainsKey("/data/new.txt"));
                await s.Run(new ModRecovery(s.Planner).CreateUninstall(id,true,true));Assert.Equal("original",s.Read());Assert.False(s.Ftp.Files.ContainsKey("/data/new.txt"));
            }
        }
        [Fact] public async Task Fresh_withdrawal_version_and_profile_checks_fail_before_writes()
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();s.Release.State="withdrawn";
                await Assert.ThrowsAsync<IOException>(()=>s.Run(s.Planner.CreateInstall(plan,true,true)));Assert.Empty(s.Ftp.Mutations);
                await s.Run(new ModRecovery(s.Planner).CreateRollback(plan.TransactionId,true,true));Assert.Equal("original",s.Read());
            }
            using(var s=new ModScenario()){
                var plan=await s.Prepare();s.Ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",ModGameDiscoveryTests.Sfo("CUSA00411","1.58"));
                await Assert.ThrowsAsync<IOException>(()=>s.Run(s.Planner.CreateInstall(plan,true,true)));Assert.Empty(s.Ftp.Mutations);
                s.Profile.ForgetSession();Assert.Throws<InvalidOperationException>(()=>new ModRecovery(s.Planner).CreateRollback(plan.TransactionId,true,true));
            }
        }
        [Fact] public async Task Another_process_lease_and_backup_storage_failure_prevent_writes()
        {
            using(var s=new ModScenario()){
                using(s.Store.AcquireLease(s.Profile.Id))await Assert.ThrowsAsync<IOException>(()=>s.Prepare());
                var backups=Path.Combine(s.Store.Root,"profiles",s.Profile.Id.ToString("N"),"backups");Directory.CreateDirectory(Path.GetDirectoryName(backups));File.WriteAllText(backups,"blocks directory");
                await Assert.ThrowsAsync<IOException>(()=>s.Prepare());Assert.Empty(s.Ftp.Mutations);
            }
        }
        [Fact] public async Task Existing_unknown_stage_is_not_overwritten()
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();var r=s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId);s.Ftp.Add(r.Files[0].StagePath,Encoding.UTF8.GetBytes("partial"));
                await Assert.ThrowsAsync<IOException>(()=>s.Run(s.Planner.CreateInstall(plan,true,true)));Assert.Empty(s.Ftp.Mutations);
                await s.Run(new ModRecovery(s.Planner).CreateRollback(plan.TransactionId,true,true));Assert.Equal("original",s.Read());Assert.True(s.Ftp.Files.ContainsKey(r.Files[0].StagePath));
            }
        }
        [Fact] public async Task Declining_prepared_plan_releases_reservation_without_remote_mutation()
        {
            using(var s=new ModScenario()){
                var plan=await s.Prepare();s.Planner.Abandon(plan);Assert.Empty(s.Ftp.Mutations);
                Assert.Equal(ModInstallState.RolledBack,s.Store.LoadTransaction(s.Profile.Id,plan.TransactionId).State);
                await s.Install();Assert.Equal("one",s.Read());
            }
        }
    }
}
