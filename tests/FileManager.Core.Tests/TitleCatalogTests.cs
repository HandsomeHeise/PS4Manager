using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Ps4;
using FileManager.Core.Tests.Support;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;
using Xunit;

namespace FileManager.Core.Tests
{
    public class TitleCatalogTests
    {
        private static InMemoryFtpClient WithGta()
        {
            return new InMemoryFtpClient()
                .AddFile("/system_data/priv/appmeta/CUSA00411/param.sfo", File.ReadAllBytes(Path.Combine("TestData", "param.sfo")))
                .AddFile("/user/appmeta/CUSA00411/icon0.png", new byte[] { 1, 2, 3 });
        }

        [Fact]
        public async Task A_title_is_resolved_once_per_session()
        {
            var ftp = WithGta();
            var catalog = new TitleCatalog(ftp, new FtpGates());

            var first = await catalog.GetAsync("CUSA00411", CancellationToken.None);
            var second = await catalog.GetAsync("CUSA00411", CancellationToken.None);

            Assert.Equal("Grand Theft Auto V", first.Name);
            Assert.Same(first, second);
            Assert.Equal(1, ftp.CallsSnapshot().Count(c => c.EndsWith("param.sfo")));
            TitleInfo known;
            Assert.True(catalog.TryGet("CUSA00411", out known));
        }

        [Fact]
        public async Task A_missing_title_is_remembered_as_its_id()
        {
            var ftp = new InMemoryFtpClient();
            var catalog = new TitleCatalog(ftp, new FtpGates());

            var info = await catalog.GetAsync("CUSA99999", CancellationToken.None);
            await catalog.GetAsync("CUSA99999", CancellationToken.None);

            Assert.Equal("CUSA99999", info.Name);
            Assert.Equal(1, ftp.CallsSnapshot().Count(c => c.EndsWith("param.sfo")));
        }

        [Fact]
        public async Task A_connection_loss_is_not_remembered()
        {
            var ftp = WithGta();
            bool broken = true;
            ftp.Failure = (verb, path) => broken ? new FtpException("weg", FtpErrorKind.Transient, 0) : null;
            var catalog = new TitleCatalog(ftp, new FtpGates());

            await Assert.ThrowsAsync<FtpException>(() => catalog.GetAsync("CUSA00411", CancellationToken.None));
            TitleInfo ignored;
            Assert.False(catalog.TryGet("CUSA00411", out ignored));

            broken = false;
            Assert.Equal("Grand Theft Auto V", (await catalog.GetAsync("CUSA00411", CancellationToken.None)).Name);
        }

        [Fact]
        public async Task A_cancelled_lookup_is_not_remembered()
        {
            var catalog = new TitleCatalog(WithGta(), new FtpGates());
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.GetAsync("CUSA00411", cts.Token));
            }
            TitleInfo ignored;
            Assert.False(catalog.TryGet("CUSA00411", out ignored));
        }
    }
}
