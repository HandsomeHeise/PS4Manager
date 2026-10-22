using PS4Manager.UI;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Gallery.Core.Caching;
using PS4Manager.Gallery.Core.Ps4;

namespace PS4Manager.Gallery.App
{
    /// <summary>
    /// Bedient ItemImageNeeded: Cache zuerst, sonst FTP (max. 3 parallel,
    /// Spec). item.Image darf von hier aus gesetzt werden — der Setter des
    /// GalleryControls marshallt die Invalidierung selbst auf den UI-Thread.
    /// Ein gescheitertes Thumbnail bleibt Platzhalter; kein Dialog, kein
    /// Absturz (Spec: stiller Fallback).
    /// </summary>
    internal sealed class ThumbnailLoader
    {
        /// <summary>Ein Tor für ALLE Loader-Generationen: auch während eines
        /// Refreshs (alte Ladung läuft aus, neue startet) gilt max. 3 parallel (Spec).</summary>
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(3);

        private readonly MediaRepository _repository;
        private readonly ThumbnailCache _cache;

        public ThumbnailLoader(MediaRepository repository, ThumbnailCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public void Enqueue(GalleryItem item, MediaItem media, CancellationToken ct)
        {
            // async void bewusst NICHT: die Task läuft frei, Fehler fängt sie selbst.
            var _ = LoadAsync(item, media, ct);
        }

        private async Task LoadAsync(GalleryItem item, MediaItem media, CancellationToken ct)
        {
            try
            {
                var cached = _cache.TryGet(media.TitleId, media.FileName);
                if (cached != null)
                {
                    item.Image = ToImage(cached, media.Kind);
                    return;
                }

                await Gate.WaitAsync(ct).ConfigureAwait(false);
                byte[] data;
                try
                {
                    data = await _repository.DownloadThumbnailAsync(media, ct).ConfigureAwait(false);
                }
                finally
                {
                    Gate.Release();
                }

                if (ct.IsCancellationRequested || data == null || data.Length == 0) return;

                _cache.Put(media.TitleId, media.FileName, data);
                item.Image = ToImage(data, media.Kind);
            }
            catch (Exception)
            {
                // Platzhalter bleibt stehen — bewusst still (Spec).
            }
        }

        private static Image ToImage(byte[] data, MediaKind kind)
        {
            using (var stream = new MemoryStream(data))
            using (var direct = Image.FromStream(stream))
            {
                var bitmap = new Bitmap(direct);
                if (kind == MediaKind.Video) DrawPlayBadge(bitmap);
                return bitmap;
            }
        }

        /// <summary>
        /// Das einzige Merkmal, das eine Clip-Kachel von einer Bild-Kachel
        /// unterscheidet. Gezeichnet wird auf das ORIGINAL (die PS4 liefert
        /// 440×248), bevor die Gallery auf ImageSize verkleinert — so bleibt
        /// das Zeichen proportional, egal wie groß die Kacheln eingestellt sind.
        /// </summary>
        private static void DrawPlayBadge(Bitmap bitmap)
        {
            float side = Math.Min(bitmap.Width, bitmap.Height);
            float radius = side * 0.22f;
            float cx = bitmap.Width / 2f;
            float cy = bitmap.Height / 2f;

            using (var g = Graphics.FromImage(bitmap))
            using (var disc = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (var glyph = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(disc, cx - radius, cy - radius, radius * 2f, radius * 2f);

                float half = radius * 0.5f;         // halbe Höhe des Dreiecks
                var triangle = new[]
                {
                    new PointF(cx - half * 0.65f, cy - half),
                    new PointF(cx - half * 0.65f, cy + half),
                    new PointF(cx + half * 0.95f, cy)
                };
                g.FillPolygon(glyph, triangle);
            }
        }
    }
}
