using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Media;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;
using PS4Manager.Gallery.Core.Caching;
using Xunit;

namespace PS4Manager.Core.Tests.Media
{
    public sealed class MediaPreviewSessionTests
    {
        [Fact]
        public async Task Cached_session_A_media_is_not_read_after_session_B_attaches()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var preview = new MediaSessionGuard(controller, controller.Current);
            var cacheRead = false;

            await controller.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None);

            Assert.Throws<OperationCanceledException>(() => preview.ReadCache(() =>
            {
                cacheRead = true;
                return new byte[] { 1, 2, 3 };
            }));
            Assert.False(cacheRead);
            Assert.False(preview.IsActive);
        }

        [Fact]
        public async Task Async_session_A_completion_is_rejected_after_session_B_attaches()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var preview = new MediaSessionGuard(controller, controller.Current);
            var completion = new TaskCompletionSource<byte[]>();
            var read = preview.RunAsync(token => completion.Task);

            await controller.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None);
            completion.SetResult(new byte[] { 4, 5, 6 });

            await Assert.ThrowsAsync<OperationCanceledException>(() => read);
            Assert.False(preview.IsActive);
        }

        [Fact]
        public async Task Replacement_cannot_attach_between_owner_check_and_cache_put_or_publish()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var first = controller.Current;
            var preview = new MediaSessionGuard(controller, first);
            Task replacement = null;
            var root = Path.Combine(Path.GetTempPath(), "media-mutation-" + Guid.NewGuid().ToString("N"));
            var images = new ThumbnailCache(Path.Combine(root, "images"));
            var videos = new MediaFileCache(Path.Combine(root, "videos"), 1024 * 1024);
            var stagedImage = images.Stage("CUSA00001", "shot.jpg", new byte[] { 1, 2, 3 });
            var part = videos.BeginStagedWrite("CUSA00001", "clip.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(part));
            File.WriteAllBytes(part, new byte[] { 4, 5, 6 });

            try
            {
                preview.Publish(
                    () =>
                    {
                        var attempt = StartReplacement(controller);
                        AssertReplacementBlockedAtController(attempt);
                        replacement = attempt.Completion;

                        images.Publish("CUSA00001", "shot.jpg", stagedImage);
                        var published = videos.Publish("CUSA00001", "clip.mp4", part);
                        Assert.Same(first, controller.Current);
                        return published;
                    },
                    published => videos.Maintain(published));

                await replacement;
                Assert.Equal(new byte[] { 1, 2, 3 }, images.TryGet("CUSA00001", "shot.jpg"));
                Assert.NotNull(videos.TryGetPath("CUSA00001", "clip.mp4"));
                Assert.Equal("second", controller.Current.Endpoint.Host);
            }
            finally
            {
                images.Discard(stagedImage);
                videos.Discard(part);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task Replacement_cannot_attach_between_owner_check_and_preview_title_render_or_play()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var first = controller.Current;
            var preview = new MediaSessionGuard(controller, first);
            Task replacement = null;
            string title = null;
            var rendered = false;
            var played = false;

            preview.Mutate(() =>
            {
                var attempt = StartReplacement(controller);
                AssertReplacementBlockedAtController(attempt);
                replacement = attempt.Completion;

                title = "session A preview";
                rendered = true;
                played = true;
                Assert.Same(first, controller.Current);
            });

            await replacement;
            Assert.Equal("session A preview", title);
            Assert.True(rendered);
            Assert.True(played);
            Assert.Equal("second", controller.Current.Endpoint.Host);
        }

        [Fact]
        public async Task Replacement_can_attach_while_post_publish_maintenance_is_running()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var preview = new MediaSessionGuard(controller, controller.Current);
            var maintenanceStarted = new ManualResetEventSlim();
            var releaseMaintenance = new ManualResetEventSlim();

            var publication = StartWork(() => preview.Publish(
                () => "visible.mp4",
                path =>
                {
                    maintenanceStarted.Set();
                    releaseMaintenance.Wait();
                }));

            Assert.True(maintenanceStarted.Wait(TimeSpan.FromSeconds(5)));
            try
            {
                await controller.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None);
                Assert.Equal("second", controller.Current.Endpoint.Host);
                Assert.False(publication.IsCompleted);
            }
            finally
            {
                releaseMaintenance.Set();
            }
            await publication;
        }

        [Fact]
        public async Task Concurrent_video_publication_waits_for_maintenance_without_blocking_replacement()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var first = new MediaSessionGuard(controller, controller.Current);
            var root = Path.Combine(Path.GetTempPath(), "media-publication-" + Guid.NewGuid().ToString("N"));
            var cache = new MediaFileCache(root, 1000);
            var stageA = cache.BeginStagedWrite("CUSA00001", "a.mp4");
            var stageB = cache.BeginStagedWrite("CUSA00001", "b.mp4");
            File.WriteAllBytes(stageA, new byte[800]);
            File.WriteAllBytes(stageB, new byte[800]);
            using (var maintenanceStarted = new ManualResetEventSlim())
            using (var releaseMaintenance = new ManualResetEventSlim())
            {
                var a = StartWork(() => PublishVideo(cache, first, "a.mp4", stageA, () =>
                {
                    maintenanceStarted.Set();
                    releaseMaintenance.Wait();
                }));
                ReplacementAttempt b = null;
                try
                {
                    Assert.True(maintenanceStarted.Wait(TimeSpan.FromSeconds(5)));
                    var replacement = StartReplacement(controller);
                    Assert.Same(replacement.Completion, await Task.WhenAny(
                        replacement.Completion, Task.Delay(TimeSpan.FromSeconds(5))));
                    await replacement.Completion;
                    var second = new MediaSessionGuard(controller, controller.Current);
                    string returnedB = null;
                    b = StartAttempt(() => returnedB = PublishVideo(cache, second, "b.mp4", stageB));

                    AssertAttemptBlocked(b);
                    Assert.True(File.Exists(cache.PathFor("CUSA00001", "a.mp4")));
                    Assert.False(File.Exists(cache.PathFor("CUSA00001", "b.mp4")));
                    Assert.True(File.Exists(stageB));

                    // A's maintenance and B's wait must not own the controller lock.
                    Assert.True(second.TryMutate(() => { }));
                    releaseMaintenance.Set();
                    await a;
                    await b.Completion;

                    Assert.Equal(cache.PathFor("CUSA00001", "b.mp4"), returnedB);
                    Assert.True(File.Exists(returnedB));
                    Assert.False(File.Exists(cache.PathFor("CUSA00001", "a.mp4")));
                }
                finally
                {
                    releaseMaintenance.Set();
                    await a;
                    if (b != null) await b.Completion;
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public async Task Session_replaced_while_waiting_for_cache_cannot_publish_or_delete_replacement_stage()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var first = new MediaSessionGuard(controller, controller.Current);
            var root = Path.Combine(Path.GetTempPath(), "media-publication-" + Guid.NewGuid().ToString("N"));
            var cache = new MediaFileCache(root, 1000);
            var blockerStage = cache.BeginStagedWrite("CUSA00001", "blocker.mp4");
            var stageA = cache.BeginStagedWrite("CUSA00001", "clip.mp4");
            var stageB = cache.BeginStagedWrite("CUSA00001", "clip.mp4");
            File.WriteAllBytes(blockerStage, new byte[800]);
            File.WriteAllBytes(stageA, new byte[] { 1 });
            File.WriteAllBytes(stageB, new byte[] { 2 });
            using (var maintenanceStarted = new ManualResetEventSlim())
            using (var releaseMaintenance = new ManualResetEventSlim())
            {
                var blocker = StartWork(() => PublishVideo(cache, first, "blocker.mp4", blockerStage, () =>
                {
                    maintenanceStarted.Set();
                    releaseMaintenance.Wait();
                }));
                ReplacementAttempt waiting = null;
                try
                {
                    Assert.True(maintenanceStarted.Wait(TimeSpan.FromSeconds(5)));
                    waiting = StartAttempt(() =>
                    {
                        try { PublishVideo(cache, first, "clip.mp4", stageA); }
                        finally { cache.Discard(stageA); }
                    });
                    AssertAttemptBlocked(waiting);
                    var replacement = StartReplacement(controller);
                    Assert.Same(replacement.Completion, await Task.WhenAny(
                        replacement.Completion, Task.Delay(TimeSpan.FromSeconds(5))));
                    await replacement.Completion;

                    releaseMaintenance.Set();
                    await blocker;
                    await Assert.ThrowsAsync<OperationCanceledException>(() => waiting.Completion);
                    Assert.False(File.Exists(stageA));
                    Assert.True(File.Exists(stageB));
                    Assert.False(File.Exists(cache.PathFor("CUSA00001", "clip.mp4")));

                    var second = new MediaSessionGuard(controller, controller.Current);
                    var returnedB = PublishVideo(cache, second, "clip.mp4", stageB);
                    Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(returnedB));
                }
                finally
                {
                    releaseMaintenance.Set();
                    await blocker;
                    if (waiting != null)
                    {
                        try { await waiting.Completion; }
                        catch (OperationCanceledException) { }
                    }
                    Directory.Delete(root, true);
                }
            }
        }

        private static string PublishVideo(MediaFileCache cache, MediaSessionGuard guard,
            string name, string stage, Action beforeMaintenance = null)
        {
            return guard.Publish(() => cache.Publish("CUSA00001", name, stage), path =>
            {
                beforeMaintenance?.Invoke();
                cache.Maintain(path);
            });
        }

        [Fact]
        public async Task Replacement_can_attach_while_a_slow_cache_read_is_still_running()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            var preview = new MediaSessionGuard(controller, controller.Current);
            var readStarted = new ManualResetEventSlim();
            var releaseRead = new ManualResetEventSlim();

            var read = StartWork(() => preview.ReadCache(() =>
            {
                readStarted.Set();
                releaseRead.Wait();
                return new byte[] { 1 };
            }));

            Assert.True(readStarted.Wait(TimeSpan.FromSeconds(5)));
            var replacement = StartReplacement(controller);
            Assert.True(replacement.Started.Wait(TimeSpan.FromSeconds(5)));
            try
            {
                var completed = await Task.WhenAny(
                    replacement.Completion,
                    Task.Delay(TimeSpan.FromSeconds(1)));
                Assert.Same(replacement.Completion, completed);
                await replacement.Completion;
                Assert.Equal("second", controller.Current.Endpoint.Host);
            }
            finally
            {
                releaseRead.Set();
            }

            await Assert.ThrowsAsync<OperationCanceledException>(() => read);
        }

        private static ReplacementAttempt StartReplacement(ManagerSessionController controller)
        {
            return StartAttempt(() => controller.ConnectAsync(
                new ManagerEndpoint("second", 2121), false, CancellationToken.None).GetAwaiter().GetResult());
        }

        private static ReplacementAttempt StartAttempt(Action work)
        {
            var started = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    started.Set();
                    work();
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return new ReplacementAttempt(thread, started, completion.Task);
        }

        private static void AssertReplacementBlockedAtController(ReplacementAttempt attempt)
        {
            AssertAttemptBlocked(attempt);
        }

        private static void AssertAttemptBlocked(ReplacementAttempt attempt)
        {
            Assert.True(attempt.Started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(
                () => (attempt.Thread.ThreadState & ThreadState.WaitSleepJoin) != 0 ||
                      attempt.Completion.IsCompleted,
                TimeSpan.FromSeconds(5)));
            Assert.False(attempt.Completion.IsCompleted);
            Assert.True((attempt.Thread.ThreadState & ThreadState.WaitSleepJoin) != 0);
        }

        private static Task StartWork(Action work)
        {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    work();
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }

        private sealed class ReplacementAttempt
        {
            public ReplacementAttempt(Thread thread, ManualResetEventSlim started, Task completion)
            {
                Thread = thread;
                Started = started;
                Completion = completion;
            }

            public Thread Thread { get; }
            public ManualResetEventSlim Started { get; }
            public Task Completion { get; }
        }

        private sealed class SessionFactory : IManagerSessionFactory
        {
            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode)
            {
                var explorer = new ExplorerSession(
                    new EmptyFtpClient(),
                    expertMode,
                    new NoPrompts(),
                    new NoConflictResolver(),
                    Path.Combine(Path.GetTempPath(), "media-preview-session-" + Guid.NewGuid().ToString("N")),
                    new NoChanges(),
                    SystemClock.Instance);
                return new ManagerSession(endpoint, explorer);
            }
        }

        private sealed class EmptyFtpClient : IFtpClient
        {
            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                return Task.FromResult((IReadOnlyList<FtpEntry>)new FtpEntry[0]);
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
