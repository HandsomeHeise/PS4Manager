using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Luma;
using PS4Manager.Core.Luma.Studio;
using Xunit;

namespace PS4Manager.Core.Tests.Luma.Studio
{
    public sealed class StudioConnectionTests
    {
        [Fact]
        public async Task Authenticated_upload_is_one_PUT_with_no_implicit_reload_or_execute()
        {
            var transport = new ReplyTransport();
            using (var session = Create(transport)) {
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "token", CancellationToken.None);
                long generation = session.Generation;
                await session.UploadAsync("hello.lua", "print('hi')", generation, CancellationToken.None);
                Assert.Equal(new[] { LumaOperation.Hello, LumaOperation.Put }, transport.Operations.ToArray());
                Assert.Equal("token", transport.Payloads[0]);
                Assert.Equal("scripts/hello.lua\0print('hi')", transport.Payloads[1]);
                await session.ReloadAsync(CancellationToken.None);
                Assert.Equal(LumaOperation.Reload, transport.Operations.Last());
            }
        }

        [Theory]
        [InlineData("../evil.lua")]
        [InlineData("folder/a.lua")]
        [InlineData("a.lua\0bad")]
        [InlineData("a.txt")]
        public async Task Unsafe_upload_names_never_reach_transport(string name)
        {
            var transport = new ReplyTransport();
            using (var session = Create(transport)) {
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
                await Assert.ThrowsAsync<ArgumentException>(() => session.UploadAsync(name, "return 1", session.Generation, CancellationToken.None));
                Assert.Single(transport.Operations);
            }
        }

        [Fact]
        public async Task Invalid_Lua_and_old_upload_authorization_cannot_send_code()
        {
            var transport = new ReplyTransport();
            using (var session = Create(transport)) {
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
                long old = session.Generation;
                await Assert.ThrowsAsync<LumaException>(() => session.ExecuteAsync("local =", CancellationToken.None));
                session.Disconnect();
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9616), "rdr2", "", CancellationToken.None);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.UploadAsync("x.lua", "return 1", old, CancellationToken.None));
                Assert.Equal(new[] { LumaOperation.Hello, LumaOperation.Hello }, transport.Operations.ToArray());
                Assert.Equal("rdr2", session.Profile);
            }
        }

        [Fact]
        public async Task Disconnect_cancels_active_request_and_reconnect_uses_a_new_client()
        {
            var first = new ReplyTransport { BlockPing = true };
            var second = new ReplyTransport();
            int created = 0;
            using (var session = new StudioConnection(() => new LumaClient(created++ == 0 ? first : second, 1))) {
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
                var active = session.PollAsync(CancellationToken.None);
                await first.PingEntered.Task;
                var queued = session.ReloadAsync(CancellationToken.None);
                session.Disconnect();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9616), "rdr2", "", CancellationToken.None);
                await session.ExecuteAsync("return 42", CancellationToken.None);
                Assert.Equal(new[] { LumaOperation.Hello, LumaOperation.Exec }, second.Operations.ToArray());
                Assert.DoesNotContain(LumaOperation.Reload, first.Operations);
            }
        }

        [Fact]
        public async Task Execute_preserves_a_unique_chunk_label_for_runtime_diagnostic_provenance()
        {
            var transport = new ReplyTransport();
            using (var session = Create(transport)) {
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
                await session.ExecuteAsync("return 42", "@studio-abc.lua", CancellationToken.None);
                Assert.Equal("@studio-abc.lua\0return 42", transport.Payloads.Last());
            }
        }

        private static StudioConnection Create(ReplyTransport transport) => new StudioConnection(() => new LumaClient(transport, 1));

        [Theory]
        [InlineData("gta5", 9616)]
        [InlineData("rdr2", 9615)]
        public async Task Mismatched_profile_endpoint_is_rejected_before_authentication_or_upload(string profile, int port)
        {
            var transport = new ReplyTransport();
            using (var session = Create(transport)) {
                await Assert.ThrowsAsync<ArgumentException>(() => session.ConnectAsync(new LumaEndpoint("offline.test", port), profile, "", CancellationToken.None));
                Assert.Empty(transport.Operations);
                Assert.False(session.Connected);
            }
        }
        [Fact]
        public async Task Upload_plan_names_the_actual_relative_target_and_explicitly_unverified_sandbox()
        {
            var transport = new ReplyTransport();
            using (var session = Create(transport)) {
                await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
                var plan = session.PrepareUpload("hello.lua");
                Assert.Equal("scripts/hello.lua", plan.RelativePath);
                Assert.Contains("not verified", plan.ConfirmationText);
                Assert.Contains("offline.test:9615", plan.ConfirmationText);
                Assert.Contains("correct game", plan.ConfirmationText);
                await session.UploadAsync(plan, "return 1", CancellationToken.None);
                Assert.Equal(plan.RelativePath + "\0return 1", transport.Payloads.Last());
                session.Disconnect();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.UploadAsync(plan, "return 1", CancellationToken.None));
            }
        }

        private sealed class ReplyTransport : ILumaTransport
        {
            public readonly System.Collections.Generic.List<LumaOperation> Operations = new System.Collections.Generic.List<LumaOperation>();
            public readonly System.Collections.Generic.List<string> Payloads = new System.Collections.Generic.List<string>();
            public readonly TaskCompletionSource<bool> PingEntered = new TaskCompletionSource<bool>();
            public bool BlockPing;
            public Task<ILumaConnection> ConnectAsync(LumaEndpoint endpoint, CancellationToken ct) => Task.FromResult<ILumaConnection>(new Connection(this));
            private sealed class Connection : Stream, ILumaConnection
            {
                private readonly ReplyTransport owner;
                private MemoryStream reply;
                public Connection(ReplyTransport owner) { this.owner = owner; }
                Stream ILumaConnection.Read => this;
                Stream ILumaConnection.Write => this;
                public override Task WriteAsync(byte[] b, int o, int n, CancellationToken ct) {
                    var op = (LumaOperation)b[o + 4]; owner.Operations.Add(op);
                    owner.Payloads.Add(Encoding.UTF8.GetString(b, o + 12, n - 12));
                    if (op == LumaOperation.Ping && owner.BlockPing) { reply = null; owner.PingEntered.TrySetResult(true); }
                    else reply = new MemoryStream(LumaProtocol.EncodeFrame(op == LumaOperation.Hello ? op : LumaOperation.Ok, BitConverter.ToUInt16(b, o + 6), Encoding.UTF8.GetBytes("ok")));
                    return Task.CompletedTask;
                }
                public override async Task<int> ReadAsync(byte[] b, int o, int n, CancellationToken ct) {
                    if (reply == null) { await Task.Delay(Timeout.Infinite, ct); return 0; }
                    return await reply.ReadAsync(b, o, n, ct);
                }
                public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
                public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
                public override void Flush() { } public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
                public override int Read(byte[] b, int o, int n) => throw new NotSupportedException();
                public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
                public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long l) => throw new NotSupportedException();
            }
        }
    }
}
