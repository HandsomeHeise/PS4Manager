using System;
using System.Threading;
using FileManager.Core.Browsing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using PS4Manager.Gallery.Core.Ps4;

namespace PS4Manager.Core.Session
{
    /// <summary>A connected console and the one Explorer graph shared by all tools.</summary>
    public sealed class ManagerSession : IDisposable
    {
        private readonly object _lifetime = new object();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private int _activeCancels;
        private bool _disposed;

        public ManagerSession(ManagerEndpoint endpoint, ExplorerSession explorer)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
            MediaClient = explorer.Ftp;
            Media = new MediaRepository(MediaClient, explorer.Gates.Browse);
            CancellationToken = _cancel.Token;
        }

        public ManagerEndpoint Endpoint { get; }
        public ExplorerSession Explorer { get; }
        public MediaRepository Media { get; }
        public FtpGates Gates { get { return Explorer.Gates; } }
        public TransferQueue Queue { get { return Explorer.Queue; } }
        public CancellationToken CancellationToken { get; }

        internal IFtpClient MediaClient { get; }
        internal bool IsDisposed { get { lock (_lifetime) return _disposed; } }

        public void Cancel()
        {
            lock (_lifetime)
            {
                if (_disposed) return;
                _activeCancels++;
            }

            try { _cancel.Cancel(); }
            finally { EndCancellation(); }
        }

        public void Dispose()
        {
            lock (_lifetime)
            {
                if (_disposed) return;
                _disposed = true;
                _activeCancels++;
            }

            try { _cancel.Cancel(); }
            finally
            {
                try { Explorer.Dispose(); }
                finally { EndCancellation(); }
            }
        }

        private void EndCancellation()
        {
            lock (_lifetime)
            {
                _activeCancels--;
                // Dispose may run from a callback while another Cancel is still
                // executing. Release the source after the last caller returns.
                if (_disposed && _activeCancels == 0) _cancel.Dispose();
            }
        }
    }
}
