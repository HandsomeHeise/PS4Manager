using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Führt Aufträge aus (Spec „Operationen und Aufträge"): einer zur Zeit,
    /// darin eine Datei zur Zeit. Mehr Parallelität bringt bei ~7 MB/s nichts,
    /// und die FTP-Überlastung aus 271477f soll sich nicht wiederholen.
    ///
    /// Fehlerpolitik: permanent (Server sagt nein, Zone, lokal) → die Datei
    /// scheitert, der Auftrag läuft weiter. Transient (Verbindung) → der
    /// Auftrag wird unterbrochen und bleibt fortsetzbar. Keine automatische
    /// Wiederholung (Gallery-Regel).
    ///
    /// Hochgeladen wird immer erst nach &lt;name&gt;.part und dann per RNTO umbenannt.
    /// </summary>
    public sealed class TransferQueue
    {
        private const string PartSuffix = ".part";
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

        private readonly ZoneGuardFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly IConflictResolver _resolver;
        private readonly IClock _clock;
        private readonly object _lock = new object();
        private readonly List<TransferJob> _jobs = new List<TransferJob>();
        private readonly Queue<TransferJob> _pending = new Queue<TransferJob>();
        private Task _worker = Task.FromResult(0);
        private bool _running;

        /// <summary>
        /// Verlangt den Wächter selbst (Abschluss-Review Plan 1): so erzwingt der
        /// Compiler, dass jeder Schreibweg der Warteschlange durch den Zonenschutz läuft.
        /// </summary>
        public TransferQueue(ZoneGuardFtpClient ftp, FtpGates gates, IConflictResolver resolver, IClock clock)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _ftp = ftp;
            _gates = gates;
            _resolver = resolver;
            _clock = clock;
            AppendResumesUploads = true;
        }

        /// <summary>Befund Task 2 Nr. 04–05. false: ein unterbrochener Upload beginnt neu (STOR).</summary>
        public bool AppendResumesUploads { get; set; }

        /// <summary>Feuert auf dem Arbeits-Thread — die App marshallt selbst auf den UI-Thread.</summary>
        public event EventHandler<TransferJob> JobChanged;

        public IReadOnlyList<TransferJob> Jobs
        {
            get { lock (_lock) return _jobs.ToList(); }
        }

        public void Enqueue(TransferJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            lock (_lock)
            {
                if (_jobs.Contains(job)) throw new InvalidOperationException("The job is already in the queue.");
                _jobs.Add(job);
                job.State = JobState.Waiting;
                job.Attempt++;
                _pending.Enqueue(job);
                StartWorkerUnlocked();
            }
            Raise(job);
        }

        public void Resume(TransferJob job)
        {
            if(job.Transaction!=null)throw new InvalidOperationException("Mod transactions must be reconciled through Mod Library recovery.");
            lock (_lock)
            {
                if (job.State != JobState.Interrupted) return;
                job.State = JobState.Waiting;
                job.InterruptReason = null;
                // Publish the new identity only after the previous terminal state is gone.
                job.Attempt++;
                _pending.Enqueue(job);
                StartWorkerUnlocked();
            }
            Raise(job);
        }

        public async Task CancelAsync(TransferJob job)
        {
            CancellationTokenSource runningCts = null;
            lock (_lock)
            {
                if (job.IsFinished) return;
                if (job.State == JobState.Running)
                {
                    runningCts = job.Cancellation;       // Der Arbeits-Thread verwirft die angefangene Datei selbst.
                }
                else
                {
                    job.State = JobState.Cancelled;      // Wartende überspringt der Arbeits-Thread
                }
            }

            if (runningCts != null)
            {
                // Cancel() AUSSERHALB des Locks: registrierte Callbacks (z. B. ein
                // UI-Thread, der Jobs liest) dürfen nie auf diesen Lock warten
                // müssen (Review Runde 1 Nr. 5).
                try { runningCts.Cancel(); }
                catch (ObjectDisposedException) { /* nur zur Absicherung: der Arbeits-Thread entsorgt den Token nicht mehr (s. RunJobAsync-finally), Dispose kommt also von nirgendwo — dieser Zweig sollte nie greifen */ }
                return;
            }

            // Nicht nur ein unterbrochener, sondern auch ein wartender (z. B.
            // fortgesetzter, dann hinter einem anderen Auftrag anstehender)
            // Auftrag kann eine angefangene Datei hinterlassen haben —
            // DiscardPartialAsync selbst ist ein No-Op, wenn nichts begann
            // (Review Runde 1 Nr. 2).
            if (job.Transaction==null && job.CurrentIndex < job.Steps.Count)
                await DiscardPartialAsync(job.Steps[job.CurrentIndex]).ConfigureAwait(false);
            job.CompleteAttempt();
            Raise(job);
        }

        public void ClearFinished()
        {
            lock (_lock) _jobs.RemoveAll(j => j.IsFinished);
        }

        public Task WhenIdle()
        {
            lock (_lock) return _worker;
        }

        private void StartWorkerUnlocked()
        {
            if (_running) return;
            _running = true;
            _worker = Task.Run(WorkAsync);
        }

        private async Task WorkAsync()
        {
            try
            {
                while (true)
                {
                    TransferJob job;
                    lock (_lock)
                    {
                        // _running MUSS im selben Lock-Block wie die Leerprüfung
                        // auf false fallen — sonst kann ein Enqueue/Resume genau
                        // in der Lücke zwischen „Warteschlange leer, Lock frei"
                        // und einem späteren Rücksetzen landen: es sieht
                        // _running noch true, startet keinen Arbeiter, und der
                        // Auftrag bleibt für immer Waiting (Review Runde 2).
                        if (_pending.Count == 0)
                        {
                            _running = false;
                            return;
                        }
                        job = _pending.Dequeue();
                        if (job.State != JobState.Waiting) continue;
                        job.Cancellation = new CancellationTokenSource();
                        job.State = JobState.Running;
                    }
                    await RunJobAsync(job).ConfigureAwait(false);
                }
            }
            catch
            {
                // Nur der ungewöhnliche Pfad greift hier noch ein (z. B. ein
                // werfender JobChanged-Abonnent, falls Raise doch einmal nicht
                // fängt) — der normale Pfad hat _running oben schon gesetzt.
                // Ein unbedingtes Zurücksetzen hier würde sonst einen
                // zwischenzeitlich gestarteten NEUEN Arbeiter wieder auf false
                // setzen und zwei Arbeiter gleichzeitig laufen lassen
                // (Review Runde 1 Nr. 1, verschärft in Runde 2).
                lock (_lock) _running = false;
                throw;
            }
        }

        private async Task RunJobAsync(TransferJob job)
        {
            if(job.Transaction!=null){await RunTransactionAsync(job).ConfigureAwait(false);return;}
            var cts = job.Cancellation;
            var index = new Dictionary<string, Dictionary<string, FtpEntry>>(StringComparer.Ordinal);
            var rate = new RateMeter(_clock);
            Raise(job);

            try
            {
                for (int i = job.CurrentIndex; i < job.Steps.Count; i++)
                {
                    job.CurrentIndex = i;
                    var step = job.Steps[i];
                    if (step.State != StepState.Pending) continue;

                    try
                    {
                        await FtpGates.RunAsync(_gates.Transfer, async () =>
                        {
                            await ExecuteAsync(job, step, index, rate, cts.Token).ConfigureAwait(false);
                            return 0;
                        }, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
                    catch (FtpException ex) when (ex.IsTransient) { throw; }
                    catch (FtpException ex) when (step.MissingIsSuccess &&
                                                  ex.Kind == FtpErrorKind.Permanent &&
                                                  ex.StatusCode == 550)
                    {
                        step.State = StepState.Skipped;
                    }
                    catch (Exception ex)
                    {
                        step.State = StepState.Failed;
                        step.Error = ex.Message;
                    }
                    Raise(job);
                }

                job.CurrentIndex = job.Steps.Count;
                job.State = job.Steps.Any(s => s.State == StepState.Failed) ? JobState.CompletedWithErrors : JobState.Completed;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                if (job.CurrentIndex < job.Steps.Count)
                    await DiscardPartialAsync(job.Steps[job.CurrentIndex]).ConfigureAwait(false);
                job.State = JobState.Cancelled;
            }
            catch (FtpException ex) when (ex.IsTransient)
            {
                job.InterruptReason = ex.Message;
                job.State = JobState.Interrupted;
            }
            finally
            {
                // Kein Dispose hier: der Token trägt weder Timer noch
                // WaitHandle, ein Leck entsteht also nicht — aber Dispose
                // könnte mit einem Cancel() aus CancelAsync überlappen, das
                // extra außerhalb von dessen Lock läuft (Review Runde 1 Nr. 5,
                // verschärft in Runde 2).
                lock (_lock) job.Cancellation = null;
                job.BytesPerSecond = 0;
            }
            Raise(job);
            job.CompleteAttempt();
        }

        private async Task RunTransactionAsync(TransferJob job)
        {
            Raise(job);
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(job.Cancellation.Token,job.SessionToken))
            {
                try
                {
                    await FtpGates.RunAsync(_gates.Transfer,async ()=>{
                        var context=new TransferTransactionContext(_ftp,(phase,done,total)=>{
                            job.Phase=phase;job.Steps[0].Size=total;job.Steps[0].Transferred=done;Raise(job);
                        });
                        await job.Transaction.ExecuteAsync(context,linked.Token).ConfigureAwait(false);return 0;
                    },linked.Token).ConfigureAwait(false);
                    job.Steps[0].State=StepState.Done;job.State=JobState.Completed;
                }
                catch(Exception ex)
                {
                    job.InterruptReason=ex is OperationCanceledException?"Stopped. Inspect Mod Library recovery before continuing.":ex.Message;
                    job.Phase=job.InterruptReason;job.State=JobState.Interrupted;
                }
                finally{lock(_lock)job.Cancellation=null;job.BytesPerSecond=0;}
            }
            Raise(job);job.CompleteAttempt();
        }

        private async Task ExecuteAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, RateMeter rate, CancellationToken ct)
        {
            switch (step.Kind)
            {
                case StepKind.CreateRemoteFolder:
                {
                    var parent = await IndexAsync(RemotePath.Parent(step.RemotePath), index, ct).ConfigureAwait(false);
                    var name = RemotePath.Name(step.RemotePath);
                    if (!parent.ContainsKey(name))
                    {
                        await _ftp.MakeDirectoryAsync(step.RemotePath, ct).ConfigureAwait(false);
                        parent[name] = new FtpEntry(name, FtpEntryKind.Directory, 0, DateTime.MinValue, null);
                        index[RemotePath.Normalize(step.RemotePath)] = new Dictionary<string, FtpEntry>(RemotePath.NameComparer(step.RemotePath));
                    }
                    step.State = StepState.Done;
                    break;
                }
                case StepKind.CreateLocalFolder:
                    Directory.CreateDirectory(step.LocalPath);
                    step.State = StepState.Done;
                    break;
                case StepKind.UploadFile:
                    await UploadAsync(job, step, index, rate, ct).ConfigureAwait(false);
                    break;
                case StepKind.DownloadFile:
                    await DownloadAsync(job, step, index, rate, ct).ConfigureAwait(false);
                    break;
                case StepKind.DeleteRemoteFile:
                    if (step.TargetIdentityRequired)
                        await EnsureRemoteTargetIdentityAsync(step, ct).ConfigureAwait(false);
                    await _ftp.DeleteAsync(step.RemotePath, ct).ConfigureAwait(false);
                    step.State = StepState.Done;
                    break;
                case StepKind.RemoveRemoteFolder:
                    await _ftp.RemoveDirectoryAsync(step.RemotePath, ct).ConfigureAwait(false);
                    step.State = StepState.Done;
                    break;
                case StepKind.RenameRemote:
                    await RenameAsync(job, step, index, ct).ConfigureAwait(false);
                    break;
            }
        }

        private async Task UploadAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, RateMeter rate, CancellationToken ct)
        {
            var parentPath = RemotePath.Parent(step.RemotePath);
            var parent = await IndexAsync(parentPath, index, ct).ConfigureAwait(false);
            var name = RemotePath.Name(step.RemotePath);

            if (!step.Started) step.TargetReplacementApproved = step.OverwriteWithoutAsking;

            FtpEntry existing;
            if (step.TargetIdentityRequired)
                await EnsureRemoteTargetIdentityAsync(step, ct).ConfigureAwait(false);
            if (!step.Started && !step.OverwriteWithoutAsking && parent.TryGetValue(name, out existing))
            {
                var keepBoth = UniqueName.For(name, false, parent.ContainsKey);
                var choice = await DecideAsync(job,
                    new ConflictInfo(step.RemotePath, true, existing.Size, step.Size, keepBoth), ct).ConfigureAwait(false);
                if (choice == ConflictChoice.Skip)
                {
                    step.State = StepState.Skipped;
                    return;
                }
                if (choice == ConflictChoice.KeepBoth)
                {
                    step.RemotePath = RemotePath.Combine(parentPath, keepBoth);
                    name = keepBoth;
                    step.ExpectedTargetExists = false;
                    step.ExpectedTargetHash = null;
                }
                else
                {
                    step.TargetReplacementApproved = true;
                }
            }

            var source = new FileInfo(step.LocalPath);
            if (!source.Exists)
                throw new FileNotFoundException("The local source file was not found.", step.LocalPath);
            if (source.Length != step.Size)
                throw new InvalidOperationException("The local source file has changed since planning: " + step.LocalPath);
            var sourceHash = HashLocalFile(step.LocalPath, ct);
            if (!step.Started)
                step.LocalSourceHash = sourceHash;
            else if (step.LocalSourceHash == null || !step.LocalSourceHash.SequenceEqual(sourceHash))
                throw new InvalidOperationException("The local source file has changed since the interruption: " + step.LocalPath);

            // Erst nach <name>.part, dann per RNTO über das Ziel (Abschluss-Review
            // Plan 1): GoldHEN überschreibt beim Umbenennen kommentarlos (Befund
            // Task 2 Nr. 13) — ein Abbruch hinterlässt so nie eine halbe Datei
            // unter dem echten Namen, und Fortsetzen hängt an die eigene .part an.
            if (!step.Started)
                step.RemotePartPath = RemotePath.Combine(parentPath, ".ps4fm-" + Guid.NewGuid().ToString("N") + PartSuffix);
            var part = step.RemotePartPath;
            if (!step.Started)
            {
                // Der zufällige Name gehört nur diesem Auftrag. Die frische LIST
                // sichert zusätzlich gegen die (extrem unwahrscheinliche) Kollision.
                var entries = await _ftp.ListAsync(parentPath, ct).ConfigureAwait(false);
                var comparer = RemotePath.NameComparer(parentPath);
                if (entries.Any(e => comparer.Equals(e.Name, RemotePath.Name(part))))
                    throw new InvalidOperationException("The temporary file “" + part + "” already exists. The upload was not started.");
            }
            long offset = 0;
            if (step.Started)
            {
                long remoteSize = 0;
                bool exists = true;
                try { remoteSize = await _ftp.GetFileSizeAsync(part, ct).ConfigureAwait(false); }
                catch (FtpException ex) when (!ex.IsTransient) { exists = false; }
                if (exists && remoteSize > step.Size)
                    throw new InvalidOperationException("The temporary file is larger than the source: " + part);
                if (exists && !RemotePrefixMatchesLocal(part, step.LocalPath, remoteSize, true, ct))
                    throw new InvalidOperationException("The source or temporary file has changed since the interruption: " + part);
                if (exists && AppendResumesUploads) offset = remoteSize;
            }

            step.Started = true;
            step.Transferred = offset;
            var progress = new StepProgress(this, job, step, rate, offset);
            if (offset == 0)
                await _ftp.UploadFromFileAsync(step.LocalPath, part, progress, ct).ConfigureAwait(false);
            else if (offset < step.Size)
                await _ftp.AppendFromFileAsync(step.LocalPath, offset, part, progress, ct).ConfigureAwait(false);
            // offset == Size: die .part ist schon vollständig — der Abbruch kam erst beim Umbenennen.

            if (!step.LocalSourceHash.SequenceEqual(HashLocalFile(step.LocalPath, ct)))
                throw new InvalidOperationException("The local source file changed during the upload: " + step.LocalPath);

            if (step.TargetIdentityRequired)
                await EnsureRemoteTargetIdentityAsync(step, ct).ConfigureAwait(false);
            else if (!step.TargetReplacementApproved)
            {
                var current = await _ftp.ListAsync(parentPath, ct).ConfigureAwait(false);
                var comparer = RemotePath.NameComparer(parentPath);
                if (current.Any(e => comparer.Equals(e.Name, name)))
                    throw new InvalidOperationException("An item named “" + name + "” appeared in the target during the upload. The completed temporary file was preserved.");
            }
            await _ftp.RenameAsync(part, step.RemotePath, ct).ConfigureAwait(false);

            parent[name] = new FtpEntry(name, FtpEntryKind.File, step.Size, DateTime.MinValue, null);
            step.Transferred = step.Size;
            step.State = StepState.Done;
        }

        private async Task EnsureRemoteTargetIdentityAsync(TransferStep step, CancellationToken ct)
        {
            var parentPath = RemotePath.Parent(step.RemotePath);
            var name = RemotePath.Name(step.RemotePath);
            var entries = await _ftp.ListAsync(parentPath, ct).ConfigureAwait(false);
            var comparer = RemotePath.NameComparer(parentPath);
            var target = entries.FirstOrDefault(e => comparer.Equals(e.Name, name));
            var exists = target != null && !target.IsDirectory;
            if (target != null && target.IsDirectory || step.ExpectedTargetExists != exists)
                throw new InvalidOperationException("The target changed after its safety snapshot was taken. The mutation was not applied.");
            if (exists && (step.ExpectedTargetHash == null ||
                !step.ExpectedTargetHash.SequenceEqual(HashRemoteFile(step.RemotePath, ct))))
                throw new InvalidOperationException("The target changed after its safety snapshot was taken. The mutation was not applied.");
        }

        private async Task DownloadAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, RateMeter rate, CancellationToken ct)
        {
            if (!step.Started) step.TargetReplacementApproved = step.OverwriteWithoutAsking;
            if (!step.Started && !step.OverwriteWithoutAsking && File.Exists(step.LocalPath))
            {
                var dir = Path.GetDirectoryName(step.LocalPath);
                var keepBoth = UniqueName.For(Path.GetFileName(step.LocalPath), false, n => File.Exists(Path.Combine(dir, n)));
                var choice = await DecideAsync(job,
                    new ConflictInfo(step.LocalPath, false, new FileInfo(step.LocalPath).Length, step.Size, keepBoth), ct).ConfigureAwait(false);
                if (choice == ConflictChoice.Skip)
                {
                    step.State = StepState.Skipped;
                    return;
                }
                if (choice == ConflictChoice.KeepBoth) step.LocalPath = Path.Combine(dir, keepBoth);
                else step.TargetReplacementApproved = true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(step.LocalPath));
            if (!step.Started)
                step.LocalPartPath = Path.Combine(Path.GetDirectoryName(step.LocalPath),
                    ".ps4fm-" + Guid.NewGuid().ToString("N") + PartSuffix);
            var part = step.LocalPartPath;
            var parentPath = RemotePath.Parent(step.RemotePath);
            var name = RemotePath.Name(step.RemotePath);
            var parent = await IndexAsync(parentPath, index, ct).ConfigureAwait(false);
            FtpEntry sourceEntry;
            if (!parent.TryGetValue(name, out sourceEntry) || sourceEntry.IsDirectory)
                throw new FileNotFoundException("The source file on the PS4 was not found.", step.RemotePath);
            if (!step.Started)
            {
                step.RemoteSourceModified = sourceEntry.Modified;
                step.RemoteSourceHash = HashRemoteFile(step.RemotePath, ct);
            }
            else if (!step.RemoteSourceModified.HasValue || step.RemoteSourceModified.Value != sourceEntry.Modified ||
                step.RemoteSourceHash == null || !step.RemoteSourceHash.SequenceEqual(HashRemoteFile(step.RemotePath, ct)))
                throw new InvalidOperationException("The source file on the PS4 has changed since the interruption: " + step.RemotePath);
            var remoteSize = await _ftp.GetFileSizeAsync(step.RemotePath, ct).ConfigureAwait(false);
            if (remoteSize != step.Size)
                throw new InvalidOperationException("The source file on the PS4 has changed since planning: " + step.RemotePath);
            long offset = 0;
            if (step.Started && File.Exists(part)) offset = new FileInfo(part).Length;
            if (offset > step.Size)
                throw new InvalidOperationException("The local temporary file is larger than the source: " + part);
            if (offset > 0 && !RemotePrefixMatchesLocal(step.RemotePath, part, offset, false, ct))
                throw new InvalidOperationException("The source or local temporary file has changed since the interruption: " + part);

            step.Started = true;
            step.Transferred = offset;
            if (offset < step.Size)
                await _ftp.DownloadToFileAsync(step.RemotePath, part, offset,
                    new StepProgress(this, job, step, rate, offset), true, ct)
                    .ConfigureAwait(false);
            else if (step.Size == 0 && !File.Exists(part))
                File.WriteAllBytes(part, new byte[0]);

            var current = await _ftp.ListAsync(parentPath, ct).ConfigureAwait(false);
            var comparer = RemotePath.NameComparer(parentPath);
            var currentSource = current.FirstOrDefault(e => comparer.Equals(e.Name, name));
            if (currentSource == null || currentSource.IsDirectory || currentSource.Size != step.Size ||
                currentSource.Modified != step.RemoteSourceModified.Value ||
                !step.RemoteSourceHash.SequenceEqual(HashLocalFile(part, ct)))
                throw new InvalidOperationException("The source file on the PS4 changed during the download: " + step.RemotePath);

            if (step.TargetReplacementApproved && File.Exists(step.LocalPath))
                File.Replace(part, step.LocalPath, null);
            else
                File.Move(part, step.LocalPath);
            step.Transferred = step.Size;
            step.State = StepState.Done;
        }

        private async Task RenameAsync(TransferJob job, TransferStep step,
            Dictionary<string, Dictionary<string, FtpEntry>> index, CancellationToken ct)
        {
            var parentPath = RemotePath.Parent(step.TargetRemotePath);
            var parent = await IndexAsync(parentPath, index, ct).ConfigureAwait(false);
            var name = RemotePath.Name(step.TargetRemotePath);
            bool replacementApproved = false;

            FtpEntry existing;
            if (parent.TryGetValue(name, out existing) && !IsSameEntry(step, parentPath, existing))
            {
                if (step.IsDirectory || existing.IsDirectory)
                    throw new InvalidOperationException("An item named “" + name + "” already exists in the target.");

                var keepBoth = UniqueName.For(name, false, parent.ContainsKey);
                var choice = await DecideAsync(job,
                    new ConflictInfo(step.TargetRemotePath, true, existing.Size, step.Size, keepBoth), ct).ConfigureAwait(false);
                if (choice == ConflictChoice.Skip)
                {
                    step.State = StepState.Skipped;
                    return;
                }
                if (choice == ConflictChoice.KeepBoth)
                {
                    step.TargetRemotePath = RemotePath.Combine(parentPath, keepBoth);
                    name = keepBoth;
                }
                else
                {
                    // Ersetzen ohne DELE vorab: RNTO überschreibt an der Konsole
                    // kommentarlos (Befund Task 2 Nr. 13, Abschluss-Review Plan 1).
                    parent.Remove(name);
                    replacementApproved = true;
                }
            }

            if (!replacementApproved)
            {
                var current = await _ftp.ListAsync(parentPath, ct).ConfigureAwait(false);
                var comparer = RemotePath.NameComparer(parentPath);
                var appeared = current.FirstOrDefault(e => comparer.Equals(e.Name, name));
                if (appeared != null && !IsSameEntry(step, parentPath, appeared))
                    throw new InvalidOperationException("An item named “" + name + "” now exists in the target.");
            }
            await _ftp.RenameAsync(step.RemotePath, step.TargetRemotePath, ct).ConfigureAwait(false);
            // Erst die Quelle austragen, dann das Ziel eintragen: bei einer reinen
            // Schreibweisen-Umbenennung auf USB sind beide derselbe Schlüssel.
            Dictionary<string, FtpEntry> source;
            if (index.TryGetValue(RemotePath.Parent(step.RemotePath), out source)) source.Remove(RemotePath.Name(step.RemotePath));
            parent[name] = new FtpEntry(name, step.IsDirectory ? FtpEntryKind.Directory : FtpEntryKind.File, step.Size, DateTime.MinValue, null);
            step.State = StepState.Done;
        }

        /// <summary>
        /// Auf FAT/exFAT (USB) findet der Index bei „a.txt" → „A.TXT" den
        /// Eintrag selbst — das ist kein Konflikt, sondern die Umbenennung.
        /// </summary>
        private static bool IsSameEntry(TransferStep step, string targetParent, FtpEntry existing)
        {
            return RemotePath.Parent(step.RemotePath) == targetParent
                && string.Equals(existing.Name, RemotePath.Name(step.RemotePath), StringComparison.Ordinal);
        }

        /// <summary>Namen eines Zielordners, einmal pro Auftrag gelistet. Fehlt der Ordner (noch), ist er leer.</summary>
        private async Task<Dictionary<string, FtpEntry>> IndexAsync(string dir,
            Dictionary<string, Dictionary<string, FtpEntry>> index, CancellationToken ct)
        {
            var key = RemotePath.Normalize(dir);
            Dictionary<string, FtpEntry> entries;
            if (index.TryGetValue(key, out entries)) return entries;

            entries = new Dictionary<string, FtpEntry>(RemotePath.NameComparer(key));
            try
            {
                foreach (var entry in await _ftp.ListAsync(key, ct).ConfigureAwait(false)) entries[entry.Name] = entry;
            }
            catch (FtpException ex) when (!ex.IsTransient)
            {
                // Ordner existiert nicht — dann gibt es auch keine Konflikte.
            }
            index[key] = entries;
            return entries;
        }

        private async Task<ConflictChoice> DecideAsync(TransferJob job, ConflictInfo info, CancellationToken ct)
        {
            if (job.RememberedChoice.HasValue) return job.RememberedChoice.Value;
            var decision = await _resolver.ResolveAsync(info, ct).ConfigureAwait(false);
            if (decision.ApplyToAll) job.RememberedChoice = decision.Choice;
            return decision.Choice;
        }

        /// <summary>Verwirft eine angefangene Datei — best effort, ein Fehler hier ändert am Abbruch nichts.</summary>
        private async Task DiscardPartialAsync(TransferStep step)
        {
            if (!step.Started) return;
            try
            {
                // Beim Upload nur löschen, was wir wirklich geschickt haben —
                // sonst reißt ein Abbruch ohne übertragene Bytes eine fremde
                // Datei gleichen .part-Namens weg (Review Runde 1 Nr. 3). Beim
                // Download entsteht die lokale .part-Datei schon mit dem
                // Aufruf, ist also immer unsere.
                if (step.Kind == StepKind.UploadFile && step.Transferred > 0 && !string.IsNullOrEmpty(step.RemotePartPath))
                {
                    var part = step.RemotePartPath;
                    long size = -1;
                    try { size = await _ftp.GetFileSizeAsync(part, CancellationToken.None).ConfigureAwait(false); }
                    catch (FtpException ex) when (!ex.IsTransient) { }
                    if (size >= 0 && size <= step.Size &&
                        RemotePrefixMatchesLocal(part, step.LocalPath, size, true, CancellationToken.None))
                        await _ftp.DeleteAsync(part, CancellationToken.None).ConfigureAwait(false);
                }
                else if (step.Kind == StepKind.DownloadFile && !string.IsNullOrEmpty(step.LocalPartPath) &&
                    File.Exists(step.LocalPartPath))
                {
                    var size = new FileInfo(step.LocalPartPath).Length;
                    if (size <= step.Size && RemotePrefixMatchesLocal(step.RemotePath, step.LocalPartPath,
                        size, false, CancellationToken.None))
                        File.Delete(step.LocalPartPath);
                }
            }
            catch (Exception)
            {
                // Rest bleibt liegen — auf der PS4 sichtbar, lokal als .part.
            }
            step.Started = false;
            step.Transferred = 0;
        }

        /// <summary>
        /// Vergleicht die bereits übertragenen Bytes mit der aktuellen Quelle.
        /// Der Serverwert bleibt der Resume-Versatz; der Client-Fortschritt ist
        /// wegen Pufferung ausdrücklich kein Eigentums- oder Längennachweis.
        /// </summary>
        private bool RemotePrefixMatchesLocal(string remotePath, string localPath, long length,
            bool remoteMustEnd, CancellationToken ct)
        {
            if (length == 0) return true;
            using (var remote = _ftp.OpenRead(remotePath, 0))
            using (var local = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var remoteBuffer = new byte[64 * 1024];
                var localBuffer = new byte[remoteBuffer.Length];
                long remaining = length;
                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int wanted = (int)Math.Min(remoteBuffer.Length, remaining);
                    int remoteRead = ReadAtMost(remote, remoteBuffer, wanted);
                    int localRead = ReadAtMost(local, localBuffer, wanted);
                    if (remoteRead != wanted || localRead != wanted) return false;
                    for (int i = 0; i < wanted; i++)
                        if (remoteBuffer[i] != localBuffer[i]) return false;
                    remaining -= wanted;
                }
                return !remoteMustEnd || remote.ReadByte() == -1;
            }
        }

        private static int ReadAtMost(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read == 0) break;
                total += read;
            }
            return total;
        }

        private static byte[] HashLocalFile(string path, CancellationToken ct)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
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

        private byte[] HashRemoteFile(string path, CancellationToken ct)
        {
            using (var stream = _ftp.OpenRead(path, 0)) return HashStream(stream, ct);
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

        private void Raise(TransferJob job)
        {
            var handler = JobChanged;
            if (handler == null) return;
            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<TransferJob>)subscriber)(this, job);
                }
                catch (Exception)
                {
                    // Eine Benachrichtigung (typischerweise BeginInvoke Richtung
                    // UI, z. B. auf ein sich schließendes Formular) darf die
                    // Warteschlange nie zum Stehen bringen (Review Runde 1 Nr. 1).
                }
            }
        }

        /// <summary>Direkter IProgress (kein Progress&lt;T&gt;): läuft synchron im Transfer, meldet höchstens alle 200 ms.</summary>
        private sealed class StepProgress : IProgress<long>
        {
            private readonly TransferQueue _queue;
            private readonly TransferJob _job;
            private readonly TransferStep _step;
            private readonly RateMeter _rate;
            private readonly long _baseline;
            private DateTime _lastRaise = DateTime.MinValue;

            public StepProgress(TransferQueue queue, TransferJob job, TransferStep step, RateMeter rate, long baseline)
            {
                _queue = queue;
                _job = job;
                _step = step;
                _rate = rate;
                _baseline = baseline;
            }

            public void Report(long value)
            {
                _step.Transferred = _baseline + value;
                _rate.Sample(_job.DoneBytes);
                _job.BytesPerSecond = _rate.BytesPerSecond;

                var now = _queue._clock.UtcNow;
                if (now - _lastRaise < ProgressInterval) return;
                _lastRaise = now;
                _queue.Raise(_job);
            }
        }
    }
}
