using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4UartClient
    {
        private static readonly Encoding LogEncoding = new UTF8Encoding(false, false);
        private readonly IFrame4UartTransport _transport;

        public Frame4UartClient()
            : this(new Frame4TcpUartTransport())
        {
        }

        public Frame4UartClient(IFrame4UartTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public Task<IFrame4UartConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            return _transport.ConnectAsync(endpoint, ct);
        }

        public static string Decode(byte[] buffer, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (count < 0 || count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
            return LogEncoding.GetString(buffer, 0, count);
        }
    }
}
