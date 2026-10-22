using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public interface IFrame4PacketTransport
    {
        Task<IFrame4PacketConnection> ConnectAsync(Frame4Endpoint endpoint, CancellationToken ct);
    }
}
