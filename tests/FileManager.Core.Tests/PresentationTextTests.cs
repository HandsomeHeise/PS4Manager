using System;
using System.Collections.Generic;
using System.IO;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class PresentationTextTests
    {
        private static FolderRow FileRow(string name, long size)
        {
            return new FolderRow("/data", new FtpEntry(name, FtpEntryKind.File, size, DateTime.MinValue, null), "File");
        }

        private static FolderRow DirRow(string dir, string name)
        {
            return new FolderRow(dir, new FtpEntry(name, FtpEntryKind.Directory, 512, DateTime.MinValue, null), "File folder");
        }

        private static readonly FolderRow[] None = new FolderRow[0];

        [Fact]
        public void The_status_line_reads_like_the_spec()
        {
            Assert.Equal("Disconnected", StatusText.Line(false, 0, None, Zone.Locked));
            Assert.Equal("Connected · 3 items · Zone: Free", StatusText.Line(true, 3, None, Zone.Free));
            Assert.Equal("Connected · 3 items · 1 selected (412 KB) · Zone: Free",
                StatusText.Line(true, 3, new[] { FileRow("a.prx", 412 * 1024) }, Zone.Free));
            Assert.Equal("Connected · 1 item · 1 selected · Zone: Warning",
                StatusText.Line(true, 1, new[] { DirRow("/user/home", "1") }, Zone.Warn));
        }

        [Theory]
        [InlineData(0L, 100L, "░░░░░░░░░░ 0 %")]
        [InlineData(78L, 100L, "████████░░ 78 %")]
        [InlineData(100L, 100L, "██████████ 100 %")]
        [InlineData(5L, 0L, "░░░░░░░░░░ 0 %")]
        public void The_bar_is_ten_blocks_and_a_percentage(long done, long total, string expected)
        {
            Assert.Equal(expected, TransferText.Bar(done, total));
        }

        [Fact]
        public void States_read_english()
        {
            var job = new TransferJob(TransferKind.Delete, "delete x", new[] { new TransferStep(StepKind.DeleteRemoteFile, "/data/x", null, null, 1) });
            Assert.Equal("Waiting", TransferText.State(job));
            Assert.Equal("░░░░░░░░░░ 0 %", TransferText.Progress(job));   // ohne Bytes: nach Schritten
            Assert.Equal("", TransferText.Rate(job));
            Assert.Equal("", TransferText.Remaining(job));
        }

        [Theory]
        [InlineData(42, "0:42 remaining")]
        [InlineData(725, "12:05 remaining")]
        [InlineData(3723, "1:02:03 remaining")]
        public void Remaining_time_reads_like_a_clock(int seconds, string expected)
        {
            Assert.Equal(expected, TransferText.RemainingText(TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void The_summary_names_waiting_jobs_when_none_runs()
        {
            var a = new TransferJob(TransferKind.Upload, "upload a", new TransferStep[0]);
            var b = new TransferJob(TransferKind.Upload, "upload b", new TransferStep[0]);

            Assert.Equal("2 waiting", TransferText.Summary(new[] { a, b }));
            Assert.Equal("No active transfers", TransferText.Summary(new TransferJob[0]));
        }

        [Fact]
        public void Write_commands_are_greyed_in_a_locked_folder()
        {
            var state = CommandState.For(true, Zone.Locked, None, p => Zone.Locked);

            Assert.False(state.CanUpload);
            Assert.False(state.CanNewFolder);
            Assert.False(state.CanPaste);
        }

        [Fact]
        public void Selection_commands_follow_the_selection()
        {
            var policy = new ZonePolicy();
            var file = FileRow("a.txt", 3);
            var game = DirRow("/user/app", "CUSA00411");
            var link = new FolderRow("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"), "Verknüpfung");

            var one = CommandState.For(true, Zone.Free, new[] { file }, policy.Effective);
            Assert.True(one.CanOpen && one.CanRename && one.CanDelete && one.CanDownload && one.CanCopy && one.CanCut);

            var gameState = CommandState.For(true, Zone.Warn, new[] { game }, policy.Effective);
            Assert.False(gameState.CanRename);                          // Spielordner behalten ihre ID
            Assert.False(gameState.CanOpen);

            var linkState = CommandState.For(true, Zone.Free, new[] { link }, policy.Effective);
            Assert.False(linkState.CanDownload);
            Assert.False(linkState.CanCopy);
            Assert.True(linkState.CanDelete);                           // der Link selbst darf weg

            var disconnected = CommandState.For(false, Zone.Free, new[] { file }, policy.Effective);
            Assert.False(disconnected.CanDelete || disconnected.CanUpload || disconnected.CanOpen);
        }

        [Fact]
        public void A_locked_item_cannot_be_deleted_or_renamed()
        {
            var sys = DirRow("/", "system");

            var state = CommandState.For(true, Zone.Locked, new[] { sys }, new ZonePolicy().Effective);

            Assert.False(state.CanDelete);
            Assert.False(state.CanRename);
            Assert.False(state.CanCut);
            Assert.True(state.CanDownload);                             // Lesen ist überall erlaubt
        }

        [Fact]
        public void A_link_cannot_be_cut_when_clipboard_export_would_filter_it()
        {
            var link = new FolderRow("/data",
                new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"),
                "Shortcut");

            var state = CommandState.For(true, Zone.Free, new[] { link }, new ZonePolicy().Effective);

            Assert.False(state.CanCut);
        }
    }
}
