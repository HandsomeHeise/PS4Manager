using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4PacketClientTests
    {
        [Fact]
        public async Task Rpc_install_sends_pid_and_reads_full_width_stub()
        {
            var response = new MemoryStream(); WriteUInt32(response, 0x80000000); WriteUInt64(response, 0x12345000UL); response.Position = 0;
            var transport = new FakePacketTransport(response);
            var stub = await new Frame4PacketClient(transport).InstallRpcAsync(new Frame4Endpoint("console"), 42, CancellationToken.None);
            Assert.Equal(0x12345000UL, stub);
            Assert.Equal(new byte[] { 0xCC,0xBB,0xAA,0xFF, 0x05,0x00,0xAA,0xBD, 4,0,0,0, 42,0,0,0 }, transport.Written.ToArray());
        }

        [Theory]
        [InlineData(0xF0000003U, 123UL)]
        [InlineData(0x80000000U, 0UL)]
        public async Task Rpc_install_rejects_failed_status_or_zero_stub(uint status, ulong stub)
        {
            var response = new MemoryStream(); WriteUInt32(response, status); WriteUInt64(response, stub); response.Position = 0;
            await Assert.ThrowsAsync<Frame4Exception>(() => new Frame4PacketClient(new FakePacketTransport(response))
                .InstallRpcAsync(new Frame4Endpoint("console"), 42, CancellationToken.None));
        }

        [Fact]
        public async Task Rpc_install_rejects_truncation_invalid_pid_and_cancellation()
        {
            var response = new MemoryStream(); WriteUInt32(response, 0x80000000); response.Position = 0;
            var transport = new FakePacketTransport(response); var client = new Frame4PacketClient(transport);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.InstallRpcAsync(new Frame4Endpoint("console"), 0, CancellationToken.None));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InstallRpcAsync(new Frame4Endpoint("console"), 42, new CancellationToken(true)));
            Assert.Empty(transport.Written.ToArray());
            await Assert.ThrowsAsync<EndOfStreamException>(() => client.InstallRpcAsync(new Frame4Endpoint("console"), 42, CancellationToken.None));
        }

        [Fact]
        public async Task Rpc_reply_from_another_process_is_rejected()
        {
            var response = new MemoryStream(); WriteUInt32(response, 0x80000000); WriteUInt32(response, 43); WriteUInt64(response, 1); response.Position = 0;
            await Assert.ThrowsAsync<Frame4Exception>(() => new Frame4PacketClient(new FakePacketTransport(response)).CallRpcAsync(
                new Frame4Endpoint("console"), new Frame4RpcCall(42, 0x1000, 0x2000, 0, 0, 0, 0, 0, 0), CancellationToken.None));
        }

        [Fact]
        public async Task Version_sends_frame4_header_to_socket_port()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 5);
            response.Write(Encoding.ASCII.GetBytes("0.4.6"), 0, 5);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            var version = await client.GetVersionAsync(new Frame4Endpoint("192.168.0.10"), CancellationToken.None);

            Assert.Equal("0.4.6", version);
            Assert.Equal(2811, transport.Port);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x01, 0x00, 0x00, 0xBD,
                0x00, 0x00, 0x00, 0x00
            }, transport.Written.ToArray());
        }

        [Fact]
        public async Task Version_rejects_unreasonable_response_length()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 1024 * 1024);
            response.Position = 0;
            var client = new Frame4PacketClient(new FakePacketTransport(response));

            var error = await Assert.ThrowsAsync<Frame4Exception>(() =>
                client.GetVersionAsync(new Frame4Endpoint("192.168.0.10"), CancellationToken.None));

            Assert.Contains("version response", error.Message);
        }

        [Fact]
        public async Task Console_info_reads_status_and_fixed_width_fields()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0x80000000);
            var info = new byte[332];
            for (var i = 0; i < 16; i++) info[i] = (byte)(i + 1);
            WriteUInt32(info, 16, 0x09000000);
            WriteUInt32(info, 20, 0x05050001);
            WriteAscii(info, 24, 50, "FreeBSD");
            WriteAscii(info, 74, 50, "9.0");
            WriteUInt32(info, 124, 1234);
            WriteAscii(info, 128, 100, "kernel build");
            WriteAscii(info, 228, 100, "CUH-7216B");
            WriteUInt32(info, 328, 8);
            response.Write(info, 0, info.Length);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            var console = await client.GetConsoleInfoAsync(new Frame4Endpoint("192.168.0.10"), CancellationToken.None);

            Assert.Equal("0102030405060708090A0B0C0D0E0F10", console.PsidHex);
            Assert.Equal(0x09000000U, console.UpdateVersion);
            Assert.Equal(0x05050001U, console.SdkVersion);
            Assert.Equal("FreeBSD", console.KernelOsType);
            Assert.Equal("9.0", console.KernelOsRelease);
            Assert.Equal(1234U, console.KernelOsRevision);
            Assert.Equal("kernel build", console.KernelVersion);
            Assert.Equal("CUH-7216B", console.HardwareModel);
            Assert.Equal(8U, console.CpuCount);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x05, 0x00, 0xDD, 0xBD,
                0x00, 0x00, 0x00, 0x00
            }, transport.Written.ToArray());
        }

        [Fact]
        public async Task Console_info_reports_frame4_status_errors()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0xF0000001);
            response.Position = 0;
            var client = new Frame4PacketClient(new FakePacketTransport(response));

            var error = await Assert.ThrowsAsync<Frame4Exception>(() =>
                client.GetConsoleInfoAsync(new Frame4Endpoint("192.168.0.10"), CancellationToken.None));

            Assert.Contains("0xF0000001", error.Message);
        }

        [Fact]
        public async Task Aob_search_sends_pattern_and_mask_after_ready_status()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0x80000000);
            WriteUInt32(response, 0x80000000);
            WriteUInt64(response, 0x1234567887654321);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            var result = await client.FindAobAsync(new Frame4Endpoint("192.168.0.10"), 42, 0x400000, 0x1000,
                Frame4AobPattern.Parse("48 8B ?? 00"), CancellationToken.None);

            Assert.Equal(0x1234567887654321UL, result);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x12, 0x00, 0xAA, 0xBD,
                0x14, 0x00, 0x00, 0x00,
                0x2A, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x10, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x00,
                0x48, 0x8B, 0x00, 0x00,
                0x01, 0x01, 0x00, 0x01
            }, transport.Written.ToArray());
        }

        [Fact]
        public async Task Prx_list_reads_packed_module_entries()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0x80000000);
            WriteUInt32(response, 1);
            var entry = new byte[284];
            WriteUInt32(entry, 0, 0x44);
            WriteAscii(entry, 4, 256, "libSceSysmodule.sprx");
            WriteUInt64(entry, 260, 0x100000);
            WriteUInt32(entry, 268, 0x2000);
            WriteUInt64(entry, 272, 0x200000);
            WriteUInt32(entry, 280, 0x1000);
            response.Write(entry, 0, entry.Length);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            var modules = await client.GetPrxModulesAsync(new Frame4Endpoint("192.168.0.10"), 42,
                CancellationToken.None);

            var module = Assert.Single(modules);
            Assert.Equal(0x44U, module.Handle);
            Assert.Equal("libSceSysmodule.sprx", module.Name);
            Assert.Equal(0x100000UL, module.TextAddress);
            Assert.Equal(0x2000U, module.TextSize);
            Assert.Equal(0x200000UL, module.DataAddress);
            Assert.Equal(0x1000U, module.DataSize);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x11, 0x00, 0xAA, 0xBD,
                0x04, 0x00, 0x00, 0x00,
                0x2A, 0x00, 0x00, 0x00
            }, transport.Written.ToArray());
        }

        [Fact]
        public async Task Prx_load_sends_process_name_and_path_blocks()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0x80000000);
            WriteUInt32(response, 0x1234);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            var handle = await client.LoadPrxAsync(new Frame4Endpoint("192.168.0.10"), "eboot.bin",
                "/data/plugin.prx", CancellationToken.None);

            Assert.Equal(0x1234U, handle);
            var written = transport.Written.ToArray();
            Assert.Equal(144, written.Length);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x0F, 0x00, 0xAA, 0xBD,
                0x00, 0x00, 0x00, 0x00
            }, written.Take(12).ToArray());
            Assert.Equal("eboot.bin", ReadAscii(written, 12, 32));
            Assert.Equal("/data/plugin.prx", ReadAscii(written, 44, 100));
        }

        [Fact]
        public async Task Prx_unload_sends_handle_and_process_name()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0x80000000);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            await client.UnloadPrxAsync(new Frame4Endpoint("192.168.0.10"), "eboot.bin", 0x44,
                CancellationToken.None);

            var written = transport.Written.ToArray();
            Assert.Equal(48, written.Length);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x10, 0x00, 0xAA, 0xBD,
                0x04, 0x00, 0x00, 0x00,
                0x44, 0x00, 0x00, 0x00
            }, written.Take(16).ToArray());
            Assert.Equal("eboot.bin", ReadAscii(written, 16, 32));
        }

        [Fact]
        public async Task Rpc_call_sends_registers_and_reads_rax()
        {
            var response = new MemoryStream();
            WriteUInt32(response, 0x80000000);
            WriteUInt32(response, 42);
            WriteUInt64(response, 0xFEEDFACECAFEBEEF);
            response.Position = 0;
            var transport = new FakePacketTransport(response);
            var client = new Frame4PacketClient(transport);

            var result = await client.CallRpcAsync(new Frame4Endpoint("192.168.0.10"),
                new Frame4RpcCall(42, 0x1000, 0x2000, 1, 2, 3, 4, 5, 6), CancellationToken.None);

            Assert.Equal(42, result.Pid);
            Assert.Equal(0xFEEDFACECAFEBEEFUL, result.Rax);
            Assert.Equal(new byte[]
            {
                0xCC, 0xBB, 0xAA, 0xFF,
                0x06, 0x00, 0xAA, 0xBD,
                0x44, 0x00, 0x00, 0x00,
                0x2A, 0x00, 0x00, 0x00,
                0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            }, transport.Written.ToArray());
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteUInt64(Stream stream, ulong value)
        {
            for (var i = 0; i < 8; i++) stream.WriteByte((byte)(value >> (8 * i)));
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++) buffer[offset + i] = (byte)(value >> (8 * i));
        }

        private static void WriteAscii(byte[] buffer, int offset, int length, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(length, bytes.Length));
        }

        private static string ReadAscii(byte[] buffer, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && buffer[end] != 0) end++;
            return Encoding.ASCII.GetString(buffer, offset, end - offset);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private sealed class FakePacketTransport : IFrame4PacketTransport
        {
            private readonly Stream _read;

            public FakePacketTransport(Stream read)
            {
                _read = read;
            }

            public MemoryStream Written { get; } = new MemoryStream();
            public int Port { get; private set; }

            public Task<IFrame4PacketConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct)
            {
                Port = endpoint.SocketPort;
                return Task.FromResult<IFrame4PacketConnection>(new FakePacketConnection(_read, Written));
            }
        }

        private sealed class FakePacketConnection : IFrame4PacketConnection
        {
            public FakePacketConnection(Stream read, Stream write)
            {
                Read = read;
                Write = write;
            }

            public Stream Read { get; private set; }
            public Stream Write { get; private set; }
            public void Dispose() { }
        }
    }
}
