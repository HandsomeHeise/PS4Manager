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
    public sealed class ManagerSessionControllerTests
    {
        private static readonly ManagerEndpoint FirstEndpoint = new ManagerEndpoint("ps4-one", 2121);
        private static readonly ManagerEndpoint SecondEndpoint = new ManagerEndpoint("ps4-two", 2121);

        [Fact]
        public async Task A_stale_connection_completion_is_disposed_and_not_published()
        {
            var factory = new ControlledFactory();
            var first = factory.EnqueueBlocked();
            factory.EnqueueSuccessful();
            var controller = new ManagerSessionController(factory);
            var events = new List<ManagerSessionChangedEventArgs>();
            controller.SessionChanged += (sender, args) => events.Add(args);

            var firstConnect = controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
            Assert.Equal(ConnectionState.Connecting, controller.State);
            await controller.ConnectAsync(SecondEndpoint, false, CancellationToken.None);
            var current = controller.Current;
            first.Release();
            await firstConnect;

            Assert.Same(SecondEndpoint, current.Endpoint);
            Assert.Same(current, controller.Current);
            Assert.Equal(ConnectionState.Connected, controller.State);
            Assert.True(first.Session.IsDisposed);
            Assert.Single(events);
            Assert.Same(current, events[0].Session);
        }

        [Fact]
        public async Task Disconnect_publishes_null_after_cancelling_the_old_session()
        {
            var factory = new ControlledFactory();
            factory.EnqueueSuccessful();
            var controller = new ManagerSessionController(factory);
            var events = new List<ManagerSessionChangedEventArgs>();
            controller.SessionChanged += (sender, args) => events.Add(args);
            await controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
            var old = controller.Current;

            await controller.DisconnectAsync();

            Assert.Null(controller.Current);
            Assert.True(old.CancellationToken.IsCancellationRequested);
            Assert.True(old.IsDisposed);
            Assert.Equal(ConnectionState.Disconnected, controller.State);
            Assert.Equal(2, events.Count);
            Assert.Null(events[1].Session);
            Assert.Equal(ConnectionState.Disconnected, events[1].State);
        }

        [Fact]
        public async Task Failed_connect_keeps_the_previous_connected_session()
        {
            var factory = new ControlledFactory();
            factory.EnqueueSuccessful();
            var failed = factory.EnqueueFailed(new IOException("offline"));
            var controller = new ManagerSessionController(factory);
            await controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
            var old = controller.Current;

            await Assert.ThrowsAsync<IOException>(() =>
                controller.ConnectAsync(SecondEndpoint, false, CancellationToken.None));

            Assert.Same(old, controller.Current);
            Assert.Equal(ConnectionState.Connected, controller.State);
            Assert.False(old.IsDisposed);
            Assert.True(failed.Session.IsDisposed);
        }

        [Fact]
        public async Task Disconnect_invalidates_an_in_flight_connection()
        {
            var factory = new ControlledFactory();
            var pending = factory.EnqueueBlocked();
            var controller = new ManagerSessionController(factory);

            var connect = controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
            await controller.DisconnectAsync();
            pending.Release();
            await connect;

            Assert.Null(controller.Current);
            Assert.Equal(ConnectionState.Disconnected, controller.State);
            Assert.True(pending.Session.IsDisposed);
        }

        [Fact]
        public async Task A_successful_replacement_cancels_the_previous_session()
        {
            var factory = new ControlledFactory();
            factory.EnqueueSuccessful();
            factory.EnqueueSuccessful();
            var controller = new ManagerSessionController(factory);
            await controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
            var old = controller.Current;

            await controller.ConnectAsync(SecondEndpoint, true, CancellationToken.None);

            Assert.Same(SecondEndpoint, controller.Current.Endpoint);
            Assert.True(controller.Current.Explorer.ExpertMode);
            Assert.True(old.CancellationToken.IsCancellationRequested);
            Assert.True(old.IsDisposed);
        }

        [Fact]
        public async Task Reentrant_disconnect_preserves_session_change_order()
        {
            var factory = new ControlledFactory();
            factory.EnqueueSuccessful();
            factory.EnqueueSuccessful();
            var controller = new ManagerSessionController(factory);
            var events = new List<ManagerSessionChangedEventArgs>();
            controller.SessionChanged += (sender, args) => events.Add(args);
            await controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
            var old = controller.Current;
            using (old.CancellationToken.Register(() => controller.DisconnectAsync().GetAwaiter().GetResult()))
            {
                await controller.ConnectAsync(SecondEndpoint, false, CancellationToken.None);
            }

            Assert.Null(controller.Current);
            Assert.Equal(3, events.Count);
            Assert.Same(SecondEndpoint, events[1].Session.Endpoint);
            Assert.Equal(ConnectionState.Connected, events[1].State);
            Assert.Null(events[2].Session);
            Assert.Equal(ConnectionState.Disconnected, events[2].State);
        }

        [Fact]
        public async Task Cancelled_check_cannot_publish_when_the_client_ignores_cancellation()
        {
            var factory = new ControlledFactory();
            var pending = factory.EnqueueBlocked();
            var controller = new ManagerSessionController(factory);
            using (var cancellation = new CancellationTokenSource())
            {
                var connect = controller.ConnectAsync(FirstEndpoint, false, cancellation.Token);
                cancellation.Cancel();
                pending.Release();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
            }

            Assert.Null(controller.Current);
            Assert.Equal(ConnectionState.Disconnected, controller.State);
            Assert.True(pending.Session.IsDisposed);
        }

        [Fact]
        public async Task Cancellation_during_a_successful_check_prevents_publication()
        {
            var factory = new ControlledFactory();
            var client = factory.EnqueueSuccessful();
            var controller = new ManagerSessionController(factory);
            using (var cancellation = new CancellationTokenSource())
            {
                client.BeforeSuccess = cancellation.Cancel;

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    controller.ConnectAsync(FirstEndpoint, false, cancellation.Token));
            }

            Assert.Null(controller.Current);
            Assert.Equal(ConnectionState.Disconnected, controller.State);
            Assert.True(client.Session.IsDisposed);
        }

        private sealed class ControlledFactory : IManagerSessionFactory
        {
            private readonly Queue<ControlledClient> _clients = new Queue<ControlledClient>();

            public ControlledClient EnqueueSuccessful() { return Enqueue(new ControlledClient()); }
            public ControlledClient EnqueueBlocked() { return Enqueue(new ControlledClient { Blocked = true }); }
            public ControlledClient EnqueueFailed(Exception failure) { return Enqueue(new ControlledClient { Failure = failure }); }

            private ControlledClient Enqueue(ControlledClient client)
            {
                _clients.Enqueue(client);
                return client;
            }

            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode)
            {
                var client = _clients.Dequeue();
                var explorer = new ExplorerSession(client, expertMode, new NoPrompts(), new NoConflictResolver(),
                    Path.Combine(Path.GetTempPath(), "manager-controller-" + Guid.NewGuid().ToString("N")),
                    new NoChanges(), SystemClock.Instance);
                client.Session = new ManagerSession(endpoint, explorer);
                return client.Session;
            }
        }

        private sealed class ControlledClient : IFtpClient
        {
            private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>();
            public bool Blocked;
            public Exception Failure;
            public Action BeforeSuccess;
            public ManagerSession Session;

            public void Release() { _release.TrySetResult(true); }

            public async Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                if (Blocked) await _release.Task;
                if (Failure != null) throw Failure;
                if (BeforeSuccess != null) BeforeSuccess();
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
            public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct) { return Task.FromResult(false); }
            public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct) { return Task.FromResult(false); }
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
