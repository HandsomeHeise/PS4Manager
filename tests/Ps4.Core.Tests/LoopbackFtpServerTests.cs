using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    /// <summary>Der bestehende FtpClient gegen den Loopback-Server — beweist den Server selbst.</summary>
    public class LoopbackFtpServerTests
    {
        [Fact]
        public async Task The_client_lists_a_directory()
        {
            using (var server = new LoopbackFtpServer())
            {
                server.AddDirectory("/data/GoldHEN/plugins");
                server.AddFile("/data/GoldHEN/plugins.ini", new byte[338]);

                var entries = await new FtpClient(server.Host, server.Port).ListAsync("/data/GoldHEN", CancellationToken.None);

                Assert.Equal(new[] { "plugins", "plugins.ini" }, entries.Select(e => e.Name).OrderBy(n => n).ToArray());
                Assert.True(entries.Single(e => e.Name == "plugins").IsDirectory);
                Assert.Equal(338, entries.Single(e => e.Name == "plugins.ini").Size);
            }
        }

        [Fact]
        public async Task The_client_downloads_bytes()
        {
            using (var server = new LoopbackFtpServer())
            {
                var data = Enumerable.Range(0, 100000).Select(i => (byte)(i % 251)).ToArray();
                server.AddFile("/data/a.bin", data);

                var result = await new FtpClient(server.Host, server.Port).DownloadAsync("/data/a.bin", CancellationToken.None);

                Assert.Equal(data, result);
            }
        }

        [Fact]
        public async Task The_client_deletes_a_file()
        {
            using (var server = new LoopbackFtpServer())
            {
                server.AddFile("/data/a.txt", new byte[] { 1 });

                await new FtpClient(server.Host, server.Port).DeleteAsync("/data/a.txt", CancellationToken.None);

                Assert.Null(server.GetFile("/data/a.txt"));
                Assert.Contains(server.CommandsSnapshot(), c => c.StartsWith("DELE"));
            }
        }
    }
}
