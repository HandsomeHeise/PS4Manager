using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.Core.Media;
using PS4Manager.Gallery.Core.Ps4;

namespace PS4Manager.Gallery.App
{
    /// <summary>
    /// Vollbild-Vorschau: zeigt das volle JPEG eingepasst oder spielt einen
    /// Clip im eingebetteten Player, Pfeiltasten blättern durch dieselbe
    /// Gruppe, Esc schließt (Spec). Bytes und Clip-Datei liefert der
    /// Besitzer (MainForm, Cache-first) über die Delegaten — dieses Fenster
    /// kennt weder FTP noch Cache.
    /// </summary>
    internal sealed class PreviewForm : AppWindow
    {
        private readonly IReadOnlyList<MediaItem> _items;
        private readonly Func<MediaItem, Task<byte[]>> _loadImageBytes;
        private readonly Func<MediaItem, IProgress<long>, Task<string>> _loadVideoFile;
        private readonly MediaSessionGuard _session;
        private readonly SynchronizationContext _uiContext;
        private readonly CancellationTokenRegistration _sessionCancellationRegistration;
        private readonly string _gameName;
        private readonly PreviewCanvas _canvas = new PreviewCanvas { Dock = DockStyle.Fill };
        private readonly VideoView _video = new VideoView { Dock = DockStyle.Fill, Visible = false };
        private int _index;

        // Zählt Navigationen; veraltete Ladevorgänge erkennen sich daran
        // (schnelles Blättern kann Ladungen in beliebiger Reihenfolge beenden —
        // Cache-Treffer vs. Netzwerk-Miss, und ein Clip-Download dauert lange).
        private int _generation;

        // Generation, für die zuletzt Play() aufgerufen wurde. MediaFailed
        // kommt asynchron aus der WPF-Pipeline zurück — ohne diesen Vergleich
        // würde ein Fehler des vorigen Clips den Titel des schon
        // weitergeblätterten neuen Items überschreiben.
        private int _videoGeneration;

        public PreviewForm(IReadOnlyList<MediaItem> items, int startIndex, string gameName,
            Func<MediaItem, Task<byte[]>> loadImageBytes,
            Func<MediaItem, IProgress<long>, Task<string>> loadVideoFile,
            MediaSessionGuard session)
        {
            _items = items;
            _index = startIndex;
            _gameName = gameName;
            _loadImageBytes = loadImageBytes;
            _loadVideoFile = loadVideoFile;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            WindowIcon.Apply(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1280, 720);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            Controls.Add(_canvas);
            Controls.Add(_video);
            _video.Failed += (s, message) =>
            {
                // Wie jeder andere Text-Schreibzugriff in diesem Fenster gegen
                // die Generation absichern: MediaFailed kann eintreffen,
                // nachdem der Nutzer längst weitergeblättert hat.
                TryApply(_videoGeneration, () => Text = "Fehler: " + message);
            };

            KeyDown += OnPreviewKeyDown;
            Shown += async (s, e) => await ShowCurrentAsync();
            _sessionCancellationRegistration = _session.Session.CancellationToken.Register(() =>
                _uiContext.Post(state => ((PreviewForm)state).InvalidateSession(), this));
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }

            var showCurrent = false;
            _session.TryMutate(() =>
            {
                // Clip-eigene Tasten zuerst. Links/Rechts bleiben bewusst das
                // Blättern — sortenabhängige Pfeiltasten wären in einer gemischten
                // Gruppe genau die Inkonsistenz, die stört.
                if (_items[_index].Kind == MediaKind.Video)
                {
                    if (e.KeyCode == Keys.Space)
                    {
                        _video.TogglePlayPause();
                        e.Handled = true;
                        return;
                    }
                    if (e.Shift && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
                    {
                        _video.Nudge(TimeSpan.FromSeconds(e.KeyCode == Keys.Left ? -10 : 10));
                        e.Handled = true;
                        return;
                    }
                }

                if (e.KeyCode == Keys.Left && _index > 0) { _index--; showCurrent = true; }
                if (e.KeyCode == Keys.Right && _index < _items.Count - 1) { _index++; showCurrent = true; }
            });
            if (showCurrent) { var _ = ShowCurrentAsync(); }
        }

