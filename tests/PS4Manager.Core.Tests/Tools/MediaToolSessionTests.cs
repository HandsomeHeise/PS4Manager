using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Media;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;
using PS4Manager.Gallery.Core.Ps4;
using Xunit;

namespace PS4Manager.Core.Tests.Tools
{
    public sealed class MediaToolSessionTests
    {
        [Fact]
        public async Task Load_from_replaced_session_is_ignored_when_it_completes_late()
        {
            var first = new ControlledFtpClient(true);
            var second = new ControlledFtpClient(false);
            var controller = new ManagerSessionController(new SessionFactory(first, second));
            var loaded = new List<string>();
            var secondLoaded = new TaskCompletionSource<bool>();

            using (var tool = NewTool(controller,
                (session, games) =>
                {
                    loaded.Add(session.Endpoint.Host);
                    if (session.Endpoint.Host == "second") secondLoaded.TrySetResult(true);
                }))
            {
                await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
                await first.MediaLoadStarted.Task;

                await controller.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None);
                await secondLoaded.Task;
                first.ReleaseMediaLoad();
                await first.MediaLoadReleased.Task;

                Assert.Equal(new[] { "second" }, loaded);
            }
        }

        [Fact]
        public async Task Detaching_cancels_the_token_used_by_queued_thumbnail_requests()
        {
            var ftp = new ControlledFtpClient(true);
            var controller = new ManagerSessionController(new SessionFactory(ftp));
            CancellationToken thumbnailToken = CancellationToken.None;

            using (var tool = new MediaToolSession(
                controller,
                (session, token) => thumbnailToken = token,
                session => { },
                (session, games) => { },
                (session, error) => { }))
            {
                await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
                await ftp.MediaLoadStarted.Task;

                await controller.DisconnectAsync();

                Assert.True(thumbnailToken.IsCancellationRequested);
                ftp.ReleaseMediaLoad();
            }
        }

        [Fact]
        public async Task Attaching_replacement_performs_one_fresh_repository_load()
        {
            var first = new ControlledFtpClient(false);
            var second = new ControlledFtpClient(false);
            var controller = new ManagerSessionController(new SessionFactory(first, second));
            var loads = new List<string>();
            var secondLoaded = new TaskCompletionSource<bool>();

            using (var tool = NewTool(controller,
                (session, games) =>
                {
                    loads.Add(session.Endpoint.Host);
                    if (session.Endpoint.Host == "second") secondLoaded.TrySetResult(true);
                }))
            {
                await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
                await WaitUntilAsync(() => loads.Contains("first"));
                await controller.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None);
                await secondLoaded.Task;

                Assert.Equal(2, second.MediaRootListCalls);
                Assert.Equal(1, loads.Count(host => host == "second"));
            }
        }

        [Fact]
        public async Task Reload_starts_one_new_repository_load_for_the_current_session()
        {
            var ftp = new ControlledFtpClient(false);
            var controller = new ManagerSessionController(new SessionFactory(ftp));
            var loadCount = 0;

            using (var tool = NewTool(controller, (session, games) => Interlocked.Increment(ref loadCount)))
            {
                await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
                await WaitUntilAsync(() => Volatile.Read(ref loadCount) == 1);

                tool.Reload();
                await WaitUntilAsync(() => Volatile.Read(ref loadCount) == 2);

                Assert.Equal(4, ftp.MediaRootListCalls);
            }
        }

        private static MediaToolSession NewTool(
            ManagerSessionController controller,
            Action<ManagerSession, IReadOnlyList<GameFolder>> loaded)
        {
            return new MediaToolSession(
                controller,
                (session, token) => { },
                session => { },
                loaded,
                (session, error) => { throw error; });
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!condition())
            {
                if (DateTime.UtcNow >= until) throw new TimeoutException("Condition was not reached.");
                await Task.Delay(10);
            }
        }

        private sealed class SessionFactory : IManagerSessionFactory
        {
            private readonly Queue<ControlledFtpClient> _clients;

            public SessionFactory(params ControlledFtpClient[] clients)
            {
                _clients = new Queue<ControlledFtpClient>(clients);
            }

            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode)
            {
                var explorer = new ExplorerSession(
                    _clients.Dequeue(),
                    expertMode,
                    new NoPrompts(),
                    new NoConflictResolver(),
                    Path.Combine(Path.GetTempPath(), "media-tool-session-" + Guid.NewGuid().ToString("N")),
                    new NoChanges(),
                    SystemClock.Instance);
                return new ManagerSession(endpoint, explorer);
            }
        }

        private sealed class ControlledFtpClient : IFtpClient
        {
            private readonly bool _blockMediaLoad;
            private readonly TaskCompletionSource<bool> _releaseMedia = new TaskCompletionSource<bool>();

            public ControlledFtpClient(bool blockMediaLoad)
            {
                _blockMediaLoad = blockMediaLoad;
                MediaLoadStarted = new TaskCompletionSource<bool>();
                MediaLoadReleased = new TaskCompletionSource<bool>();
            }

            public TaskCompletionSource<bool> MediaLoadStarted { get; }
            public TaskCompletionSource<bool> MediaLoadReleased { get; }
            public int MediaRootListCalls { get; private set; }

            public void ReleaseMediaLoad()
            {
                _releaseMedia.TrySetResult(true);
            }

            public async Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                if (path == "/") return new FtpEntry[0];
                if (path == Ps4Paths.MediaRoot(MediaKind.Photo) ||
                    path == Ps4Paths.MediaRoot(MediaKind.Video))
                {
                    MediaRootListCalls++;
                    MediaLoadStarted.TrySetResult(true);
                    if (_blockMediaLoad) await _releaseMedia.Task;
                    MediaLoadReleased.TrySetResult(true);
                    return new FtpEntry[0];
                }
                return new FtpEntry[0];
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

        private sealed class NoChanges : IFileChangeSource
        {
            public event EventHandler<string> Changed { add { } remove { } }
            public void Watch(string directory) { }
            public void Dispose() { }
        }
    }
}
