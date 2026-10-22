using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Ersetzt Verknüpfungen im Pfad durch ihr Ziel (Plan-1-Übergabe „Links",
    /// Abschluss-Review Important #3): Zonen urteilen nach dem Pfad, in den
    /// wirklich geschrieben würde — „/data/x" → „/system" darf nicht als frei
    /// durchgehen. Listet dazu jeden Vorfahren einmal (über den Cache).
    /// </summary>
    public static class PathResolver
    {
        public const int MaxLinkHops = 8;

        public static async Task<string> ResolveAsync(DirectoryCache cache, string path, CancellationToken ct)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            var current = RemotePath.Normalize(path);
            for (int hop = 0; hop <= MaxLinkHops; hop++)
            {
                var next = await ResolveFirstLinkAsync(cache, current, ct).ConfigureAwait(false);
                if (next == null) return current;
                current = next;
            }
            throw new InvalidOperationException("Too many nested shortcuts: " + RemotePath.Normalize(path));
        }

        /// <summary>Löst die erste Verknüpfung im Pfad auf; null, wenn keine (mehr) vorkommt.</summary>
        private static async Task<string> ResolveFirstLinkAsync(DirectoryCache cache, string path, CancellationToken ct)
        {
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var dir = RemotePath.Root;
            for (int i = 0; i < segments.Length; i++)
            {
                System.Collections.Generic.IReadOnlyList<FtpEntry> entries;
                try
                {
                    entries = await cache.ListAsync(dir, ct).ConfigureAwait(false);
                }
                catch (FtpException ex) when (!ex.IsTransient)
                {
                    return null;                                  // Vorfahr fehlt — das LIST des Ziels meldet es
                }

                var entry = entries.FirstOrDefault(e => e.Name == segments[i]);
                if (entry != null && entry.Kind == FtpEntryKind.Link && !string.IsNullOrEmpty(entry.LinkTarget))
                {
                    var target = RemotePath.ResolveLink(dir, entry.LinkTarget);
                    var rest = string.Join("/", segments.Skip(i + 1));
                    return rest.Length == 0 ? target : RemotePath.Combine(target, rest);
                }
                dir = RemotePath.Combine(dir, segments[i]);
            }
            return null;
        }
    }
}
