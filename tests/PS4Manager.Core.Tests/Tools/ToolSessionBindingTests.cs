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

namespace PS4Manager.Core.Tests.Tools
{
    public sealed class ToolSessionBindingTests
    {
        [Fact]
        public async Task Reconnect_detaches_old_before_attaching_new()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            var calls = new List<string>();

            using (var binding = new ToolSessionBinding(
                controller,
                session => calls.Add("attach:" + session.Endpoint.Host),
                session => calls.Add("detach:" + session.Endpoint.Host)))
            {
                await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
                await controller.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None);
            }

            Assert.Equal(
                new[] { "attach:first", "detach:first", "attach:second", "detach:second" },
                calls);
        }

        [Fact]
        public async Task Existing_session_is_attached_and_disposal_detaches_once()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            await controller.ConnectAsync(new ManagerEndpoint("current", 2121), false, CancellationToken.None);
            var calls = new List<string>();
            var binding = new ToolSessionBinding(
                controller,
                session => calls.Add("attach:" + session.Endpoint.Host),
                session => calls.Add("detach:" + session.Endpoint.Host));

            binding.Dispose();
            binding.Dispose();

            Assert.Equal(new[] { "attach:current", "detach:current" }, calls);
        }

        [Fact]
        public async Task Stale_queued_notification_does_not_reattach_the_replaced_session()
        {
            var controller = new ManagerSessionController(new SessionFactory());
            controller.SessionChanged += (sender, args) =>
            {
                if (args.Session != null && args.Session.Endpoint.Host == "first")
                {
                    controller.ConnectAsync(
                        new ManagerEndpoint("second", 2121),
                        false,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
            };
            var calls = new List<string>();

            using (var binding = new ToolSessionBinding(
                controller,
                session => calls.Add("attach:" + session.Endpoint.Host),
                session => calls.Add("detach:" + session.Endpoint.Host)))
            {
                await controller.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None);
            }

            Assert.Equal(new[] { "attach:second", "detach:second" }, calls);
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
                    Path.Combine(Path.GetTempPath(), "tool-session-binding-" + Guid.NewGuid().ToString("N")),
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
