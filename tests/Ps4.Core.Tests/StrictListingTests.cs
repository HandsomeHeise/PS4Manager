using System;
using System.IO;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public sealed class StrictListingTests
    {
        [Theory]
        [InlineData("prw-r--r-- 1 0 0 0 Aug 1 2026 mod.txt")]
        [InlineData("-rw-r--r-- 1 0 0 bad Aug 1 2026 mod.txt")]
        [InlineData("truncated row")]
        [InlineData("srwxr-xr-x 1 0 0 0 Aug 1 2026 socket")]
        public void Safety_listing_rejects_rows_the_browser_would_drop(string line)
        {Assert.Empty(UnixListingParser.Parse(line));Assert.Throws<InvalidDataException>(()=>UnixListingParser.ParseStrict(line));}
        [Fact] public void Complete_empty_and_regular_listings_are_valid()
        {
            Assert.Empty(UnixListingParser.ParseStrict("total 0\r\n"));
            Assert.Equal("mod.txt",Assert.Single(UnixListingParser.ParseStrict("-rw-r--r-- 1 0 0 0 Aug 1 2026 mod.txt")).Name);
        }
    }
}
