using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4TcpUartTransport : IFrame4UartTransport
    {
        public async Task<IFrame4UartConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct)
        {
            var client = new TcpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.UartPort).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new Frame4TcpUartConnection(client);
        }

        private sealed class Frame4TcpUartConnection : IFrame4UartConnection
        {
            private readonly TcpClient _client;

            public Frame4TcpUartConnection(TcpClient client)
            {
                _client = client;
                Read = client.GetStream();
            }

            public System.IO.Stream Read { get; private set; }

            public void Dispose()
            {
                _client.Close();
            }
        }
    }
}
