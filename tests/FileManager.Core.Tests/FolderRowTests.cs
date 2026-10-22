using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileManager.Core.Presentation;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class FolderRowTests
    {
        private static FtpEntry File(string name, long size)
        {
            return new FtpEntry(name, FtpEntryKind.File, size, new DateTime(2026, 9, 11), null);
        }

        private static FtpEntry Dir(string name)
        {
            return new FtpEntry(name, FtpEntryKind.Directory, 512, new DateTime(2026, 9, 7), null);
        }

        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(338L, "338 B")]
        [InlineData(1024L, "1 KB")]
        [InlineData(1025L, "2 KB")]
        [InlineData(1572864L, "1.5 MB")]
        [InlineData(11873615872L, "11.1 GB")]
        public void Sizes_read_like_in_explorer(long bytes, string expected)
        {
            Assert.Equal(expected, SizeFormat.Bytes(bytes));
        }

        [Fact]
        public void Rates_and_dates_are_german()
        {
            Assert.Equal("6.9 MB/s", SizeFormat.Rate(6.9 * 1024 * 1024));
            Assert.Equal("850 KB/s", SizeFormat.Rate(850 * 1024));
            Assert.Equal("", SizeFormat.Rate(0));
            Assert.Equal("9/11/2026", SizeFormat.Date(new DateTime(2026, 9, 11)));
            Assert.Equal("", SizeFormat.Date(DateTime.MinValue));
        }

        [Fact]
        public void Sizes_sort_by_bytes_not_by_text()
        {
            var cells = new List<SortValue>
            {
                new FolderRow("/data", File("gross.pkg", 2048), "PKG").SizeCell,
                new FolderRow("/data", File("klein.ini", 338), "Konfiguration").SizeCell
            };

            cells.Sort((a, b) => System.Collections.Comparer.Default.Compare(a, b));

            Assert.Equal(new[] { "338 B", "2 KB" }, cells.Select(c => c.ToString()).ToArray());
        }

        [Fact]
        public void A_game_folder_is_recognised_and_shows_its_name_once_known()
        {
            var row = new FolderRow("/mnt/ext0/user/app", Dir("CUSA00411"), "Dateiordner");

            Assert.True(row.IsTitleFolder);
            Assert.Equal("CUSA00411", row.TitleId);
            Assert.Equal("CUSA00411", row.ShownName);

            row.TitleName = "Grand Theft Auto V";

            Assert.Equal("Grand Theft Auto V", row.ShownName);
            Assert.Equal("Grand Theft Auto V", row.NameCell.ToString());
            Assert.Equal("CUSA00411", row.TitleIdCell.ToString());
        }

        [Fact]
        public void A_file_named_like_a_title_is_no_game_folder()
        {
            Assert.Null(new FolderRow("/data", File("CUSA00411", 1), "Datei").TitleId);
        }

        [Fact]
        public void Folders_rank_before_files_and_have_no_size()
        {
            var folder = new FolderRow("/data", Dir("GoldHEN"), "Dateiordner");
            var file = new FolderRow("/data", File("a.txt", 3), "Textdokument");

            Assert.Equal(0, folder.GroupRank);
            Assert.Equal(1, file.GroupRank);
            Assert.Equal("", folder.SizeCell.ToString());
            Assert.Equal("/data/GoldHEN", folder.Item.Path);
            Assert.Equal("9/7/2026", folder.ModifiedCell.ToString());
        }

        [Fact]
        public void A_utf8_name_is_shown_decoded_but_the_item_keeps_the_wire_name()
        {
            var wire = FtpClient.PathEncoding.GetString(Encoding.UTF8.GetBytes("Ärger.txt"));
            var row = new FolderRow("/data", File(wire, 1), "Textdokument");

            Assert.Equal("Ärger.txt", row.DisplayName);
            Assert.Equal("/data/" + wire, row.Item.Path);
        }

        [Fact]
        public void A_link_is_marked()
        {
            var row = new FolderRow("/data", new FtpEntry("sys", FtpEntryKind.Link, 7, DateTime.MinValue, "/system"), "Verknüpfung");

            Assert.True(row.IsLink);
            Assert.False(row.IsFolder);
            Assert.Equal("", row.ModifiedCell.ToString());
        }
    }
}
