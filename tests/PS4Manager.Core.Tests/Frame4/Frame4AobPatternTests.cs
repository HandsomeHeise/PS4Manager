using System;
using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4AobPatternTests
    {
        [Fact]
        public void Parse_accepts_hex_bytes_and_wildcards()
        {
            var pattern = Frame4AobPattern.Parse("48 8B ?? ? 00");

            Assert.Equal(new byte[] { 0x48, 0x8B, 0x00, 0x00, 0x00 }, pattern.Bytes);
            Assert.Equal(new byte[] { 0x01, 0x01, 0x00, 0x00, 0x01 }, pattern.Mask);
            Assert.Equal(5, pattern.Length);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("4")]
        [InlineData("GG")]
        [InlineData("4800")]
        public void Parse_rejects_invalid_patterns(string text)
        {
            Assert.Throws<ArgumentException>(() => Frame4AobPattern.Parse(text));
        }
    }
}
