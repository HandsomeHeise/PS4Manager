using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Config;
using PS4Manager.Core.Media;
using PS4Manager.Core.Session;
using PS4Manager.Gallery.Core.Caching;
using PS4Manager.Gallery.Core.Config;
using Ps4.Core.Ftp;
using PS4Manager.Gallery.Core.Ps4;

namespace PS4Manager.Gallery.App
{
    public sealed class MediaGalleryForm : AppWindow
    {
        private static readonly string UnifiedDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS4Manager");

        private readonly UiButton _refresh = new UiButton { Text = "Aktualisieren", AutoSize = true, Enabled = false };
        private readonly UiLabel _status = new UiLabel { Text = "Getrennt." };
        private readonly AppGallery _gallery = new AppGallery { Dock = DockStyle.Fill };
        private readonly AppPopup _itemMenu = new AppPopup();

        private ThumbnailCache _thumbnails;
        private ThumbnailCache _fullImages;

        /// <summary>Obergrenze des Clip-Caches (Spec: 4 GB).</summary>
        private const long VideoCacheBudgetBytes = 4L * 1024 * 1024 * 1024;

        private MediaFileCache _videoFiles;

        // Die Registry entfernt einen abgeschlossenen Download nur, solange
        // derselbe Task den Schlüssel noch besitzt. So kann die späte
        // Aufräumfortsetzung einer ersetzten Sitzung keinen neuen Download
        // derselben Datei aus der aktuellen Sitzung entfernen.
        private readonly MediaDownloadRegistry _videoDownloads = new MediaDownloadRegistry();
        private readonly List<PreviewForm> _previews = new List<PreviewForm>();

        private readonly ToolUiDispatcher _uiDispatcher = new ToolUiDispatcher(
            SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        private readonly ManagerSessionController _sessions;
        private MediaToolSession _mediaToolSession;
        private ManagerSession _managerSession;
        private MediaRepository _repository;
        private MediaTransferPlanner _transfers;
        private CancellationToken _loadToken;
        private ThumbnailLoader _thumbnailLoader;

        public MediaGalleryForm(ManagerSessionController sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            WindowIcon.Apply(this);
            InitializeCaches(Path.Combine(UnifiedDataRoot, "cache", "gallery"));
            InitializeUi();
            _mediaToolSession = CreateMediaToolSession(_sessions);
        }

        private void InitializeCaches(string cacheRoot)
        {
            _thumbnails = new ThumbnailCache(cacheRoot);
            _fullImages = new ThumbnailCache(Path.Combine(cacheRoot, "full"));
            _videoFiles = new MediaFileCache(Path.Combine(cacheRoot, "video"), VideoCacheBudgetBytes);
        }

        private void InitializeUi()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "PS4 Screenshot Gallery";
            ClientSize = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;

            // Nur noch die Werkzeugleiste des Werkzeugfensters: Verbunden und
            // getrennt wird im Dashboard, hier gibt es nur Aktualisieren und die
            // Statuszeile.
            var toolbar = new UiToolbar { Dock = DockStyle.Top };
            _refresh.Dock = DockStyle.Left;
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            toolbar.Controls.Add(_status);
            toolbar.Controls.Add(_refresh);

            _gallery.Gallery.ImageSize = new Size(192, 108);
            _gallery.Gallery.ItemCheckMode = ItemCheckMode.Multiple;

            _gallery.Gallery.ItemImageNeeded += (s, e) =>
            {
                var shot = e.Item.Tag as MediaItem;
                if (shot == null || _thumbnailLoader == null || _managerSession == null) return;
                _thumbnailLoader.Enqueue(e.Item, shot, _loadToken);
            };

            // Kontextmenü auf Auswahl: Rechtsklick nur wirksam, wenn etwas
            // angehakt ist — sonst gäbe es nichts, worauf sich die Einträge
            // beziehen könnten.
            _gallery.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                if (!_gallery.Gallery.CheckedItems.GetEnumerator().MoveNext()) return;
                ShowItemMenu(e.Location);
            };
            _gallery.Gallery.ItemDoubleClick += (s, e) => OpenPreview(e.Item);

            Controls.Add(_gallery);
            Controls.Add(toolbar);       // Dock=Top NACH Dock=Fill hinzufügen: WinForms dockt in umgekehrter Add-Reihenfolge

            _refresh.Click += (s, e) => _mediaToolSession.Reload();
        }

