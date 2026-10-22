using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager.Core.Browsing
{
    /// <summary>
    /// Das Verbindungsbudget des File Managers (Spec „Verbindungslimit"):
    /// eine Übertragung, zwei fürs Durchsuchen (Listings, Titel-Infos), eine
    /// für Explorer beim Lesen virtueller Dateien. Anders als in der Gallery
    /// geht die Rechnung auf: die App setzt
    /// ServicePointManager.DefaultConnectionLimit = ConnectionLimit.
    ///
    /// Die Shell hat bewusst KEINEN Semaphor — siehe FtpComStream.
    /// </summary>
    public sealed class FtpGates
    {
        public const int TransferSlots = 1;
        public const int BrowseSlots = 2;
        public const int ShellSlots = 1;
        public const int ConnectionLimit = TransferSlots + BrowseSlots + ShellSlots;

        public SemaphoreSlim Transfer { get; } = new SemaphoreSlim(TransferSlots, TransferSlots);
        public SemaphoreSlim Browse { get; } = new SemaphoreSlim(BrowseSlots, BrowseSlots);

        public static async Task<T> RunAsync<T>(SemaphoreSlim gate, Func<Task<T>> operation, CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
