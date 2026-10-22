using System;
using PS4Manager.Gallery.Core.Ps4;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    public class MediaItemTests
    {
        private static readonly DateTime Fallback = new DateTime(2000, 1, 1);

        [Fact]
        public void The_capture_time_comes_from_the_file_name()
        {
            var ts = MediaItem.ParseTimestamp("20260801_084933_00470650.jpg", Fallback);
            Assert.Equal(new DateTime(2026, 8, 1, 8, 49, 33), ts);
        }

        [Fact]
        public void A_clip_name_parses_the_same_way()
        {
            // Der Video-Zweig nutzt dieselbe Namensregel (verifiziert 2026-08-15) —
            // dieser Test hält fest, dass ParseTimestamp dafür nichts Eigenes braucht.
            var ts = MediaItem.ParseTimestamp("20260810_212645_00367837.mp4", Fallback);
            Assert.Equal(new DateTime(2026, 8, 10, 21, 26, 45), ts);
        }

        [Theory]
        [InlineData("kein_datum.jpg")]
        [InlineData("2026_kaputt.jpg")]
        [InlineData("20261341_990000_x.jpg")]   // Monat 13, Stunde 99
        [InlineData("kurz.jpg")]
        public void A_malformed_name_falls_back(string name)
        {
            Assert.Equal(Fallback, MediaItem.ParseTimestamp(name, Fallback));
        }
    }
}
