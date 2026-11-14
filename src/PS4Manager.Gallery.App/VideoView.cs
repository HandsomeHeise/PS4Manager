using PS4Manager.UI;
using System;
using System.Drawing;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;

namespace PS4Manager.Gallery.App
{
    /// <summary>
    /// Der eingebaute Player: WPF-MediaElement in einem ElementHost, darunter
    /// eine geskinnte Transportleiste als gewöhnliches WinForms-Control. Weil
    /// sich Video- und Leistenrechteck nicht überlappen, gibt es kein
    /// Airspace-Problem — der Skin bricht nur dort, wo ohnehin Video läuft.
    ///
    /// Kennt weder FTP noch Cache: es bekommt einen fertigen lokalen Dateipfad.
    /// </summary>
    internal sealed class VideoView : UiPanel
    {
        private readonly ElementHost _host = new ElementHost { Dock = DockStyle.Fill };
        private readonly MediaElement _media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform
        };

        private readonly UiPanel _bar = new UiToolbar { Dock = DockStyle.Bottom };
        private readonly UiButton _playPause = new UiButton { Text = "Pause", AutoSize = true };
        private readonly SeekBar _seek = new SeekBar();
        private readonly UiLabel _time = new UiLabel { Text = "0:00", AutoSize = true };

        private readonly System.Windows.Forms.Timer _tick =
            new System.Windows.Forms.Timer { Interval = 250 };

        private bool _playing;

        public VideoView()
        {
            _host.Child = _media;

            // Der ElementHost malt nicht mit dem Skin — ihn wenigstens auf die
            // Panelfarbe setzen, damit die Ränder neben dem eingepassten Bild
            // nicht systemgrau aufblitzen.
            _host.BackColor = AppTheme.Background;

            _bar.Controls.Add(_seek);
            _bar.Controls.Add(_playPause);
            _bar.Controls.Add(_time);

            Controls.Add(_host);
            Controls.Add(_bar);      // Dock=Bottom NACH Dock=Fill hinzufügen

            _playPause.Click += (s, e) => TogglePlayPause();
            _seek.SeekRequested += (s, e) => SeekToFraction(_seek.Value);
            _bar.Resize += (s, e) => LayoutBar();
            // _time und _playPause sind AutoSize=true und wachsen bei jeder
            // Textänderung nach rechts, ohne die Zeile selbst neu anzuordnen —
            // ohne diese Verdrahtung bliebe die Leiste auf dem Stand vom
            // letzten Resize/MediaOpened stehen und die Zeitanzeige würde am
            // rechten Rand abgeschnitten bzw. der Regler vom Button überlappt.
            _time.TextChanged += (s, e) => LayoutBar();
            _playPause.TextChanged += (s, e) => LayoutBar();
            _tick.Tick += OnTick;

            _media.MediaOpened += (s, e) => { UpdateDisplay(); LayoutBar(); Opened?.Invoke(this, EventArgs.Empty); };
            _media.MediaEnded += (s, e) =>
            {
                // Am Ende stehen bleiben statt still weiterzulaufen: Pause plus
                // Rücksprung auf Null macht den Zustand sichtbar richtig.
                _media.Pause();
                _media.Position = TimeSpan.Zero;
                SetPlaying(false);
            };
            _media.MediaFailed += (s, e) =>
            {
                SetPlaying(false);
                var handler = Failed;
                if (handler != null)
                    handler(this, e.ErrorException == null
                        ? "Der Clip lässt sich nicht abspielen."
                        : e.ErrorException.Message);
            };

            LayoutBar();
        }

        /// <summary>Klartext für den Fenstertitel — kein Dialog, kein Absturz.</summary>
        public event EventHandler<string> Failed;
        public event EventHandler Opened;
        public TimeSpan Position => _media.Position;
        public TimeSpan Duration => _media.Source != null && _media.NaturalDuration.HasTimeSpan
            ? _media.NaturalDuration.TimeSpan : TimeSpan.Zero;

