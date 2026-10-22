using System;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public interface IFrame4HttpTransport
    {
        Task<string> GetStringAsync(Uri uri, CancellationToken ct);
    }
}
