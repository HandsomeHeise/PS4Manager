using System;
using System.Collections.Generic;
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
    public sealed class StudioProjectSyncTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "studio-sync-" + Guid.NewGuid().ToString("N"));
        private readonly ProjectTransport wire = new ProjectTransport();
        private readonly StudioConnection session;
        public StudioProjectSyncTests() { Directory.CreateDirectory(root); session = new StudioConnection(() => new LumaClient(wire, 1)); }
        public void Dispose() { session.Dispose(); Directory.Delete(root, true); }
        private void Local(string name, string source) { string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, source, new UTF8Encoding(false)); }
        private async Task<StudioProjectPreview> Compare()
        {
            if (!session.Connected) await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
            return await StudioProjectSync.CompareAsync(session, StudioWorkspace.Open(root, "gta5"), CancellationToken.None);
        }
        [Fact] public async Task Compare_and_selected_nested_upload_use_real_wire_paths_without_reload()
        {
            Local("a.lua", "return 1"); Local("b.lua", "return 2"); Local("lib/new.lua", "return 3");
            wire.Files["scripts/a.lua"] = "return 1"; wire.Files["scripts/b.lua"] = "return 0"; wire.Files["scripts/remote.lua"] = "return 4";
            var preview = await Compare();
            Assert.Equal(new[] { StudioSyncStatus.Unchanged, StudioSyncStatus.Changed, StudioSyncStatus.New, StudioSyncStatus.RemoteOnly }, preview.Entries.Select(e => e.Status));
            Assert.Contains("not verified", preview.ConfirmationText); Assert.Contains("offline.test:9615", preview.ConfirmationText);
            var completed = new List<string>();
            await StudioProjectSync.UploadAsync(session, preview, new[] { "lib/new.lua" }, completed.Add, CancellationToken.None);
            Assert.Equal("return 3", wire.Files["scripts/lib/new.lua"]);
            Assert.Equal("return 0", wire.Files["scripts/b.lua"]);
            Assert.Equal(new[] { "lib/new.lua" }, completed);
            Assert.DoesNotContain(wire.Operations, op => op == LumaOperation.Exec || op == LumaOperation.Reload || op == LumaOperation.Rm);
            Assert.Contains("scripts/lib", wire.Paths); Assert.Contains("scripts/a.lua", wire.Paths);
        }
        [Fact] public async Task Changed_remote_since_preview_blocks_replacement()
        {
            Local("a.lua", "return 2"); wire.Files["scripts/a.lua"] = "return 1";
            var preview = await Compare(); wire.Files["scripts/a.lua"] = "return 3";
            await Assert.ThrowsAsync<IOException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua" }, null, CancellationToken.None));
            Assert.DoesNotContain(LumaOperation.Put, wire.Operations); Assert.Equal("return 3", wire.Files["scripts/a.lua"]);
        }
        [Fact] public async Task Changed_local_disk_blocks_entire_batch_before_first_write()
        {
            Local("a.lua", "return 1"); Local("b.lua", "return 2"); var preview = await Compare(); Local("b.lua", "return 3");
            await Assert.ThrowsAsync<StudioFileConflictException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua", "b.lua" }, null, CancellationToken.None));
            Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }
        [Fact] public async Task All_selected_syntax_is_checked_before_any_upload()
        {
            Local("a.lua", "return 1"); Local("b.lua", "local ="); var preview = await Compare();
            await Assert.ThrowsAsync<LumaException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua", "b.lua" }, null, CancellationToken.None));
            Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }
        [Fact] public async Task Reconnected_session_cannot_use_old_preview()
        {
            Local("a.lua", "return 1"); var preview = await Compare(); session.Disconnect();
            await session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua" }, null, CancellationToken.None));
            Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }
        [Theory] [InlineData("-- truncated: listing incomplete --\n")] [InlineData("../escape.lua\n")]
        [InlineData("a.lua\na.lua\n")]
        public async Task Invalid_or_incomplete_listing_cannot_authorize_new_file(string listing)
        {
            Local("a.lua", "return 1"); wire.ListOverride = listing;
            await Assert.ThrowsAsync<IOException>(() => Compare()); Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }
        [Fact] public async Task Remote_read_failure_is_not_mistaken_for_absence()
        {
            Local("a.lua", "return 1"); wire.Files["scripts/a.lua"] = "return 0"; wire.FailGet = true;
            await Assert.ThrowsAsync<LumaException>(() => Compare());
        }
        [Fact] public async Task Partial_failure_reports_only_successes_and_never_retries()
        {
            Local("a.lua", "return 1"); Local("b.lua", "return 2"); var preview = await Compare(); wire.FailPut = "scripts/b.lua";
            var completed = new List<string>();
            await Assert.ThrowsAsync<LumaException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua", "b.lua" }, completed.Add, CancellationToken.None));
            Assert.Equal(new[] { "a.lua" }, completed); Assert.Equal(2, wire.Operations.Count(op => op == LumaOperation.Put));
            Assert.Equal("return 1", wire.Files["scripts/a.lua"]); Assert.False(wire.Files.ContainsKey("scripts/b.lua"));
        }
        [Fact] public async Task Utf8_bom_is_preserved_byte_for_byte()
        {
            Local("a.lua", "\ufeffreturn 1"); var preview = await Compare();
            await StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua" }, null, CancellationToken.None);
            Assert.Equal("\ufeffreturn 1", wire.Files["scripts/a.lua"]);
        }
        [Fact] public async Task Wrong_profile_is_rejected_without_reading_remote_files()
        {
            await session.ConnectAsync(new LumaEndpoint("offline.test", 9616), "rdr2", "", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => StudioProjectSync.CompareAsync(session, StudioWorkspace.Open(root, "gta5"), CancellationToken.None));
            Assert.Single(wire.Operations);
        }
        [Fact] public async Task Remote_case_collision_is_not_treated_as_a_new_file()
        {
            Local("a.lua", "return 1"); wire.Files["scripts/A.lua"] = "return 0";
            await Assert.ThrowsAsync<IOException>(() => Compare()); Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }
        [Theory] [InlineData("return '\0'")] [InlineData("\u001bLua")]
        public async Task Local_binary_is_rejected_before_any_remote_listing(string source)
        {
            Local("a.lua", source); await Assert.ThrowsAsync<IOException>(() => Compare()); Assert.Single(wire.Operations);
        }
        [Fact] public async Task Oversized_local_file_is_rejected_before_any_remote_listing()
        {
            Local("a.lua", new string('x', 1024 * 1024 + 1));
            await Assert.ThrowsAsync<IOException>(() => Compare()); Assert.Single(wire.Operations);
        }
        [Fact] public async Task Remote_creation_after_preview_is_not_overwritten()
        {
            Local("a.lua", "return 1"); var preview = await Compare(); wire.Files["scripts/a.lua"] = "return 7";
            await Assert.ThrowsAsync<IOException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua" }, null, CancellationToken.None));
            Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }
        [Fact] public async Task Failed_listing_is_not_treated_as_a_missing_directory()
        {
            Local("a.lua", "return 1"); wire.ListError = "permission denied";
            await Assert.ThrowsAsync<LumaException>(() => Compare()); Assert.DoesNotContain(LumaOperation.Get, wire.Operations);
        }
        [Fact] public async Task Disk_change_during_remote_preflight_blocks_the_pending_PUT()
        {
            Local("a.lua", "return 1"); var preview = await Compare();
            wire.BeforeResponse = (op, path) => { if (op == LumaOperation.Ls) Local("a.lua", "return 2"); };
            await Assert.ThrowsAsync<StudioFileConflictException>(() => StudioProjectSync.UploadAsync(session, preview, new[] { "a.lua" }, null, CancellationToken.None));
            Assert.DoesNotContain(LumaOperation.Put, wire.Operations);
        }

        internal sealed class ProjectTransport : ILumaTransport
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<LumaOperation> Operations = new List<LumaOperation>();
            public readonly List<string> Paths = new List<string>();
            public string ListOverride, ListError, FailPut; public bool FailGet;
            public Action<LumaOperation, string> BeforeResponse;
            public Task<ILumaConnection> ConnectAsync(LumaEndpoint endpoint, CancellationToken ct) => Task.FromResult<ILumaConnection>(new Connection(this));
            private sealed class Connection : Stream, ILumaConnection
            {
                private readonly ProjectTransport owner; private MemoryStream reply;
                public Connection(ProjectTransport owner) { this.owner = owner; }
                Stream ILumaConnection.Read => this; Stream ILumaConnection.Write => this;
                public override Task WriteAsync(byte[] bytes, int offset, int count, CancellationToken ct)
                {
                    var op = (LumaOperation)bytes[offset + 4]; string payload = Encoding.UTF8.GetString(bytes, offset + 12, count - 12);
                    owner.Operations.Add(op); owner.Paths.Add(payload.Split('\0')[0]); string result = "ok"; var response = LumaOperation.Ok;
                    if (op == LumaOperation.Ls) {
                        string prefix = payload + "/";
                        var names = owner.Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k.Substring(prefix.Length).Split('/')[0]).Distinct();
                        result = owner.ListOverride ?? string.Join("\n", names);
                        if (owner.ListError != null) { result = owner.ListError; response = LumaOperation.Err; }
                        if (payload != "scripts" && !names.Any()) { result = "not found"; response = LumaOperation.Err; }
                    } else if (op == LumaOperation.Get) {
                        if (owner.FailGet || !owner.Files.TryGetValue(payload, out result)) { result = "read failed"; response = LumaOperation.Err; }
                    } else if (op == LumaOperation.Put) {
                        int separator = payload.IndexOf('\0'); string name = payload.Substring(0, separator);
                        if (name == owner.FailPut) { result = "write failed"; response = LumaOperation.Err; }
                        else owner.Files[name] = payload.Substring(separator + 1);
                    }
                    owner.BeforeResponse?.Invoke(op, payload);
                    reply = new MemoryStream(LumaProtocol.EncodeFrame(response, BitConverter.ToUInt16(bytes, offset + 6), Encoding.UTF8.GetBytes(result)));
                    return Task.CompletedTask;
                }
                public override Task<int> ReadAsync(byte[] b, int o, int n, CancellationToken ct) => reply.ReadAsync(b, o, n, ct);
                public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
                public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
                public override void Flush() { } public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
                public override int Read(byte[] b, int o, int n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
                public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long l) => throw new NotSupportedException();
            }
        }
    }
}
