using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;

namespace PS4Manager.Gallery.Core.Ps4
{
    /// <summary>
    /// Kennt die Ordnerstruktur der PS4 und sonst nichts: kein UI, kein Cache.
    /// Alle Netz-I/O läuft über IFtpClient — die Tests stecken ein Dictionary
    /// dahinter, die App den echten FtpClient.
    /// </summary>
    public sealed class MediaRepository
    {
        /// <summary>Max. gleichzeitige FTP-Zugriffe beim Laden (Spec: 3).</summary>
        private const int LoadParallelism = 3;

        private static readonly MediaKind[] Kinds = { MediaKind.Photo, MediaKind.Video };

        private readonly IFtpClient _ftp;
        private readonly SemaphoreSlim _browseGate;

        public MediaRepository(IFtpClient ftp)
            : this(ftp, null)
        {
        }

        public MediaRepository(IFtpClient ftp, SemaphoreSlim browseGate)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            _ftp = ftp;
            _browseGate = browseGate;
        }

        private async Task<T> BrowseAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            if (_browseGate == null) return await operation().ConfigureAwait(false);
            await _browseGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await operation().ConfigureAwait(false); }
            finally { _browseGate.Release(); }
        }

        private async Task BrowseAsync(Func<Task> operation, CancellationToken ct)
        {
            if (_browseGate == null) { await operation().ConfigureAwait(false); return; }
            await _browseGate.WaitAsync(ct).ConfigureAwait(false);
            try { await operation().ConfigureAwait(false); }
            finally { _browseGate.Release(); }
        }

        public async Task<IReadOnlyList<GameFolder>> LoadAsync(CancellationToken ct)
        {
            var titleIds = await LoadTitleIdsAsync(ct).ConfigureAwait(false);

            var gate = new SemaphoreSlim(LoadParallelism);
            var tasks = titleIds.Select(id => LoadGameGatedAsync(id, gate, ct)).ToList();

            var games = await Task.WhenAll(tasks).ConfigureAwait(false);

            return games
                .Where(g => g.Items.Count > 0)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Vereinigt die Title-IDs aus Foto- und Video-Zweig: ein Spiel kann nur
        /// Clips haben (oder nur Screenshots) und fiele sonst aus der Ansicht.
        /// Ein fehlender Zweig ist der Normalfall und darf nicht scheitern —
        /// scheitern BEIDE, ist die Verbindung tot und der Fehler gehört laut
        /// gemeldet, sonst zeigte die App still "0 Spiele".
        /// </summary>
        private async Task<IReadOnlyList<string>> LoadTitleIdsAsync(CancellationToken ct)
        {
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            ExceptionDispatchInfo firstFailure = null;
            int reachable = 0;

            foreach (var kind in Kinds)
            {
                try
                {
                    var entries = await BrowseAsync(() => _ftp.ListAsync(Ps4Paths.MediaRoot(kind), ct), ct).ConfigureAwait(false);
                    reachable++;
                    foreach (var entry in entries)
                        if (entry.IsDirectory) ids.Add(entry.Name);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (firstFailure == null) firstFailure = ExceptionDispatchInfo.Capture(ex);
                }
            }

            if (reachable == 0) firstFailure.Throw();
            return ids.ToList();
        }

        private async Task<GameFolder> LoadGameGatedAsync(string titleId, SemaphoreSlim gate, CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await LoadGameAsync(titleId, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<GameFolder> LoadGameAsync(string titleId, CancellationToken ct)
        {
            var items = new List<MediaItem>();
            foreach (var kind in Kinds)
                await CollectAsync(titleId, kind, items, ct).ConfigureAwait(false);

            // Neueste zuerst; bei gleicher Sekunde nach Name, damit die
            // Reihenfolge reproduzierbar ist (List.Sort ist nicht stabil).
            items.Sort((a, b) =>
            {
                int byTime = b.Timestamp.CompareTo(a.Timestamp);
                return byTime != 0
                    ? byTime
                    : string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });

            string name = null;
            byte[] icon = null;
            try
            {
                name = SfoReader.ReadTitle(
                    await BrowseAsync(() => _ftp.DownloadAsync(TitlePaths.SfoPath(titleId), ct), ct).ConfigureAwait(false));
            }
            catch (Exception) { /* Fallback unten — Spec: stiller Fallback */ }
            try
            {
                icon = await BrowseAsync(() => _ftp.DownloadAsync(TitlePaths.IconPath(titleId), ct), ct).ConfigureAwait(false);
            }
            catch (Exception) { /* ohne Icon */ }

            return new GameFolder(titleId, string.IsNullOrEmpty(name) ? titleId : name, icon, items);
        }

        private async Task CollectAsync(string titleId, MediaKind kind, List<MediaItem> into, CancellationToken ct)
        {
            var titleDir = Ps4Paths.TitleDir(kind, titleId);

            // Tolerant, anders als das Hash-Ordner-Listing unten: Titel-IDs
            // stammen aus der Vereinigung beider Zweige (siehe
            // LoadTitleIdsAsync), aber nicht jedes Spiel hat sowohl
            // Screenshots als auch Clips — "dieser Zweig kennt das Spiel
            // nicht" ist hier der Normalfall, ohne den die Vereinigung nicht
            // funktionieren würde. Unten ist die Lage eine andere: dort hat
            // das Title-Dir-Listing den Hash-Ordner gerade erst gemeldet, ein
            // Fehler dort ist also keine normale Abwesenheit mehr, sondern
            // eine Anomalie, die laut auffallen muss. Akzeptierte Kehrseite
            // hier: ein vorübergehender Transportfehler genau in diesem
            // Moment lässt die Items dieses Zweigs für dieses Spiel still
            // verschwinden, statt das gesamte Laden scheitern zu lassen.
            IReadOnlyList<FtpEntry> hashDirs;
            try
            {
                hashDirs = await BrowseAsync(() => _ftp.ListAsync(titleDir, ct), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return; }        // dieser Zweig kennt das Spiel nicht

            foreach (var hash in hashDirs.Where(e => e.IsDirectory))
            {
                // Kein Fangnetz hier, anders als beim Title-Dir-Listing oben: die
                // Title-Dir-Liste hat diesen Hash-Ordner gerade erst gemeldet, ein
                // Fehler hier ist also keine normale Abwesenheit, sondern eine
                // Anomalie (Rechtefehler, Transportfehler, Race mit Konsole) und
                // muss laut auffallen (Spec: "Verbindungs-/Listenfehler → roter
                // Status mit Klartext") statt die Gallery still zu verkürzen.
                var files = await BrowseAsync(() => _ftp.ListAsync(titleDir + "/" + hash.Name, ct), ct).ConfigureAwait(false);

                foreach (var file in files)
                {
                    if (file.IsDirectory) continue;
                    if (!IsMedia(kind, file.Name)) continue;

                    into.Add(new MediaItem(kind, titleId, hash.Name, file.Name, file.Size,
                        MediaItem.ParseTimestamp(file.Name, DateTime.MinValue)));
                }
            }
        }

        /// <summary>
        /// Foto-Zweig: nur *.jpg. Video-Zweig: alles AUSSER der .dat-Begleitdatei —
        /// der Zweig wird allein von der Konsole gepflegt, und eine Endungsliste
        /// würde abweichende Container (.webm o. ä.) still verschlucken.
        /// </summary>
        private static bool IsMedia(MediaKind kind, string name)
        {
            if (kind == MediaKind.Photo)
                return name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
            return !name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
        }

        public Task<byte[]> DownloadThumbnailAsync(MediaItem item, CancellationToken ct)
        {
            return BrowseAsync(() => _ftp.DownloadAsync(Ps4Paths.ThumbnailPath(item), ct), ct);
        }

        /// <summary>Nur für Fotos: ein Clip gehört gestreamt, nicht in ein byte[].</summary>
        public Task<byte[]> DownloadFullAsync(MediaItem item, CancellationToken ct)
        {
            if (item.Kind == MediaKind.Video)
                throw new InvalidOperationException(
                    "Ein Clip wird gestreamt (DownloadToFileAsync), nicht als byte[] geladen.");
            return BrowseAsync(() => _ftp.DownloadAsync(Ps4Paths.MediaPath(item), ct), ct);
        }

        /// <summary>Streamender Download — der Weg für Clips.</summary>
        public Task DownloadToFileAsync(MediaItem item, string localPath,
            IProgress<long> progress, CancellationToken ct)
        {
            return BrowseAsync(() => _ftp.DownloadToFileAsync(Ps4Paths.MediaPath(item), localPath, progress, ct), ct);
        }

    }
}
