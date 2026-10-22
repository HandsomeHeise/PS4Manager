using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;
using Xunit;

namespace Ps4.Core.Tests
{
    public class TitleInfoLoaderTests
    {
        [Theory]
        [InlineData("CUSA00411", true)]
        [InlineData("PPSA01234", true)]
        [InlineData("NPXS20001", true)]
        [InlineData("cusa00411", false)]
        [InlineData("CUSA0041", false)]
        [InlineData("CUSA004111", false)]
        [InlineData("CUSA00411 ", false)]
        [InlineData("GoldHEN", false)]
        [InlineData("CUSA٠٠٤١١", false)]   // arabisch-indische Ziffern: \d würde sie nehmen
        [InlineData(null, false)]
        public void Title_ids_are_recognised(string name, bool expected)
        {
            Assert.Equal(expected, TitleId.IsMatch(name));
        }

        [Fact]
        public async Task A_title_with_sfo_and_icon_is_resolved()
        {
            var ftp = new StubFtpClient();
            ftp.Files["/system_data/priv/appmeta/CUSA00411/param.sfo"] = File.ReadAllBytes(Path.Combine("TestData", "param.sfo"));
            ftp.Files["/user/appmeta/CUSA00411/icon0.png"] = new byte[] { 1, 2, 3 };

            var info = await TitleInfoLoader.LoadAsync(ftp, "CUSA00411", CancellationToken.None);

            Assert.Equal("CUSA00411", info.Id);
            Assert.Equal("Grand Theft Auto V", info.Name);
            Assert.True(info.HasRealName);
            Assert.Equal(new byte[] { 1, 2, 3 }, info.IconPng);
        }

        [Fact]
        public async Task Missing_files_fall_back_to_the_id_silently()
        {
            var info = await TitleInfoLoader.LoadAsync(new StubFtpClient(), "CUSA00411", CancellationToken.None);

            Assert.Equal("CUSA00411", info.Name);
            Assert.False(info.HasRealName);
            Assert.Null(info.IconPng);
        }

        [Fact]
        public async Task A_connection_loss_is_not_mistaken_for_a_missing_name()
        {
            var ftp = new StubFtpClient { FailWith = new FtpException("weg", FtpErrorKind.Transient, 0) };
            await Assert.ThrowsAsync<FtpException>(() => TitleInfoLoader.LoadAsync(ftp, "CUSA00411", CancellationToken.None));
        }

        [Fact]
        public async Task Cancellation_is_passed_through()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => TitleInfoLoader.LoadAsync(new StubFtpClient(), "CUSA00411", cts.Token));
            }
        }
    }
}
