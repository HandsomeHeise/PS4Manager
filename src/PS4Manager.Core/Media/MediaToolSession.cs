using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Session;
using PS4Manager.Gallery.Core.Ps4;

namespace PS4Manager.Core.Media
{
    /// <summary>
    /// Headless session adapter for the gallery. Each attachment owns one
    /// cancellation token shared by repository and thumbnail reads, and stale
    /// repository completions are rejected by session identity and generation.
    /// </summary>
    public sealed class MediaToolSession : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Action<ManagerSession, CancellationToken> _attached;
        private readonly Action<ManagerSession> _detached;
        private readonly Action<ManagerSession, IReadOnlyList<GameFolder>> _loaded;
        private readonly Action<ManagerSession, Exception> _loadFailed;
        private readonly ToolSessionBinding _binding;

        private ManagerSession _current;
        private CancellationTokenSource _loadCancellation;
        private long _generation;
        private bool _disposed;

        public MediaToolSession(
            ManagerSessionController sessions,
            Action<ManagerSession, CancellationToken> attached,
            Action<ManagerSession> detached,
            Action<ManagerSession, IReadOnlyList<GameFolder>> loaded,
            Action<ManagerSession, Exception> loadFailed)
        {
            if (sessions == null) throw new ArgumentNullException(nameof(sessions));
            _attached = attached ?? throw new ArgumentNullException(nameof(attached));
            _detached = detached ?? throw new ArgumentNullException(nameof(detached));
            _loaded = loaded ?? throw new ArgumentNullException(nameof(loaded));
            _loadFailed = loadFailed ?? throw new ArgumentNullException(nameof(loadFailed));
            _binding = new ToolSessionBinding(sessions, Attach, Detach);
        }

        private void Attach(ManagerSession session)
        {
            StartLoad(session, true);
        }

        public void Reload()
        {
            ManagerSession session;
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(MediaToolSession));
                session = _current;
            }
            if (session != null) StartLoad(session, false);
        }

        private void StartLoad(ManagerSession session, bool isAttachment)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken);
            CancellationTokenSource previous;
            long generation;
            lock (_sync)
            {
                if (_disposed || (!isAttachment && !ReferenceEquals(_current, session)))
                {
                    cancellation.Dispose();
                    return;
                }
                previous = _loadCancellation;
                _current = session;
                _loadCancellation = cancellation;
                generation = ++_generation;
            }

            if (previous != null)
            {
                try { previous.Cancel(); }
                finally { previous.Dispose(); }
            }
            _attached(session, cancellation.Token);
            var ignored = LoadAsync(session, cancellation, generation);
        }

        private void Detach(ManagerSession session)
        {
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (!ReferenceEquals(_current, session)) return;
                _current = null;
                cancellation = _loadCancellation;
                _loadCancellation = null;
                ++_generation;
            }

            if (cancellation != null)
            {
                try { cancellation.Cancel(); }
                finally { cancellation.Dispose(); }
            }
            _detached(session);
        }

        private async Task LoadAsync(
            ManagerSession session,
            CancellationTokenSource cancellation,
            long generation)
        {
            try
            {
                var games = await session.Media.LoadAsync(cancellation.Token).ConfigureAwait(false);
                if (IsCurrent(session, cancellation, generation)) _loaded(session, games);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (IsCurrent(session, cancellation, generation)) _loadFailed(session, ex);
            }
        }

        private bool IsCurrent(
            ManagerSession session,
            CancellationTokenSource cancellation,
            long generation)
        {
            lock (_sync)
            {
                return !_disposed &&
                       !cancellation.IsCancellationRequested &&
                       ReferenceEquals(_current, session) &&
                       ReferenceEquals(_loadCancellation, cancellation) &&
                       _generation == generation;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
            }

            _binding.Dispose();
            lock (_sync) _disposed = true;
        }
    }
}
