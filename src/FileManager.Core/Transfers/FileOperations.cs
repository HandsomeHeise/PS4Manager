using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Plant aus einer Anwenderaktion einen Auftrag (Spec „Operationen und
    /// Aufträge"). Hier sitzt die erste Hälfte des Zonenschutzes: gesperrt
    /// wird abgelehnt, ohne zu fragen; Warnzone kostet genau eine Bestätigung
    /// pro Auftrag. Ausgeführt wird erst in der TransferQueue.
    /// </summary>
    public sealed class FileOperations
    {
        public const string CopyWithinPs4Message = "Copying within the PS4 is not supported yet. Use Ctrl+X to move items.";
        public const string NewFolderName = "New folder";

        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;
        private readonly ZonePolicy _policy;
        private readonly IUserPrompts _prompts;

        public FileOperations(IFtpClient ftp, FtpGates gates, ZonePolicy policy, IUserPrompts prompts)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (prompts == null) throw new ArgumentNullException(nameof(prompts));
            _ftp = ftp;
            _gates = gates;
            _policy = policy;
            _prompts = prompts;
        }

        public Task<TransferJob> PlanUploadAsync(IReadOnlyList<string> localPaths, string remoteDir, CancellationToken ct)
        {
            // Große lokale Ordner dürfen die Oberfläche beim Planen nicht blockieren.
            return Task.Run(() => PlanUploadCoreAsync(localPaths, remoteDir, ct), ct);
        }

        private async Task<TransferJob> PlanUploadCoreAsync(IReadOnlyList<string> localPaths, string remoteDir, CancellationToken ct)
        {
            var steps = new List<TransferStep>();
            foreach (var local in localPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (IsDriveRoot(local)) throw new ArgumentException("Entire drives cannot be uploaded: " + local, nameof(localPaths));
                var trimmed = local.TrimEnd('\\', '/');
                var target = RemotePath.Combine(remoteDir, Path.GetFileName(trimmed));
                if (Directory.Exists(trimmed)) AddLocalTree(trimmed, target, steps, ct);
                else if (File.Exists(trimmed)) steps.Add(new TransferStep(StepKind.UploadFile, target, trimmed, null, new FileInfo(trimmed).Length));
                else throw new FileNotFoundException("Local file not found: " + trimmed, trimmed);
            }

            if (!await ApproveAsync("Upload", steps.Select(s => s.RemotePath), ct).ConfigureAwait(false)) return null;
            var names = localPaths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();
            return new TransferJob(TransferKind.Upload, "Upload " + Items(names) + " to " + RemotePath.Normalize(remoteDir), steps);
        }

        public async Task<TransferJob> PlanDownloadAsync(IReadOnlyList<RemoteItem> items, string localDir, CancellationToken ct)
        {
            var steps = new List<TransferStep>();
            foreach (var item in items)
            {
                var local = Path.Combine(localDir, LocalName.Sanitize(item.Name));
                if (item.IsDirectory) await AddRemoteTreeForDownloadAsync(item.Path, local, steps, ct).ConfigureAwait(false);
                else steps.Add(new TransferStep(StepKind.DownloadFile, item.Path, local, null, item.Size));
            }
            return new TransferJob(TransferKind.Download, "Download " + Items(items.Select(i => i.Name).ToList()), steps);
        }

        public TransferJob PlanOpenForEdit(RemoteItem file, string localFilePath)
        {
            var step = new TransferStep(StepKind.DownloadFile, file.Path, localFilePath, null, file.Size) { OverwriteWithoutAsking = true };
            return new TransferJob(TransferKind.Download, "Open " + file.Name, new[] { step });
        }

        public async Task<TransferJob> PlanWriteBackAsync(string localFilePath, string remotePath, CancellationToken ct)
        {
            var target = RemotePath.Normalize(remotePath);
            if (!await ApproveAsync("Write back", new[] { target }, ct).ConfigureAwait(false)) return null;
            var step = new TransferStep(StepKind.UploadFile, target, localFilePath, null, new FileInfo(localFilePath).Length)
            {
                OverwriteWithoutAsking = true
            };
            return new TransferJob(TransferKind.Upload, "Write back " + RemotePath.Name(target), new[] { step });
        }

        public async Task<TransferJob> PlanDeleteAsync(IReadOnlyList<RemoteItem> items, CancellationToken ct)
        {
            // Gesperrte Auswahl vor dem rekursiven Auflisten ablehnen.
            var lockedItem = items.FirstOrDefault(i => _policy.Effective(i.Path) == Zone.Locked);
            if (lockedItem != null) throw new ZoneViolationException(lockedItem.Path);
            var steps = new List<TransferStep>();
            foreach (var item in items)
            {
                if (item.IsDirectory) await AddRemoteTreeForDeleteAsync(item.Path, steps, ct).ConfigureAwait(false);
                else steps.Add(new TransferStep(StepKind.DeleteRemoteFile, item.Path, null, null, item.Size));
            }

            var paths = steps.Select(s => s.RemotePath).ToList();
            var zone = _policy.Strictest(paths);
            if (zone == Zone.Locked) throw new ZoneViolationException(paths.First(p => _policy.Effective(p) == Zone.Locked));

            var summary = new DeleteSummary(
                steps.Count(s => s.Kind == StepKind.DeleteRemoteFile),
                steps.Count(s => s.Kind == StepKind.RemoveRemoteFolder),
                steps.Where(s => s.Kind == StepKind.DeleteRemoteFile).Sum(s => s.Size),
                zone,
                items.Select(i => i.Name).ToList());
            if (!await _prompts.ConfirmDeleteAsync(summary, ct).ConfigureAwait(false)) return null;

            return new TransferJob(TransferKind.Delete, "Delete " + Items(items.Select(i => i.Name).ToList()), steps);
        }

        public async Task<TransferJob> PlanCreateFolderAsync(string parentDir, CancellationToken ct)
        {
            var probe = RemotePath.Combine(parentDir, NewFolderName);
            if (_policy.Effective(probe) == Zone.Locked) throw new ZoneViolationException(probe);
            var entries = await ListGatedAsync(parentDir, ct).ConfigureAwait(false);
            var comparer = RemotePath.NameComparer(parentDir);
            var name = UniqueName.For(NewFolderName, true, n => entries.Any(e => comparer.Equals(e.Name, n)));
            var path = RemotePath.Combine(parentDir, name);
            if (!await ApproveAsync("New folder", new[] { path }, ct).ConfigureAwait(false)) return null;
            return new TransferJob(TransferKind.CreateFolder, "Create folder “" + name + "”",
                new[] { new TransferStep(StepKind.CreateRemoteFolder, path, null, null, 0) });
        }

        public async Task<TransferJob> PlanRenameAsync(RemoteItem item, string newName, CancellationToken ct)
        {
            ValidateName(newName);
            var parent = RemotePath.Parent(item.Path);
            var target = RemotePath.Combine(parent, newName);
            if (target == item.Path) return null;
            if (_policy.Effective(item.Path) == Zone.Locked) throw new ZoneViolationException(item.Path);
            if (_policy.Effective(target) == Zone.Locked) throw new ZoneViolationException(target);

            var entries = await ListGatedAsync(parent, ct).ConfigureAwait(false);
            var comparer = RemotePath.NameComparer(parent);
            if (entries.Any(e => comparer.Equals(e.Name, newName) && e.Name != item.Name))
                throw new InvalidOperationException("An item named “" + newName + "” already exists here.");

            if (!await ApproveAsync("Rename", new[] { item.Path, target }, ct).ConfigureAwait(false)) return null;
            var step = new TransferStep(StepKind.RenameRemote, item.Path, null, target, item.Size) { IsDirectory = item.IsDirectory };
            return new TransferJob(TransferKind.Rename, "Rename “" + item.Name + "” to “" + newName + "”", new[] { step });
        }

        public async Task<TransferJob> PlanMoveAsync(IReadOnlyList<RemoteItem> items, string targetDir, CancellationToken ct)
        {
            var target = RemotePath.Normalize(targetDir);
            var steps = new List<TransferStep>();
            foreach (var item in items)
            {
                if (item.IsDirectory && RemotePath.IsSameOrBelow(target, item.Path))
                    throw new InvalidOperationException("A folder cannot be moved into itself.");
                var destination = RemotePath.Combine(target, item.Name);
                if (destination == item.Path) continue;
                steps.Add(new TransferStep(StepKind.RenameRemote, item.Path, null, destination, item.Size) { IsDirectory = item.IsDirectory });
            }
            if (steps.Count == 0) return null;

            var paths = steps.Select(s => s.RemotePath).Concat(steps.Select(s => s.TargetRemotePath));
            if (!await ApproveAsync("Move", paths, ct).ConfigureAwait(false)) return null;
            return new TransferJob(TransferKind.Move, "Move " + Items(steps.Select(s => RemotePath.Name(s.RemotePath)).ToList()) + " to " + target, steps);
        }

        public TransferJob PlanCopyWithinPs4()
        {
            throw new NotSupportedException(CopyWithinPs4Message);
        }

        private async Task<bool> ApproveAsync(string action, IEnumerable<string> paths, CancellationToken ct)
        {
            var list = paths.ToList();
            var zone = _policy.Strictest(list);
            if (zone == Zone.Locked) throw new ZoneViolationException(list.First(p => _policy.Effective(p) == Zone.Locked));
            if (zone == Zone.Warn)
                return await _prompts.ConfirmWarnZoneAsync(action, list.Where(p => _policy.Effective(p) == Zone.Warn).ToList(), ct).ConfigureAwait(false);
            return true;
        }

        private static bool IsDriveRoot(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root)
                && string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        private static void AddLocalTree(string localDir, string remoteDir, List<TransferStep> steps, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            steps.Add(new TransferStep(StepKind.CreateRemoteFolder, remoteDir, localDir, null, 0));
            foreach (var file in Directory.GetFiles(localDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                steps.Add(new TransferStep(StepKind.UploadFile, RemotePath.Combine(remoteDir, Path.GetFileName(file)), file, null, new FileInfo(file).Length));
            }
            foreach (var sub in Directory.GetDirectories(localDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // Verzeichnisverknüpfungen können aus dem Ordner heraus oder zurück führen.
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                AddLocalTree(sub, RemotePath.Combine(remoteDir, Path.GetFileName(sub)), steps, ct);
            }
        }

        private async Task AddRemoteTreeForDownloadAsync(string remoteDir, string localDir, List<TransferStep> steps, CancellationToken ct)
        {
            steps.Add(new TransferStep(StepKind.CreateLocalFolder, remoteDir, localDir, null, 0));
            var entries = await ListGatedAsync(remoteDir, ct).ConfigureAwait(false);
            foreach (var file in entries.Where(e => e.Kind == FtpEntryKind.File).OrderBy(e => e.Name, StringComparer.Ordinal))
                steps.Add(new TransferStep(StepKind.DownloadFile, RemotePath.Combine(remoteDir, file.Name),
                    Path.Combine(localDir, LocalName.Sanitize(file.Name)), null, file.Size));
            foreach (var dir in entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                await AddRemoteTreeForDownloadAsync(RemotePath.Combine(remoteDir, dir.Name),
                    Path.Combine(localDir, LocalName.Sanitize(dir.Name)), steps, ct).ConfigureAwait(false);
        }

        /// <summary>Nachordnung: erst die Dateien, dann die Unterordner (rekursiv), zuletzt der Ordner selbst — RMD geht nur auf leere Ordner.</summary>
        private async Task AddRemoteTreeForDeleteAsync(string remoteDir, List<TransferStep> steps, CancellationToken ct)
        {
            var entries = await ListGatedAsync(remoteDir, ct).ConfigureAwait(false);
            foreach (var file in entries.Where(e => !e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                steps.Add(new TransferStep(StepKind.DeleteRemoteFile, RemotePath.Combine(remoteDir, file.Name), null, null, file.Size));
            foreach (var dir in entries.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.Ordinal))
                await AddRemoteTreeForDeleteAsync(RemotePath.Combine(remoteDir, dir.Name), steps, ct).ConfigureAwait(false);
            steps.Add(new TransferStep(StepKind.RemoveRemoteFolder, remoteDir, null, null, 0));
        }

        private Task<IReadOnlyList<FtpEntry>> ListGatedAsync(string path, CancellationToken ct)
        {
            return FtpGates.RunAsync(_gates.Browse, () => _ftp.ListAsync(path, ct), ct);
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("The name cannot be empty.", nameof(name));
            if (name.IndexOf('/') >= 0) throw new ArgumentException("The name cannot contain a slash.", nameof(name));
            if (name == "." || name == "..") throw new ArgumentException("This name is not allowed.", nameof(name));
            // Ein Steuerzeichen (z. B. \n) im Namen könnte, weil FtpWebRequest
            // Kommandoparameter nur an "\r\n" abschneidet, ein zweites
            // FTP-Kommando einschleusen, das der Zonenschutz nie zu sehen bekam.
            if (name.Any(char.IsControl)) throw new ArgumentException("The name cannot contain control characters.", nameof(name));
        }

        private static string Items(IReadOnlyList<string> names)
        {
            return names.Count == 1 ? names[0] : names.Count + " items";
        }
    }
}
