using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Tests.Support;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public class DirectoryCacheTests
    {
        private static int Lists(InMemoryFtpClient ftp, string path)
        {
            return ftp.CallsSnapshot().Count(c => c == "LIST " + path);
        }

        [Fact]
        public void The_connection_budget_adds_up()
        {
            var gates = new FtpGates();
            Assert.Equal(4, FtpGates.ConnectionLimit);
            Assert.Equal(FtpGates.ConnectionLimit, gates.Transfer.CurrentCount + gates.Browse.CurrentCount + FtpGates.ShellSlots);
        }

        [Fact]
        public async Task A_second_listing_comes_from_the_cache()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            var cache = new DirectoryCache(ftp, new FtpGates());

            await cache.ListAsync("/data", CancellationToken.None);
            var again = await cache.ListAsync("/data/", CancellationToken.None);

            Assert.Equal("a.txt", again.Single().Name);
            Assert.Equal(1, Lists(ftp, "/data"));
            IReadOnlyList<FtpEntry> cached;
            Assert.True(cache.TryGetCached("/data", out cached));
        }

        [Fact]
        public async Task Concurrent_listings_of_one_folder_share_one_request()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            var first = cache.ListAsync("/data", CancellationToken.None);
            var second = cache.ListAsync("/data", CancellationToken.None);
            ftp.ListGate.SetResult(true);
            await Task.WhenAll(first, second);

            Assert.Equal(1, Lists(ftp, "/data"));
        }

        [Fact]
        public async Task A_failed_listing_is_not_cached()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            int calls = 0;
            ftp.Failure = (verb, path) => verb == "LIST" && calls++ == 0
                ? new FtpException("weg", FtpErrorKind.Transient, 0)
                : null;
            var cache = new DirectoryCache(ftp, new FtpGates());

            await Assert.ThrowsAsync<FtpException>(() => cache.ListAsync("/data", CancellationToken.None));
            var entries = await cache.ListAsync("/data", CancellationToken.None);

            Assert.Single(entries);
            IReadOnlyList<FtpEntry> ignored;
            Assert.True(cache.TryGetCached("/data", out ignored));
        }

        [Fact]
        public async Task Invalidate_forces_a_fresh_listing()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            var cache = new DirectoryCache(ftp, new FtpGates());
            await cache.ListAsync("/data", CancellationToken.None);

            ftp.AddFile("/data/b.txt", "b");
            cache.Invalidate("/data");
            var entries = await cache.ListAsync("/data", CancellationToken.None);

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public async Task InvalidateBelow_drops_the_subtree_and_keeps_siblings()
        {
            var ftp = new InMemoryFtpClient().AddDir("/data/a/b").AddDir("/data/c");
            var cache = new DirectoryCache(ftp, new FtpGates());
            await cache.ListAsync("/data/a", CancellationToken.None);
            await cache.ListAsync("/data/a/b", CancellationToken.None);
            await cache.ListAsync("/data/c", CancellationToken.None);

            cache.InvalidateBelow("/data/a");

            IReadOnlyList<FtpEntry> ignored;
            Assert.False(cache.TryGetCached("/data/a", out ignored));
            Assert.False(cache.TryGetCached("/data/a/b", out ignored));
            Assert.True(cache.TryGetCached("/data/c", out ignored));
        }

        [Fact]
        public async Task A_cancelled_waiter_does_not_cancel_the_shared_listing()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a.txt", "a");
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            using (var cts = new CancellationTokenSource())
            {
                var impatient = cache.ListAsync("/data", cts.Token);
                var patient = cache.ListAsync("/data", CancellationToken.None);
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient);

                ftp.ListGate.SetResult(true);
                Assert.Single(await patient);
            }
            Assert.Equal(1, Lists(ftp, "/data"));
        }

        [Fact]
        public async Task At_most_two_listings_run_at_once()
        {
            var ftp = new InMemoryFtpClient().AddDir("/a").AddDir("/b").AddDir("/c");
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            var all = new[] { "/a", "/b", "/c" }.Select(p => cache.ListAsync(p, CancellationToken.None)).ToArray();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (ftp.CallsSnapshot().Count(c => c.StartsWith("LIST")) < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(2, ftp.CallsSnapshot().Count(c => c.StartsWith("LIST")));

            ftp.ListGate.SetResult(true);
            await Task.WhenAll(all);
            Assert.Equal(3, ftp.CallsSnapshot().Count(c => c.StartsWith("LIST")));
        }

        [Fact]
        public async Task A_listing_that_fails_after_an_await_is_not_served_again()
        {
            // "/fehlt" existiert nicht: InMemoryFtpClient wirft NotFound erst NACH
            // dem await auf ListGate — also genau der (asynchrone) Fehlerfall aus
            // dem echten Betrieb, nicht ein synchron scheiterndes LIST.
            var ftp = new InMemoryFtpClient();
            ftp.ListGate = new TaskCompletionSource<bool>();
            var cache = new DirectoryCache(ftp, new FtpGates());

            var pending = cache.ListAsync("/fehlt", CancellationToken.None);
            var gate = ftp.ListGate;
            var ignored = Task.Run(() => gate.SetResult(true));   // Freigabe von einem anderen Thread aus dem Pool
            await Assert.ThrowsAsync<FtpException>(() => pending);

            ftp.AddDir("/fehlt");
            var entries = await cache.ListAsync("/fehlt", CancellationToken.None);

            Assert.Empty(entries);
            Assert.Equal(2, Lists(ftp, "/fehlt"));
        }
    }
}
