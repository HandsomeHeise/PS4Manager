using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Ps4;
using FileManager.Core.Tests.Support;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public class QuickAccessBuilderTests
    {
        /// <summary>Nachbau der am 2026-09-11 erhobenen Konsole (Spec „Dateisystem").</summary>
        private static InMemoryFtpClient RealConsole()
        {
            var name = new byte[17];
            Encoding.UTF8.GetBytes("Spieler_1").CopyTo(name, 0);
            return new InMemoryFtpClient()
                .AddDir("/data/GoldHEN/plugins").AddDir("/data/GoldHEN/cheats").AddDir("/data/GoldHEN/patches")
                .AddFile("/data/GoldHEN/plugins.ini", "[default]")
                .AddDir("/data/pkg")
                .AddDir("/user/av_contents/photo")
                .AddDir("/user/app")                                      // leer — Spiele liegen auf ext0
                .AddDir("/user/home/194450ad/savedata")
                .AddFile("/user/home/194450ad/username.dat", name)
                .AddDir("/mnt/ext0/user/app/CUSA23827")
                .AddDir("/mnt/usb0")                                      // existiert immer, leer
                .AddFile("/mnt/usb1/backup.zip", "x");
        }

        private static Task<System.Collections.Generic.IReadOnlyList<QuickAccessEntry>> Build(InMemoryFtpClient ftp, params string[] pinned)
        {
            var gates = new FtpGates();
            return new QuickAccessBuilder(new DirectoryCache(ftp, gates), ftp, gates).BuildAsync(pinned, CancellationToken.None);
        }

        [Fact]
        public async Task The_real_console_yields_the_expected_entries_in_order()
        {
            var entries = await Build(RealConsole());

            Assert.Equal(new[]
            {
                "GoldHEN|/data/GoldHEN",
                "Plugins|/data/GoldHEN/plugins",
                "Cheats|/data/GoldHEN/cheats",
                "Patches|/data/GoldHEN/patches",
                "PKG folder|/data/pkg",
                "Captures|/user/av_contents",
                "Games (extended storage)|/mnt/ext0/user/app",
                "Save data (Spieler_1)|/user/home/194450ad/savedata",
                "USB (usb1)|/mnt/usb1",
                "Extended storage|/mnt/ext0"
            }, entries.Select(e => e.Label + "|" + e.Path).ToArray());
        }

        [Fact]
        public async Task Internal_games_show_up_when_the_folder_is_not_empty()
        {
            var ftp = RealConsole().AddDir("/user/app/CUSA00411");
            var entries = await Build(ftp);
            Assert.Contains(entries, e => e.Label == "Games (internal)" && e.Path == "/user/app" && e.Kind == QuickAccessKind.Games);
        }

        [Fact]
        public async Task Without_goldhen_there_are_no_goldhen_entries()
        {
            var ftp = new InMemoryFtpClient().AddDir("/data/pkg");
            var entries = await Build(ftp);
            Assert.DoesNotContain(entries, e => e.Kind == QuickAccessKind.GoldHen || e.Kind == QuickAccessKind.Plugins);
            Assert.Contains(entries, e => e.Kind == QuickAccessKind.PkgFolder);
        }

        [Fact]
        public async Task A_user_without_a_readable_name_is_shown_by_id()
        {
            var ftp = new InMemoryFtpClient().AddDir("/user/home/1a2b3c4d/savedata");
            var entries = await Build(ftp);
            Assert.Contains(entries, e => e.Label == "Save data (1a2b3c4d)");
        }

        [Fact]
        public async Task Pinned_folders_come_last_named_after_their_folder()
        {
            var entries = await Build(RealConsole(), "/data/InsulinBO3", "/");
            Assert.Equal("InsulinBO3|/data/InsulinBO3", entries[entries.Count - 2].Label + "|" + entries[entries.Count - 2].Path);
            Assert.Equal("/|/", entries.Last().Label + "|" + entries.Last().Path);
            Assert.Equal(QuickAccessKind.Pinned, entries.Last().Kind);
        }

        [Fact]
        public async Task A_connection_loss_fails_loudly_instead_of_yielding_an_empty_list()
        {
            var ftp = RealConsole();
            ftp.Failure = (verb, path) => new FtpException("weg", FtpErrorKind.Transient, 0);
            await Assert.ThrowsAsync<FtpException>(() => Build(ftp));
        }

        [Theory]
        [InlineData(new byte[] { 0x53, 0x70, 0x69, 0x65, 0x6C, 0x65, 0x72, 0x00, 0x00 }, "Spieler")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00 }, null)]
        [InlineData(new byte[] { 0xFF, 0xFE, 0x00 }, null)]
        [InlineData(new byte[] { 0x41, 0x07, 0x42 }, null)]
        [InlineData(new byte[0], null)]
        public void The_user_name_is_read_defensively(byte[] data, string expected)
        {
            Assert.Equal(expected, UserNameReader.Parse(data));
        }
    }
}
