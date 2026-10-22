using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Session
{
    /// <summary>Owns the currently published console session.</summary>
    public sealed class ManagerSessionController
    {
        private readonly IManagerSessionFactory _factory;
        private readonly object _sync = new object();
        private readonly object _mediaPublicationSync = new object();
        private readonly Queue<ManagerSessionChangedEventArgs> _notifications = new Queue<ManagerSessionChangedEventArgs>();
        private long _generation;
        private ManagerSession _current;
        private ConnectionState _state;
        private bool _dispatching;

        public ManagerSessionController(IManagerSessionFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public event EventHandler<ManagerSessionChangedEventArgs> SessionChanged;

        public ManagerSession Current
        {
            get { lock (_sync) return _current; }
        }

        public ConnectionState State
        {
            get { lock (_sync) return _state; }
        }

        internal bool TryMutateCurrent(ManagerSession expected, Action mutation)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));

            lock (_sync)
            {
                if (!ReferenceEquals(_current, expected) ||
                    expected.CancellationToken.IsCancellationRequested)
                    return false;

                mutation();
                return true;
            }
        }

        internal T RunMediaPublication<T>(Func<T> transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            // A publication may wait here without owning _sync. The transaction
            // itself takes _sync only for its short exact-session visibility
            // mutation; cache enumeration and eviction remain outside _sync.
            lock (_mediaPublicationSync)
            {
                return transaction();
            }
        }

        public async Task ConnectAsync(ManagerEndpoint endpoint, bool expertMode, CancellationToken ct)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            ct.ThrowIfCancellationRequested();

            long generation;
            lock (_sync)
            {
                generation = ++_generation;
                _state = ConnectionState.Connecting;
            }

            ManagerSession candidate = null;
            try
            {
                candidate = _factory.Create(endpoint, expertMode);
                if (candidate == null) throw new InvalidOperationException("The session factory returned null.");
                await candidate.Explorer.CheckAsync(ct).ConfigureAwait(false);

                ManagerSession previous;
                bool dispatch;
                lock (_sync)
                {
                    if (generation != _generation) return;
                    ct.ThrowIfCancellationRequested();
                    previous = _current;
                    _current = candidate;
                    _state = ConnectionState.Connected;
                    dispatch = QueueNotification(new ManagerSessionChangedEventArgs(candidate, _state));
                    candidate = null;
                }

                try
                {
                    if (previous != null) previous.Dispose();
                }
                finally
                {
                    if (dispatch) DispatchNotifications();
                }
            }
            catch
            {
                lock (_sync)
                {
                    if (generation == _generation)
                        _state = _current == null ? ConnectionState.Disconnected : ConnectionState.Connected;
                }
                throw;
            }
            finally
            {
                if (candidate != null) candidate.Dispose();
            }
        }

        public Task DisconnectAsync()
        {
            ManagerSession previous;
            bool dispatch = false;
            lock (_sync)
            {
                ++_generation;
                previous = _current;
                _current = null;
                _state = ConnectionState.Disconnected;
                if (previous != null)
                    dispatch = QueueNotification(new ManagerSessionChangedEventArgs(null, _state));
            }

            if (previous != null)
            {
                try { previous.Dispose(); }
                finally { if (dispatch) DispatchNotifications(); }
            }
            return Task.CompletedTask;
        }

        // Called under _sync. Reserve the first publisher as the dispatcher so a
        // reentrant connection change cannot deliver its event out of order.
        private bool QueueNotification(ManagerSessionChangedEventArgs args)
        {
            _notifications.Enqueue(args);
            if (_dispatching) return false;
            _dispatching = true;
            return true;
        }

        private void DispatchNotifications()
        {
            Exception firstFailure = null;
            while (true)
            {
                ManagerSessionChangedEventArgs args;
                lock (_sync)
                {
                    if (_notifications.Count == 0)
                    {
                        _dispatching = false;
                        break;
                    }
                    args = _notifications.Dequeue();
                }

                try { OnSessionChanged(args); }
                catch (Exception ex) { if (firstFailure == null) firstFailure = ex; }
            }
            if (firstFailure != null) throw firstFailure;
        }

        private void OnSessionChanged(ManagerSessionChangedEventArgs args)
        {
            var handler = SessionChanged;
            if (handler != null) handler(this, args);
        }
    }
}
