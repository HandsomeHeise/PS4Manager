using System;

namespace PS4Manager.Core.Session
{
    /// <summary>Keeps a tool attached to exactly the session published by the shared controller.</summary>
    public sealed class ToolSessionBinding : IDisposable
    {
        private readonly object _sync = new object();
        private readonly ManagerSessionController _sessions;
        private readonly Action<ManagerSession> _attach;
        private readonly Action<ManagerSession> _detach;
        private ManagerSession _current;
        private bool _disposed;

        public ToolSessionBinding(
            ManagerSessionController sessions,
            Action<ManagerSession> attach,
            Action<ManagerSession> detach)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _attach = attach ?? throw new ArgumentNullException(nameof(attach));
            _detach = detach ?? throw new ArgumentNullException(nameof(detach));

            _sessions.SessionChanged += OnSessionChanged;
            try { ChangeSession(_sessions.Current); }
            catch
            {
                _sessions.SessionChanged -= OnSessionChanged;
                throw;
            }
        }

        private void OnSessionChanged(object sender, ManagerSessionChangedEventArgs args)
        {
            // A later publication can overtake delivery of an older queued event.
            // Bind tools to the controller's authoritative current value, never
            // briefly back to the stale event payload.
            ChangeSession(_sessions.Current);
        }

        private void ChangeSession(ManagerSession session)
        {
            lock (_sync)
            {
                if (_disposed || ReferenceEquals(_current, session)) return;

                var previous = _current;
                _current = session;
                if (previous != null) _detach(previous);
                if (session != null) _attach(session);
            }
        }

        public void Dispose()
        {
            _sessions.SessionChanged -= OnSessionChanged;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                var previous = _current;
                _current = null;
                if (previous != null) _detach(previous);
            }
        }
    }
}
