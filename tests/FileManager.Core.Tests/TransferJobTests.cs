using System;
using System.Linq;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class TransferJobTests
    {
        private static TransferStep Upload(long size) { return new TransferStep(StepKind.UploadFile, "/data/x", "C:\\x", null, size); }

        [Fact]
        public void Bytes_count_only_transfer_steps()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/d", null, null, 0),
                Upload(1000), Upload(3000)
            });
            job.Steps[1].State = StepState.Done;
            job.Steps[2].Transferred = 500;

            Assert.Equal(4000, job.TotalBytes);
            Assert.Equal(1500, job.DoneBytes);
        }

        [Fact]
        public void The_remaining_time_follows_the_rate()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[] { Upload(10000) });
            Assert.Null(job.Remaining);
            job.Steps[0].Transferred = 4000;
            job.BytesPerSecond = 2000;
            Assert.Equal(TimeSpan.FromSeconds(3), job.Remaining);
        }

        [Fact]
        public void The_summary_counts_items_and_names_failures_and_skips()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/d", null, null, 0),
                Upload(1), Upload(1), Upload(1), Upload(1)
            });
            job.Steps[1].State = StepState.Done;
            job.Steps[2].State = StepState.Done;
            job.Steps[3].State = StepState.Failed;
            job.Steps[4].State = StepState.Skipped;

            Assert.Equal("2 of 4 transferred, 1 failed, 1 skipped", job.Summary);
            Assert.Single(job.Failures);
        }

        [Fact]
        public void Deleting_speaks_of_deleted_files_and_ignores_folder_steps()
        {
            var job = new TransferJob(TransferKind.Delete, "t", new[]
            {
                new TransferStep(StepKind.DeleteRemoteFile, "/data/a", null, null, 1),
                new TransferStep(StepKind.RemoveRemoteFolder, "/data", null, null, 0)
            });
            job.Steps[0].State = StepState.Done;
            job.Steps[1].State = StepState.Done;

            Assert.Equal("1 of 1 deleted", job.Summary);
        }

        [Fact]
        public void A_job_of_only_folder_steps_counts_those()
        {
            var job = new TransferJob(TransferKind.CreateFolder, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/Neuer Ordner", null, null, 0)
            });
            job.Steps[0].State = StepState.Done;
            Assert.Equal("1 of 1 completed", job.Summary);
        }

        [Fact]
        public void A_job_names_the_listings_it_makes_stale()
        {
            var upload = new TransferJob(TransferKind.Upload, "t", new[]
            {
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods", null, null, 0),
                new TransferStep(StepKind.UploadFile, "/data/mods/a.txt", "C:\\a.txt", null, 1)
            });
            Assert.Equal(new[] { "/data", "/data/mods" }, upload.RemoteFoldersToRefresh.OrderBy(p => p).ToArray());
            Assert.Empty(upload.RemoteSubtreesGone);

            var move = new TransferJob(TransferKind.Move, "t", new[]
            {
                new TransferStep(StepKind.RenameRemote, "/data/a", null, "/data/b/a", 0) { IsDirectory = true }
            });
            Assert.Equal(new[] { "/data", "/data/b" }, move.RemoteFoldersToRefresh.OrderBy(p => p).ToArray());
            Assert.Equal(new[] { "/data/a" }, move.RemoteSubtreesGone.ToArray());

            var download = new TransferJob(TransferKind.Download, "t", new[]
            {
                new TransferStep(StepKind.DownloadFile, "/data/x", "C:\\x", null, 1)
            });
            Assert.Empty(download.RemoteFoldersToRefresh);
        }

        [Fact]
        public void Finished_means_completed_or_cancelled_but_not_interrupted()
        {
            var job = new TransferJob(TransferKind.Upload, "t", new[] { Upload(1) });
            Assert.False(job.IsFinished);
            job.State = JobState.Interrupted;
            Assert.False(job.IsFinished);
            job.State = JobState.CompletedWithErrors;
            Assert.True(job.IsFinished);
            job.State = JobState.Cancelled;
            Assert.True(job.IsFinished);
        }
    }
}
