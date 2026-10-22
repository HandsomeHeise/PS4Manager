using System.IO;
using Ps4.Core.Titles;
using Xunit;

namespace Ps4.Core.Tests
{
    public class SfoReaderTests
    {
        private static byte[] RealSfo()
        {
            return File.ReadAllBytes(Path.Combine("TestData", "param.sfo"));
        }

        [Fact]
        public void The_real_gta_v_sfo_yields_its_title()
        {
            Assert.Equal("Grand Theft Auto V", SfoReader.ReadTitle(RealSfo()));
        }

        [Fact]
        public void Garbage_bytes_yield_null_instead_of_throwing()
        {
            Assert.Null(SfoReader.ReadTitle(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.Null(SfoReader.ReadTitle(new byte[0]));
            Assert.Null(SfoReader.ReadTitle(null));
        }

        [Fact]
        public void A_truncated_real_file_yields_null()
        {
            var real = RealSfo();
            var cut = new byte[64];
            System.Array.Copy(real, cut, 64);
            Assert.Null(SfoReader.ReadTitle(cut));
        }

        [Fact]
        public void Uint_wraparound_in_datatable_yields_null()
        {
            // Regression test für Integer-Overflow: dataTable=0xFFFFFFF0u + dataOffset=0x20u
            // würde unter 32-Bit-Arithmetik zu 16 wrappen, aber sollte bei korrekter
            // 64-Bit-Widening das Bounds-Check fehlschlagen lassen.

            var buffer = new byte[256];

            // PSF Magic bei Offset 0
            System.BitConverter.GetBytes(0x46535000u).CopyTo(buffer, 0);

            // keyTable bei Offset 0x08 = 0x40 (zeigt auf Key-Daten)
            System.BitConverter.GetBytes(0x40u).CopyTo(buffer, 0x08);

            // dataTable bei Offset 0x0C = 0xFFFFFFF0u (nahe uint.MaxValue)
            System.BitConverter.GetBytes(0xFFFFFFF0u).CopyTo(buffer, 0x0C);

            // count bei Offset 0x10 = 1 (ein Eintrag)
            System.BitConverter.GetBytes(1u).CopyTo(buffer, 0x10);

            // Eintrag bei Offset 0x14 (16 Bytes total)
            // keyOffset = 0 (Key ist bei keyTable + 0 = 0x40)
            System.BitConverter.GetBytes((ushort)0).CopyTo(buffer, 0x14);

            // format = 0x0400 (UTF-8 String)
            System.BitConverter.GetBytes((ushort)0x0400).CopyTo(buffer, 0x16);

            // length = 4
            System.BitConverter.GetBytes(4u).CopyTo(buffer, 0x18);

            // max length = 4
            System.BitConverter.GetBytes(4u).CopyTo(buffer, 0x1C);

            // dataOffset bei Offset 0x20 = 0x20u (wraps zusammen mit dataTable)
            System.BitConverter.GetBytes(0x20u).CopyTo(buffer, 0x20);

            // "TITLE" Key bei Offset 0x40
            System.Text.Encoding.ASCII.GetBytes("TITLE\0").CopyTo(buffer, 0x40);

            // Mit dem Fix: start = (long)0xFFFFFFF0 + 0x20 = 4294967312, out of bounds
            // Ohne Fix: start = 0xFFFFFFF0 + 0x20 wraps zu 16, in bounds (buffer ist 256 Bytes)
            Assert.Null(SfoReader.ReadTitle(buffer));
        }
    }
}
