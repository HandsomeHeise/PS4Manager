using System;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Session;

namespace PS4Manager.Core.Media
{
    /// <summary>
    /// Captures the session that owns a preview. Cache reads and asynchronous
    /// completions are accepted only while that exact session remains current.
    /// </summary>
    public sealed class MediaSessionGuard
    {
        private readonly ManagerSessionController _sessions;

        public MediaSessionGuard(ManagerSessionController sessions, ManagerSession session)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public ManagerSession Session { get; }

        public bool IsActive
        {
            get
            {
                return !Session.CancellationToken.IsCancellationRequested &&
                       ReferenceEquals(_sessions.Current, Session);
            }
        }

        public T ReadCache<T>(Func<T> read) where T : class
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            ThrowIfInactive();
            var value = read();
            ThrowIfInactive();
            return value;
        }

        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            ThrowIfInactive();
            var value = await operation(Session.CancellationToken).ConfigureAwait(false);
            ThrowIfInactive();
            return value;
        }

        public void ThrowIfInactive()
        {
            if (!IsActive) throw new OperationCanceledException(Session.CancellationToken);
        }

        public bool TryMutate(Action mutation)
        {
            return _sessions.TryMutateCurrent(Session, mutation);
        }

        public void Mutate(Action mutation)
        {
            if (!TryMutate(mutation))
                throw new OperationCanceledException(Session.CancellationToken);
        }

        public T Mutate<T>(Func<T> mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            T result = default(T);
            Mutate(() => { result = mutation(); });
            return result;
        }

        public T Publish<T>(Func<T> makeVisible, Action<T> maintenance)
        {
            if (makeVisible == null) throw new ArgumentNullException(nameof(makeVisible));
            if (maintenance == null) throw new ArgumentNullException(nameof(maintenance));

            return _sessions.RunMediaPublication(() =>
            {
                var result = Mutate(makeVisible);
                maintenance(result);
                return result;
            });
        }
    }
}
