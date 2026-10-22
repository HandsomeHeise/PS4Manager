using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace FileManager.Core.Browsing
{
    /// <summary>
    /// Listings pro Verbindung, geteilt von Baum und Liste. Gleichzeitige
    /// Anfragen nach demselben Ordner teilen sich EIN LIST; ein Wartender, der
    /// abbricht, bricht nur sein Warten ab, nicht das gemeinsame LIST (das ist
    /// durch die 8-s-Frist des FtpClient begrenzt). Fehlschläge werden nicht
    /// gemerkt. FTP meldet fremde Änderungen nicht — invalidiert wird nach
    /// eigenen Operationen und per F5 (Spec „Verzeichnis-Cache").
    /// </summary>
    public sealed class DirectoryCache
    {
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly object _lock = new object();
        private readonly Dictionary<string, Task<IReadOnlyList<FtpEntry>>> _entries =
            new Dictionary<string, Task<IReadOnlyList<FtpEntry>>>(StringComparer.Ordinal);

        public DirectoryCache(IFtpClient ftp, FtpGates gates)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _ftp = ftp;
            _gates = gates;
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            var key = RemotePath.Normalize(path);
            Task<IReadOnlyList<FtpEntry>> task;
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out task))
                {
                    task = LoadAsync(key);
                    _entries[key] = task;
                    // ExecuteSynchronously garantiert, dass diese Räumung läuft, BEVOR ein
                    // wartender Aufrufer aus seinem await zurückkehrt — nicht nur bei einem
                    // synchron scheiternden LIST (Tests ohne echtes Netz), sondern genauso
                    // bei einem echten LIST, das erst NACH einem await fehlschlägt. Ohne
                    // ExecuteSynchronously kann die Räumung als eigenes Arbeitselement erst
                    // NACH dem Aufrufer laufen: der ruft dann sofort erneut ListAsync auf,
                    // sieht die gescheiterte Task noch im Cache und bekommt denselben Fehler
                    // zurück, statt ein frisches LIST auszulösen. Tragend im Produktivbetrieb,
                    // kein reines Testdetail (DirectoryCacheTests
                    // .A_listing_that_fails_after_an_await_is_not_served_again deckt das ab).
                    // Läuft ohnehin nur eine schnelle Entfernung unter demselben (reentranten) _lock.
                    task.ContinueWith(t => Forget(key, t), CancellationToken.None,
                        TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            return WaitAsync(task, ct);
        }

        public bool TryGetCached(string path, out IReadOnlyList<FtpEntry> entries)
        {
            Task<IReadOnlyList<FtpEntry>> task;
            lock (_lock)
            {
                if (_entries.TryGetValue(RemotePath.Normalize(path), out task) && task.Status == TaskStatus.RanToCompletion)
                {
                    entries = task.Result;
                    return true;
                }
            }
            entries = null;
            return false;
        }

        public void Invalidate(string path)
        {
            lock (_lock) _entries.Remove(RemotePath.Normalize(path));
        }

        public void InvalidateBelow(string path)
        {
            lock (_lock)
            {
                foreach (var key in _entries.Keys.Where(k => RemotePath.IsSameOrBelow(k, path)).ToList())
                    _entries.Remove(key);
            }
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        private Task<IReadOnlyList<FtpEntry>> LoadAsync(string key)
        {
            return FtpGates.RunAsync(_gates.Browse, () => _ftp.ListAsync(key, CancellationToken.None), CancellationToken.None);
        }

        private void Forget(string key, Task<IReadOnlyList<FtpEntry>> failed)
        {
            lock (_lock)
            {
                Task<IReadOnlyList<FtpEntry>> current;
                if (_entries.TryGetValue(key, out current) && current == failed) _entries.Remove(key);
            }
        }

        private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct)
        {
            if (task.IsCompleted || !ct.CanBeCanceled) return await task.ConfigureAwait(false);

            var cancelled = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
                    throw new OperationCanceledException(ct);
            }
            return await task.ConfigureAwait(false);
        }
    }
}
