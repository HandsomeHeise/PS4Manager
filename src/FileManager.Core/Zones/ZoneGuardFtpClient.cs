using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace FileManager.Core.Zones
{
    /// <summary>
    /// Die zweite, harte Hälfte des Zonenschutzes (Spec „Durchsetzung
    /// doppelt"): Jeder Schreibaufruf wird geprüft, BEVOR er den Draht
    /// erreicht. Warnzonen lässt er durch — deren Bestätigung holt die
    /// Auftragsplanung ein. Alle Schreibwege der App laufen über diese Klasse.
    /// </summary>
    public sealed class ZoneGuardFtpClient : IFtpClient, IStrictFtpListingClient
    {
        private readonly IFtpClient _inner;
        private readonly ZonePolicy _policy;

        public ZoneGuardFtpClient(IFtpClient inner, ZonePolicy policy)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            _inner = inner;
            _policy = policy;
        }

        private void Check(string path)
        {
            if (_policy.Effective(path) == Zone.Locked) throw new ZoneViolationException(path);
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct) { return _inner.ListAsync(path, ct); }
        public Task<IReadOnlyList<FtpEntry>> ListStrictAsync(string path, CancellationToken ct)
        {
            var strict = _inner as IStrictFtpListingClient;
            if (strict == null) throw new InvalidDataException("The FTP client cannot provide a complete safety listing.");
            return strict.ListStrictAsync(path, ct);
        }
        public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { return _inner.DownloadAsync(path, ct); }

        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)
        {
            return _inner.DownloadToFileAsync(path, localPath, progress, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct)
        {
            return _inner.DownloadToFileAsync(path, localPath, offset, progress, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress,
            bool keepPartialOnFailure, CancellationToken ct)
        {
            var client = _inner as FtpClient;
            return client != null
                ? client.DownloadToFileAsync(path, localPath, offset, progress, keepPartialOnFailure, ct)
                : _inner.DownloadToFileAsync(path, localPath, offset, progress, ct);
        }

        public Stream OpenRead(string path, long offset) { return _inner.OpenRead(path, offset); }
        public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { return _inner.GetFileSizeAsync(path, ct); }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            Check(remotePath);
            return _inner.UploadFromFileAsync(localPath, remotePath, progress, ct);
        }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            Check(remotePath);
            return _inner.AppendFromFileAsync(localPath, localOffset, remotePath, progress, ct);
        }

        public Task MakeDirectoryAsync(string path, CancellationToken ct)
        {
            Check(path);
            return _inner.MakeDirectoryAsync(path, ct);
        }

        public Task RemoveDirectoryAsync(string path, CancellationToken ct)
        {
            Check(path);
            return _inner.RemoveDirectoryAsync(path, ct);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
        {
            Check(fromPath);
            Check(toPath);
            return _inner.RenameAsync(fromPath, toPath, ct);
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            Check(path);
            return _inner.DeleteAsync(path, ct);
        }
    }
}
