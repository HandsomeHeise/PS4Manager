using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaTcpTransport : ILumaTransport
    {
        public async Task<ILumaConnection> ConnectAsync(LumaEndpoint endpoint, CancellationToken ct)
        {
            var client = new TcpClient();
            using (ct.Register(() => client.Close()))
            {
                await client.ConnectAsync(endpoint.Host.Trim('[', ']'), endpoint.Port).ConfigureAwait(false);
            }
            client.NoDelay = true;
            return new LumaTcpConnection(client);
        }

        private sealed class LumaTcpConnection : ILumaConnection
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;

            public LumaTcpConnection(TcpClient client)
            {
                _client = client;
                _stream = client.GetStream();
            }

            public System.IO.Stream Read { get { return _stream; } }
            public System.IO.Stream Write { get { return _stream; } }

            public void Dispose()
            {
                _stream.Dispose();
                _client.Close();
            }
        }
    }
}
