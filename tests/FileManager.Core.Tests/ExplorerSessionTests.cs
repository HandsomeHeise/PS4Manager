using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Presentation;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;
using System.Collections.Generic;

namespace FileManager.Core.Tests
{
    public sealed class ExplorerSessionTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly TempDir _temp = new TempDir();
        private readonly ExplorerSession _session;

        public ExplorerSessionTests()
        {
            _ftp.AddFile("/data/mods/a.txt", "a").AddDir("/system_data");
            _session = Create(false);
        }

        private ExplorerSession Create(bool expert)
        {
            return new ExplorerSession(_ftp, expert, new RecordingPrompts(), new ScriptedResolver(),
                Path.Combine(_temp.Path, "open"), new FakeChangeSource(), new TestClock());
        }

        public void Dispose()
        {
            _session.Dispose();
            _temp.Dispose();
        }

        [Fact]
        public async Task Every_write_of_the_queue_goes_through_the_zone_guard()
        {
            var step = new TransferStep(StepKind.UploadFile, "/system_data/x", _temp.File("x", "x"), null, 1);
            var job = new TransferJob(TransferKind.Upload, "t", new[] { step });

            _session.Queue.Enqueue(job);
            await _session.Queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public void Expert_mode_is_the_policy_switch()
        {
            using (var expert = Create(true))
            {
                Assert.True(expert.Policy.ExpertMode);
                expert.ExpertMode = false;
                Assert.False(expert.Policy.ExpertMode);
            }
        }

        [Fact]
        public async Task A_finished_delete_invalidates_the_folder_and_the_subtree()
        {
            await _session.Cache.ListAsync("/data", CancellationToken.None);
            await _session.Cache.ListAsync("/data/mods", CancellationToken.None);
            var job = new TransferJob(TransferKind.Delete, "t", new[]
            {
                new TransferStep(StepKind.DeleteRemoteFile, "/data/mods/a.txt", null, null, 1),
                new TransferStep(StepKind.RemoveRemoteFolder, "/data/mods", null, null, 0)
            });

            var refresh = _session.ApplyFinished(job);

            IReadOnlyList<FtpEntry> cached;
            Assert.False(_session.Cache.TryGetCached("/data", out cached));
            Assert.False(_session.Cache.TryGetCached("/data/mods", out cached));
            Assert.Contains("/data", refresh);
        }

        [Fact]
        public async Task Both_spellings_of_user_data_are_invalidated()
        {
            _ftp.AddDir("/user/data/mods");      // der Fake kennt keinen Alias — hier nur als eigener Ordner für den Cache
            await _session.Cache.ListAsync("/user/data/mods", CancellationToken.None);
            var job = new TransferJob(TransferKind.CreateFolder, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods/neu", null, null, 0)
            });

            _session.ApplyFinished(job);

            IReadOnlyList<FtpEntry> cached;
            Assert.False(_session.Cache.TryGetCached("/user/data/mods", out cached));
        }

        [Fact]
        public async Task Finished_queue_writes_invalidate_cache_without_a_file_manager_observer()
        {
            await _session.Cache.ListAsync("/data/mods", CancellationToken.None);
            var local = _temp.File("changed.txt", "changed");
            var job = new TransferJob(TransferKind.Upload, "write plugins", new[]
            {
                new TransferStep(StepKind.UploadFile, "/data/mods/a.txt", local, null, new FileInfo(local).Length)
                {
                    OverwriteWithoutAsking = true
                }
            });

            _session.Queue.Enqueue(job);
            await _session.Queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            IReadOnlyList<FtpEntry> cached;
            Assert.False(_session.Cache.TryGetCached("/data/mods", out cached));
        }

        [Fact]
        public async Task Check_fails_loudly_without_a_console()
        {
            _ftp.Failure = (verb, path) => verb == "LIST"
                ? new FtpException("Keine Verbindung zur PS4 — läuft der FTP-Server von GoldHEN?", FtpErrorKind.Transient, 0) : null;

            var ex = await Assert.ThrowsAsync<FtpException>(() => _session.CheckAsync(CancellationToken.None));
            Assert.True(ex.IsTransient);
        }

        [Fact]
        public void All_parts_share_one_guarded_client()
        {
            Assert.NotNull(_session.Ftp);
            Assert.NotNull(_session.Browser);
            Assert.NotNull(_session.QuickAccess);
            Assert.NotNull(_session.Titles);
            Assert.NotNull(_session.Operations);
            Assert.NotNull(_session.Editing);
        }
    }
}
