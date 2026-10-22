using System;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaEndpoint
    {
        public const int GtaPort = 9615;
        public const int Rdr2Port = 9616;

        public LumaEndpoint(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Host = host.Trim();
            Port = port;
        }

        public string Host { get; }
        public int Port { get; }
    }
}
