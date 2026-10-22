using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using PS4Manager.Core.Games;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class InstalledGamesAdapterTests
    {
        [Theory]
        [InlineData(true, false, InstalledState.NotInstalled)]
        [InlineData(false, false, InstalledState.Unknown)]
        [InlineData(false, true, InstalledState.Installed)]
        public void Missing_metadata_does_not_invent_absence(bool complete, bool found, InstalledState expected)
        {
            var games = found ? new[] { new ModGame { TitleId = "CUSA02624" } } : new ModGame[0];
            Assert.Equal(expected, InstalledGamesAdapter.Map("CUSA02624", new ModGameDiscoveryReport(games, complete)).State);
        }

        [Fact]
        public async Task Unreadable_root_keeps_absent_game_unknown()
        {
            var ftp = new ModMemoryFtp(); ftp.Directories.Add("/user/app");
            var discovery = new ModGameDiscovery(ftp, new FtpGates());
            Assert.False((await discovery.DiscoverReportAsync(CancellationToken.None)).EnumerationComplete);
            Assert.Equal(InstalledState.Unknown, Assert.Single(await new InstalledGamesAdapter(discovery).DiscoverAsync(CancellationToken.None)).State);
            ftp.Directories.Add("/mnt/ext0/user/app");
            Assert.Equal(InstalledState.NotInstalled, Assert.Single(await new InstalledGamesAdapter(discovery).DiscoverAsync(CancellationToken.None)).State);
            Assert.Empty(ftp.Mutations);
        }

        [Fact]
        public async Task External_installed_directory_survives_missing_metadata()
        {
            var ftp = new ModMemoryFtp(); ftp.Directories.Add("/mnt/ext0/user/app");
            ftp.Directories.Add("/mnt/ext0/user/app/CUSA02624");
            var adapter = new InstalledGamesAdapter(new ModGameDiscovery(ftp, new FtpGates()));
            var game = Assert.Single(await adapter.DiscoverAsync(CancellationToken.None));
            Assert.Equal(InstalledState.Installed, game.State); Assert.NotNull(game.Reason);
            ftp.Add("/system_data/priv/appmeta/external/CUSA02624/param.sfo", ModGameDiscoveryTests.Sfo("CUSA02624", "01.33"));
            game = Assert.Single(await adapter.DiscoverAsync(CancellationToken.None));
            Assert.Equal("1.33", game.Version); Assert.Null(game.Reason);
        }

        [Fact]
        public async Task Transient_failure_is_unknown_but_cancellation_propagates()
        {
            var ftp = new ModMemoryFtp(); ftp.Directories.Add("/user/app"); ftp.Directories.Add("/user/app/CUSA02624");
            ftp.FailRead = path => new FtpException("offline", FtpErrorKind.Transient, 421);
            var adapter = new InstalledGamesAdapter(new ModGameDiscovery(ftp, new FtpGates()));
            Assert.Equal(InstalledState.Unknown, Assert.Single(await adapter.DiscoverAsync(CancellationToken.None)).State);
            await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() => adapter.DiscoverAsync(new CancellationToken(true)));
        }
    }
}
