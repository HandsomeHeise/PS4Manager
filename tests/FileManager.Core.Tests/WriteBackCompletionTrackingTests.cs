using System;
using System.IO;
using System.Threading.Tasks;
using FileManager.Core.Presentation;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class WriteBackCompletionTrackingTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly TempDir _temp = new TempDir();
        private readonly ExplorerSession _session;

        public WriteBackCompletionTrackingTests()
        {
            _ftp.AddFile("/data/plugins.ini", "original");
            _session = new ExplorerSession(
                _ftp,
                false,
                new RecordingPrompts(),
                new ScriptedResolver(),
                Path.Combine(_temp.Path, "open"),
                new FakeChangeSource(),
                new TestClock());
        }

        [Fact]
        public async Task Completed_write_back_is_finalized_after_tool_observer_detaches()
        {
            var local = _temp.File("plugins.ini", "original");
            var edit = _session.Editing.Register(
                "/data/plugins.ini",
                local,
                new FileInfo(local).Length,
                _ftp.Modified("/data/plugins.ini"));
            File.WriteAllText(local, "changed");
            _session.Editing.MarkLocalSavePending(edit);

            var step = new TransferStep(
                StepKind.UploadFile,
                edit.RemotePath,
                edit.LocalPath,
                null,
                new FileInfo(edit.LocalPath).Length)
            {
                OverwriteWithoutAsking = true
            };
            var job = new TransferJob(TransferKind.Upload, "write back", new[] { step });
            _session.Editing.ProtectWriteBack(job, edit);
            var completion = _session.Editing.TrackWriteBackAsync(job, edit);

            var listGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ftp.ListGate = listGate;
            EventHandler<TransferJob> toolObserver = (sender, changed) => { };
            _session.Queue.JobChanged += toolObserver;
            _session.Queue.Enqueue(job);
            await WaitUntilAsync(() => job.State == JobState.Running);

            _session.Queue.JobChanged -= toolObserver;
            _ftp.ListGate = null;
            listGate.SetResult(true);

            Assert.Equal(JobState.Completed, await completion);
            await _session.Queue.WhenIdle();
            Assert.False(edit.HasPendingChange);
            Assert.Empty(_session.Editing.PendingSessions);
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("The transfer did not start.");
                await Task.Delay(10);
            }
        }

        public void Dispose()
        {
            _session.Dispose();
            _temp.Dispose();
        }
    }
}
