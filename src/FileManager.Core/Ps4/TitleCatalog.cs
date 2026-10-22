using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;

namespace FileManager.Core.Ps4
{
    /// <summary>
    /// Titelnamen und Icons pro Verbindung (Spec „PS4-Wissen"): geladen erst,
    /// wenn ein Spielordner sichtbar wird, über das Durchsuchen-Tor, für die
    /// Sitzung gemerkt. Der ID-Rückfall bei fehlender Datei wird gemerkt, ein
    /// Verbindungsabbruch oder Abbruch (Navigieren weg) nicht.
    /// </summary>
    public sealed class TitleCatalog
    {
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly ConcurrentDictionary<string, TitleInfo> _known =
            new ConcurrentDictionary<string, TitleInfo>(StringComparer.Ordinal);

        public TitleCatalog(IFtpClient ftp, FtpGates gates)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _ftp = ftp;
            _gates = gates;
        }

        public bool TryGet(string titleId, out TitleInfo info)
        {
            return _known.TryGetValue(titleId, out info);
        }

        public async Task<TitleInfo> GetAsync(string titleId, CancellationToken ct)
        {
            TitleInfo info;
            if (_known.TryGetValue(titleId, out info)) return info;

            return await FtpGates.RunAsync(_gates.Browse, async () =>
            {
                TitleInfo again;
                if (_known.TryGetValue(titleId, out again)) return again;
                var loaded = await TitleInfoLoader.LoadAsync(_ftp, titleId, ct).ConfigureAwait(false);
                _known[titleId] = loaded;
                return loaded;
            }, ct).ConfigureAwait(false);
        }
    }
}
