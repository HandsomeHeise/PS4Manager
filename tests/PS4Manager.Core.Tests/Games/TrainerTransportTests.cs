using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Games;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class TrainerTransportTests
    {
        [Fact]
        public async Task Same_pid_replacement_with_missing_stub_is_never_called_or_reinstalled()
        {
            var wire = new Wire(); var transport = wire.Create();
            await transport.CallAsync(Target(), Identity(), 0x401000, new ulong[0], CancellationToken.None);
            wire.StubMissing = true;
            await Assert.ThrowsAnyAsync<Exception>(() => transport.CallAsync(Target(), Identity(), 0x401000, new ulong[0], CancellationToken.None));
            Assert.Equal(1, wire.Commands.Count(c => c == 0xBDAA0006));
            Assert.Equal(1, wire.Commands.Count(c => c == 0xBDAA0005));
        }
        [Fact]
        public void Rpc_widths_ignore_unspecified_upper_bits()
        {
            Assert.False(RpcReturn.AsBool(0x1234567800000000UL));
            Assert.True(RpcReturn.AsBool(0x1234567800000001UL));
            Assert.Equal(1, RpcReturn.AsInt32(0xFEDCBA9800000001UL));
            Assert.Equal(-1, RpcReturn.AsInt32(0x11111111FFFFFFFFUL));
        }

        [Fact]
        public async Task Calls_reuse_stub_but_reads_do_not_install_it()
        {
            var wire = new Wire(); var transport = wire.Create(); var target = Target();
            await transport.GetProcessesAsync(target, CancellationToken.None);
            await transport.ReadAsync(target, 42, 0x100001000, 1, CancellationToken.None);
            Assert.Empty(wire.Commands);
            Assert.Equal(0x1234567800000001UL, await transport.CallAsync(target, Identity(), 0x401000, new ulong[] { 8, 9 }, CancellationToken.None));
            await transport.CallAsync(target, Identity(), 0x401000, new ulong[0], CancellationToken.None);
            Assert.Equal(1, wire.Commands.Count(c => c == 0xBDAA0005));
            Assert.Equal(2, wire.Commands.Count(c => c == 0xBDAA0006));
            Assert.All(wire.Hosts, h => Assert.Equal("console", h));
        }

        [Fact]
        public async Task New_generation_never_reuses_an_old_stub()
        {
            var wire = new Wire(); var transport = wire.Create();
            await transport.CallAsync(Target(), Identity(), 0x401000, new ulong[0], CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => transport.CallAsync(Target(2), Identity(), 0x401000, new ulong[0], CancellationToken.None));
            transport.Invalidate();
            await transport.CallAsync(Target(2), Identity(2), 0x401000, new ulong[0], CancellationToken.None);
            Assert.Equal(2, wire.Commands.Count(c => c == 0xBDAA0005));
        }

        [Fact]
        public async Task Uncertain_install_cannot_be_repeated_after_invalidation()
        {
            var wire = new Wire { LoseInstall = true }; var transport = wire.Create();
            await Assert.ThrowsAsync<TrainerDispatchException>(() => transport.CallAsync(Target(), Identity(), 0x401000, new ulong[0], CancellationToken.None));
            transport.Invalidate();
            await Assert.ThrowsAsync<InvalidOperationException>(() => transport.CallAsync(Target(2), Identity(2), 0x401000, new ulong[0], CancellationToken.None));
            Assert.Equal(1, wire.Commands.Count(c => c == 0xBDAA0005));
            Assert.DoesNotContain(0xBDAA0006U, wire.Commands);
        }

        [Fact]
        public async Task Changed_target_during_identity_read_never_sends_mutation()
        {
            bool current = true; var wire = new Wire { OnInfo = () => current = false }; var transport = wire.Create();
            var target = new TrainerTarget(1, new Frame4Endpoint("console"), () => current, CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.WriteAsync(target, Identity(), 0x100001000, new byte[] { 1 }, CancellationToken.None));
            Assert.DoesNotContain(wire.Paths, p => p.Contains("write-memory"));
            Assert.DoesNotContain(0xBDAA0005U, wire.Commands);
        }

        [Fact]
        public async Task Wrong_title_and_oversized_reads_fail_closed()
        {
            var wire = new Wire { Title = "CUSA99999" }; var transport = wire.Create();
            await Assert.ThrowsAsync<InvalidOperationException>(() => transport.CallAsync(Target(), Identity(), 0x401000, new ulong[0], CancellationToken.None));
            Assert.DoesNotContain(0xBDAA0005U, wire.Commands);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ReadAsync(Target(), 42, 0x1000, 4097, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ReadAsync(Target(), 42, ulong.MaxValue, 4, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => transport.ReadAsync(Target(), 42, 0x1000, 4, CancellationToken.None));
        }

        private static TrainerTarget Target(long generation = 1) => new TrainerTarget(generation, new Frame4Endpoint("console"), () => true, CancellationToken.None);
        private static TrainerIdentity Identity(long generation = 1) => new TrainerIdentity(generation, "console", 42, "CUSA02624", 0x400000, "test");

        private sealed class Wire : IFrame4HttpTransport, IFrame4PacketTransport
        {
            internal readonly List<uint> Commands = new List<uint>();
            internal readonly List<string> Hosts = new List<string>();
            internal readonly List<string> Paths = new List<string>();
            internal bool LoseInstall;
            internal bool StubMissing;
            internal string Title = "CUSA02624";
            internal Action OnInfo;
            internal Frame4TrainerTransport Create() => new Frame4TrainerTransport(new Frame4Client(this), new Frame4PacketClient(this));
            public Task<string> GetStringAsync(Uri uri, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); Hosts.Add(uri.Host); Paths.Add(uri.PathAndQuery);
                if (uri.AbsolutePath.EndsWith("process-info")) { OnInfo?.Invoke(); return Task.FromResult("{\"pid\":42,\"name\":\"eboot.bin\",\"title_id\":\"" + Title + "\"}"); }
                if (uri.AbsolutePath.EndsWith("process-list")) return Task.FromResult("[{\"pid\":42,\"name\":\"eboot.bin\"}]");
                if (uri.AbsolutePath.EndsWith("read-memory")) {
                    if (uri.Query.ToLowerInvariant().Contains("length=0x19a")) {
                        var bytes = Convert.FromBase64String("UlNUQqMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAbGlia2VybmVsLnNwcngAbGlia2VybmVsX3dlYi5zcHJ4AGxpYmtlcm5lbF9zeXMuc3ByeAAAAAAAAAAAAHNjZUtlcm5lbFVzbGVlcAAAAAAAAAAAALkAAAAASI0V1P///74AAAAASI09k////+jEAAAASIXAdD+5AAAAAEiNFbL///++AAAAAEiNPYD////oogAAAEiFwHQduQAAAABIjRWQ////vgAAAABIjT1x////6IAAAABIjRWQ////SI01ef///0iLPWr////ocQAAAIA9J////wB0SUyLDQ7///9MiwX//v//SIsN8P7//0iLFeH+//9IizXS/v//SIs9w/7//0yLJbT+//9B/9RIiQXi/v//xgXj/v//AMYF3f7//wG/oIYBAEyLJR////9B/9TrnTHAw7hSAgAASYnKDwXDuE8CAABJicoPBcM=");
                        if (StubMissing) Array.Clear(bytes, 0, bytes.Length);
                        return Task.FromResult("{\"bytes\":\"" + BitConverter.ToString(bytes).Replace("-", " ") + "\"}");
                    }
                    return Task.FromResult("{\"bytes\":\"04\"}");
                }
                return Task.FromResult("{\"success\":true}");
            }
            public Task<IFrame4PacketConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); Hosts.Add(endpoint.Host);
                return Task.FromResult<IFrame4PacketConnection>(new Connection(this));
            }
            private sealed class Connection : IFrame4PacketConnection
            {
                private readonly MemoryStream write = new MemoryStream();
                public Connection(Wire wire) { Read = new ResponseStream(() => {
                    var cmd = BitConverter.ToUInt32(write.ToArray(), 4); wire.Commands.Add(cmd);
                    var data = new MemoryStream(); var w = new BinaryWriter(data); w.Write(0x80000000U);
                    if (cmd == 0xBDAA0005) { if (!wire.LoseInstall) w.Write(0x100001000UL); }
                    else if (cmd == 0xBDAA0006) { w.Write(42); w.Write(0x1234567800000001UL); }
                    else if (cmd == 0xBDAA0011) {
                        w.Write(1U); w.Write(0U); var name = new byte[256]; Encoding.ASCII.GetBytes("eboot.bin").CopyTo(name, 0); w.Write(name);
                        w.Write(0x400000UL); w.Write(0x1520000U); w.Write(0x2000000UL); w.Write(0xF000000U);
                    } else throw new InvalidOperationException("Unexpected wire command");
                    return data.ToArray();
                }); }
                public Stream Read { get; }
                public Stream Write => write;
                public void Dispose() { Read.Dispose(); write.Dispose(); }
            }
            private sealed class ResponseStream : MemoryStream
            {
                private readonly Func<byte[]> response; private bool initialized;
                internal ResponseStream(Func<byte[]> response) { this.response = response; }
                public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
                {
                    if (!initialized) { var bytes = response(); Write(bytes, 0, bytes.Length); Position = 0; initialized = true; }
                    return base.ReadAsync(buffer, offset, count, ct);
                }
            }
        }
    }
}
