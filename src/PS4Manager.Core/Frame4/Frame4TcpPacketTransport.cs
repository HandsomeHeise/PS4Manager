using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4TcpPacketTransport : IFrame4PacketTransport
    {
        public async Task<IFrame4PacketConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var client = new TcpClient();
            using (ct.Register(() => client.Close()))
            {
                await client.ConnectAsync(endpoint.Host.Trim('[', ']'), endpoint.SocketPort).ConfigureAwait(false);
            }
            return new TcpPacketConnection(client);
        }

        private sealed class TcpPacketConnection : IFrame4PacketConnection
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;

            public TcpPacketConnection(TcpClient client)
            {
                _client = client;
                _stream = client.GetStream();
            }

            public Stream Read { get { return _stream; } }
            public Stream Write { get { return _stream; } }

            public void Dispose()
            {
                _stream.Dispose();
                _client.Close();
            }
        }
    }
}
