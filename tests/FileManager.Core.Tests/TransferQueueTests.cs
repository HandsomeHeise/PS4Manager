using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class TransferQueueTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly ScriptedResolver _resolver = new ScriptedResolver();
        private readonly TempDir _temp = new TempDir();
        private readonly TransferQueue _queue;

        public TransferQueueTests()
        {
            _ftp.AddDir("/data");
            _queue = new TransferQueue(new ZoneGuardFtpClient(_ftp, new ZonePolicy()), new FtpGates(), _resolver, new TestClock());
        }

        public void Dispose()
        {
            _temp.Dispose();
        }

        private static byte[] Pattern(int length)
        {
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        private string LocalBytes(string name, byte[] data)
        {
            var path = Path.Combine(_temp.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            return path;
        }

        private static TransferStep Upload(string local, string remote)
        {
            return new TransferStep(StepKind.UploadFile, remote, local, null, new FileInfo(local).Length);
        }

        private async Task<TransferJob> Run(TransferKind kind, params TransferStep[] steps)
        {
            var job = new TransferJob(kind, "Test", steps);
            _queue.Enqueue(job);
            await _queue.WhenIdle();
            return job;
        }

        [Theory]
        [InlineData("/data", "a.txt.part")]
        [InlineData("/mnt/usb0", "A.TXT.PART")]
        public async Task An_upload_preserves_an_existing_partial_file(string directory, string existingName)
        {
            var existingPath = directory + "/" + existingName;
            _ftp.AddFile(existingPath, "fremde Daten");

            var step = Upload(_temp.File("a.txt", "neu"), directory + "/a.txt");
            var job = await Run(TransferKind.Upload, step);

            Assert.Equal(Encoding.UTF8.GetBytes("fremde Daten"), _ftp.File(existingPath));
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File(directory + "/a.txt")));
            Assert.Contains("STOR " + step.RemotePartPath, _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task An_upload_creates_the_folder_and_stores_the_file()
        {
            var a = _temp.File("mods/a.txt", "aaa");

            var job = await Run(TransferKind.Upload,
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods", null, null, 0),
                Upload(a, "/data/mods/a.txt"));

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/mods/a.txt")));
            Assert.Equal("1 of 1 transferred", job.Summary);
            Assert.Equal(3, job.DoneBytes);
        }

        [Fact]
        public async Task An_existing_folder_is_merged_not_recreated()
        {
            _ftp.AddDir("/data/mods");
            var a = _temp.File("mods/a.txt", "aaa");

            await Run(TransferKind.Upload,
                new TransferStep(StepKind.CreateRemoteFolder, "/data/mods", null, null, 0),
                Upload(a, "/data/mods/a.txt"));

            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("MKD"));
        }

        [Fact]
        public async Task A_failing_file_is_noted_and_the_job_goes_on()
        {
            int stores = 0;
            _ftp.Failure = (verb, path) => verb == "STOR" && ++stores == 2
                ? new FtpException("Name nicht erlaubt", FtpErrorKind.Permanent, 553) : null;

            var job = await Run(TransferKind.Upload,
                Upload(_temp.File("a.txt", "a"), "/data/a.txt"),
                Upload(_temp.File("b.txt", "b"), "/data/b.txt"),
                Upload(_temp.File("c.txt", "c"), "/data/c.txt"));

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.NotNull(_ftp.File("/data/c.txt"));
            var failure = Assert.Single(job.Failures);
            Assert.Equal("Name nicht erlaubt", failure.Error);
            Assert.Equal("2 of 3 transferred, 1 failed", job.Summary);
        }

        [Fact]
        public async Task A_connection_loss_interrupts_and_resume_appends_the_rest()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;

            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);
            var part = step.RemotePartPath;

            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal("Verbindung zur PS4 unterbrochen", job.InterruptReason);
            Assert.Equal(100000, _ftp.File(part).Length);
            Assert.Null(_ftp.File("/data/big.bin"));             // der echte Name bleibt unberührt

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(data, _ftp.File("/data/big.bin"));
            Assert.Null(_ftp.File(part));
            Assert.Contains("SIZE " + part, _ftp.CallsSnapshot());
            Assert.Contains("APPE " + part + "@100000", _ftp.CallsSnapshot());
            Assert.Contains("RNFR " + part + " -> /data/big.bin", _ftp.CallsSnapshot());
            Assert.Empty(_resolver.Asked);                      // die Teildatei ist unsere — kein Konflikt
        }

        [Fact]
        public async Task Resuming_refuses_a_local_source_changed_since_the_interruption()
        {
            var original = Enumerable.Repeat((byte)'A', 300000).ToArray();
            var big = LocalBytes("big.bin", original);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);
            var originalWriteTime = File.GetLastWriteTimeUtc(big);

            File.WriteAllBytes(big, Enumerable.Repeat((byte)'B', original.Length).ToArray());
            File.SetLastWriteTimeUtc(big, originalWriteTime);     // Metadaten allein dürfen nicht reichen.
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Equal(original.Take(100000).ToArray(), _ftp.File(step.RemotePartPath));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE") || c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task Resuming_refuses_a_local_source_changed_after_the_uploaded_prefix()
        {
            var original = Pattern(300000);
            var big = LocalBytes("big.bin", original);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);
            var originalWriteTime = File.GetLastWriteTimeUtc(big);

            var changed = original.ToArray();
            changed[200000] ^= 0xFF;
            File.WriteAllBytes(big, changed);
            File.SetLastWriteTimeUtc(big, originalWriteTime);
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Equal(original.Take(100000).ToArray(), _ftp.File(step.RemotePartPath));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE") || c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task Resuming_refuses_a_partial_file_whose_size_changed()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);

            var foreign = Enumerable.Repeat((byte)0x7B, data.Length).ToArray();
            _ftp.AddFile(step.RemotePartPath, foreign);
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Equal(foreign, _ftp.File(step.RemotePartPath));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE") || c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task Resuming_refuses_a_same_size_partial_with_different_content()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);

            var foreign = Enumerable.Repeat((byte)0x7B, 100000).ToArray();
            _ftp.AddFile(step.RemotePartPath, foreign);
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Equal(foreign, _ftp.File(step.RemotePartPath));
        }

        [Fact]
        public async Task Resume_uses_the_server_size_when_it_differs_from_the_last_progress_report()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);

            _ftp.AddFile(step.RemotePartPath, data.Take(90000).ToArray());
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(data, _ftp.File("/data/big.bin"));
            Assert.Contains("APPE " + step.RemotePartPath + "@90000", _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task Cancelling_preserves_a_partial_file_whose_size_changed()
        {
            var big = LocalBytes("big.bin", Pattern(300000));
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);
            var foreign = Enumerable.Repeat((byte)0x7B, 300000).ToArray();
            _ftp.AddFile(step.RemotePartPath, foreign);

            await _queue.CancelAsync(job);

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.Equal(foreign, _ftp.File(step.RemotePartPath));
            Assert.DoesNotContain("DELE " + step.RemotePartPath, _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task Cancelling_preserves_a_same_size_partial_with_different_content()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);
            var foreign = Enumerable.Repeat((byte)0x7B, 100000).ToArray();
            _ftp.AddFile(step.RemotePartPath, foreign);

            await _queue.CancelAsync(job);

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.Equal(foreign, _ftp.File(step.RemotePartPath));
        }

        [Fact]
        public async Task An_upload_refuses_a_final_target_that_appeared_during_transfer()
        {
            var appeared = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "STOR" && !appeared)
                {
                    appeared = true;
                    _ftp.AddFile("/data/a.txt", "fremde Daten");
                }
                return null;
            };

            var step = Upload(_temp.File("a.txt", "neue Daten"), "/data/a.txt");
            var job = await Run(TransferKind.Upload, step);

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("fremde Daten", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Equal("neue Daten", Encoding.UTF8.GetString(_ftp.File(step.RemotePartPath)));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task Moving_refuses_a_target_that_appeared_after_the_conflict_listing()
        {
            _ftp.AddDir("/data/ziel");
            _ftp.AddFile("/data/a.txt", "quelle");
            bool added = false;
            _ftp.AfterList = path =>
            {
                if (path == "/data/ziel" && !added)
                {
                    added = true;
                    _ftp.AddFile("/data/ziel/a.txt", "fremde Daten");
                }
            };

            var job = await Run(TransferKind.Move,
                new TransferStep(StepKind.RenameRemote, "/data/a.txt", null, "/data/ziel/a.txt", 6));

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("quelle", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Equal("fremde Daten", Encoding.UTF8.GetString(_ftp.File("/data/ziel/a.txt")));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task Without_append_a_resumed_upload_starts_over()
        {
            _queue.AppendResumesUploads = false;
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var step = Upload(big, "/data/big.bin");
            var job = await Run(TransferKind.Upload, step);

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(data, _ftp.File("/data/big.bin"));
            Assert.Equal(2, _ftp.CallsSnapshot().Count(c => c == "STOR " + step.RemotePartPath));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task A_download_resumes_from_the_part_file()
        {
            var data = Pattern(300000);
            _ftp.AddFile("/data/big.bin", data);
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;

            var step = new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, data.Length);
            var job = await Run(TransferKind.Download, step);

            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal(100000, new FileInfo(step.LocalPartPath).Length);
            Assert.False(File.Exists(target));

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(data, File.ReadAllBytes(target));
            Assert.False(File.Exists(step.LocalPartPath));
            Assert.Contains("RETR /data/big.bin@100000", _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task A_zero_byte_file_download_creates_an_empty_local_file()
        {
            _ftp.AddFile("/data/plugins.ini", new byte[0]);
            var target = Path.Combine(_temp.Path, "plugins.ini");

            var job = await Run(TransferKind.Download,
                new TransferStep(StepKind.DownloadFile, "/data/plugins.ini", target, null, 0));

            Assert.Equal(JobState.Completed, job.State);
            Assert.True(File.Exists(target));
            Assert.Equal(0, new FileInfo(target).Length);
            Assert.Empty(job.Failures);
        }

        [Fact]
        public async Task A_download_resume_refuses_a_changed_remote_prefix()
        {
            var original = Enumerable.Repeat((byte)'A', 300000).ToArray();
            _ftp.AddFile("/data/big.bin", original);
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;
            var step = new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, original.Length);
            var job = await Run(TransferKind.Download, step);

            _ftp.ChangeRemotely("/data/big.bin", Enumerable.Repeat((byte)'B', original.Length).ToArray());
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.False(File.Exists(target));
            Assert.Equal(original.Take(100000).ToArray(), File.ReadAllBytes(step.LocalPartPath));
        }

        [Fact]
        public async Task A_download_resume_refuses_a_remote_change_after_the_downloaded_prefix()
        {
            var original = Pattern(300000);
            _ftp.AddFile("/data/big.bin", original);
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;
            var step = new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, original.Length);
            var job = await Run(TransferKind.Download, step);

            var changed = original.ToArray();
            changed[200000] ^= 0xFF;
            _ftp.ChangeRemotelyWithoutMetadata("/data/big.bin", changed);
            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.False(File.Exists(target));
            Assert.Equal(original.Take(100000).ToArray(), File.ReadAllBytes(step.LocalPartPath));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c == "RETR /data/big.bin@100000");
        }

        [Fact]
        public async Task Write_back_refuses_a_target_changed_during_the_upload()
        {
            var original = Encoding.UTF8.GetBytes("old");
            _ftp.AddFile("/data/plugins.ini", original);
            var appeared = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "STOR" && !appeared)
                {
                    appeared = true;
                    _ftp.ChangeRemotelyWithoutMetadata("/data/plugins.ini", Encoding.UTF8.GetBytes("foreign"));
                }
                return null;
            };
            var step = Upload(_temp.File("plugins.ini", "new"), "/data/plugins.ini");
            step.OverwriteWithoutAsking = true;
            step.TargetIdentityRequired = true;
            step.ExpectedTargetExists = true;
            using (var hash = SHA256.Create()) step.ExpectedTargetHash = hash.ComputeHash(original);

            var job = await Run(TransferKind.Upload, step);

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("foreign", Encoding.UTF8.GetString(_ftp.File("/data/plugins.ini")));
            Assert.Equal("new", Encoding.UTF8.GetString(_ftp.File(step.RemotePartPath)));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("RNFR"));
        }

        [Fact]
        public async Task A_download_resume_refuses_a_replaced_local_partial()
        {
            var data = Pattern(300000);
            _ftp.AddFile("/data/big.bin", data);
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;
            var step = new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, data.Length);
            var job = await Run(TransferKind.Download, step);
            var foreign = Enumerable.Repeat((byte)0x7B, 100000).ToArray();
            File.WriteAllBytes(step.LocalPartPath, foreign);

            _ftp.InterruptAfterBytes = 0;
            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.False(File.Exists(target));
            Assert.Equal(foreign, File.ReadAllBytes(step.LocalPartPath));
        }

        [Fact]
        public async Task A_download_preserves_an_unrelated_existing_part_file()
        {
            _ftp.AddFile("/data/a.txt", "remote");
            var target = Path.Combine(_temp.Path, "a.txt");
            File.WriteAllText(target + ".part", "fremde Daten");

            var step = new TransferStep(StepKind.DownloadFile, "/data/a.txt", target, null, 6);
            var job = await Run(TransferKind.Download, step);

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("remote", File.ReadAllText(target));
            Assert.Equal("fremde Daten", File.ReadAllText(target + ".part"));
        }

        [Fact]
        public async Task A_download_preserves_a_final_target_that_appeared_during_transfer()
        {
            _ftp.AddFile("/data/a.txt", "remote");
            var target = Path.Combine(_temp.Path, "a.txt");
            bool appeared = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "RETR" && !appeared)
                {
                    appeared = true;
                    File.WriteAllText(target, "fremde Daten");
                }
                return null;
            };

            var step = new TransferStep(StepKind.DownloadFile, "/data/a.txt", target, null, 6);
            var job = await Run(TransferKind.Download, step);

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("fremde Daten", File.ReadAllText(target));
            Assert.True(File.Exists(step.LocalPartPath));
        }

        [Fact]
        public async Task Cancelling_a_running_upload_deletes_the_half_file()
        {
            var big = LocalBytes("big.bin", Pattern(3000000));
            var step = Upload(big, "/data/big.bin");
            var job = new TransferJob(TransferKind.Upload, "Test", new[] { step });
            _queue.JobChanged += (s, j) =>
            {
                if (j.State == JobState.Running && j.DoneBytes > 0) _queue.CancelAsync(j);
            };

            _queue.Enqueue(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Null(_ftp.File(step.RemotePartPath));
            Assert.Contains("DELE " + step.RemotePartPath, _ftp.CallsSnapshot());
        }

        [Fact]
        public async Task Cancelling_an_interrupted_download_deletes_the_part_file()
        {
            _ftp.AddFile("/data/big.bin", Pattern(300000));
            var target = Path.Combine(_temp.Path, "big.bin");
            _ftp.InterruptAfterBytes = 100000;
            var step = new TransferStep(StepKind.DownloadFile, "/data/big.bin", target, null, 300000);
            var job = await Run(TransferKind.Download, step);

            await _queue.CancelAsync(job);

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.False(File.Exists(step.LocalPartPath));
        }

        [Fact]
        public async Task A_conflict_asks_and_keep_both_picks_a_free_name()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Then(ConflictChoice.KeepBoth);

            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "neu!"), "/data/a.txt"));

            var asked = Assert.Single(_resolver.Asked);
            Assert.Equal("a (2).txt", asked.KeepBothName);
            Assert.Equal(3, asked.ExistingSize);
            Assert.Equal(4, asked.NewSize);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Equal("neu!", Encoding.UTF8.GetString(_ftp.File("/data/a (2).txt")));
        }

        [Fact]
        public async Task Apply_to_all_asks_only_once()
        {
            _ftp.AddFile("/data/a.txt", "x").AddFile("/data/b.txt", "y");
            _resolver.Then(ConflictChoice.Skip, applyToAll: true);

            var job = await Run(TransferKind.Upload,
                Upload(_temp.File("a.txt", "1"), "/data/a.txt"),
                Upload(_temp.File("b.txt", "2"), "/data/b.txt"));

            Assert.Single(_resolver.Asked);
            Assert.Equal("0 of 2 transferred, 2 skipped", job.Summary);
            Assert.Equal("x", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
        }

        [Fact]
        public async Task Replace_overwrites()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Then(ConflictChoice.Replace);

            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "neu"), "/data/a.txt"));

            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
        }

        [Fact]
        public async Task A_protected_upload_refuses_to_replace_bytes_other_than_the_backup_snapshot()
        {
            var original = Encoding.UTF8.GetBytes("gesichert");
            _ftp.AddFile("/data/a.txt", original);
            _resolver.Then(ConflictChoice.Replace);
            var step = Upload(_temp.File("a.txt", "neu"), "/data/a.txt");
            var job = new TransferJob(TransferKind.Upload, "Test", new[] { step });
            job.ProtectRemoteFile("/data/a.txt", original);
            _ftp.AddFile("/data/a.txt", "inzwischen geändert");

            _queue.Enqueue(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("inzwischen geändert", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
        }

        [Fact]
        public async Task A_protected_upload_can_keep_both_without_requiring_the_old_identity_at_the_new_name()
        {
            var original = Encoding.UTF8.GetBytes("alt");
            _ftp.AddFile("/data/a.txt", original);
            _resolver.Then(ConflictChoice.KeepBoth);
            var step = Upload(_temp.File("a.txt", "neu"), "/data/a.txt");
            var job = new TransferJob(TransferKind.Upload, "Test", new[] { step });
            job.ProtectRemoteFile("/data/a.txt", original);

            _queue.Enqueue(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/a (2).txt")));
        }

        [Fact]
        public async Task A_protected_absent_upload_refuses_a_target_that_appears_before_execution()
        {
            var step = Upload(_temp.File("a.txt", "neu"), "/data/a.txt");
            var job = new TransferJob(TransferKind.Upload, "Test", new[] { step });
            job.ProtectRemoteAbsence("/data/a.txt");
            _ftp.AddFile("/data/a.txt", "fremd");

            _queue.Enqueue(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("fremd", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Empty(_resolver.Asked);
        }

        [Fact]
        public async Task A_protected_delete_refuses_to_remove_bytes_other_than_the_backup_snapshot()
        {
            var original = Encoding.UTF8.GetBytes("gesichert");
            _ftp.AddFile("/data/a.txt", original);
            var step = new TransferStep(StepKind.DeleteRemoteFile, "/data/a.txt", null, null, original.Length);
            var job = new TransferJob(TransferKind.Delete, "Test", new[] { step });
            job.ProtectRemoteFile("/data/a.txt", original);
            _ftp.AddFile("/data/a.txt", "inzwischen geändert");

            _queue.Enqueue(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Equal("inzwischen geändert", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
        }

        [Fact]
        public async Task Write_back_overwrites_without_asking()
        {
            _ftp.AddFile("/data/plugins.ini", "alt");
            var step = Upload(_temp.File("plugins.ini", "neu"), "/data/plugins.ini");
            step.OverwriteWithoutAsking = true;

            await Run(TransferKind.Upload, step);

            Assert.Empty(_resolver.Asked);
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/plugins.ini")));
        }

        [Fact]
        public async Task A_local_conflict_on_download_can_keep_both()
        {
            _ftp.AddFile("/data/x.txt", "remote");
            var target = _temp.File("x.txt", "lokal");
            _resolver.Then(ConflictChoice.KeepBoth);

            await Run(TransferKind.Download, new TransferStep(StepKind.DownloadFile, "/data/x.txt", target, null, 6));

            Assert.Equal("lokal", File.ReadAllText(target));
            Assert.Equal("remote", File.ReadAllText(Path.Combine(_temp.Path, "x (2).txt")));
        }

        [Fact]
        public async Task Moving_onto_an_existing_file_can_replace_it()
        {
            _ftp.AddFile("/data/a.txt", "neu").AddFile("/data/ziel/a.txt", "alt");
            _resolver.Then(ConflictChoice.Replace);

            await Run(TransferKind.Move, new TransferStep(StepKind.RenameRemote, "/data/a.txt", null, "/data/ziel/a.txt", 3));

            Assert.Single(_resolver.Asked);
            Assert.Null(_ftp.File("/data/a.txt"));
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/ziel/a.txt")));
            Assert.DoesNotContain("DELE /data/ziel/a.txt", _ftp.CallsSnapshot());   // RNTO überschreibt selbst
        }

        [Fact]
        public async Task Moving_onto_an_existing_file_can_skip()
        {
            _ftp.AddFile("/data/a.txt", "neu").AddFile("/data/ziel/a.txt", "alt");
            _resolver.Then(ConflictChoice.Skip);

            var job = await Run(TransferKind.Move,
                new TransferStep(StepKind.RenameRemote, "/data/a.txt", null, "/data/ziel/a.txt", 3));

            Assert.Single(_resolver.Asked);
            Assert.Equal(StepState.Skipped, job.Steps[0].State);
            Assert.NotNull(_ftp.File("/data/a.txt"));
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/ziel/a.txt")));
        }

        [Fact]
        public async Task Moving_a_folder_onto_an_existing_name_fails_the_step()
        {
            _ftp.AddDir("/data/mods").AddDir("/data/ziel/mods");
            var step = new TransferStep(StepKind.RenameRemote, "/data/mods", null, "/data/ziel/mods", 0) { IsDirectory = true };

            var job = await Run(TransferKind.Move, step);

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.Contains("already exists", step.Error);
            Assert.Empty(_resolver.Asked);
        }

        [Fact]
        public async Task Delete_steps_run_in_order()
        {
            _ftp.AddFile("/data/alt/a.txt", "a");

            var job = await Run(TransferKind.Delete,
                new TransferStep(StepKind.DeleteRemoteFile, "/data/alt/a.txt", null, null, 1),
                new TransferStep(StepKind.RemoveRemoteFolder, "/data/alt", null, null, 0));

            Assert.Equal(JobState.Completed, job.State);
            Assert.False(_ftp.HasDir("/data/alt"));
        }

        [Fact]
        public async Task A_zone_violation_during_execution_is_a_failed_step()
        {
            var guarded = new TransferQueue(new ZoneGuardFtpClient(_ftp, new ZonePolicy()), new FtpGates(), _resolver, new TestClock());
            _ftp.AddDir("/system_data");
            var job = new TransferJob(TransferKind.Upload, "t", new[] { Upload(_temp.File("x", "x"), "/system_data/x") });

            guarded.Enqueue(job);
            await guarded.WhenIdle();

            Assert.Equal(JobState.CompletedWithErrors, job.State);
            Assert.StartsWith("Locked area", job.Steps[0].Error);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public async Task Jobs_run_one_after_another_and_a_cancelled_waiting_job_is_skipped()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Hold = new TaskCompletionSource<bool>();
            _resolver.Then(ConflictChoice.Replace);
            var first = new TransferJob(TransferKind.Upload, "1", new[] { Upload(_temp.File("a.txt", "neu"), "/data/a.txt") });
            var second = new TransferJob(TransferKind.Upload, "2", new[] { Upload(_temp.File("b.txt", "b"), "/data/b.txt") });
            var third = new TransferJob(TransferKind.Upload, "3", new[] { Upload(_temp.File("c.txt", "c"), "/data/c.txt") });

            _queue.Enqueue(first);
            _queue.Enqueue(second);
            _queue.Enqueue(third);
            await _queue.CancelAsync(second);
            _resolver.Hold.SetResult(true);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, first.State);
            Assert.Equal(JobState.Cancelled, second.State);
            Assert.Equal(JobState.Completed, third.State);
            Assert.Null(_ftp.File("/data/b.txt"));
            var stores = _ftp.CallsSnapshot().Where(c => c.StartsWith("STOR")).ToArray();
            Assert.Equal(new[] { "STOR " + first.Steps[0].RemotePartPath, "STOR " + third.Steps[0].RemotePartPath }, stores);
        }

        [Fact]
        public async Task ClearFinished_keeps_interrupted_jobs()
        {
            _ftp.InterruptAfterBytes = 10;
            var interrupted = await Run(TransferKind.Upload, Upload(LocalBytes("x.bin", Pattern(100)), "/data/x.bin"));
            _ftp.InterruptAfterBytes = 0;
            var done = await Run(TransferKind.Upload, Upload(_temp.File("y.txt", "y"), "/data/y.txt"));

            _queue.ClearFinished();

            Assert.Equal(new[] { interrupted }, _queue.Jobs.ToArray());
            Assert.Equal(JobState.Completed, done.State);
        }

        // --- Review Runde 1 ---------------------------------------------

        [Fact]
        public async Task A_throwing_JobChanged_handler_does_not_kill_the_worker()
        {
            _queue.JobChanged += (s, j) => throw new InvalidOperationException("Absichtlich kaputter Abonnent.");
            var first = new TransferJob(TransferKind.Upload, "1", new[] { Upload(_temp.File("a.txt", "a"), "/data/a.txt") });
            var second = new TransferJob(TransferKind.Upload, "2", new[] { Upload(_temp.File("b.txt", "b"), "/data/b.txt") });

            _queue.Enqueue(first);
            _queue.Enqueue(second);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, first.State);
            Assert.Equal(JobState.Completed, second.State);
        }

        [Fact]
        public async Task Cancelling_a_resumed_job_waiting_behind_another_discards_its_partial_upload()
        {
            var data = Pattern(300000);
            var big = LocalBytes("big.bin", data);
            _ftp.InterruptAfterBytes = 100000;
            var stepA = Upload(big, "/data/big.bin");
            var jobA = await Run(TransferKind.Upload, stepA);
            Assert.Equal(JobState.Interrupted, jobA.State);
            Assert.Equal(100000, _ftp.File(stepA.RemotePartPath).Length);
            _ftp.InterruptAfterBytes = 0;

            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Hold = new TaskCompletionSource<bool>();
            _resolver.Then(ConflictChoice.Replace);
            var jobB = new TransferJob(TransferKind.Upload, "B", new[] { Upload(_temp.File("a.txt", "neu"), "/data/a.txt") });

            _queue.Enqueue(jobB);          // läuft sofort an und hängt im Konfliktdialog
            _queue.Resume(jobA);           // steht hinter B an, hat noch nicht begonnen
            await _queue.CancelAsync(jobA);

            _resolver.Hold.SetResult(true);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Cancelled, jobA.State);
            Assert.Null(_ftp.File("/data/big.bin"));
            Assert.Null(_ftp.File(stepA.RemotePartPath));
            Assert.Equal(JobState.Completed, jobB.State);
        }

        [Fact]
        public async Task A_transient_failure_before_any_bytes_leaves_the_target_untouched_and_resume_uploads_fresh()
        {
            _ftp.AddFile("/data/plugins.ini", "alt");
            bool failedOnce = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "STOR" && !failedOnce)
                {
                    failedOnce = true;
                    return new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 426);
                }
                return null;
            };
            var step = Upload(_temp.File("plugins.ini", "neu"), "/data/plugins.ini");
            step.OverwriteWithoutAsking = true;

            var job = await Run(TransferKind.Upload, step);

            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/plugins.ini")));   // unangetastet

            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/plugins.ini")));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task A_transient_failure_before_any_bytes_leaves_the_target_untouched_on_cancel()
        {
            _ftp.AddFile("/data/plugins.ini", "alt");
            bool failedOnce = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "STOR" && !failedOnce)
                {
                    failedOnce = true;
                    return new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 426);
                }
                return null;
            };
            var step = Upload(_temp.File("plugins.ini", "neu"), "/data/plugins.ini");
            step.OverwriteWithoutAsking = true;

            var job = await Run(TransferKind.Upload, step);
            Assert.Equal(JobState.Interrupted, job.State);

            await _queue.CancelAsync(job);

            Assert.Equal(JobState.Cancelled, job.State);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/plugins.ini")));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("DELE"));
        }

        // --- Review Runde 2 ---------------------------------------------

        [Fact]
        public async Task Jobs_enqueued_while_the_worker_drains_are_never_stranded()
        {
            // Regressions-Rauchtest, kein Nachweis des verlorenen Wecksignals
            // (Review Runde 2): weil hier zwischen den Enqueues immer auf
            // WhenIdle gewartet wird, ist die Race — Enqueue genau zwischen
            // Leerprüfung und Rücksetzen von _running in WorkAsync — durch den
            // Testaufbau selbst ausgeschlossen. Für einen echten Nachweis
            // bräuchte es einen Hook mitten im Lock-Fenster von WorkAsync. Was
            // hier bleibt: über viele Enqueue/WhenIdle-Zyklen prüfen, dass kein
            // Auftrag hängen bleibt.
            for (int i = 0; i < 200; i++)
            {
                var a = new TransferJob(TransferKind.Upload, "a" + i,
                    new[] { Upload(_temp.File("a" + i + ".txt", "a"), "/data/a" + i + ".txt") });
                _queue.Enqueue(a);
                await _queue.WhenIdle();
                Assert.Equal(JobState.Completed, a.State);

                var b = new TransferJob(TransferKind.Upload, "b" + i,
                    new[] { Upload(_temp.File("b" + i + ".txt", "b"), "/data/b" + i + ".txt") });
                _queue.Enqueue(b);
                await _queue.WhenIdle();
                Assert.Equal(JobState.Completed, b.State);
            }

            Assert.DoesNotContain(_queue.Jobs, j => j.State == JobState.Waiting);
        }

        // --- Plan 3, Übergabe aus Plan 1 --------------------------------

        [Fact]
        public void The_queue_only_accepts_the_zone_guard()
        {
            var parameter = typeof(TransferQueue).GetConstructors().Single().GetParameters()[0];
            Assert.Equal(typeof(ZoneGuardFtpClient), parameter.ParameterType);
        }

        [Fact]
        public async Task An_upload_goes_to_a_part_file_and_is_renamed_into_place()
        {
            var step = Upload(_temp.File("a.txt", "aaa"), "/data/a.txt");
            await Run(TransferKind.Upload, step);

            var calls = _ftp.CallsSnapshot();
            Assert.Contains("STOR " + step.RemotePartPath, calls);
            Assert.Contains("RNFR " + step.RemotePartPath + " -> /data/a.txt", calls);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Null(_ftp.File(step.RemotePartPath));
        }

        [Fact]
        public async Task A_long_remote_name_uses_a_short_sibling_for_the_partial_upload()
        {
            var name = new string('a', 240);
            var step = Upload(_temp.File("source.bin", "aaa"), "/data/" + name);

            var job = await Run(TransferKind.Upload, step);

            Assert.Equal(JobState.Completed, job.State);
            Assert.StartsWith("/data/.ps4fm-", step.RemotePartPath);
            Assert.True(RemotePath.Name(step.RemotePartPath).Length <= 255);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/" + name)));
        }

        [Fact]
        public async Task A_long_local_name_uses_a_short_sibling_for_the_partial_download()
        {
            _ftp.AddFile("/data/a.bin", "aaa");
            var target = Path.Combine(_temp.Path, new string('a', 180));
            var step = new TransferStep(StepKind.DownloadFile, "/data/a.bin", target, null, 3);

            var job = await Run(TransferKind.Download, step);

            Assert.Equal(JobState.Completed, job.State);
            Assert.StartsWith(Path.Combine(_temp.Path, ".ps4fm-"), step.LocalPartPath);
            Assert.Equal("aaa", File.ReadAllText(target));
        }

        [Fact]
        public async Task Replacing_swaps_the_new_file_in_with_one_rename()
        {
            _ftp.AddFile("/data/a.txt", "alt");
            _resolver.Then(ConflictChoice.Replace);

            await Run(TransferKind.Upload, Upload(_temp.File("a.txt", "neu"), "/data/a.txt"));

            Assert.Equal("neu", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("DELE"));
        }

        [Fact]
        public async Task A_connection_loss_while_renaming_resumes_with_the_rename_only()
        {
            bool failedOnce = false;
            _ftp.Failure = (verb, path) =>
            {
                if (verb == "RNFR" && !failedOnce)
                {
                    failedOnce = true;
                    return new FtpException("Verbindung zur PS4 unterbrochen", FtpErrorKind.Transient, 426);
                }
                return null;
            };

            var step = Upload(_temp.File("a.txt", "aaa"), "/data/a.txt");
            var job = await Run(TransferKind.Upload, step);
            Assert.Equal(JobState.Interrupted, job.State);
            Assert.Equal(3, _ftp.File(step.RemotePartPath).Length);

            _queue.Resume(job);
            await _queue.WhenIdle();

            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("aaa", Encoding.UTF8.GetString(_ftp.File("/data/a.txt")));
            Assert.Single(_ftp.CallsSnapshot(), c => c.StartsWith("STOR"));
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("APPE"));
        }

        [Fact]
        public async Task On_usb_a_name_differing_only_in_case_is_a_conflict()
        {
            _ftp.AddFile("/mnt/usb0/README.TXT", "alt");
            _resolver.Then(ConflictChoice.Skip);

            await Run(TransferKind.Upload, Upload(_temp.File("Readme.txt", "neu"), "/mnt/usb0/Readme.txt"));

            var asked = Assert.Single(_resolver.Asked);
            Assert.Equal("Readme (2).txt", asked.KeepBothName);
        }

        [Fact]
        public async Task On_the_internal_disk_names_differing_in_case_are_different_files()
        {
            _ftp.AddFile("/data/README.TXT", "alt");

            await Run(TransferKind.Upload, Upload(_temp.File("Readme.txt", "neu"), "/data/Readme.txt"));

            Assert.Empty(_resolver.Asked);
            Assert.Equal("alt", Encoding.UTF8.GetString(_ftp.File("/data/README.TXT")));
        }

        [Fact]
        public async Task On_usb_a_case_only_rename_is_not_a_conflict()
        {
            _ftp.AddFile("/mnt/usb0/a.txt", "a");

            var job = await Run(TransferKind.Rename,
                new TransferStep(StepKind.RenameRemote, "/mnt/usb0/a.txt", null, "/mnt/usb0/A.TXT", 1));

            Assert.Equal(JobState.Completed, job.State);
            Assert.Empty(_resolver.Asked);
            Assert.Contains("RNFR /mnt/usb0/a.txt -> /mnt/usb0/A.TXT", _ftp.CallsSnapshot());
        }
    }
}