        private MediaToolSession CreateMediaToolSession(ManagerSessionController sessions)
        {
            return new MediaToolSession(
                sessions,
                OnSessionAttached,
                OnSessionDetached,
                OnMediaLoaded,
                OnMediaLoadFailed);
        }

        /// <summary>
        /// Baut das Kontextmenü bei jedem Öffnen frisch — der Aktiv-Zustand
        /// hängt an der aktuellen Auswahl. AppPopup bindet diese Aktionen an
        /// ein natives DevExpress-Popupmenü und erwartet Bildschirmkoordinaten.
        /// </summary>
        private void ShowItemMenu(Point location)
        {
            var selected = new List<GalleryItem>(_gallery.Gallery.CheckedItems);

            var download = new MenuEntry("Herunterladen…");
            download.Click += async (s, e) => await DownloadSelectedAsync(selected);

            var copyToClipboard = new MenuEntry("In Zwischenablage kopieren") { Enabled = selected.Count == 1 };
            copyToClipboard.Click += async (s, e) => await CopyToClipboardAsync(selected);

            var openPreview = new MenuEntry("Vorschau öffnen") { Enabled = selected.Count == 1 };
            openPreview.Click += (s, e) => OpenPreview(selected[0]);

            var delete = new MenuEntry("Löschen…");
            delete.Click += async (s, e) => await DeleteSelectedAsync(selected);

            _itemMenu.Items.Clear();
            _itemMenu.Items.Add(download);
            _itemMenu.Items.Add(copyToClipboard);
            _itemMenu.Items.Add(openPreview);
            _itemMenu.Items.Add(MenuEntry.Separator());
            _itemMenu.Items.Add(delete);
            _itemMenu.Show(_gallery, _gallery.PointToScreen(location));
        }

        private async Task DownloadSelectedAsync(List<GalleryItem> selected)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var session = _managerSession;
                var token = _loadToken;
                if (!IsCurrentSession(session, token)) return;

