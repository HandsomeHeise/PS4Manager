using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public interface IFrame4UartTransport
    {
        Task<IFrame4UartConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct);
    }
}
