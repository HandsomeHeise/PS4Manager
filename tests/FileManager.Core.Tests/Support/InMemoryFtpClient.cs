using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace FileManager.Core.Tests.Support
{
    /// <summary>
    /// Dateisystem der Konsole im Speicher, mit Protokoll jedes Aufrufs
    /// („VERB /pfad"). Verhält sich wie GoldHEN, soweit erhoben: fehlende
    /// Pfade → permanente 550, RMD nur auf leere Ordner, RNTO überschreibt
    /// eine existierende Zieldatei kommentarlos (Befund Task 2 Nr. 13; mit
    /// RenameOverwrites = false stattdessen 550).
    /// </summary>
    internal sealed class InMemoryFtpClient : IFtpClient
    {
        private static readonly string[] WriteVerbs = { "STOR", "APPE", "MKD", "RMD", "RNFR", "DELE" };
        private const int Chunk = 64 * 1024;

        private readonly object _lock = new object();
        private readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _modified = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _links = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _calls = new List<string>();

        /// <summary>(Verb, Pfad) → Ausnahme, die statt der Ausführung fliegt; null = normal.</summary>
        public Func<string, string, Exception> Failure;

        /// <summary>Transfers brechen nach so vielen Bytes (je Aufruf) transient ab; 0 = nie.</summary>
        public long InterruptAfterBytes;

        /// <summary>Solange gesetzt und nicht erfüllt, hängt jedes LIST — für Nebenläufigkeitstests.</summary>
        public TaskCompletionSource<bool> ListGate;

        /// <summary>Testhaken nach dem Erstellen eines LIST-Snapshots, bevor der Aufrufer ihn erhält.</summary>
        public Action<string> AfterList;

        /// <summary>Wie GoldHEN (Befund Task 2 Nr. 13): Standard true.</summary>
        public bool RenameOverwrites = true;

        public DateTime Today = new DateTime(2026, 9, 11);

        public InMemoryFtpClient AddDir(string path)
        {
            lock (_lock) AddDirUnlocked(Norm(path));
            return this;
        }

        public InMemoryFtpClient AddFile(string path, byte[] data)
        {
            lock (_lock)
            {
                var p = Norm(path);
                AddDirUnlocked(Parent(p));
                _files[p] = data;
                _modified[p] = Today;
            }
            return this;
        }

        public InMemoryFtpClient AddFile(string path, string text)
        {
            return AddFile(path, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Eine Verknüpfung (LIST zeigt „l…  name -> ziel"). Der Fake folgt ihr nicht.</summary>
        public InMemoryFtpClient AddLink(string path, string target)
        {
            lock (_lock)
            {
                var p = Norm(path);
                AddDirUnlocked(Parent(p));
                _links[p] = target;
            }
            return this;
        }

        public byte[] File(string path)
        {
            lock (_lock)
            {
                byte[] data;
                return _files.TryGetValue(Norm(path), out data) ? data : null;
            }
        }

        public bool HasDir(string path)
        {
            lock (_lock) return _dirs.Contains(Norm(path));
        }

        public DateTime Modified(string path)
        {
            lock (_lock) return _modified[Norm(path)];
        }

        /// <summary>Simuliert eine Änderung auf der Konsole durch jemand anderen.</summary>
        public void ChangeRemotely(string path, byte[] data)
        {
            lock (_lock)
            {
                var p = Norm(path);
                _files[p] = data;
                _modified[p] = _modified[p].AddDays(1);
            }
        }

        public string[] CallsSnapshot()
        {
            lock (_lock) return _calls.ToArray();
        }

        public int WriteCallCount
        {
            get { lock (_lock) return _calls.Count(c => WriteVerbs.Contains(c.Substring(0, c.IndexOf(' ')))); }
        }

        private void Record(string verb, string path)
        {
            lock (_lock) _calls.Add(verb + " " + path);
            var failure = Failure;
            var ex = failure != null ? failure(verb, Norm(path)) : null;
            if (ex != null) throw ex;
        }

        private static FtpException NotFound()
        {
            return new FtpException("Nicht vorhanden oder kein Zugriff", FtpErrorKind.Permanent, 550);
        }

        public async Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var p = Norm(path);
            Record("LIST", p);
            var gate = ListGate;
            if (gate != null) await gate.Task.ConfigureAwait(false);
            IReadOnlyList<FtpEntry> result;
            lock (_lock)
            {
                if (!_dirs.Contains(p)) throw NotFound();
                var entries = new List<FtpEntry>();
                foreach (var d in _dirs.Where(d => d != "/" && Parent(d) == p).OrderBy(d => d, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(d), FtpEntryKind.Directory, 512, Today, null));
                foreach (var f in _files.Keys.Where(f => Parent(f) == p).OrderBy(f => f, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(f), FtpEntryKind.File, _files[f].Length, _modified[f], null));
                foreach (var l in _links.Keys.Where(l => Parent(l) == p).OrderBy(l => l, StringComparer.Ordinal))
                    entries.Add(new FtpEntry(Name(l), FtpEntryKind.Link, _links[l].Length, Today, _links[l]));
                result = entries;
            }
            var after = AfterList;
            if (after != null) after(p);
            return result;
        }

        public void ChangeRemotelyWithoutMetadata(string path, byte[] data)
        {
            var p = Norm(path);
            lock (_lock) _files[p] = data.ToArray();
        }

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Record("RETR", Norm(path));
            var data = File(path);
            if (data == null) throw NotFound();
            return Task.FromResult(data);
        }

        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)
        {
            return DownloadToFileAsync(path, localPath, 0, progress, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Record("RETR", Norm(path) + "@" + offset);
            var data = File(path);
            if (data == null) throw NotFound();
            if (offset > 0 && (!System.IO.File.Exists(localPath) || new FileInfo(localPath).Length != offset))
                throw new InvalidOperationException("Teildatei passt nicht zum Versatz.");

            using (var target = new FileStream(localPath, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                CopyChunked(data, offset, target, progress, ct);
            }
            return Task.FromResult(0);
        }

        public Stream OpenRead(string path, long offset)
        {
            Record("RETR", Norm(path) + "@" + offset);
            var data = File(path);
            if (data == null) throw NotFound();
            return new MemoryStream(data, (int)offset, data.Length - (int)offset, false);
        }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            return StoreAsync("STOR", localPath, 0, remotePath, progress, ct);
        }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            return StoreAsync("APPE", localPath, localOffset, remotePath, progress, ct);
        }

        private Task StoreAsync(string verb, string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var local = System.IO.File.ReadAllBytes(localPath);   // lokaler Fehler bleibt lokal, wie im echten Client
            var p = Norm(remotePath);
            Record(verb, verb == "APPE" ? p + "@" + localOffset : p);
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(p)) || _dirs.Contains(p)) throw NotFound();
            }

            using (var received = new MemoryStream())
            {
                // Wie ein echter Server: was bis zum Abbruch ankam, liegt auf der Konsole.
                Exception interrupted = null;
                try { CopyChunked(local, localOffset, received, progress, ct); }
                catch (Exception ex) when (ex is FtpException || ex is OperationCanceledException) { interrupted = ex; }

                lock (_lock)
                {
                    byte[] existing;
                    var bytes = received.ToArray();
                    _files[p] = verb == "APPE" && _files.TryGetValue(p, out existing) ? existing.Concat(bytes).ToArray() : bytes;
                    _modified[p] = Today;
                }
                if (interrupted != null) throw interrupted;
            }
            return Task.FromResult(0);
        }

        private void CopyChunked(byte[] data, long offset, Stream target, IProgress<long> progress, CancellationToken ct)
        {
            long done = 0;
            for (long pos = offset; pos < data.Length; pos += Chunk)
            {
                ct.ThrowIfCancellationRequested();
                int count = (int)Math.Min(Chunk, data.Length - pos);
                if (InterruptAfterBytes > 0 && done + count > InterruptAfterBytes)
                {
                    int partial = (int)(InterruptAfterBytes - done);
                    target.Write(data, (int)pos, partial);
                    if (progress != null) progress.Report(done + partial);
                    throw new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 426);
                }
                target.Write(data, (int)pos, count);
                done += count;
                if (progress != null) progress.Report(done);
            }
        }

        public Task<long> GetFileSizeAsync(string path, CancellationToken ct)
        {
            Record("SIZE", Norm(path));
            var data = File(path);
            if (data == null) throw NotFound();
            return Task.FromResult((long)data.Length);
        }

        public Task MakeDirectoryAsync(string path, CancellationToken ct)
        {
            var p = Norm(path);
            Record("MKD", p);
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(p)) || _dirs.Contains(p) || _files.ContainsKey(p)) throw NotFound();
                _dirs.Add(p);
            }
            return Task.FromResult(0);
        }

        public Task RemoveDirectoryAsync(string path, CancellationToken ct)
        {
            var p = Norm(path);
            Record("RMD", p);
            lock (_lock)
            {
                bool empty = !_dirs.Any(d => d != p && Parent(d) == p) && !_files.Keys.Any(f => Parent(f) == p);
                if (p == "/" || !_dirs.Contains(p) || !empty) throw NotFound();
                _dirs.Remove(p);
            }
            return Task.FromResult(0);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
        {
            var from = Norm(fromPath);
            var to = Norm(toPath);
            Record("RNFR", from + " -> " + to);
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(to))) throw NotFound();
                bool targetExists = _files.ContainsKey(to) || _dirs.Contains(to);
                if (targetExists && !(RenameOverwrites && _files.ContainsKey(to) && _files.ContainsKey(from))) throw NotFound();

                if (_files.ContainsKey(from))
                {
                    _files[to] = _files[from];
                    _modified[to] = _modified[from];
                    _files.Remove(from);
                    _modified.Remove(from);
                }
                else if (_dirs.Contains(from) && from != "/")
                {
                    foreach (var d in _dirs.Where(d => d == from || d.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                    {
                        _dirs.Remove(d);
                        _dirs.Add(to + d.Substring(from.Length));
                    }
                    foreach (var f in _files.Keys.Where(f => f.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                    {
                        _files[to + f.Substring(from.Length)] = _files[f];
                        _modified[to + f.Substring(from.Length)] = _modified[f];
                        _files.Remove(f);
                        _modified.Remove(f);
                    }
                }
                else throw NotFound();
            }
            return Task.FromResult(0);
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            var p = Norm(path);
            Record("DELE", p);
            lock (_lock)
            {
                if (!_files.Remove(p) && !_links.Remove(p)) throw NotFound();
                _modified.Remove(p);
            }
            return Task.FromResult(0);
        }

        private void AddDirUnlocked(string path)
        {
            if (path == "/") return;
            AddDirUnlocked(Parent(path));
            _dirs.Add(path);
        }

        private static string Norm(string path) { return FileManager.Core.Paths.RemotePath.Normalize(path); }
        private static string Parent(string path) { return FileManager.Core.Paths.RemotePath.Parent(path); }
        private static string Name(string path) { return FileManager.Core.Paths.RemotePath.Name(path); }
    }
}
