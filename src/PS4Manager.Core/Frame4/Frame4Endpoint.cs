using System;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4Endpoint
    {
        public const int DefaultHttpPort = 2812;
        public const int DefaultSocketPort = 2811;
        public const int DefaultUartPort = 3321;

        public Frame4Endpoint(string host)
            : this(host, DefaultHttpPort, DefaultUartPort)
        {
        }

        public Frame4Endpoint(string host, int httpPort, int uartPort)
            : this(host, httpPort, DefaultSocketPort, uartPort)
        {
        }

        public Frame4Endpoint(string host, int httpPort, int socketPort, int uartPort)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            foreach (var character in host)
                if (char.IsControl(character))
                    throw new ArgumentException("The PS4 host cannot contain control characters.", nameof(host));
            var trimmed = host.Trim();
            var hostType = Uri.CheckHostName(trimmed);
            if (trimmed.Length == 0 || hostType == UriHostNameType.Unknown)
                throw new ArgumentException("Enter a valid PS4 host name or IP address.", nameof(host));
            if (httpPort < 1 || httpPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(httpPort), "The frame4 HTTP port must be between 1 and 65535.");
            if (socketPort < 1 || socketPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(socketPort), "The frame4 socket port must be between 1 and 65535.");
            if (uartPort < 1 || uartPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(uartPort), "The frame4 UART port must be between 1 and 65535.");

            Host = hostType == UriHostNameType.IPv6 && !trimmed.StartsWith("[", StringComparison.Ordinal)
                ? "[" + trimmed + "]" : trimmed;
            HttpPort = httpPort;
            SocketPort = socketPort;
            UartPort = uartPort;
            HttpBaseUri = new Uri("http://" + Host + ":" + HttpPort + "/");
        }

        public string Host { get; private set; }
        public int HttpPort { get; private set; }
        public int SocketPort { get; private set; }
        public int UartPort { get; private set; }
        public Uri HttpBaseUri { get; private set; }
    }
}
