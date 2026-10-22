using System;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace Ps4.Core.Titles
{
    /// <summary>
    /// Name und Icon eines Titels von der Konsole. Wie in der Gallery ist eine
    /// fehlende oder kaputte Datei ein stiller Fallback auf die ID — aber ein
    /// Verbindungsabbruch ist KEIN fehlender Name: der geht durch, damit ein
    /// Aufrufer ihn nicht als „dieser Titel hat keinen Namen" zwischenspeichert.
    /// </summary>
    public static class TitleInfoLoader
    {
        public static async Task<TitleInfo> LoadAsync(IFtpClient ftp, string titleId, CancellationToken ct)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));

            var sfo = await TryDownloadAsync(ftp, TitlePaths.SfoPath(titleId), ct).ConfigureAwait(false);
            var icon = await TryDownloadAsync(ftp, TitlePaths.IconPath(titleId), ct).ConfigureAwait(false);
            return new TitleInfo(titleId, SfoReader.ReadTitle(sfo), icon);
        }

        private static async Task<byte[]> TryDownloadAsync(IFtpClient ftp, string path, CancellationToken ct)
        {
            try
            {
                return await ftp.DownloadAsync(path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FtpException ex) when (ex.IsTransient)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
