using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PS4Manager.Core.Frame4
{
    public static class Frame4HexDump
    {
        public static string Format(ulong baseAddress, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            var lines = new List<string>();
            for (var offset = 0; offset < bytes.Length; offset += 16)
            {
                var chunk = bytes.Skip(offset).Take(16).ToArray();
                var hex = string.Join(" ", chunk.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))
                    .PadRight(47);
                var ascii = new string(chunk.Select(ToAscii).ToArray());
                lines.Add(Hex(baseAddress + (ulong)offset) + "  " + hex + "  |" + ascii + "|");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static char ToAscii(byte value)
        {
            return value >= 32 && value <= 126 ? (char)value : '.';
        }

        private static string Hex(ulong value)
        {
            return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
        }
    }
}
