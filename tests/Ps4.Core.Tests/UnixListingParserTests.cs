using System;
using System.Linq;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public class UnixListingParserTests
    {
        private const string RealGoldHenListing =
            "drwxr-xr-x 2 0 0 512 Aug  1 2026 .\r\n" +
            "drwxr-xr-x 5 0 0 512 Aug  1 2026 ..\r\n" +
            "-rw-r--r-- 1 0 0 461788 Aug  1 2026 20260801_084933_00470650.jpg\r\n" +
            "-rwxrwx--- 1 0 0 592 Aug  1 2026 20260801_084933_00470650.dat\r\n" +
            "drwxr-xr-x 3 0 0 512 Jul 24 2026 CUSA00411\r\n";

        [Fact]
        public void Dot_and_dotdot_are_skipped()
        {
            var entries = UnixListingParser.Parse(RealGoldHenListing);
            Assert.DoesNotContain(entries, e => e.Name == "." || e.Name == "..");
            Assert.Equal(3, entries.Count);
        }

        [Fact]
        public void Files_and_directories_are_told_apart()
        {
            var entries = UnixListingParser.Parse(RealGoldHenListing);

            var jpg = entries.Single(e => e.Name == "20260801_084933_00470650.jpg");
            Assert.False(jpg.IsDirectory);
            Assert.Equal(461788, jpg.Size);

            var dir = entries.Single(e => e.Name == "CUSA00411");
            Assert.True(dir.IsDirectory);
        }

        [Fact]
        public void Special_permission_bits_do_not_confuse_the_parser()
        {
            var entries = UnixListingParser.Parse(RealGoldHenListing);
            var dat = entries.Single(e => e.Name == "20260801_084933_00470650.dat");
            Assert.False(dat.IsDirectory);
            Assert.Equal(592, dat.Size);
        }

        [Fact]
        public void A_name_with_spaces_survives()
        {
            var entries = UnixListingParser.Parse(
                "-rw-r--r-- 1 0 0 10 Aug  1 2026 mit zwei leerzeichen.jpg\n");
            Assert.Equal("mit zwei leerzeichen.jpg", entries.Single().Name);
        }

        [Fact]
        public void Garbage_lines_and_blank_lines_are_ignored()
        {
            var entries = UnixListingParser.Parse("total 12\r\n\r\nkaputt\r\n");
            Assert.Empty(entries);
        }

        [Fact]
        public void Null_or_empty_input_yields_an_empty_list()
        {
            Assert.Empty(UnixListingParser.Parse(null));
            Assert.Empty(UnixListingParser.Parse(""));
        }

        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        [Fact]
        public void The_date_of_a_real_goldhen_line_is_read()
        {
            // Wörtlich von der Konsole (LIST /, 2026-09-11) — GoldHEN liefert nur das Datum.
            var entry = UnixListingParser.Parse("drwxrwxrwx 24 0 0 1024 Sep  9 2026 data\r\n", Today).Single();
            Assert.Equal(FtpEntryKind.Directory, entry.Kind);
            Assert.Equal(new DateTime(2026, 9, 9), entry.Modified);
        }

        [Fact]
        public void Sizes_above_4_gb_survive()
        {
            // Wörtlich aus /data/pkg.
            var entry = UnixListingParser.Parse(
                "-rwxrwxrwx 1 0 0 11873615872 Sep 11 2026 Call.of.Duty.Black.Ops.6.CUSA23827.MWIII.Shared.Content.Pack.3-CyB1K.pkg\r\n",
                Today).Single();
            Assert.Equal(11873615872L, entry.Size);
            Assert.Equal(FtpEntryKind.File, entry.Kind);
        }

        [Fact]
        public void A_time_instead_of_a_year_means_this_year()
        {
            var entry = UnixListingParser.Parse("-rw-r--r-- 1 0 0 10 Aug 10 12:34 x.txt\n", Today).Single();
            Assert.Equal(new DateTime(2026, 8, 10, 12, 34, 0), entry.Modified);
        }

        [Fact]
        public void A_time_that_would_lie_in_the_future_belongs_to_last_year()
        {
            var entry = UnixListingParser.Parse("-rw-r--r-- 1 0 0 10 Dec 30 23:00 x.txt\n",
                new DateTime(2026, 1, 5)).Single();
            Assert.Equal(new DateTime(2025, 12, 30, 23, 0, 0), entry.Modified);
        }

        [Fact]
        public void A_link_keeps_its_name_and_target()
        {
            var entry = UnixListingParser.Parse("lrwxrwxrwx 1 0 0 9 Sep  1 2026 usb -> /mnt/usb0\n", Today).Single();
            Assert.Equal(FtpEntryKind.Link, entry.Kind);
            Assert.Equal("usb", entry.Name);
            Assert.Equal("/mnt/usb0", entry.LinkTarget);
            Assert.False(entry.IsDirectory);
        }

        [Fact]
        public void An_unreadable_date_keeps_the_entry_with_min_value()
        {
            var entry = UnixListingParser.Parse("-rw-r--r-- 1 0 0 10 Foo  1 2026 x.txt\n", Today).Single();
            Assert.Equal(DateTime.MinValue, entry.Modified);
            Assert.Equal("x.txt", entry.Name);
        }

        [Fact]
        public void Device_files_are_skipped()
        {
            Assert.Empty(UnixListingParser.Parse("crw-rw-rw- 1 0 0 0 Sep 11 2026 null\n", Today));
        }

        [Fact]
        public void The_old_constructor_still_means_file_or_directory()
        {
            var dir = new FtpEntry("x", true, 1);
            Assert.Equal(FtpEntryKind.Directory, dir.Kind);
            Assert.Equal(DateTime.MinValue, dir.Modified);
            Assert.Null(dir.LinkTarget);
            Assert.Equal(FtpEntryKind.File, new FtpEntry("y", false, 1).Kind);
        }
    }
}
