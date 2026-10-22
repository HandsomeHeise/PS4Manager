using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Xunit;

namespace FileManager.Core.Tests
{
    internal sealed class RecordingPrompts : IUserPrompts
    {
        public bool Answer = true;
        public readonly List<string> WarnActions = new List<string>();
        public readonly List<DeleteSummary> Deletes = new List<DeleteSummary>();

        public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
        {
            WarnActions.Add(action);
            return Task.FromResult(Answer);
        }

        public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
        {
            Deletes.Add(summary);
            return Task.FromResult(Answer);
        }
    }

    public sealed class FileOperationsTests : IDisposable
    {
        private readonly InMemoryFtpClient _ftp = new InMemoryFtpClient();
        private readonly ZonePolicy _policy = new ZonePolicy();
        private readonly RecordingPrompts _prompts = new RecordingPrompts();
        private readonly TempDir _temp = new TempDir();
        private readonly FileOperations _ops;

        public FileOperationsTests()
        {
            _ops = new FileOperations(_ftp, new FtpGates(), _policy, _prompts);
        }

        public void Dispose()
        {
            _temp.Dispose();
        }

        private static string Describe(TransferStep s)
        {
            return s.Kind + " " + (s.Kind == StepKind.RenameRemote ? s.RemotePath + " -> " + s.TargetRemotePath : s.RemotePath);
        }

        [Fact]
        public async Task Uploading_a_folder_creates_folders_before_their_files()
        {
            _ftp.AddDir("/data");
            var mods = _temp.Sub("mods");
            _temp.File("mods/a.txt", "aaa");
            _temp.File("mods/sub/b.txt", "bb");

            var job = await _ops.PlanUploadAsync(new[] { mods }, "/data", CancellationToken.None);

            Assert.Equal(new[]
            {
                "CreateRemoteFolder /data/mods",
                "UploadFile /data/mods/a.txt",
                "CreateRemoteFolder /data/mods/sub",
                "UploadFile /data/mods/sub/b.txt"
            }, job.Steps.Select(Describe).ToArray());
            Assert.Equal(5, job.TotalBytes);
            Assert.Equal(TransferKind.Upload, job.Kind);
            Assert.Equal("Upload mods to /data", job.Title);
            Assert.Empty(_prompts.WarnActions);
        }

        [Fact]
        public async Task Uploading_into_a_warn_zone_asks_once_and_respects_no()
        {
            _ftp.AddDir("/user/home/194450ad/savedata");
            var a = _temp.File("a.bin", "a");
            var b = _temp.File("b.bin", "b");
            _prompts.Answer = false;

            var job = await _ops.PlanUploadAsync(new[] { a, b }, "/user/home/194450ad/savedata", CancellationToken.None);

            Assert.Null(job);
            Assert.Equal(new[] { "Upload" }, _prompts.WarnActions.ToArray());
        }

        [Fact]
        public async Task Uploading_into_a_locked_zone_is_refused_without_asking()
        {
            var a = _temp.File("a.bin", "a");
            await Assert.ThrowsAsync<ZoneViolationException>(
                () => _ops.PlanUploadAsync(new[] { a }, "/system_data", CancellationToken.None));
            Assert.Empty(_prompts.WarnActions);
            Assert.Equal(0, _ftp.WriteCallCount);
        }

        [Fact]
        public async Task Downloading_a_folder_mirrors_it_locally()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "x").AddFile("/data/GoldHEN/plugins/a.prx", "yy");
            var target = _temp.Sub("ziel");

            var job = await _ops.PlanDownloadAsync(
                new[] { new RemoteItem("/data/GoldHEN", true, 0, DateTime.MinValue) }, target, CancellationToken.None);

