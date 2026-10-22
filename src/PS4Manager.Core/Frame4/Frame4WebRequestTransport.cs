using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Frame4
{
    public sealed class Frame4WebRequestTransport : IFrame4HttpTransport
    {
        public async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            ct.ThrowIfCancellationRequested();
            var request = WebRequest.CreateHttp(uri);
            request.Method = "GET";
            request.Timeout = 3000;
            request.ReadWriteTimeout = 3000;
            using (ct.Register(() => request.Abort()))
            using (var response = await request.GetResponseAsync().ConfigureAwait(false))
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }
    }
}
