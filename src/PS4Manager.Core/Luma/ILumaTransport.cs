using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Luma
{
    public interface ILumaTransport
    {
        Task<ILumaConnection> ConnectAsync(LumaEndpoint endpoint, CancellationToken ct);
    }

    public interface ILumaConnection : System.IDisposable
    {
        Stream Read { get; }
        Stream Write { get; }
    }
}