            Assert.Equal(new[]
            {
                StepKind.CreateLocalFolder + " " + Path.Combine(target, "GoldHEN"),
                StepKind.DownloadFile + " " + Path.Combine(target, "GoldHEN", "plugins.ini"),
                StepKind.CreateLocalFolder + " " + Path.Combine(target, "GoldHEN", "plugins"),
                StepKind.DownloadFile + " " + Path.Combine(target, "GoldHEN", "plugins", "a.prx")
            }, job.Steps.Select(s => s.Kind + " " + s.LocalPath).ToArray());
            Assert.Equal(3, job.TotalBytes);
        }

        [Fact]
        public async Task Downloaded_names_are_made_windows_safe()
        {
            _ftp.AddFile("/data/a:b.txt", "x");
            var target = _temp.Sub("ziel");

            var job = await _ops.PlanDownloadAsync(
                new[] { new RemoteItem("/data/a:b.txt", false, 1, DateTime.MinValue) }, target, CancellationToken.None);

            Assert.Equal(Path.Combine(target, "a_b.txt"), job.Steps.Single().LocalPath);
            Assert.Equal("/data/a:b.txt", job.Steps.Single().RemotePath);
        }

        [Fact]
        public async Task Deleting_a_tree_removes_files_first_and_the_deepest_folder_first()
        {
            _ftp.AddFile("/data/alt/a.txt", "12345").AddFile("/data/alt/sub/b.txt", "12").AddFile("/data/c.txt", "1");

            var job = await _ops.PlanDeleteAsync(new[]
            {
                new RemoteItem("/data/alt", true, 0, DateTime.MinValue),
                new RemoteItem("/data/c.txt", false, 1, DateTime.MinValue)
            }, CancellationToken.None);

            Assert.Equal(new[]
            {
                "DeleteRemoteFile /data/alt/a.txt",
                "DeleteRemoteFile /data/alt/sub/b.txt",
                "RemoveRemoteFolder /data/alt/sub",
                "RemoveRemoteFolder /data/alt",
                "DeleteRemoteFile /data/c.txt"
            }, job.Steps.Select(Describe).ToArray());

            var summary = Assert.Single(_prompts.Deletes);
            Assert.Equal(3, summary.FileCount);
            Assert.Equal(2, summary.FolderCount);
            Assert.Equal(8, summary.TotalBytes);
            Assert.Equal(Zone.Free, summary.Zone);
            Assert.Equal(new[] { "alt", "c.txt" }, summary.TopLevelNames.ToArray());
        }

        [Fact]
        public async Task Deleting_always_asks_and_no_means_no()
        {
            _ftp.AddFile("/data/a.txt", "a");
            _prompts.Answer = false;

            var job = await _ops.PlanDeleteAsync(new[] { new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue) }, CancellationToken.None);

            Assert.Null(job);
            Assert.Single(_prompts.Deletes);
        }

        [Fact]
        public async Task Deleting_in_a_warn_zone_asks_one_combined_question()
        {
            _ftp.AddFile("/user/home/194450ad/savedata/CUSA00411/sdimg_x", "s");

            await _ops.PlanDeleteAsync(new[] { new RemoteItem("/user/home/194450ad/savedata/CUSA00411", true, 0, DateTime.MinValue) },
                CancellationToken.None);

            Assert.Equal(Zone.Warn, Assert.Single(_prompts.Deletes).Zone);
            Assert.Empty(_prompts.WarnActions);
        }

        [Fact]
        public async Task Deleting_in_a_locked_zone_is_refused_without_asking()
        {
            _ftp.AddFile("/system_data/priv/x", "x");
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanDeleteAsync(new[] { new RemoteItem("/system_data/priv/x", false, 1, DateTime.MinValue) }, CancellationToken.None));
            Assert.Empty(_prompts.Deletes);
        }

        [Fact]
        public async Task A_new_folder_gets_a_free_name()
        {
            _ftp.AddDir("/data/New folder");

            var job = await _ops.PlanCreateFolderAsync("/data", CancellationToken.None);

            Assert.Equal("CreateRemoteFolder /data/New folder (2)", Describe(job.Steps.Single()));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a/b")]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("a\nb")]
        [InlineData("a\0b")]
        public async Task Invalid_names_are_refused(string name)
        {
            _ftp.AddFile("/data/a.txt", "a");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue), name, CancellationToken.None));
        }

        [Fact]
        public async Task Renaming_onto_an_existing_name_is_refused()
        {
            _ftp.AddFile("/data/a.txt", "a").AddFile("/data/b.txt", "b");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue), "b.txt", CancellationToken.None));
            Assert.Contains("already exists here", ex.Message);
        }

        [Fact]
        public async Task Renaming_to_the_same_name_is_nothing_to_do_and_a_real_rename_is_one_step()
        {
            _ftp.AddFile("/data/a.txt", "a");
            var item = new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue);

            Assert.Null(await _ops.PlanRenameAsync(item, "a.txt", CancellationToken.None));
            var job = await _ops.PlanRenameAsync(item, "c.txt", CancellationToken.None);
            Assert.Equal("RenameRemote /data/a.txt -> /data/c.txt", Describe(job.Steps.Single()));
        }

        [Fact]
        public async Task A_folder_cannot_move_into_itself()
        {
            _ftp.AddDir("/data/a/b");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ops.PlanMoveAsync(new[] { new RemoteItem("/data/a", true, 0, DateTime.MinValue) }, "/data/a/b", CancellationToken.None));
        }

        [Fact]
        public async Task Moving_renames_into_the_target_and_skips_items_already_there()
        {
            _ftp.AddFile("/data/a.txt", "a").AddDir("/data/sub").AddDir("/data/ziel");

            var job = await _ops.PlanMoveAsync(new[]
            {
                new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue),
                new RemoteItem("/data/sub", true, 0, DateTime.MinValue),
                new RemoteItem("/data/ziel/x", false, 1, DateTime.MinValue)
            }, "/data/ziel", CancellationToken.None);

            Assert.Equal(new[]
            {
                "RenameRemote /data/a.txt -> /data/ziel/a.txt",
                "RenameRemote /data/sub -> /data/ziel/sub"
            }, job.Steps.Select(Describe).ToArray());
            Assert.True(job.Steps[1].IsDirectory);
        }

        [Fact]
        public async Task Moving_out_of_a_locked_zone_is_refused()
        {
            _ftp.AddFile("/system_data/x", "x").AddDir("/data");
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanMoveAsync(new[] { new RemoteItem("/system_data/x", false, 1, DateTime.MinValue) }, "/data", CancellationToken.None));
        }

        [Fact]
        public void Copying_within_the_console_says_what_to_do_instead()
        {
            var ex = Assert.Throws<NotSupportedException>(() => _ops.PlanCopyWithinPs4());
            Assert.Equal("Copying within the PS4 is not supported yet. Use Ctrl+X to move items.", ex.Message);
        }

        [Fact]
        public async Task Open_and_write_back_never_ask_about_conflicts()
        {
            _ftp.AddFile("/data/GoldHEN/plugins.ini", "x");
            var local = _temp.File("open/plugins.ini", "neu");

            var open = _ops.PlanOpenForEdit(new RemoteItem("/data/GoldHEN/plugins.ini", false, 1, DateTime.MinValue), local);
            var back = await _ops.PlanWriteBackAsync(local, "/data/GoldHEN/plugins.ini", CancellationToken.None);

            Assert.True(open.Steps.Single().OverwriteWithoutAsking);
            Assert.Equal(StepKind.DownloadFile, open.Steps.Single().Kind);
            Assert.True(back.Steps.Single().OverwriteWithoutAsking);
            Assert.Equal(StepKind.UploadFile, back.Steps.Single().Kind);
            Assert.Equal(3, back.TotalBytes);
        }

        [Fact]
        public async Task Writing_back_into_a_locked_file_is_refused()
        {
            var local = _temp.File("open/x.dat", "neu");
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanWriteBackAsync(local, "/system_data/priv/x.dat", CancellationToken.None));
        }
        // --- Plan 3, Übergabe aus Plan 1 --------------------------------

        private static void Junction(string link, string target)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using (var process = Process.Start(psi))
            {
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
        }

        [Fact]
        public async Task A_drive_root_cannot_be_uploaded()
        {
            _ftp.AddDir("/data");
            var root = Path.GetPathRoot(_temp.Path);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _ops.PlanUploadAsync(new[] { root }, "/data", CancellationToken.None));
            Assert.StartsWith("Entire drives cannot be uploaded", ex.Message);
        }

        [Fact]
        public async Task A_junction_inside_an_uploaded_folder_is_not_followed()
        {
            _ftp.AddDir("/data");
            var mods = _temp.Sub("mods");
            _temp.File("mods/a.txt", "aaa");
            Junction(Path.Combine(mods, "schleife"), _temp.Sub("outside"));
            _temp.File("outside/secret.txt", "bleibt lokal");

            var job = await _ops.PlanUploadAsync(new[] { mods }, "/data", CancellationToken.None);

            Assert.Equal(new[] { "CreateRemoteFolder /data/mods", "UploadFile /data/mods/a.txt" },
                job.Steps.Select(Describe).ToArray());
        }

        [Fact]
        public async Task A_new_folder_on_usb_avoids_a_name_differing_only_in_case()
        {
            _ftp.AddDir("/mnt/usb0/new folder");

            var job = await _ops.PlanCreateFolderAsync("/mnt/usb0", CancellationToken.None);

            Assert.Equal("/mnt/usb0/New folder (2)", job.Steps.Single().RemotePath);
        }

        [Fact]
        public async Task On_usb_renaming_only_the_case_is_allowed()
        {
            _ftp.AddFile("/mnt/usb0/a.txt", "a");

            var job = await _ops.PlanRenameAsync(new RemoteItem("/mnt/usb0/a.txt", false, 1, DateTime.MinValue), "A.txt", CancellationToken.None);

            Assert.Equal("/mnt/usb0/A.txt", job.Steps.Single().TargetRemotePath);
        }

        [Fact]
        public async Task On_usb_renaming_onto_a_name_differing_in_case_is_refused()
        {
            _ftp.AddFile("/mnt/usb0/a.txt", "a").AddFile("/mnt/usb0/B.txt", "b");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/mnt/usb0/a.txt", false, 1, DateTime.MinValue), "b.txt", CancellationToken.None));
        }

        [Fact]
        public async Task A_locked_selection_is_refused_before_listing()
        {
            _ftp.AddFile("/system/x/y.bin", "y");

            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanDeleteAsync(new[] { new RemoteItem("/system/x", true, 0, DateTime.MinValue) }, CancellationToken.None));
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanRenameAsync(new RemoteItem("/system/x", true, 0, DateTime.MinValue), "z", CancellationToken.None));
            await Assert.ThrowsAsync<ZoneViolationException>(() =>
                _ops.PlanCreateFolderAsync("/system", CancellationToken.None));

            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c.StartsWith("LIST"));
        }

        [Fact]
        public async Task Deleting_a_link_removes_the_link_not_its_target()
        {
            _ftp.AddFile("/data/echt/a.txt", "a").AddLink("/data/verweis", "/data/echt");
            var link = RemoteItem.From("/data", (await _ftp.ListAsync("/data", CancellationToken.None)).Single(e => e.Name == "verweis"));

            var job = await _ops.PlanDeleteAsync(new[] { link }, CancellationToken.None);

            Assert.Equal(new[] { "DeleteRemoteFile /data/verweis" }, job.Steps.Select(Describe).ToArray());
            Assert.DoesNotContain(_ftp.CallsSnapshot(), c => c == "LIST /data/verweis" || c == "LIST /data/echt");
        }

        [Fact]
        public async Task A_link_inside_a_deleted_folder_is_not_followed()
        {
            _ftp.AddFile("/data/mods/a.txt", "a").AddLink("/data/mods/schleife", "/data/mods");

            var job = await _ops.PlanDeleteAsync(new[] { new RemoteItem("/data/mods", true, 0, DateTime.MinValue) }, CancellationToken.None);

            Assert.Equal(new[]
            {
                "DeleteRemoteFile /data/mods/a.txt",
                "DeleteRemoteFile /data/mods/schleife",
                "RemoveRemoteFolder /data/mods"
            }, job.Steps.Select(Describe).ToArray());
        }

        [Fact]
        public async Task A_link_inside_a_downloaded_folder_is_skipped()
        {
            _ftp.AddFile("/data/mods/a.txt", "a").AddLink("/data/mods/schleife", "/data/mods");

            var job = await _ops.PlanDownloadAsync(new[] { new RemoteItem("/data/mods", true, 0, DateTime.MinValue) }, _temp.Path, CancellationToken.None);

            Assert.DoesNotContain(job.Steps, s => s.RemotePath.EndsWith("schleife", StringComparison.Ordinal));
        }
    }
}
