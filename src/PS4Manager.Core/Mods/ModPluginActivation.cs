using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Mods
{
    /// <summary>Explicit, separately queued activation. Never changes the installation receipt.</summary>
    public sealed class ModPluginActivation : IQueuedTransaction
    {
        public const string ConfigurationPath = "/data/GoldHEN/plugins.ini";
        private readonly ModInstallPlanner planner;
        private readonly Guid transaction;
        private readonly GoldHenBackupWriter backups;

        public ModPluginActivation(ModInstallPlanner planner, Guid transaction, GoldHenBackupWriter backups)
        { this.planner = planner; this.transaction = transaction; this.backups = backups; }

        public static IReadOnlyList<string> PluginPaths(ModReceipt receipt)
        {
            if (receipt == null || receipt.State != ModInstallState.Installed) return new string[0];
            return receipt.Files.Where(f => f.Source != null && f.Target.EndsWith(".prx", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Target).ToArray();
        }

        public async Task ExecuteAsync(TransferTransactionContext context, CancellationToken ct)
        {
            planner.Confirmed();
            using (planner.Store.AcquireLease(planner.Profile.Id))
            {
                var receipt = planner.Store.LoadTransaction(planner.Profile.Id, transaction);
                planner.ValidateSaved(receipt, ct);
                var plugins = PluginPaths(receipt);
                if (plugins.Count == 0) throw new InvalidOperationException("No installed PRX plugin is available for activation.");
                // Activation is separate from the signed package. Do not invalidate a
                // package receipt (including this one) that owns the configuration.
                var configChange = new ModReceipt { TransactionId = Guid.NewGuid() };
                configChange.Files.Add(new ModReceiptFile { Target = ConfigurationPath });
                ModInstallPlanner.Conflicts(configChange, planner.Store.Load(planner.Profile.Id));
                var ftp = context.Ftp;
                foreach (var path in plugins)
                {
                    if (RemotePath.Canonical(RemotePath.Parent(path)) != GoldHenDiscoveryService.PluginsPath)
                        throw new InvalidDataException("Enable plugins outside the GoldHEN plugins folder manually in GoldHEN Manager.");
                    var file = receipt.Files.Single(f => f.Target == path);
                    await ModInstallPlanner.CheckParents(ftp, path, ct).ConfigureAwait(false);
                    await Verify(ftp, path, file.DesiredHash, file.DesiredSize, ct).ConfigureAwait(false);
                }
                await ModInstallPlanner.CheckParents(ftp, ConfigurationPath, ct).ConfigureAwait(false);
                context.Report("Backing up GoldHEN configuration");
                var before = await Read(ftp, ConfigurationPath, ct).ConfigureAwait(false);
                if (before.Size > 1048576) throw new InvalidDataException("GoldHEN configuration is too large to edit automatically.");
                var bytes = before.Exists ? planner.Store.ReadBackup(planner.Profile.Id, before.Hash) : new byte[0];
                var document = TextFileContent.FromBytes(bytes);
                var text = document.Text;
                foreach (var path in plugins)
                    text = IniPluginEditor.SetEnabled(text, new PluginEntry(RemotePath.Name(path), path, GoldHenActivation.Disabled, true, -1), true, receipt.TitleId, false);
                if (text == document.Text) { context.Report("Already enabled in GoldHEN. Start the game to load the plugin."); return; }

                // Keep both a local, discoverable backup and the untouched remote original.
                var operation = Guid.NewGuid().ToString("N");
                var stage = GoldHenDiscoveryService.Root + "/.ps4m-activate-" + operation + ".stage";
                var original = GoldHenDiscoveryService.Root + "/.ps4m-activate-" + operation + ".original";
                backups.Write(ConfigurationPath, bytes, "enable-mod-plugin; original=" + (before.Exists ? original : "absent"));
                var updated = document.ToBytes(text);
                var hash = planner.Store.SaveBackup(planner.Profile.Id, updated);
                var local = planner.Store.VerifiedBackupPath(planner.Profile.Id, hash);
                await Verify(ftp, stage, null, 0, ct).ConfigureAwait(false);
                await Verify(ftp, original, null, 0, ct).ConfigureAwait(false);
                await ftp.UploadFromFileAsync(local, stage, null, ct).ConfigureAwait(false);
                await Verify(ftp, stage, hash, updated.Length, ct).ConfigureAwait(false);
                await Verify(ftp, ConfigurationPath, before.Hash, before.Size, ct).ConfigureAwait(false);
                planner.Confirmed(); ct.ThrowIfCancellationRequested();
                context.Report("Enabling plugin for " + receipt.TitleId);
                try
                {
                    if (before.Exists)
                    {
                        await ftp.RenameAsync(ConfigurationPath, original, ct).ConfigureAwait(false);
                        await Verify(ftp, original, before.Hash, before.Size, ct).ConfigureAwait(false);
                    }
                    await Verify(ftp, ConfigurationPath, null, 0, ct).ConfigureAwait(false);
                    await ftp.RenameAsync(stage, ConfigurationPath, ct).ConfigureAwait(false);
                    await Verify(ftp, ConfigurationPath, hash, updated.Length, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Best effort only while still connected: restore by rename, not by
                    // uploading a possibly incorrect FTP readback over an original.
                    if (!ct.IsCancellationRequested && before.Exists)
                    {
                        try
                        {
                            var current = await Read(ftp, ConfigurationPath, ct).ConfigureAwait(false);
                            if (!current.Exists)
                            {
                                await Verify(ftp, original, before.Hash, before.Size, ct).ConfigureAwait(false);
                                await ftp.RenameAsync(original, ConfigurationPath, ct).ConfigureAwait(false);
                            }
                        }
                        catch { /* Original is retained for manual recovery; never overwrite unknown bytes. */ }
                    }
                    throw new IOException("GoldHEN activation could not be verified. Installation remains intact. Recovery files: " + stage + " / " + original, ex);
                }
                context.Report("Enabled in GoldHEN for " + receipt.TitleId + ". Start the game to load the plugin.");
            }
        }
        private Task<ModRemoteSnapshot> Read(IFtpClient ftp, string path, CancellationToken ct)
        { return ModRemoteSnapshot.ReadAsync(ftp, path, planner.Store, planner.Profile.Id, ct); }
        private Task Verify(IFtpClient ftp, string path, string hash, long size, CancellationToken ct)
        { return ModRemoteSnapshot.VerifyAsync(ftp, path, hash, size, planner.Store, planner.Profile.Id, ct); }
    }
}
