using System;
using System.Text;

namespace Ps4.Core.Titles
{
    /// <summary>
    /// Liest den TITLE-Eintrag aus Sonys param.sfo.
    ///
    /// Format (little-endian): Header 0x00 Magic "\0PSF", 0x08 Offset der
    /// Schlüsseltabelle, 0x0C Offset der Datentabelle, 0x10 Eintragszahl.
    /// Ab 0x14 je Eintrag 16 Bytes: u16 Schlüssel-Offset, u16 Format,
    /// u32 Datenlänge, u32 Maximallänge, u32 Daten-Offset. Schlüssel sind
    /// nullterminiertes ASCII, TITLE ist ein UTF-8-String.
    ///
    /// Jede Abweichung liefert null — ein fehlender Name ist ein Fallback
    /// auf die Title-ID, nie ein Absturz (Spec, Fehlerbehandlung).
    /// </summary>
    public static class SfoReader
    {
        private const uint Magic = 0x46535000;      // "\0PSF" als u32 little-endian

        public static string ReadTitle(byte[] data)
        {
            return ReadString(data, "TITLE");
        }

        public static string ReadString(byte[] data, string key)
        {
            try
            {
                if (data == null || data.Length < 0x14) return null;
                if (BitConverter.ToUInt32(data, 0x00) != Magic) return null;

                uint keyTable = BitConverter.ToUInt32(data, 0x08);
                uint dataTable = BitConverter.ToUInt32(data, 0x0C);
                uint count = BitConverter.ToUInt32(data, 0x10);

                for (uint i = 0; i < count; i++)
                {
                    long entry = 0x14 + i * 16;
                    if (entry + 16 > data.Length) return null;

                    ushort keyOffset = BitConverter.ToUInt16(data, (int)entry);
                    uint length = BitConverter.ToUInt32(data, (int)entry + 4);
                    uint dataOffset = BitConverter.ToUInt32(data, (int)entry + 12);

                    string name = ReadKey(data, (long)keyTable + keyOffset);
                    if (name != key) continue;

                    long start = (long)dataTable + dataOffset;
                    if (start < 0 || start + length > data.Length) return null;

                    string value = Encoding.UTF8.GetString(data, (int)start, (int)length);
                    int nul = value.IndexOf('\0');
                    return nul >= 0 ? value.Substring(0, nul) : value;
                }
            }
            catch (Exception)
            {
                // Absichtlich breit: korrupte Datei = kein Name, mehr nicht.
            }

            return null;
        }

        private static string ReadKey(byte[] data, long offset)
        {
            if (offset < 0 || offset >= data.Length) return null;

            int end = (int)offset;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, (int)offset, end - (int)offset);
        }
    }
}
