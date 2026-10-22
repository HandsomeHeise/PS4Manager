using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    internal sealed class FakeChangeSource : IFileChangeSource
    {
        public string Watched;
        public event EventHandler<string> Changed;
        public void Watch(string directory) { Watched = directory; }
        public void Raise(string path) { var h = Changed; if (h != null) h(this, path); }
        public void Dispose() { }
    }

    public sealed class EditSessionManagerTests : IDisposable
    {
        private readonly TempDir _temp = new TempDir();
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly FakeChangeSource _changes = new FakeChangeSource();
        private readonly TestClock _clock = new TestClock();
        private readonly EditSessionManager _manager;
        private readonly List<EditSession> _announced = new List<EditSession>();

        private DateTime _saved = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Speichert wie ein Editor: neuer Inhalt, neue Schreibzeit (ausdrücklich
        /// gesetzt — NTFS-Zeitstempel sind für schnelle Folgen zu grob), dann das
        /// Ereignis des Watchers.
        /// </summary>
        private void Save(EditSession session, string text)
        {
            File.WriteAllText(session.LocalPath, text);
            _saved = _saved.AddSeconds(1);
            File.SetLastWriteTimeUtc(session.LocalPath, _saved);
            _changes.Raise(session.LocalPath);
        }

        public EditSessionManagerTests()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "[default]");
            _manager = new EditSessionManager(Path.Combine(_temp.Path, "open"), _ftp, new FtpGates(), _changes, _clock);
            _manager.ChangeDetected += (s, e) => _announced.Add(e);
        }

        public void Dispose()
        {
            _manager.Dispose();
            _temp.Dispose();
        }

        private EditSession OpenPluginsIni()
        {
            var local = _manager.PrepareLocalPath("/data/GoldHEN/plugins.ini");
            File.WriteAllText(local, "[default]");
            return _manager.Register("/data/GoldHEN/plugins.ini", local, 9, _ftp.Modified("/data/GoldHEN/plugins.ini"));
        }

        private static TransferJob CompletedWriteBack(EditSession session, byte[] uploaded)
        {
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(uploaded);
            var step = new TransferStep(StepKind.UploadFile, session.RemotePath, session.LocalPath, null, uploaded.Length)
            {
                LocalSourceHash = hash,
                State = StepState.Done
            };
            return new TransferJob(TransferKind.Upload, "write back", new[] { step });
        }

        [Fact]
        public void The_work_root_is_watched()
        {
            Assert.Equal(Path.Combine(_temp.Path, "open"), _changes.Watched);
            Assert.True(Directory.Exists(_changes.Watched));
        }

        [Fact]
        public async Task A_cancelled_async_registration_does_not_leave_a_watched_session()
        {
            var local = _manager.PrepareLocalPath("/data/GoldHEN/plugins.ini");
            File.WriteAllText(local, "[default]");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _manager.RegisterAsync(
                    "/data/GoldHEN/plugins.ini", local, 9, _ftp.Modified("/data/GoldHEN/plugins.ini"), cts.Token));
            }

            File.WriteAllText(local, "[changed]");
            _changes.Raise(local);
            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();

            Assert.Empty(_announced);
            Assert.Empty(_manager.PendingSessions);
        }

        [Fact]
        public void A_change_is_announced_after_one_quiet_second_and_only_once()
        {
            var session = OpenPluginsIni();
            Save(session, "Änderung " + _saved.Ticks);

            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _manager.Tick();
            Assert.Empty(_announced);

            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _manager.Tick();
            _manager.Tick();

            Assert.Same(session, Assert.Single(_announced));
            Assert.True(session.HasPendingChange);
            Assert.Equal(new[] { session }, _manager.PendingSessions.ToArray());
        }

        [Fact]
        public void A_burst_of_saves_is_one_announcement()
        {
            var session = OpenPluginsIni();
            Save(session, "Änderung " + _saved.Ticks);
            _clock.Advance(TimeSpan.FromMilliseconds(600));
            Save(session, "Änderung " + _saved.Ticks);

            _clock.Advance(TimeSpan.FromMilliseconds(600));
            _manager.Tick();
            Assert.Empty(_announced);

            _clock.Advance(TimeSpan.FromMilliseconds(500));
            _manager.Tick();
            Assert.Single(_announced);
        }

        [Fact]
        public void A_file_still_locked_by_the_editor_waits()
        {
            var session = OpenPluginsIni();
            Save(session, "Änderung " + _saved.Ticks);
            _clock.Advance(TimeSpan.FromSeconds(2));

            using (new FileStream(session.LocalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                _manager.Tick();
                Assert.Empty(_announced);
            }

            _manager.Tick();
            Assert.Single(_announced);
        }

        [Fact]
        public void Changes_to_unknown_files_are_ignored()
        {
            OpenPluginsIni();
            _changes.Raise(Path.Combine(_temp.Path, "open", "fremd.txt"));
            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();
            Assert.Empty(_announced);
        }

        [Fact]
        public async Task The_console_side_is_checked_before_writing_back()
        {
            var session = OpenPluginsIni();
            Assert.False(await _manager.RemoteChangedAsync(session, CancellationToken.None));

            _ftp.ChangeRemotely("/data/GoldHEN/plugins.ini", new byte[] { 1, 2, 3 });
            Assert.True(await _manager.RemoteChangedAsync(session, CancellationToken.None));

            await _ftp.DeleteAsync("/data/GoldHEN/plugins.ini", CancellationToken.None);
            Assert.True(await _manager.RemoteChangedAsync(session, CancellationToken.None));
        }

        [Fact]
        public async Task A_same_size_remote_change_with_unchanged_listing_metadata_is_detected()
        {
            var session = OpenPluginsIni();
            _ftp.ChangeRemotelyWithoutMetadata("/data/GoldHEN/plugins.ini", Encoding.UTF8.GetBytes("[changed]"));

            Assert.True(await _manager.RemoteChangedAsync(session, CancellationToken.None));
        }

        [Fact]
        public async Task An_approved_remote_version_becomes_the_write_back_identity()
        {
            var session = OpenPluginsIni();
            var changed = Encoding.UTF8.GetBytes("[changed]");
            _ftp.ChangeRemotelyWithoutMetadata("/data/GoldHEN/plugins.ini", changed);
            Assert.True(await _manager.RemoteChangedAsync(session, CancellationToken.None));
            _manager.AcceptObservedRemoteVersion(session);
            var step = new TransferStep(StepKind.UploadFile, session.RemotePath, session.LocalPath, null,
                new FileInfo(session.LocalPath).Length) { OverwriteWithoutAsking = true };
            var job = new TransferJob(TransferKind.Upload, "write back", new[] { step });

            _manager.ProtectWriteBack(job, session);

            Assert.True(step.TargetIdentityRequired);
            Assert.True(step.ExpectedTargetExists);
            using (var hash = SHA256.Create()) Assert.Equal(hash.ComputeHash(changed), step.ExpectedTargetHash);
        }

        [Fact]
        public void An_accepted_backup_snapshot_becomes_the_exact_write_back_identity()
        {
            var session = OpenPluginsIni();
            var backedUp = Encoding.UTF8.GetBytes("the bytes in the safety backup");
            var step = new TransferStep(StepKind.UploadFile, session.RemotePath, session.LocalPath, null,
                new FileInfo(session.LocalPath).Length) { OverwriteWithoutAsking = true };
            var job = new TransferJob(TransferKind.Upload, "write back", new[] { step });

            _manager.AcceptRemoteSnapshot(session, backedUp);
            _manager.ProtectWriteBack(job, session);

            Assert.True(step.ExpectedTargetExists);
            using (var hash = SHA256.Create()) Assert.Equal(hash.ComputeHash(backedUp), step.ExpectedTargetHash);
        }

        [Fact]
        public void A_backup_snapshot_is_compared_with_the_original_remote_baseline()
        {
            var session = OpenPluginsIni();

            Assert.False(_manager.RemoteSnapshotChanged(session, Encoding.UTF8.GetBytes("[default]")));
            Assert.True(_manager.RemoteSnapshotChanged(session, Encoding.UTF8.GetBytes("[changed]")));
        }

        [Fact]
        public async Task Completing_a_write_back_moves_the_baseline_and_clears_the_change()
        {
            var session = OpenPluginsIni();
            Save(session, "Änderung " + _saved.Ticks);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            var uploaded = File.ReadAllBytes(session.LocalPath);
            _ftp.ChangeRemotely("/data/GoldHEN/plugins.ini", uploaded);   // unser eigener Upload
            await _manager.CompleteWriteBackAsync(session, CompletedWriteBack(session, uploaded), CancellationToken.None);

            Assert.False(session.HasPendingChange);
            Assert.Equal(uploaded.Length, session.BaseSize);
            Assert.False(await _manager.RemoteChangedAsync(session, CancellationToken.None));
            Assert.Empty(_manager.PendingSessions);
        }

        [Fact]
        public async Task A_save_during_write_back_completion_remains_pending()
        {
            var session = OpenPluginsIni();
            Save(session, "uploaded version");
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();
            _announced.Clear();

            var uploaded = File.ReadAllBytes(session.LocalPath);
            _ftp.ChangeRemotely("/data/GoldHEN/plugins.ini", uploaded);
            _ftp.AfterList = _ =>
            {
                _ftp.AfterList = null;
                Save(session, "newer local version");
            };

            await _manager.CompleteWriteBackAsync(session, CompletedWriteBack(session, uploaded), CancellationToken.None);

            Assert.True(session.HasPendingChange);
            Assert.Equal(new[] { session }, _manager.PendingSessions.ToArray());
            Assert.False(await _manager.RemoteChangedAsync(session, CancellationToken.None));
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();
            Assert.Equal(new[] { session }, _announced.ToArray());
        }

        [Fact]
        public void An_integrated_editor_save_is_pending_without_triggering_the_external_editor_prompt()
        {
            var session = OpenPluginsIni();
            Save(session, "integrated editor");

            _manager.MarkLocalSavePending(session);
            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();

            Assert.True(session.HasPendingChange);
            Assert.Empty(_announced);
            Assert.Equal(new[] { session }, _manager.PendingSessions.ToArray());
        }

        [Fact]
        public void Discard_clears_the_change_but_keeps_watching()
        {
            var session = OpenPluginsIni();
            Save(session, "Änderung " + _saved.Ticks);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            _manager.Discard(session);
            Assert.False(session.HasPendingChange);

            Save(session, "Änderung " + _saved.Ticks);
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();
            Assert.Equal(2, _announced.Count);
        }

        [Fact]
        public void Each_file_gets_its_own_folder_and_a_windows_safe_name()
        {
            var first = _manager.PrepareLocalPath("/data/a:b.txt");
            var second = _manager.PrepareLocalPath("/data/a:b.txt");

            Assert.Equal("a_b.txt", Path.GetFileName(first));
            Assert.NotEqual(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
            Assert.True(Directory.Exists(Path.GetDirectoryName(first)));
            Assert.StartsWith(Path.Combine(_temp.Path, "open"), first);
        }

        [Fact]
        public void CleanWorkRoot_removes_the_leftovers_of_the_last_session()
        {
            var root = _temp.Sub("alt");
            _temp.File("alt/1a2b/x.txt", "x");

            EditSessionManager.CleanWorkRoot(root);

            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }

        [Fact]
        public void Only_files_above_200_mb_need_a_confirmation()
        {
            Assert.False(EditSessionManager.NeedsSizeConfirmation(200L * 1024 * 1024));
            Assert.True(EditSessionManager.NeedsSizeConfirmation(200L * 1024 * 1024 + 1));
        }
        [Fact]
        public void A_touch_without_a_real_change_is_not_announced()
        {
            var session = OpenPluginsIni();
            _changes.Raise(session.LocalPath);                 // z. B. das .part→Name-Umbenennen

            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();

            Assert.Empty(_announced);
            Assert.False(session.HasPendingChange);
        }

        [Fact]
        public void The_same_state_is_not_announced_twice()
        {
            var session = OpenPluginsIni();
            Save(session, "[geändert]");
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            _changes.Raise(session.LocalPath);                 // Ereignis ohne neuen Inhalt
            _clock.Advance(TimeSpan.FromSeconds(1));
            _manager.Tick();

            Assert.Single(_announced);
        }

        [Fact]
        public void A_deleted_working_copy_stops_waiting()
        {
            var session = OpenPluginsIni();
            Save(session, "[geändert]");
            File.Delete(session.LocalPath);

            _clock.Advance(TimeSpan.FromSeconds(2));
            _manager.Tick();
            File.WriteAllText(session.LocalPath, "[default]");   // Datei taucht wieder auf — kein Nachzügler
            _manager.Tick();

            Assert.Empty(_announced);
        }
    }
}
