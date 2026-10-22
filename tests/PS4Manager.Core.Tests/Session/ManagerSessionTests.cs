using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.Session
{
    public sealed class ManagerSessionTests
    {
        private static readonly ManagerEndpoint Endpoint = new ManagerEndpoint("192.168.0.10", 2121);

        [Fact]
        public void Explorer_and_media_share_the_same_guarded_client_and_transfer_graph()
        {
            using (var session = CreateSession())
            {
                Assert.Same(session.Explorer.Ftp, session.MediaClient);
                Assert.Same(session.Explorer.Gates, session.Gates);
                Assert.Same(session.Explorer.Queue, session.Queue);
                Assert.Same(Endpoint, session.Endpoint);
                Assert.NotNull(session.Media);
            }
        }

        [Fact]
        public void Cancel_cancels_the_session_token_and_dispose_is_idempotent()
        {
            var session = CreateSession();
            var token = session.CancellationToken;

            session.Cancel();
            Assert.True(token.IsCancellationRequested);
            session.Dispose();
            session.Dispose();

            Assert.True(session.IsDisposed);
            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        public void Dispose_cancels_before_disposing_the_explorer()
        {
            var changes = new RecordingChangeSource();
            var session = CreateSession(changes);
            changes.OnDispose = () => Assert.True(session.CancellationToken.IsCancellationRequested);

            session.Dispose();

            Assert.Equal(1, changes.DisposeCount);
        }

        [Fact]
        public void Dispose_releases_the_explorer_even_when_a_cancel_callback_fails()
        {
            var changes = new RecordingChangeSource();
            var session = CreateSession(changes);
            using (session.CancellationToken.Register(() => { throw new InvalidOperationException("callback"); }))
            {
                Assert.Throws<AggregateException>(() => session.Dispose());
            }

            Assert.Equal(1, changes.DisposeCount);
            session.Dispose();
        }

        [Fact]
        public void Cancel_callback_can_wait_for_concurrent_dispose()
        {
            var changes = new RecordingChangeSource();
            var session = CreateSession(changes);
            var workerStarted = new ManualResetEventSlim();
            var workerFinished = new ManualResetEventSlim();
            Thread worker = null;
            Exception workerFailure = null;

            using (session.CancellationToken.Register(() =>
            {
                worker = new Thread(() =>
                {
                    workerStarted.Set();
                    try { session.Dispose(); }
                    catch (Exception ex) { workerFailure = ex; }
                    finally { workerFinished.Set(); }
                });
                worker.IsBackground = true;
                worker.Start();
                Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(3)), "Dispose worker did not start.");
                Assert.True(workerFinished.Wait(TimeSpan.FromSeconds(3)), "Dispose could not finish during the cancellation callback.");
            }))
            {
                try { session.Cancel(); }
                finally
                {
                    session.Dispose();
                    if (worker != null) Assert.True(worker.Join(3000));
                }
            }

            Assert.Null(workerFailure);
            Assert.Equal(1, changes.DisposeCount);
        }

        [Fact]
        public async Task Media_loading_uses_the_explorer_browse_budget()
        {
            var ftp = new InMemoryFtpClient();
            using (var session = CreateSession(null, ftp))
            {
                Assert.True(session.Gates.Browse.Wait(0));
                Assert.True(session.Gates.Browse.Wait(0));
                var heldSlots = 2;
                try
                {
                    var loading = session.Media.LoadAsync(session.CancellationToken);
                    Assert.Equal(0, ftp.ListCalls);
                    session.Gates.Browse.Release(heldSlots);
                    heldSlots = 0;
                    await loading;
                    Assert.Equal(2, ftp.ListCalls);
                }
                finally
                {
                    if (heldSlots != 0) session.Gates.Browse.Release(heldSlots);
                }
            }
        }

        [Theory]
        [InlineData(null, 2121)]
        [InlineData("", 2121)]
        [InlineData("   ", 2121)]
        [InlineData("ftp://ps4", 2121)]
        [InlineData("ps4/path", 2121)]
        [InlineData("ps4\r\n", 2121)]
        [InlineData("ps4", 0)]
        [InlineData("ps4", 65536)]
        public void Endpoint_rejects_invalid_hosts_and_ports(string host, int port)
        {
            Assert.ThrowsAny<ArgumentException>(() => new ManagerEndpoint(host, port));
        }

        [Fact]
        public void Endpoint_accepts_valid_host_and_port_boundaries()
        {
            var first = new ManagerEndpoint("  ps4.local  ", 1);
            var last = new ManagerEndpoint("192.168.0.10", 65535);

            Assert.Equal("ps4.local", first.Host);
            Assert.Equal(1, first.Port);
            Assert.Equal(65535, last.Port);
        }

        [Fact]
        public void Endpoint_brackets_a_bare_ipv6_host_for_ftp_uris()
        {
            var endpoint = new ManagerEndpoint("::1", 2121);

            Assert.Equal("[::1]", endpoint.Host);
        }

        private static ManagerSession CreateSession(RecordingChangeSource changes = null, InMemoryFtpClient ftp = null)
        {
            var explorer = new ExplorerSession(ftp ?? new InMemoryFtpClient(), false,
                new NoPrompts(), new NoConflictResolver(),
                Path.Combine(Path.GetTempPath(), "manager-session-" + Guid.NewGuid().ToString("N")),
                changes ?? new RecordingChangeSource(), SystemClock.Instance);
            return new ManagerSession(Endpoint, explorer);
        }

        private sealed class NoPrompts : IUserPrompts
        {
            public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
            {
                return Task.FromResult(false);
            }

            public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
            {
                return Task.FromResult(false);
            }
        }

        private sealed class NoConflictResolver : IConflictResolver
        {
            public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
            {
                return Task.FromResult(new ConflictDecision(ConflictChoice.Skip, false));
            }
        }

        private sealed class RecordingChangeSource : IFileChangeSource
        {
            public event EventHandler<string> Changed { add { } remove { } }
            public Action OnDispose;
            public int DisposeCount;

            public void Watch(string directory) { }
            public void Dispose()
            {
                DisposeCount++;
                if (OnDispose != null) OnDispose();
            }
        }

        private sealed class InMemoryFtpClient : IFtpClient
        {
            public int ListCalls;
            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                ListCalls++;
                return Task.FromResult<IReadOnlyList<FtpEntry>>(new FtpEntry[0]);
            }
            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DeleteAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        }
    }
}
