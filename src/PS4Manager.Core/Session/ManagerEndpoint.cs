using System;

namespace PS4Manager.Core.Session
{
    public sealed class ManagerEndpoint
    {
        public ManagerEndpoint(string host, int port)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            foreach (var character in host)
                if (char.IsControl(character))
                    throw new ArgumentException("The PS4 host cannot contain control characters.", nameof(host));
            var trimmed = host.Trim();
            var hostType = Uri.CheckHostName(trimmed);
            if (trimmed.Length == 0 || hostType == UriHostNameType.Unknown)
                throw new ArgumentException("Enter a valid PS4 host name or IP address.", nameof(host));
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "The FTP port must be between 1 and 65535.");

            Host = hostType == UriHostNameType.IPv6 && !trimmed.StartsWith("[", StringComparison.Ordinal)
                ? "[" + trimmed + "]" : trimmed;
            Port = port;
        }

        public string Host { get; }
        public int Port { get; }
    }
}
