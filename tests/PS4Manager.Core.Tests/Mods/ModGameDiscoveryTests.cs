using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModGameDiscoveryTests
    {
        [Fact]
        public async Task Closed_external_game_uses_persistent_external_metadata_for_name_and_version()
        {
            var ftp=Console();ftp.Directories.Remove("/user/app/CUSA00411");
            ftp.Directories.Add("/mnt/ext0/user/app");ftp.Directories.Add("/mnt/ext0/user/app/CUSA00411");
            ftp.Files.Remove("/system_data/priv/appmeta/CUSA00411/param.sfo");
            ftp.Add("/system_data/priv/appmeta/external/CUSA00411/param.sfo",Sfo("CUSA00411","01.57"));
            var discovery=new ModGameDiscovery(ftp,new FtpGates(),ct=>Task.FromResult("CUSA03041"));
            var game=Assert.Single(await discovery.DiscoverAsync(CancellationToken.None));
            Assert.Equal("GTA V",game.Name);Assert.Equal("1.57",game.Version);Assert.Equal(ModRunningState.NotRunning,game.RunningState);
            Assert.Null(game.InvalidReason);
            ftp.Add("/system_data/priv/appmeta/external/CUSA00411/param.sfo",Sfo("CUSA00411","1.58"));
            Assert.Equal("1.58",(await discovery.RefreshAsync("CUSA00411",CancellationToken.None)).Version);
            Assert.Empty(ftp.Mutations);
        }
        [Fact]
        public async Task Missing_version_keeps_the_readable_game_name_without_authorizing_install()
        {
            var ftp=Console();ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",Sfo("CUSA00411",null));
            var game=Assert.Single(await new ModGameDiscovery(ftp,new FtpGates()).DiscoverAsync(CancellationToken.None));
            Assert.Equal("GTA V",game.Name);Assert.Null(game.Version);Assert.NotNull(game.InvalidReason);
        }
        [Fact]
        public async Task External_metadata_is_not_used_for_an_internal_only_install()
        {
            var ftp=Console();ftp.Files.Remove("/system_data/priv/appmeta/CUSA00411/param.sfo");
            ftp.Add("/system_data/priv/appmeta/external/CUSA00411/param.sfo",Sfo("CUSA00411","1.57"));
            Assert.Null((await new ModGameDiscovery(ftp,new FtpGates()).RefreshAsync("CUSA00411",CancellationToken.None)).Version);
        }
        [Fact]
        public async Task Conflicting_active_metadata_is_not_hidden_by_external_fallback()
        {
            var ftp=Console();ftp.Directories.Add("/mnt/ext0/user/app");ftp.Directories.Add("/mnt/ext0/user/app/CUSA00411");
            ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",Sfo("CUSA03041","1.57"));
            ftp.Add("/system_data/priv/appmeta/external/CUSA00411/param.sfo",Sfo("CUSA00411","1.57"));
            Assert.Null((await new ModGameDiscovery(ftp,new FtpGates()).RefreshAsync("CUSA00411",CancellationToken.None)).Version);
        }
        [Fact]
        public async Task Reads_console_version_and_rechecks_without_cache()
        {
            var ftp=Console();var discovery=new ModGameDiscovery(ftp,new FtpGates());
            var game=Assert.Single(await discovery.DiscoverAsync(CancellationToken.None));
            Assert.Equal("1.57",game.Version);Assert.Equal("GTA V",game.Name);
            ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",Sfo("CUSA00411","1.58"));
            Assert.Equal("1.58",(await discovery.RefreshAsync("CUSA00411",CancellationToken.None)).Version);
        }
        [Theory]
        [InlineData("CUSA00411",null)]
        [InlineData("CUSA03041","1.57")]
        [InlineData("CUSA00411","bad")]
        public async Task Unknown_or_mismatched_metadata_does_not_authorize_install(string title,string version)
        {
            var ftp=Console();ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",Sfo(title,version));
            var game=await new ModGameDiscovery(ftp,new FtpGates()).RefreshAsync("CUSA00411",CancellationToken.None);
            Assert.Null(game.Version);Assert.False(string.IsNullOrEmpty(game.InvalidReason));
        }
        [Fact]
        public async Task Stale_appmeta_is_not_an_installed_game()
        {
            var ftp=Console();ftp.Directories.Remove("/user/app/CUSA00411");
            Assert.Empty(await new ModGameDiscovery(ftp,new FtpGates()).DiscoverAsync(CancellationToken.None));
        }
        [Fact]
        public async Task External_titles_are_discovered_and_duplicate_ids_are_deduplicated()
        {
            var ftp=Console();ftp.Directories.Add("/mnt/ext0/user/app");ftp.Directories.Add("/mnt/ext0/user/app/CUSA00411");
            Assert.Single(await new ModGameDiscovery(ftp,new FtpGates()).DiscoverAsync(CancellationToken.None));
            ftp.Directories.Remove("/user/app/CUSA00411");
            Assert.Equal("1.57",Assert.Single(await new ModGameDiscovery(ftp,new FtpGates()).DiscoverAsync(CancellationToken.None)).Version);
        }
        [Fact]
        public async Task Malformed_and_embedded_null_metadata_cannot_authorize_install()
        {
            var ftp=Console();var discovery=new ModGameDiscovery(ftp,new FtpGates());
            ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",new byte[]{0,1});
            Assert.Null((await discovery.RefreshAsync("CUSA00411",CancellationToken.None)).Version);
            ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",Sfo("CUSA00411","1.57\0different"));
            Assert.Null((await discovery.RefreshAsync("CUSA00411",CancellationToken.None)).Version);
        }
        [Fact]
        public async Task Frame4_failure_does_not_block_ftp_discovery()
        {
            var discovery=new ModGameDiscovery(Console(),new FtpGates(),ct=>throw new IOException("offline"));
            Assert.Equal(ModRunningState.Unknown,Assert.Single(await discovery.DiscoverAsync(CancellationToken.None)).RunningState);
        }
        internal static ModMemoryFtp Console()
        {
            var ftp=new ModMemoryFtp();ftp.Directories.Add("/user/app");ftp.Directories.Add("/user/app/CUSA00411");
            ftp.Add("/system_data/priv/appmeta/CUSA00411/param.sfo",Sfo("CUSA00411","1.57"));return ftp;
        }
        internal static byte[] Sfo(string title,string version)
        {
            var values=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>("TITLE_ID",title),new KeyValuePair<string,string>("TITLE","GTA V")};
            if(version!=null)values.Add(new KeyValuePair<string,string>("APP_VER",version));
            using(var keys=new MemoryStream())using(var data=new MemoryStream())using(var table=new MemoryStream()) {
                var tw=new BinaryWriter(table);foreach(var value in values){var kb=Encoding.UTF8.GetBytes(value.Key+"\0");var db=Encoding.UTF8.GetBytes(value.Value+"\0");tw.Write((ushort)keys.Length);tw.Write((ushort)0x0204);tw.Write(db.Length);tw.Write(db.Length);tw.Write((uint)data.Length);keys.Write(kb,0,kb.Length);data.Write(db,0,db.Length);}
                using(var output=new MemoryStream()) {var writer=new BinaryWriter(output);writer.Write(0x46535000U);writer.Write(0x101U);writer.Write((uint)(20+table.Length));writer.Write((uint)(20+table.Length+keys.Length));writer.Write(values.Count);writer.Write(table.ToArray());writer.Write(keys.ToArray());writer.Write(data.ToArray());return output.ToArray();}
            }
        }
    }
}
