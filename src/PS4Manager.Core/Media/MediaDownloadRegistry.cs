using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Media
{
    /// <summary>
    /// Deduplicates in-flight video downloads. Completion removes an entry
    /// only when the key still belongs to that exact task, so a late previous
    /// session cannot remove a replacement session's download.
    /// </summary>
    public sealed class MediaDownloadRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Task<string>> _downloads =
            new Dictionary<string, Task<string>>(StringComparer.Ordinal);

        public Task<string> GetOrAdd(string key, Func<Task<string>> factory)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            Task<string> existing;
            Task<string> created;
            lock (_sync)
            {
                if (_downloads.TryGetValue(key, out existing)) return existing;
                created = factory();
                if (created == null) throw new InvalidOperationException("The download factory returned null.");
                _downloads[key] = created;
            }

            created.ContinueWith(
                completed => RemoveIfOwned(key, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return created;
        }

        public void Reset()
        {
            lock (_sync) _downloads.Clear();
        }

        private void RemoveIfOwned(string key, Task<string> completed)
        {
            lock (_sync)
            {
                Task<string> current;
                if (_downloads.TryGetValue(key, out current) && ReferenceEquals(current, completed))
                    _downloads.Remove(key);
            }
        }
    }
}
