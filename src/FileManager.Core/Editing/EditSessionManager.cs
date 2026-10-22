using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Editing
{
    /// <summary>
    /// Öffnen mit Rückschreiben (Spec): Arbeitskopien unter workRoot werden
    /// beobachtet; nach einer Sekunde Ruhe und sobald der Editor die Datei
    /// freigibt, meldet ChangeDetected — einmal pro Änderungsschub. Die Uhr
    /// läuft über IClock und den Takt der App (Tick), damit das testbar ist.
    /// Hochgeladen wird NICHT hier, sondern als Auftrag über FileOperations
    /// und die Warteschlange — samt Zonenprüfung.
    /// Angekündigt werden nur Änderungen gegenüber dem zuletzt gesehenen
    /// lokalen Stand (Schreibzeit und Länge).
    /// </summary>
    public sealed class EditSessionManager : IDisposable
    {
        public const long AskAboveBytes = 200L * 1024 * 1024;
        public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

        private readonly string _workRoot;
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly IFileChangeSource _changes;
        private readonly IClock _clock;
        private readonly TransferQueue _queue;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly object _lock = new object();
        private readonly List<EditSession> _sessions = new List<EditSession>();
        private readonly Dictionary<TransferJob, PendingWriteBack> _writeBacks =
            new Dictionary<TransferJob, PendingWriteBack>();
        private bool _disposed;

        private sealed class PendingWriteBack
        {
            public EditSession Session;
            public TaskCompletionSource<JobState> Completion;
        }

        public EditSessionManager(string workRoot, IFtpClient ftp, FtpGates gates, IFileChangeSource changes, IClock clock)
            : this(workRoot, ftp, gates, changes, clock, null)
        {
        }

        internal EditSessionManager(string workRoot, IFtpClient ftp, FtpGates gates, IFileChangeSource changes,
            IClock clock, TransferQueue queue)
        {
            if (workRoot == null) throw new ArgumentNullException(nameof(workRoot));
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _workRoot = workRoot;
            _ftp = ftp;
            _gates = gates;
            _changes = changes;
            _clock = clock;
            _queue = queue;

            Directory.CreateDirectory(workRoot);
            _changes.Changed += OnChanged;
            _changes.Watch(workRoot);
            if (_queue != null) _queue.JobChanged += OnWriteBackJobChanged;
        }

        public event EventHandler<EditSession> ChangeDetected;

        public static bool NeedsSizeConfirmation(long size)
        {
            return size > AskAboveBytes;
        }

        /// <summary>Beim Start: Arbeitskopien der letzten Sitzung weg (Spec).</summary>
        public static void CleanWorkRoot(string workRoot)
        {
            if (!Directory.Exists(workRoot)) return;
            foreach (var dir in Directory.GetDirectories(workRoot))
            {
                try { Directory.Delete(dir, true); } catch (Exception) { /* noch offen — beim nächsten Start */ }
            }
            foreach (var file in Directory.GetFiles(workRoot))
            {
                try { File.Delete(file); } catch (Exception) { }
            }
        }

        public IReadOnlyList<EditSession> PendingSessions
        {
            get { lock (_lock) return _sessions.Where(s => s.HasPendingChange).ToList(); }
        }

        public string PrepareLocalPath(string remotePath)
        {
            var dir = Path.Combine(_workRoot, Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, LocalName.Sanitize(RemotePath.Name(remotePath)));
        }

        public EditSession Register(string remotePath, string localPath, long size, DateTime modified)
        {
            return RegisterCore(remotePath, localPath, size, modified, CancellationToken.None);
        }

        private EditSession RegisterCore(string remotePath, string localPath, long size, DateTime modified,
            CancellationToken ct)
        {
            var session = new EditSession(RemotePath.Normalize(remotePath), localPath, size, modified);
            session.BaseContentHash = HashFile(localPath, ct);
            ct.ThrowIfCancellationRequested();
            session.BaseRemoteExists = true;
            Stamp(session);
            ct.ThrowIfCancellationRequested();
            lock (_lock) _sessions.Add(session);
            return session;
        }

        public Task<EditSession> RegisterAsync(string remotePath, string localPath, long size, DateTime modified,
            CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return RegisterCore(remotePath, localPath, size, modified, ct);
            }, ct);
        }

        private void OnChanged(object sender, string fullPath)
        {
            lock (_lock)
            {
                var session = _sessions.FirstOrDefault(s => string.Equals(s.LocalPath, fullPath, StringComparison.OrdinalIgnoreCase));
                if (session != null) session.LastChangeUtc = _clock.UtcNow;
            }
        }

        public void Tick()
        {
            var ready = new List<EditSession>();
            var now = _clock.UtcNow;
            lock (_lock)
            {
                foreach (var session in _sessions)
                {
                    if (!session.LastChangeUtc.HasValue) continue;
                    if (now - session.LastChangeUtc.Value < Quiet) continue;
                    if (!File.Exists(session.LocalPath))
                    {
                        session.LastChangeUtc = null;
                        continue;
                    }
                    if (!IsReadable(session.LocalPath)) continue;
                    session.LastChangeUtc = null;
                    if (!ChangedSinceStamp(session)) continue;
                    Stamp(session);
                    session.HasPendingChange = true;
                    ready.Add(session);
                }
            }

            var handler = ChangeDetected;
            if (handler == null) return;
            foreach (var session in ready) handler(this, session);
        }

        public async Task<bool> RemoteChangedAsync(EditSession session, CancellationToken ct)
        {
            var entry = await FindRemoteAsync(session, ct).ConfigureAwait(false);
            byte[] hash = null;
            if (entry != null) hash = await HashRemoteAsync(session.RemotePath, ct).ConfigureAwait(false);
            lock (_lock)
            {
                session.ObservedRemoteExists = entry != null;
                session.ObservedRemoteSize = entry != null ? entry.Size : 0;
                session.ObservedRemoteModified = entry != null ? entry.Modified : DateTime.MinValue;
                session.ObservedRemoteHash = hash;
                return session.BaseRemoteExists != session.ObservedRemoteExists ||
                    (entry != null && (entry.Size != session.BaseSize || entry.Modified != session.BaseModified ||
                        session.BaseContentHash == null || !session.BaseContentHash.SequenceEqual(hash)));
            }
        }

        public void AcceptObservedRemoteVersion(EditSession session)
        {
            lock (_lock)
            {
                session.BaseRemoteExists = session.ObservedRemoteExists;
                session.BaseSize = session.ObservedRemoteSize;
                session.BaseModified = session.ObservedRemoteModified;
                session.BaseContentHash = session.ObservedRemoteHash == null ? null : session.ObservedRemoteHash.ToArray();
            }
        }

        /// <summary>
        /// Macht den vollständig gesicherten Remote-Inhalt zur Vorbedingung
        /// des folgenden Write-backs. Änderungen nach der Sicherung werden so
        /// von der TransferQueue abgelehnt.
        /// </summary>
        public void AcceptRemoteSnapshot(EditSession session, byte[] content)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (content == null) throw new ArgumentNullException(nameof(content));
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(content);
            lock (_lock)
            {
                session.BaseRemoteExists = true;
                session.BaseSize = content.LongLength;
                session.BaseModified = DateTime.MinValue;
                session.BaseContentHash = hash;
            }
        }

        public bool RemoteSnapshotChanged(EditSession session, byte[] content)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (content == null) throw new ArgumentNullException(nameof(content));
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(content);
            lock (_lock)
            {
                return !session.BaseRemoteExists || session.BaseContentHash == null ||
                    !session.BaseContentHash.SequenceEqual(hash);
            }
        }

        public void ProtectWriteBack(TransferJob job, EditSession session)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            var step = job.Steps.Single(s => s.Kind == StepKind.UploadFile);
            lock (_lock)
            {
                step.TargetIdentityRequired = true;
                step.ExpectedTargetExists = session.BaseRemoteExists;
                step.ExpectedTargetHash = session.BaseContentHash == null ? null : session.BaseContentHash.ToArray();
            }
        }

        /// <summary>
        /// Registers completion before a write-back job is enqueued. The session,
        /// not a tool window, owns finalization so detaching the UI cannot strand
        /// a successfully uploaded edit in the pending state.
        /// </summary>
        public Task<JobState> TrackWriteBackAsync(TransferJob job, EditSession session)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (_queue == null) throw new InvalidOperationException("This edit manager is not attached to a transfer queue.");

            var completion = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(EditSessionManager));
                if (_writeBacks.ContainsKey(job)) throw new InvalidOperationException("The write-back job is already tracked.");
                _writeBacks.Add(job, new PendingWriteBack { Session = session, Completion = completion });
            }
            if (job.IsFinished) OnWriteBackJobChanged(_queue, job);
            return completion.Task;
        }

        private async void OnWriteBackJobChanged(object sender, TransferJob job)
        {
            if (!job.IsFinished) return;

            PendingWriteBack pending;
            lock (_lock)
            {
                if (!_writeBacks.TryGetValue(job, out pending)) return;
                _writeBacks.Remove(job);
            }

            if (job.State != JobState.Completed)
            {
                pending.Completion.TrySetResult(job.State);
                return;
            }

            try
            {
                await CompleteWriteBackAsync(pending.Session, job, _lifetime.Token).ConfigureAwait(false);
                pending.Completion.TrySetResult(job.State);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                pending.Completion.TrySetCanceled();
            }
            catch (Exception ex)
            {
                pending.Completion.TrySetException(ex);
            }
        }

        public async Task CompleteWriteBackAsync(EditSession session, TransferJob completedJob, CancellationToken ct)
        {
            if (completedJob == null) throw new ArgumentNullException(nameof(completedJob));
            var upload = completedJob.Steps.Single(s => s.Kind == StepKind.UploadFile);
            if (upload.LocalSourceHash == null)
                throw new InvalidOperationException("The completed upload has no source identity.");
            var uploadedHash = upload.LocalSourceHash.ToArray();
            var entry = await FindRemoteAsync(session, ct).ConfigureAwait(false);
            var size = entry != null ? entry.Size : new FileInfo(session.LocalPath).Length;
            var modified = entry != null ? entry.Modified : DateTime.MinValue;
            // Schreibzugriffe bleiben bis nach der Zustandsentscheidung gesperrt.
            // Sonst könnte eine Speicherung zwischen Hash und Stamp verloren gehen.
            using (var local = new FileStream(session.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var localHash = await Task.Run(() => HashStream(local, ct), ct).ConfigureAwait(false);
                var localStillMatchesUpload = uploadedHash.SequenceEqual(localHash);
                lock (_lock)
                {
                    session.BaseSize = size;
                    session.BaseModified = modified;
                    session.BaseContentHash = uploadedHash;
                    session.BaseRemoteExists = true;
                    if (localStillMatchesUpload)
                    {
                        Stamp(session);
                        session.LastChangeUtc = null;
                        session.HasPendingChange = false;
                    }
                    else
                    {
                        // Eine weitere Speicherung während des Uploads gehört noch nicht
                        // zur Remote-Baseline und muss erneut angeboten werden.
                        session.LastChangeUtc = _clock.UtcNow;
                        session.HasPendingChange = true;
                    }
                }
            }
        }

        /// <summary>
        /// Markiert eine Änderung des eingebauten Editors als ausstehend und
        /// absorbiert das dazugehörige verzögerte Dateisystem-Ereignis.
        /// </summary>
        public void MarkLocalSavePending(EditSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            lock (_lock)
            {
                Stamp(session);
                session.LastChangeUtc = null;
                session.HasPendingChange = true;
            }
        }

        public void Discard(EditSession session)
        {
            lock (_lock) session.HasPendingChange = false;
        }

        /// <summary>Frisches Listing am Verzeichnis-Cache vorbei — der Cache könnte veraltet sein.</summary>
        private async Task<FtpEntry> FindRemoteAsync(EditSession session, CancellationToken ct)
        {
            IReadOnlyList<FtpEntry> entries;
            try
            {
                entries = await FtpGates.RunAsync(_gates.Browse,
                    () => _ftp.ListAsync(RemotePath.Parent(session.RemotePath), ct), ct).ConfigureAwait(false);
            }
            catch (FtpException ex) when (!ex.IsTransient)
            {
                return null;                                      // Ordner weg
            }
            var name = RemotePath.Name(session.RemotePath);
            return entries.FirstOrDefault(e => e.Name == name);
        }

        private static bool IsReadable(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void Stamp(EditSession session)
        {
            var info = new FileInfo(session.LocalPath);
            session.LocalWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            session.LocalLength = info.Exists ? info.Length : -1;
        }

        private static bool ChangedSinceStamp(EditSession session)
        {
            var info = new FileInfo(session.LocalPath);
            // Beim Speichern über Umbenennen kann die Datei kurz verschwinden.
            if (!info.Exists) return false;
            return info.LastWriteTimeUtc != session.LocalWriteUtc || info.Length != session.LocalLength;
        }

        private Task<byte[]> HashRemoteAsync(string path, CancellationToken ct)
        {
            return FtpGates.RunAsync(_gates.Transfer,
                () => Task.Run(() =>
                {
                    using (var stream = _ftp.OpenRead(path, 0)) return HashStream(stream, ct);
                }, ct), ct);
        }

        private static byte[] HashFile(string path, CancellationToken ct)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return HashStream(stream, ct);
        }

        private static byte[] HashStream(Stream stream, CancellationToken ct)
        {
            using (var hash = SHA256.Create())
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return hash.Hash;
            }
        }

        public void Dispose()
        {
            PendingWriteBack[] pending;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                pending = _writeBacks.Values.ToArray();
                _writeBacks.Clear();
            }
            if (_queue != null) _queue.JobChanged -= OnWriteBackJobChanged;
            _lifetime.Cancel();
            foreach (var writeBack in pending) writeBack.Completion.TrySetCanceled();
            _changes.Changed -= OnChanged;
            _changes.Dispose();
            _lifetime.Dispose();
        }
    }
}
