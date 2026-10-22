using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class RemoteItemTests
    {
        [Fact]
        public void From_keeps_the_kind_and_the_link_target()
        {
            var item = RemoteItem.From("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, new DateTime(2026, 9, 11), "/system"));

            Assert.Equal("/data/sys", item.Path);
            Assert.Equal(FtpEntryKind.Link, item.Kind);
            Assert.True(item.IsLink);
            Assert.False(item.IsDirectory);
            Assert.Equal("/system", item.LinkTarget);
        }

        [Fact]
        public void The_old_constructor_still_means_file_or_directory()
        {
            var dir = new RemoteItem("/data/a", true, 0, DateTime.MinValue);
            var file = new RemoteItem("/data/b", false, 3, DateTime.MinValue);

            Assert.Equal(FtpEntryKind.Directory, dir.Kind);
            Assert.True(dir.IsDirectory);
            Assert.Equal(FtpEntryKind.File, file.Kind);
            Assert.False(file.IsLink);
            Assert.Null(file.LinkTarget);
        }

        [Fact]
        public void A_link_target_on_something_else_is_dropped()
        {
            Assert.Null(new RemoteItem("/data/a", FtpEntryKind.File, 1, DateTime.MinValue, "/x").LinkTarget);
        }

        [Fact]
        public async Task The_fake_console_lists_links_with_their_target()
        {
            var ftp = new InMemoryFtpClient().AddDir("/data").AddLink("/data/sys", "/system");

            var entry = (await ftp.ListAsync("/data", CancellationToken.None)).Single();
            var item = RemoteItem.From("/data", entry);

            Assert.True(item.IsLink);
            Assert.Equal("/system", item.LinkTarget);

            await ftp.DeleteAsync("/data/sys", CancellationToken.None);
            Assert.Empty(await ftp.ListAsync("/data", CancellationToken.None));
        }
    }
}
