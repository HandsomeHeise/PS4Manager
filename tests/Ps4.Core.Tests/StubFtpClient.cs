using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace Ps4.Core.Tests
{
    /// <summary>Nur DownloadAsync lebt: Pfad → Bytes; fehlend → 550. Alles andere wirft.</summary>
    internal sealed class StubFtpClient : IFtpClient
    {
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public Exception FailWith;

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailWith != null) throw FailWith;
            byte[] data;
            if (!Files.TryGetValue(path, out data))
                throw new FtpException("Nicht vorhanden oder kein Zugriff", FtpErrorKind.Permanent, 550);
            return Task.FromResult(data);
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
        public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
        public Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }
        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
        public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task MakeDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task RemoveDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { throw new NotSupportedException(); }
        public Task DeleteAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
    }
}
