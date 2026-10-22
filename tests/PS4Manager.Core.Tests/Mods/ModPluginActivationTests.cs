using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Tests.Mods.Support;
using FileManager.Core.Transfers;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModPluginActivationTests
    {
        private const string Plugin="/data/GoldHEN/plugins/Luma.prx";
        private const string Config="/data/GoldHEN/plugins.ini";
        [Theory][InlineData(false)][InlineData(true)]
        public async Task Explicit_activation_enables_only_the_installed_title_and_preserves_existing_config(bool existing)
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin);var id=await s.Install();
                const string original="; keep me\r\n[CUSA03041]\r\n/data/GoldHEN/plugins/Other.prx\r\n";
                if(existing)s.Ftp.Add(Config,Encoding.UTF8.GetBytes(original));
                s.Ftp.Mutations.Clear();var backupRoot=Path.Combine(s.Root,"goldhen");
                await s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(backupRoot,new Clock())));
                var text=Encoding.UTF8.GetString(s.Ftp.Files[Config]);
                Assert.Contains("[CUSA00411]",text);Assert.Contains(Plugin,text);Assert.DoesNotContain("[default]",text);
                if(existing){Assert.StartsWith(original,text);Assert.Contains(Directory.GetFiles(backupRoot,"plugins.ini",SearchOption.AllDirectories),p=>File.ReadAllText(p)==original);}
                Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,id).State);
                s.Ftp.Mutations.Clear();await s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(backupRoot,new Clock())));
                Assert.Empty(s.Ftp.Mutations);
            }
        }
        [Fact] public async Task Unrelated_legacy_plugin_lines_are_preserved_verbatim()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin);var id=await s.Install();
                const string original="; old config\r\nOther.prx\r\n[CUSA03041]\r\n# Second.prx\r\n[CUSA00411]\r\n; Luma.prx\r\n";
                s.Ftp.Add(Config,Encoding.UTF8.GetBytes(original));
                await s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(Path.Combine(s.Root,"goldhen"),new Clock())));
                Assert.Equal(original.Replace("; Luma.prx",Plugin),Encoding.UTF8.GetString(s.Ftp.Files[Config]));
            }
        }
        [Fact] public async Task Configuration_change_at_original_rename_stops_activation_and_retains_changed_bytes()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin);var id=await s.Install();s.Ftp.Add(Config,Encoding.UTF8.GetBytes("; original\n"));s.Ftp.Mutations.Clear();
                s.Ftp.BeforeMutation=p=>{if(p.EndsWith(".original"))s.Ftp.Files[Config]=Encoding.UTF8.GetBytes("; late edit\n");};
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(Path.Combine(s.Root,"goldhen"),new Clock()))));
                Assert.False(s.Ftp.Files.ContainsKey(Config));
                Assert.Equal("; late edit\n",Encoding.UTF8.GetString(s.Ftp.Files.Single(f=>f.Key.EndsWith(".original")&&f.Key.Contains(".ps4m-activate-")).Value));
                Assert.DoesNotContain(s.Ftp.Mutations,m=>m.EndsWith(" -> "+Config));
                Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,id).State);
            }
        }
        [Fact] public async Task Configuration_owned_by_an_installed_mod_is_not_modified()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Config);s.Package("; managed config\n",Config);await s.Install();
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin,true);var id=await s.Install();s.Ftp.Mutations.Clear();
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(Path.Combine(s.Root,"goldhen"),new Clock()))));
                Assert.Empty(s.Ftp.Mutations);Assert.Equal("; managed config\n",Encoding.UTF8.GetString(s.Ftp.Files[Config]));
            }
        }
        [Fact] public async Task Concurrent_configuration_change_is_preserved()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin);var id=await s.Install();s.Ftp.Add(Config,Encoding.UTF8.GetBytes("; original\n"));s.Ftp.Mutations.Clear();
                s.Ftp.BeforeMutation=p=>{if(p.EndsWith(".stage"))s.Ftp.Files[Config]=Encoding.UTF8.GetBytes("; external edit\n");};
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(Path.Combine(s.Root,"goldhen"),new Clock()))));
                Assert.Equal("; external edit\n",Encoding.UTF8.GetString(s.Ftp.Files[Config]));Assert.DoesNotContain(s.Ftp.Mutations,m=>m.StartsWith("rename "));
                Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,id).State);
            }
        }
        [Fact] public async Task Failed_activation_keeps_installation_and_original_configuration()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin);var id=await s.Install();s.Ftp.Add(Config,Encoding.UTF8.GetBytes("; original\n"));
                int replacements=0;s.Ftp.BeforeMutation=p=>{if(p==Config&&++replacements==1)throw new IOException("write failed");};
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(Path.Combine(s.Root,"goldhen"),new Clock()))));
                Assert.Equal("; original\n",Encoding.UTF8.GetString(s.Ftp.Files[Config]));
                Assert.Equal("plugin",Encoding.UTF8.GetString(s.Ftp.Files[Plugin]));Assert.Equal(ModInstallState.Installed,s.Store.LoadTransaction(s.Profile.Id,id).State);
            }
        }
        [Fact] public async Task Changed_plugin_or_failed_backup_blocks_activation_before_any_write()
        {
            using(var s=new ModScenario()){
                s.Ftp.Parent(Plugin);s.Package("plugin",Plugin);var id=await s.Install();s.Ftp.Add(Config,Encoding.UTF8.GetBytes("; original\n"));
                s.Ftp.Files[Plugin]=Encoding.UTF8.GetBytes("other!");s.Ftp.Mutations.Clear();
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(Path.Combine(s.Root,"goldhen"),new Clock()))));Assert.Empty(s.Ftp.Mutations);
                s.Ftp.Files[Plugin]=Encoding.UTF8.GetBytes("plugin");var blocked=Path.Combine(s.Root,"blocked");File.WriteAllText(blocked,"not a directory");
                await Assert.ThrowsAsync<IOException>(()=>s.Run(new ModPluginActivation(s.Planner,id,new GoldHenBackupWriter(blocked,new Clock()))));Assert.Empty(s.Ftp.Mutations);
            }
        }
        [Theory][InlineData(ModInstallState.Incomplete)][InlineData(ModInstallState.RolledBack)][InlineData(ModInstallState.Uninstalled)]
        public void Only_installed_prx_payloads_offer_activation(ModInstallState state)
        {
            var receipt=new ModReceipt{State=state};receipt.Files.Add(new ModReceiptFile{Source="payload/Luma.prx",Target=Plugin});
            Assert.Empty(ModPluginActivation.PluginPaths(receipt));receipt.State=ModInstallState.Installed;
            Assert.Equal(Plugin,Assert.Single(ModPluginActivation.PluginPaths(receipt)));
            receipt.Files[0].Source=null;Assert.Empty(ModPluginActivation.PluginPaths(receipt));
            receipt.Files[0].Source="payload/file.lua";receipt.Files[0].Target="/data/scripts/a.lua";Assert.Empty(ModPluginActivation.PluginPaths(receipt));
        }
        private sealed class Clock:IClock {public DateTime UtcNow{get{return DateTime.UtcNow;}}}
    }
}