        public void Pause()
        {
            if (_media.Source == null || !_playing) return;
            _media.Pause();
            SetPlaying(false);
        }

        public void Play(string localFile)
        {
            _media.Source = new Uri(localFile);
            _media.Play();
            SetPlaying(true);
        }

        /// <summary>
        /// Muss beim Wegblättern UND beim Schließen laufen, sonst spielt der Ton
        /// des vorigen Clips weiter.
        /// </summary>
        public void Stop()
        {
            _tick.Stop();
            _media.Stop();
            _media.Close();
            _media.Source = null;
            SetPlaying(false);
            _seek.Value = 0;
            _time.Text = "0:00";
        }

        public void TogglePlayPause()
        {
            if (_media.Source == null) return;
            if (_playing) _media.Pause(); else _media.Play();
            SetPlaying(!_playing);
        }

        public void Nudge(TimeSpan delta)
        {
            if (_media.Source == null || !_media.NaturalDuration.HasTimeSpan) return;

            var total = _media.NaturalDuration.TimeSpan;
            var target = _media.Position + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > total) target = total;
            _media.Position = target;
            UpdateDisplay();
        }

        private void SeekToFraction(double fraction)
        {
            if (_media.Source == null || !_media.NaturalDuration.HasTimeSpan) return;
            _media.Position = TimeSpan.FromSeconds(
                _media.NaturalDuration.TimeSpan.TotalSeconds * fraction);
            UpdateDisplay();
        }

        /// <summary>
        /// Einziger Eigentümer des Tickgebers: Nur während echter Wiedergabe muss
        /// die Anzeige laufend nachgeführt werden — im Pausen- oder Endzustand
        /// wäre das reine Verschwendung. Alles hier zu bündeln hält Play, Pause,
        /// Ende und Fehler automatisch im Einklang, statt dass jeder Pfad selbst
        /// an Start/Stop des Tickgebers denken müsste.
        /// </summary>
        private void SetPlaying(bool playing)
        {
            _playing = playing;
            _playPause.Text = playing ? "Pause" : "Abspielen";
            if (playing) _tick.Start(); else _tick.Stop();
            UpdateDisplay();
        }

        private void OnTick(object sender, EventArgs e)
        {
            UpdateDisplay();
        }

        /// <summary>
        /// Eigene Methode statt nur im Tickgeber verankert, weil ein Sprung im
        /// pausierten Zustand — wo der Tickgeber steht — Leiste und Zeit sonst
        /// veraltet stehen ließe, bis die Wiedergabe fortgesetzt wird.
        /// </summary>
        private void UpdateDisplay()
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                var total = _media.NaturalDuration.TimeSpan;
                _seek.Enabled = true;
                if (!_seek.IsDragging && total.TotalSeconds > 0)
                    _seek.Value = _media.Position.TotalSeconds / total.TotalSeconds;
                _time.Text = Format(_media.Position) + " / " + Format(total);
            }
            else
            {
                // Container ohne verwertbare Dauer: abspielen geht, springen nicht.
                _seek.Enabled = false;
                _seek.Value = 0;
                _time.Text = Format(_media.Position);
            }
        }

        private void LayoutBar()
        {
            int margin = DpiScale.Scale(8, DeviceDpi);
            var area = _bar.DisplayRectangle;
            _playPause.Location = new Point(area.Left, area.Top + (area.Height - _playPause.Height) / 2);

            _time.Location = new Point(
                area.Right - _time.Width,
                area.Top + (area.Height - _time.Height) / 2);

            int left = _playPause.Right + margin;
            int right = _time.Left - margin;
            _seek.SetBounds(left, area.Top + (area.Height - _seek.Height) / 2,
                Math.Max(1, right - left), _seek.Height);
        }

        private static string Format(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            return value.Hours > 0
                ? string.Format("{0}:{1:00}:{2:00}", value.Hours, value.Minutes, value.Seconds)
                : string.Format("{0}:{1:00}", value.Minutes, value.Seconds);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tick.Stop();
                _tick.Dispose();
                _media.Close();
                _host.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
