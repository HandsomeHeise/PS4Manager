using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.GoldHen
{
    public sealed class GoldHenRemoteSnapshot
    {
        internal GoldHenRemoteSnapshot(string remotePath, byte[] content, GoldHenBackup backup)
        {
            RemotePath = remotePath;
            Content = content;
            Backup = backup;
        }

        public string RemotePath { get; }
        public byte[] Content { get; }
        public GoldHenBackup Backup { get; }

        public void Protect(TransferJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            job.ProtectRemoteFile(RemotePath, Content);
        }
    }

    /// <summary>
    /// Sichert den aktuellen Remote-Inhalt und bindet die folgende Mutation
    /// an genau diesen Stand. So bleibt die Regel auch bei Queue-Wartezeit und
    /// externen Änderungen wahr.
    /// </summary>
    public sealed class GoldHenMutationSafety
    {
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly GoldHenBackupWriter _writer;

        public GoldHenMutationSafety(IFtpClient ftp, FtpGates gates, GoldHenBackupWriter writer)
        {
            _ftp = ftp ?? throw new ArgumentNullException(nameof(ftp));
            _gates = gates ?? throw new ArgumentNullException(nameof(gates));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public async Task<GoldHenRemoteSnapshot> BackupCurrentAsync(string remotePath, string operation,
            CancellationToken token)
        {
            var bytes = await FtpGates.RunAsync(_gates.Browse,
                () => _ftp.DownloadAsync(remotePath, token), token).ConfigureAwait(false);
            var backup = _writer.Write(remotePath, bytes, operation);
            return new GoldHenRemoteSnapshot(RemotePath.Normalize(remotePath), bytes, backup);
        }

        public async Task<IReadOnlyList<GoldHenRemoteSnapshot>> ProtectUploadAsync(TransferJob job,
            string operation, CancellationToken token)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            var snapshots = new List<GoldHenRemoteSnapshot>();
            var uploads = job.Steps.Where(s => s.Kind == StepKind.UploadFile).ToList();
            foreach (var group in uploads.GroupBy(s => RemotePath.Parent(s.RemotePath), StringComparer.Ordinal))
            {
                var entries = await FtpGates.RunAsync(_gates.Browse,
                    () => _ftp.ListAsync(group.Key, token), token).ConfigureAwait(false);
                var comparer = RemotePath.NameComparer(group.Key);
                foreach (var step in group)
                {
                    var entry = entries.FirstOrDefault(e => comparer.Equals(e.Name, RemotePath.Name(step.RemotePath)));
                    if (entry == null)
                    {
                        job.ProtectRemoteAbsence(step.RemotePath);
                        continue;
                    }
                    if (entry.IsDirectory)
                        throw new InvalidOperationException("The upload target is a directory: " + step.RemotePath);
                    var snapshot = await BackupCurrentAsync(step.RemotePath, operation, token).ConfigureAwait(false);
                    snapshot.Protect(job);
                    snapshots.Add(snapshot);
                }
            }
            return snapshots;
        }
    }
}
