using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4UartClientTests
    {
        [Fact]
        public async Task Connect_uses_uart_port()
        {
            var transport = new FakeUartTransport();
            var client = new Frame4UartClient(transport);

            using (await client.ConnectAsync(new Frame4Endpoint("192.168.0.10", 2812, 3321), CancellationToken.None))
            {
            }

            Assert.Equal(3321, transport.Port);
        }

        [Fact]
        public void Decode_replaces_invalid_utf8_without_throwing()
        {
            var text = Frame4UartClient.Decode(new byte[] { 0x48, 0x69, 0xFF }, 3);

            Assert.StartsWith("Hi", text);
        }

        private sealed class FakeUartTransport : IFrame4UartTransport
        {
            public int Port { get; private set; }

            public Task<IFrame4UartConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct)
            {
                Port = endpoint.UartPort;
                return Task.FromResult<IFrame4UartConnection>(
                    new FakeUartConnection(new MemoryStream(Encoding.UTF8.GetBytes("log"))));
            }
        }

        private sealed class FakeUartConnection : IFrame4UartConnection
        {
            public FakeUartConnection(Stream read)
            {
                Read = read;
            }

            public Stream Read { get; private set; }
            public void Dispose() { }
        }
    }
}