                try
                {
                    var media = selected.ConvertAll(item => (MediaItem)item.Tag);
                    var job = _transfers.PlanDownload(media, dialog.SelectedPath);
                    await EnqueueAndWaitAsync(session, job, token);
                    if (!IsCurrentSession(session, token)) return;

                    int done = job.Steps.Count(step => step.State == StepState.Done);
                    _status.Text = done + " Datei(en) heruntergeladen.";
                    ShowJobFailures("Herunterladen", job);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (IsCurrentSession(session, token))
                        _status.Text = "Herunterladen fehlgeschlagen: " + Root(ex).Message;
                }
            }
        }

        private static int Percent(long done, long total)
        {
            if (total <= 0) return 0;
            long percent = done * 100 / total;
            return percent > 100 ? 100 : (int)percent;
        }

        /// <summary>Voll-JPEG mit Cache — auch die Vorschau (Task 13) läuft hierüber.</summary>
        private async Task<byte[]> GetFullImageBytesAsync(MediaItem shot, MediaSessionGuard guard)
        {
            var cached = guard.ReadCache(() => _fullImages.TryGet(shot.TitleId, shot.FileName));
            if (cached != null) return cached;

            var data = await guard.RunAsync(token => guard.Session.Media.DownloadFullAsync(shot, token));
            var staged = _fullImages.Stage(shot.TitleId, shot.FileName, data);
            try
            {
                guard.Mutate(() => _fullImages.Publish(shot.TitleId, shot.FileName, staged));
            }
            finally
            {
                _fullImages.Discard(staged);
            }
            return data;
        }

        private async Task CopyToClipboardAsync(List<GalleryItem> selected)
        {
            if (selected.Count != 1) return;
            var media = (MediaItem)selected[0].Tag;
            MediaSessionGuard guard;
            try { guard = CreateCurrentMediaGuard(); }
            catch (InvalidOperationException) { return; }
            catch (OperationCanceledException) { return; }
            try
            {
                if (media.Kind == MediaKind.Video)
                {
                    // Ein Clip kann kein Bild in der Zwischenablage sein. Als
                    // Dateiliste ist er dafür in Explorer, Chat oder Mail
                    // einfügbar — nützlicher, als den Eintrag zu sperren.
                    var progress = new Progress<long>(bytes =>
                    {
                        guard.TryMutate(() =>
                            _status.Text = "Lade Clip … " + Percent(bytes, media.Size) + " %");
                    });

                    var file = await GetVideoFileAsync(media, progress, guard);
                    guard.Mutate(() =>
                    {
                        var paths = new System.Collections.Specialized.StringCollection();
                        paths.Add(file);
                        Clipboard.SetFileDropList(paths);
                        _status.Text = "Clip in Zwischenablage: " + media.FileName;
                    });
                    return;
                }

                var data = await GetFullImageBytesAsync(media, guard);
                guard.Mutate(() =>
                {
                    ClipboardImage.Copy(data);
                    _status.Text = "In Zwischenablage kopiert: " + media.FileName;
                });
            }
            catch (Exception ex)
            {
                guard.TryMutate(() =>
                    _status.Text = "Zwischenablage fehlgeschlagen: " + Root(ex).Message);
            }
        }

        private void OpenPreview(GalleryItem item)
        {
            var shot = item.Tag as MediaItem;
            if (shot == null || _repository == null) return;

            foreach (var group in _gallery.Gallery.Groups)
            {
                var items = new List<MediaItem>();
                int start = -1;
                foreach (var candidate in group.Items)
                {
                    if (ReferenceEquals(candidate, item)) start = items.Count;
                    items.Add((MediaItem)candidate.Tag);
                }
                if (start < 0) continue;

                var game = (GameFolder)group.Tag;
                MediaSessionGuard guard;
                try { guard = CreateCurrentMediaGuard(); }
                catch (InvalidOperationException) { return; }
                catch (OperationCanceledException) { return; }
                var preview = new PreviewForm(
                    items,
                    start,
                    game.Name,
                    media => GetFullImageBytesAsync(media, guard),
                    (media, progress) => GetVideoFileAsync(media, progress, guard),
                    guard);
                preview.FormClosed += (sender, args) => _previews.Remove(preview);
                _previews.Add(preview);
                preview.Show(this);
                return;
            }
        }

        /// <summary>
        /// Clip-Datei aus dem Cache oder streamend nachgeladen. Läuft für
        /// denselben Clip bereits ein Download (erneutes Anspringen nach dem
        /// Wegblättern), wird dessen Task zurückgegeben statt ein zweites Mal
        /// denselben Transfer abzuwarten. Sitzungsspezifische Stage-Dateien
        /// verhindern zusätzlich Kollisionen mit einem ablaufenden Vorgänger.
        /// </summary>
        private Task<string> GetVideoFileAsync(
            MediaItem item,
            IProgress<long> progress,
            MediaSessionGuard guard)
        {
            var cached = guard.ReadCache(() => _videoFiles.TryGetPath(item.TitleId, item.FileName));
            if (cached != null) return Task.FromResult(cached);

            var key = item.TitleId + "/" + item.FileName;
            return guard.Mutate(() => _videoDownloads.GetOrAdd(
                    key,
                    () => DownloadVideoAsync(item, progress, guard)));
        }

        /// <summary>
        /// Eigentlicher Transfer, von GetVideoFileAsync getrennt. Der Registry-
        /// Eintrag wird bei Abschluss nur entfernt, wenn er noch exakt diesem
        /// Task gehört; eine alte Sitzung kann so keinen Ersatz-Download löschen.
        /// </summary>
        private async Task<string> DownloadVideoAsync(
            MediaItem item,
            IProgress<long> progress,
            MediaSessionGuard guard)
        {
            // GetOrAdd läuft als kurze atomare Registry-Mutation. Erst nach
            // dem Yield beginnt Dateisystem-/FTP-Arbeit außerhalb des
            // Controller-Locks.
            await Task.Yield();
            guard.ThrowIfInactive();
            var part = _videoFiles.BeginStagedWrite(item.TitleId, item.FileName);
            try
            {
                await guard.Session.Media.DownloadToFileAsync(
                    item,
                    part,
                    progress,
                    guard.Session.CancellationToken);
                return guard.Publish(
                    () => _videoFiles.Publish(item.TitleId, item.FileName, part),
                    published => _videoFiles.Maintain(published));
            }
            finally
            {
                _videoFiles.Discard(part);
            }
        }

        private MediaSessionGuard CreateCurrentMediaGuard()
        {
            var session = _managerSession;
            if (session == null) throw new InvalidOperationException("Nicht verbunden.");
            var guard = new MediaSessionGuard(_sessions, session);
            guard.ThrowIfInactive();
            return guard;
        }

        private async Task DeleteSelectedAsync(List<GalleryItem> selected)
        {
            var session = _managerSession;
            var token = _loadToken;
            if (!IsCurrentSession(session, token)) return;

            try
            {
                var media = selected.ConvertAll(item => (MediaItem)item.Tag);
                var job = await _transfers.PlanDeleteAsync(media, token);
                if (job == null || !IsCurrentSession(session, token)) return;

                await EnqueueAndWaitAsync(session, job, token);
                if (!IsCurrentSession(session, token)) return;

                int removed = 0;
                foreach (var item in selected)
                {
                    var value = (MediaItem)item.Tag;
                    var primary = job.Steps.First(step => step.RemotePath == Ps4Paths.MediaPath(value));
                    if (primary.State != StepState.Done) continue;
                    _thumbnails.Remove(value.TitleId, value.FileName);
                    _fullImages.Remove(value.TitleId, value.FileName);
                    _videoFiles.Remove(value.TitleId, value.FileName);
                    RemoveFromGallery(item);
                    removed++;
                }

                _status.Text = removed + " Datei(en) gelöscht.";
                ShowJobFailures("Löschen", job);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(session, token))
                    _status.Text = "Löschen fehlgeschlagen: " + Root(ex).Message;
            }
        }

        private void RemoveFromGallery(GalleryItem item)
        {
            foreach (var group in _gallery.Gallery.Groups)
            {
                if (group.Items.Remove(item))
                {
                    // Bilder gehören dem Konsumenten — vor dem Abhängen nullen,
                    // DANN disposen, um GDI+-Leck zu vermeiden (wie in ClearGroups).
                    var image = item.Image;
                    item.Image = null;
                    if (image != null) image.Dispose();

                    if (group.Items.Count == 0)
                    {
                        // Auch das CaptionImage der leergewordenen Gruppe gehört
                        // dem Konsumenten (siehe ClearGroups) — vor dem Entfernen
                        // erst nullen, DANN disposen, sonst GDI+-Leck.
                        var captionImage = group.CaptionImage;
                        group.CaptionImage = null;
                        if (captionImage != null) captionImage.Dispose();

                        _gallery.Gallery.Groups.Remove(group);
                    }
                    return;
                }
            }
        }

        private void OnSessionAttached(ManagerSession session, CancellationToken token)
        {
            RunOnUi(() => AttachSessionOnUi(session, token));
        }

        private void AttachSessionOnUi(ManagerSession session, CancellationToken token)
        {
            if (token.IsCancellationRequested || IsDisposed) return;
            _managerSession = session;
            _repository = session.Media;
            _transfers = new MediaTransferPlanner(session.Explorer.Operations);
            _loadToken = token;
            _thumbnailLoader = new ThumbnailLoader(_repository, _thumbnails);
            _videoDownloads.Reset();
            ClearGroups();
            Text = "PS4 Screenshot Gallery — " + session.Endpoint.Host + ":" + session.Endpoint.Port;
            _refresh.Enabled = false;
            _status.Text = "Medien werden geladen …";
        }

        private void OnSessionDetached(ManagerSession session)
        {
            RunOnUi(() => DetachSessionOnUi(session));
        }

        private void DetachSessionOnUi(ManagerSession session)
        {
            if (!ReferenceEquals(_managerSession, session)) return;
            ClosePreviews();
            _managerSession = null;
            _repository = null;
            _transfers = null;
            _loadToken = CancellationToken.None;
            _thumbnailLoader = null;
            _videoDownloads.Reset();
            ClearGroups();
            Text = "PS4 Screenshot Gallery";
            _refresh.Enabled = false;
            _status.Text = "Getrennt.";
        }

        private void OnMediaLoaded(ManagerSession session, IReadOnlyList<GameFolder> games)
        {
            RunOnUi(() => ApplyMediaOnUi(session, games));
        }

        private void ApplyMediaOnUi(ManagerSession session, IReadOnlyList<GameFolder> games)
        {
            if (!IsCurrentSession(session, _loadToken)) return;
            ClearGroups();
            int photoCount = 0;
            int clipCount = 0;
            foreach (var game in games)
            {
                var group = new GalleryItemGroup
                {
                    Caption = game.Name + " (" + game.TitleId + ")",
                    CaptionImage = ToImage(game.IconBytes),
                    Tag = game
                };
                foreach (var shot in game.Items)
                {
                    group.Items.Add(new GalleryItem
                    {
                        Caption = shot.Timestamp.ToString("dd.MM.yyyy HH:mm"),
                        Description = FormatSize(shot.Size),
                        Tag = shot
                    });
                    if (shot.Kind == MediaKind.Video) clipCount++; else photoCount++;
                }
                _gallery.Gallery.Groups.Add(group);
            }

            _refresh.Enabled = true;
            _status.Text = games.Count + " Spiele, " + photoCount + " Screenshots, " + clipCount + " Clips.";
        }

        private void OnMediaLoadFailed(ManagerSession session, Exception error)
        {
            RunOnUi(() =>
            {
                if (!ReferenceEquals(session, _managerSession)) return;
                _refresh.Enabled = true;
                _status.Text = "Fehler: " + Root(error).Message;
            });
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed) return;
            try
            {
                _uiDispatcher.Dispatch(() =>
                {
                    if (!IsDisposed) action();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private bool IsCurrentSession(ManagerSession session, CancellationToken token)
        {
            return session != null &&
                   !token.IsCancellationRequested &&
                   ReferenceEquals(session, _managerSession) &&
                   token == _loadToken;
        }

        private static async Task EnqueueAndWaitAsync(
            ManagerSession session,
            TransferJob job,
            CancellationToken token)
        {
            var completion = new TaskCompletionSource<TransferJob>();
            EventHandler<TransferJob> changed = null;
            changed = (sender, candidate) =>
            {
                if (ReferenceEquals(candidate, job) && IsTerminal(candidate.State))
                    completion.TrySetResult(candidate);
            };

            session.Queue.JobChanged += changed;
            try
            {
                using (token.Register(() => completion.TrySetCanceled()))
                {
                    session.Queue.Enqueue(job);
                    if (IsTerminal(job.State)) completion.TrySetResult(job);
                    await completion.Task;
                }
            }
            finally
            {
                session.Queue.JobChanged -= changed;
            }
        }

        private static bool IsTerminal(JobState state)
        {
            return state == JobState.Completed ||
                   state == JobState.CompletedWithErrors ||
                   state == JobState.Cancelled ||
                   state == JobState.Interrupted;
        }

        private void ShowJobFailures(string title, TransferJob job)
        {
            var failures = job.Failures
                .Select(step => step.RemotePath + " — " + step.Error)
                .ToList();
            if (job.State == JobState.Interrupted && !string.IsNullOrEmpty(job.InterruptReason))
                failures.Add(job.InterruptReason);
            if (failures.Count == 0) return;
            MessageBox.Show(this, "Fehlgeschlagen:\n" + string.Join("\n", failures),
                title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static Image ToImage(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    // Kopie ziehen: Image.FromStream verlangt einen offenen Stream
                    // für die Lebenszeit des Bildes — die Kopie entkoppelt das.
                    using (var direct = Image.FromStream(stream))
                    {
                        return new Bitmap(direct);
                    }
                }
            }
            catch (Exception)
            {
                return null;     // korrupte Bytes: dann eben ohne Icon/Bild
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        /// <summary>
        /// Bilder in Gruppen und Einträgen gehören der Medienansicht. Ohne
        /// diese Methode würde jedes
        /// Verbinden/Aktualisieren (bloßes Groups.Clear()) die Bitmaps der
        /// vorigen Generation als GDI+-Leck zurücklassen. Erst auf null setzen,
        /// DANN disposen: ein gerade laufendes Neuzeichnen greift sonst auf ein
        /// bereits entsorgtes Bild zu.
        /// </summary>
        private void ClearGroups()
        {
            foreach (var group in _gallery.Gallery.Groups)
            {
                var captionImage = group.CaptionImage;
                if (captionImage != null)
                {
                    group.CaptionImage = null;
                    captionImage.Dispose();
                }

                foreach (var item in group.Items)
                {
                    var image = item.Image;
                    if (image != null)
                    {
                        item.Image = null;
                        image.Dispose();
                    }
                }
            }

            _gallery.Gallery.Groups.Clear();
        }

        private void ClosePreviews()
        {
            foreach (var preview in _previews.ToArray())
            {
                if (!preview.IsDisposed) preview.Close();
            }
            _previews.Clear();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClosePreviews();
            if (_mediaToolSession != null) _mediaToolSession.Dispose();
            ClearGroups();
            _itemMenu.Dispose();     // AppPopup ist IDisposable (MenuController), Muster wie GridForm._rowMenu
            base.OnFormClosed(e);
        }
    }
}
