using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Luma;
using Xunit;

namespace PS4Manager.Core.Tests.Luma
{
    public sealed class LumaProtocolTests
    {
        [Fact]
        public void Frame_encoder_writes_glua_header_and_payload()
        {
            var frame = LumaProtocol.EncodeFrame(LumaOperation.Exec, 0x1234,
                Encoding.UTF8.GetBytes("console\0return 1"));

            Assert.Equal(new byte[]
            {
                0x47, 0x4C, 0x55, 0x41,
                0x08, 0x00, 0x34, 0x12,
                0x10, 0x00, 0x00, 0x00
            }, frame.Take(12).ToArray());
            Assert.Equal("console\0return 1", Encoding.UTF8.GetString(frame, 12, frame.Length - 12));
        }

        [Fact]
        public void Resource_list_splits_on_last_space_and_reports_truncation()
        {
            var list = LumaResourceList.Parse("main running\nmy resource stopped\n-- truncated: listing incomplete --\n");

            Assert.True(list.Truncated);
            Assert.Equal(2, list.Entries.Count);
            Assert.Equal("main", list.Entries[0].Name);
            Assert.Equal("running", list.Entries[0].State);
            Assert.Equal("my resource", list.Entries[1].Name);
            Assert.Equal("stopped", list.Entries[1].State);
        }

        [Fact]
        public async Task Client_sends_hello_before_authenticated_request_and_ignores_unsolicited_log()
        {
            var input = new MemoryStream();
            WriteFrame(input, LumaOperation.Log, 0, "old log");
            WriteFrame(input, LumaOperation.Hello, 5, "hello");
            WriteFrame(input, LumaOperation.Ok, 6, "done");
            input.Position = 0;
            var transport = new FakeTransport(input);
            var client = new LumaClient(transport, 5);
            var logs = "";
            client.LogReceived += (s, e) => logs += e.Text;

            await client.ConnectAsync(new LumaEndpoint("192.168.0.10", 9615), CancellationToken.None);
            await client.HelloAsync("secret", CancellationToken.None);
            var result = await client.ReloadAsync(CancellationToken.None);

            Assert.Equal("done", result);
            Assert.Equal("old log", logs);
            var written = transport.Written.ToArray();
            Assert.Equal("secret", PayloadOf(written, 0));
            Assert.Equal("reload", OperationName(written, LumaProtocol.FrameSize(written, 0)));
        }

        [Fact]
        public async Task Execute_sends_chunk_name_then_lua_source()
        {
            var input = new MemoryStream();
            WriteFrame(input, LumaOperation.Ok, 7, "42");
            input.Position = 0;
            var transport = new FakeTransport(input);
            var client = new LumaClient(transport, 7);

            await client.ConnectAsync(new LumaEndpoint("192.168.0.10", 9615), CancellationToken.None);
            var result = await client.ExecuteAsync("return 42", CancellationToken.None);

            Assert.Equal("42", result);
            Assert.Equal(LumaOperation.Exec, (LumaOperation)transport.Written.ToArray()[4]);
            Assert.Equal("console\0return 42", PayloadOf(transport.Written.ToArray(), 0));
        }

        private static string OperationName(byte[] frames, int offset)
        {
            return ((LumaOperation)frames[offset + 4]).ToString().ToLowerInvariant();
        }

        private static string PayloadOf(byte[] frames, int offset)
        {
            var len = BitConverter.ToInt32(frames, offset + 8);
            return Encoding.UTF8.GetString(frames, offset + 12, len);
        }

        private static void WriteFrame(Stream stream, LumaOperation op, ushort seq, string text)
        {
            var frame = LumaProtocol.EncodeFrame(op, seq, Encoding.UTF8.GetBytes(text));
            stream.Write(frame, 0, frame.Length);
        }

        private sealed class FakeTransport : ILumaTransport
        {
            private readonly Stream _read;

            public FakeTransport(Stream read)
            {
                _read = read;
            }

            public MemoryStream Written { get; } = new MemoryStream();

            public Task<ILumaConnection> ConnectAsync(LumaEndpoint endpoint, CancellationToken ct)
            {
                return Task.FromResult<ILumaConnection>(new FakeConnection(_read, Written));
            }
        }

        private sealed class FakeConnection : ILumaConnection
        {
            public FakeConnection(Stream read, Stream write)
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