        private async Task ShowCurrentAsync()
        {
            // Eigene Generation ziehen: Falls beim Eintreffen der Antwort längst
            // weitergeblättert wurde (oder das Fenster inzwischen entsorgt ist),
            // bricht dieser Aufruf unten ab. Beim Clip ist das keine Feinheit —
            // sein Download kann Minuten dauern.
            int gen = 0;
            MediaItem item = null;
            string title = null;
            var initialized = false;
            if (!_session.TryMutate(() =>
            {
                if (IsDisposed) return;
                gen = ++_generation;
                _video.Stop();             // Ton des vorigen Clips beenden
                item = _items[_index];
                title = _gameName + " — " + item.Timestamp.ToString("dd.MM.yyyy HH:mm") +
                        "  (" + (_index + 1) + "/" + _items.Count + ")";
                Text = title;
                initialized = true;
            }) || !initialized) return;

            if (item.Kind == MediaKind.Video)
                await ShowVideoAsync(item, title, gen).ConfigureAwait(true);
            else
                await ShowImageAsync(item, gen).ConfigureAwait(true);
        }

        private async Task ShowImageAsync(MediaItem item, int gen)
        {
            if (!TryApply(gen, () =>
            {
                _video.Visible = false;
                _canvas.Visible = true;
                _canvas.SetImage(null);
            })) return;

            try
            {
                var data = await _loadImageBytes(item);
                using (var stream = new MemoryStream(data))
                using (var direct = Image.FromStream(stream))
                {
                    var bitmap = new Bitmap(direct);

                    // Die Fortsetzung läuft auf dem UI-Thread; TryApply hält
                    // zusätzlich den Session-Controller-Lock bis SetImage
                    // abgeschlossen ist.
                    var applied = TryApply(gen, () =>
                    {
                        if (_canvas.IsDisposed) return;
                        _canvas.SetImage(bitmap);
                    });
                    if (!applied || _canvas.IsDisposed) bitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                TryApply(gen, () =>
                {
                    if (!_canvas.IsDisposed) Text = "Fehler: " + Root(ex).Message;
                });
            }
        }

        private async Task ShowVideoAsync(MediaItem item, string title, int gen)
        {
            // Vor dem Warten setzen, nicht erst im Fortschritts-Callback: hängt
            // sich diese Navigation an einen bereits laufenden Download einer
            // früheren an, gehört der Progress-Callback noch der ersten
            // Generation und aktualisiert diesen Titel nie — ohne diese Zeile
            // stünde das Fenster ohne jede Ladeanzeige da.
            if (!TryApply(gen, () =>
            {
                _canvas.SetImage(null);
                _canvas.Visible = false;
                _video.Visible = true;
                Text = title + " — lädt …";
            })) return;

            try
            {
                var progress = new Progress<long>(bytes =>
                {
                    TryApply(gen, () =>
                        Text = title + " — lädt " + Percent(bytes, item.Size) + " %");
                });

                var file = await _loadVideoFile(item, progress);

                TryApply(gen, () =>
                {
                    if (_video.IsDisposed) return;
                    Text = title;
                    _videoGeneration = gen;
                    _video.Play(file);
                });
            }
            catch (Exception ex)
            {
                TryApply(gen, () => Text = "Fehler: " + Root(ex).Message);
            }
        }

        private bool TryApply(int generation, Action update)
        {
            var applied = false;
            if (!_session.TryMutate(() =>
            {
                if (generation != _generation || IsDisposed) return;
                update();
                applied = true;
            })) return false;
            return applied;
        }

        private void InvalidateSession()
        {
            if (IsDisposed) return;
            _generation++;
            _video.Stop();
            Close();
        }

        private static int Percent(long done, long total)
        {
            if (total <= 0) return 0;
            long percent = done * 100 / total;
            return percent > 100 ? 100 : (int)percent;
        }

        // Wie in MainForm: zeigt die innerste Ursache statt der äußeren
        // WebException-Hülle ("Der Remoteserver hat einen Fehler
        // zurückgegeben…"), damit beide Fenster dieselbe Störung gleich
        // erklären. Absichtlich dupliziert statt in eine gemeinsame
        // Hilfsklasse ausgelagert — das wäre ein Umbau über diese Runde hinaus.
        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Ohne das läuft der Ton weiter, bis der Finalizer aufräumt.
            _generation++;
            _video.Stop();
            _sessionCancellationRegistration.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>Skin-Fläche + eingepasstes Bild; kein PictureBox (unskinbar).</summary>
        private sealed class PreviewCanvas : DevExpress.XtraEditors.PictureEdit
        {
            private Image _image;

            public PreviewCanvas() { Properties.SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom; Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder; Properties.ShowMenu = false; }

            public void SetImage(Image image)
            {
                var old = _image;
                _image = image;
                Image = image;
                if (old != null) old.Dispose();
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _image != null) _image.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
