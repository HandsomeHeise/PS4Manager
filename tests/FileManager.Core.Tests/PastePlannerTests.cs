using System;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class PastePlannerTests
    {
        [Theory]
        [InlineData(ClipMode.Cut, true, false, PasteAction.MoveItems)]
        [InlineData(ClipMode.Copy, true, false, PasteAction.RefuseCopyWithinPs4)]
        [InlineData(ClipMode.Copy, false, true, PasteAction.UploadFiles)]
        [InlineData(ClipMode.Cut, false, true, PasteAction.UploadFiles)]
        [InlineData(ClipMode.None, false, true, PasteAction.UploadFiles)]
        [InlineData(ClipMode.None, false, false, PasteAction.Nothing)]
        [InlineData(ClipMode.Cut, false, false, PasteAction.Nothing)]
        public void Paste_follows_who_owns_the_clipboard(ClipMode mode, bool ours, bool files, PasteAction expected)
        {
            Assert.Equal(expected, PastePlanner.Decide(mode, ours, files));
        }

        [Theory]
        [InlineData(true, false, Zone.Free, DropAction.Move)]
        [InlineData(false, true, Zone.Warn, DropAction.Upload)]
        [InlineData(true, false, Zone.Locked, DropAction.None)]
        [InlineData(false, true, Zone.Locked, DropAction.None)]
        [InlineData(false, false, Zone.Free, DropAction.None)]
        public void Drops_move_or_upload_but_never_into_locked_folders(bool own, bool files, Zone zone, DropAction expected)
        {
            Assert.Equal(expected, PastePlanner.ForDrop(own, files, zone));
        }

        [Fact]
        public void Only_folders_are_drop_targets()
        {
            var folder = new FolderRow("/data", new FtpEntry("pkg", FtpEntryKind.Directory, 512, DateTime.MinValue, null), "");
            var file = new FolderRow("/data", new FtpEntry("a.txt", FtpEntryKind.File, 1, DateTime.MinValue, null), "");
            var link = new FolderRow("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"), "");

            Assert.Equal("/data/pkg", PastePlanner.DropFolder(folder, "/data"));
            Assert.Equal("/data", PastePlanner.DropFolder(file, "/data"));
            Assert.Equal("/data", PastePlanner.DropFolder(link, "/data"));
            Assert.Equal("/data", PastePlanner.DropFolder(null, "/data"));
        }

        [Fact]
        public void A_move_to_where_things_already_are_is_not_useful()
        {
            var a = new RemoteItem("/data/a.txt", false, 1, DateTime.MinValue);
            var mods = new RemoteItem("/data/mods", true, 0, DateTime.MinValue);

            Assert.False(PastePlanner.IsUsefulMove(new[] { a }, "/data"));
            Assert.True(PastePlanner.IsUsefulMove(new[] { a }, "/data/pkg"));
            Assert.False(PastePlanner.IsUsefulMove(new[] { mods }, "/data/mods/sub"));
            Assert.False(PastePlanner.IsUsefulMove(new[] { mods }, "/data/mods"));
        }
    }
}
