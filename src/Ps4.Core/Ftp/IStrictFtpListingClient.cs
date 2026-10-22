using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ps4.Core.Ftp
{
    /// <summary>A listing that rejects unknown or malformed rows instead of silently omitting them.</summary>
    public interface IStrictFtpListingClient
    {
        Task<IReadOnlyList<FtpEntry>> ListStrictAsync(string path, CancellationToken ct);
    }
}
