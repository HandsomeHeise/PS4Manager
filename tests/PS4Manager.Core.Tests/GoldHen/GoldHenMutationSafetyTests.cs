using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenMutationSafetyTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "goldhen-safety-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public async Task Backup_current_async_stores_the_remote_bytes_not_the_replacement()
        {
            var ftp = new FakeFtp();
            ftp.Add("/data/GoldHEN/plugins.ini", "old remote");
            var safety = Safety(ftp);

            var snapshot = await safety.BackupCurrentAsync("/data/GoldHEN/plugins.ini", "toggle", CancellationToken.None);

            Assert.Equal(Encoding.UTF8.GetBytes("old remote"), File.ReadAllBytes(snapshot.Backup.PayloadPath));
            Assert.Equal(Encoding.UTF8.GetBytes("old remote"), snapshot.Content);
        }

        [Fact]
        public async Task Upload_collisions_are_backed_up_before_the_job_can_run()
        {
            var ftp = new FakeFtp();
            ftp.Add("/data/GoldHEN/plugins/existing.prx", "installed");
            var existing = Local("existing.prx", "replacement");
            var fresh = Local("fresh.prx", "new");
            var job = new TransferJob(TransferKind.Upload, "upload", new[]
            {
                new TransferStep(StepKind.UploadFile, "/data/GoldHEN/plugins/existing.prx", existing, null, 11),
                new TransferStep(StepKind.UploadFile, "/data/GoldHEN/plugins/fresh.prx", fresh, null, 3)
            });

            var snapshots = await Safety(ftp).ProtectUploadAsync(job, "upload-replace", CancellationToken.None);

            var collision = Assert.Single(snapshots);
            Assert.Equal("/data/GoldHEN/plugins/existing.prx", collision.RemotePath);
            Assert.Equal(Encoding.UTF8.GetBytes("installed"), File.ReadAllBytes(collision.Backup.PayloadPath));
        }

        private GoldHenMutationSafety Safety(FakeFtp ftp)
        {
            return new GoldHenMutationSafety(ftp, new FtpGates(),
                new GoldHenBackupWriter(_root, new FixedClock()));
        }

        private string Local(string name, string content)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, content);
            return path;
        }

        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get { return new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc); } }
        }

        private sealed class FakeFtp : IFtpClient
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public void Add(string path, string content) { _files[path] = Encoding.UTF8.GetBytes(content); }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                var prefix = path.TrimEnd('/') + "/";
                IReadOnlyList<FtpEntry> entries = _files
                    .Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && p.Key.Substring(prefix.Length).IndexOf('/') < 0)
                    .Select(p => new FtpEntry(p.Key.Substring(prefix.Length), false, p.Value.Length))
                    .ToList();
                return Task.FromResult(entries);
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { return Task.FromResult(_files[path]); }
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
}
