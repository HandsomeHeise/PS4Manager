using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Die Kopierschleife der Transfers — als eigener Typ, weil hier die
    /// Untätigkeits-Frist lebt und die sich ohne echten FTP-Server testen
    /// lassen muss.
    ///
    /// Der Unterschied zu Stream.CopyToAsync: nach JEDEM gelesenen Block ruft
    /// die Schleife resetIdleTimeout. Eine tote Verbindung fliegt damit weiter
    /// nach der Frist auf, ein langer gesunder Transfer läuft durch — eine
    /// Gesamtfrist würde einen mehrminütigen Clip mittendrin abschneiden.
    /// </summary>
    public static class FtpTransfer
    {
        public const int BufferSize = 81920;

        public static async Task<long> CopyAsync(Stream source, Stream destination,
            IProgress<long> progress, Action resetIdleTimeout, CancellationToken ct)
        {
            var buffer = new byte[BufferSize];
            long total = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                int read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read == 0) break;

                if (resetIdleTimeout != null) resetIdleTimeout();

                await destination.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                total += read;

                if (progress != null) progress.Report(total);
            }

            return total;
        }
    }
}
