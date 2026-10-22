using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Media;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;
using PS4Manager.Gallery.Core.Ps4;
using Xunit;

namespace PS4Manager.Core.Tests.Media
{
    public sealed class MediaTransferPlannerTests : IDisposable
    {
        private static readonly MediaItem Photo = new MediaItem(
            MediaKind.Photo, "CUSA00001", "photo-hash", "20260924_120000_photo.jpg", 1234, DateTime.MinValue);

        private static readonly MediaItem Video = new MediaItem(
            MediaKind.Video, "CUSA00002", "video-hash", "20260924_130000_video.mp4", 5678, DateTime.MinValue);

        private readonly string _temp = Path.Combine(
            Path.GetTempPath(), "media-transfer-planner-" + Guid.NewGuid().ToString("N"));
        private readonly RecordingFtpClient _ftp = new RecordingFtpClient();
        private readonly ConfirmingPrompts _prompts = new ConfirmingPrompts();
        private readonly ManagerSession _session;
        private readonly MediaTransferPlanner _planner;

        public MediaTransferPlannerTests()
        {
            Directory.CreateDirectory(_temp);
            var explorer = new ExplorerSession(
                _ftp,
                false,
                _prompts,
                new NoConflictResolver(),
                Path.Combine(_temp, "work"),
                new NoChanges(),
                SystemClock.Instance);
            _session = new ManagerSession(new ManagerEndpoint("ps4", 2121), explorer);
            _planner = new MediaTransferPlanner(_session.Explorer.Operations);
        }

        [Fact]
        public async Task Delete_plans_primary_media_dat_and_thumbnail_in_safe_order()
        {
            var job = await _planner.PlanDeleteAsync(new[] { Video }, CancellationToken.None);

            Assert.Equal(new[]
            {
                Ps4Paths.MediaPath(Video),
                Ps4Paths.DatPath(Video),
                Ps4Paths.ThumbnailPath(Video)
            }, job.Steps.Select(step => step.RemotePath));
            Assert.False(job.Steps[0].MissingIsSuccess);
            Assert.True(job.Steps[1].MissingIsSuccess);
            Assert.True(job.Steps[2].MissingIsSuccess);
        }

        [Fact]
        public void Downloads_are_transfer_jobs_visible_in_the_shared_queue()
        {
            var job = _planner.PlanDownload(new[] { Photo }, _temp);

            _session.Queue.Enqueue(job);

            Assert.Contains(job, _session.Queue.Jobs);
            Assert.Equal(StepKind.DownloadFile, Assert.Single(job.Steps).Kind);
            Assert.Equal(Ps4Paths.MediaPath(Photo), job.Steps[0].RemotePath);
        }

        [Fact]
        public async Task Ftp_550_is_ignored_only_for_optional_companion_deletes()
        {
            _ftp.DeleteFailure = new FtpException("missing", FtpErrorKind.Permanent, 550);
            var job = await _planner.PlanDeleteAsync(new[] { Video }, CancellationToken.None);

            _session.Queue.Enqueue(job);
            await _session.Queue.WhenIdle();

            Assert.Equal(StepState.Failed, job.Steps[0].State);
            Assert.Equal(StepState.Skipped, job.Steps[1].State);
            Assert.Equal(StepState.Skipped, job.Steps[2].State);
            Assert.Equal(JobState.CompletedWithErrors, job.State);
        }

        [Fact]
        public async Task Delete_confirmation_counts_selected_media_not_internal_companions()
        {
            await _planner.PlanDeleteAsync(new[] { Video }, CancellationToken.None);

            Assert.Equal(1, Assert.Single(_prompts.DeleteSummaries).FileCount);
        }

        public void Dispose()
        {
            _session.Dispose();
            try { Directory.Delete(_temp, true); } catch (Exception) { }
        }

        private sealed class ConfirmingPrompts : IUserPrompts
        {
            public readonly List<DeleteSummary> DeleteSummaries = new List<DeleteSummary>();

            public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
            {
                return Task.FromResult(true);
            }

            public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
            {
                DeleteSummaries.Add(summary);
                return Task.FromResult(true);
            }
        }

        private sealed class NoConflictResolver : IConflictResolver
        {
            public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
            {
                return Task.FromResult(new ConflictDecision(ConflictChoice.Skip, false));
            }
        }

        private sealed class NoChanges : IFileChangeSource
        {
            public event EventHandler<string> Changed { add { } remove { } }
            public void Watch(string directory) { }
            public void Dispose() { }
        }

        private sealed class RecordingFtpClient : IFtpClient
        {
            public Exception DeleteFailure;

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                return Task.FromResult((IReadOnlyList<FtpEntry>)new FtpEntry[0]);
            }

            public Task DeleteAsync(string path, CancellationToken ct)
            {
                if (DeleteFailure != null) throw DeleteFailure;
                return Task.CompletedTask;
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { throw new NotSupportedException(); }
        }
    }
}
