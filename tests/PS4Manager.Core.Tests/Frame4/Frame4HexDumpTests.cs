using PS4Manager.Core.Frame4;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4HexDumpTests
    {
        [Fact]
        public void Format_includes_ascii_column_and_stable_rows()
        {
            var text = Frame4HexDump.Format(0x400000, new byte[]
            {
                0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0xFF, 0x20,
                0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                0x49
            });

            Assert.Equal(
                "0x400000  48 65 6C 6C 6F 00 FF 20 41 42 43 44 45 46 47 48  |Hello.. ABCDEFGH|\r\n" +
                "0x400010  49                                               |I|", text);
        }
    }
}
