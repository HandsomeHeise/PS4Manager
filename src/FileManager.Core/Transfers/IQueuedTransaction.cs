using System.Threading;
using System.Threading.Tasks;

namespace FileManager.Core.Transfers
{
    public interface IQueuedTransaction
    {
        Task ExecuteAsync(TransferTransactionContext context, CancellationToken ct);
    }
}
