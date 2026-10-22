using System;
using System.Text;

namespace FileManager.Core.Ps4
{
    /// <summary>
    /// username.dat unter /user/home/&lt;id&gt;: der Name als UTF-8 in einem mit
    /// NUL aufgefüllten 17-Byte-Feld (Befund Task 2 Nr. 20: „Benutzer1" + 8×
    /// NUL). Alles Unplausible ergibt null — dann zeigt der Schnellzugriff die
    /// ID.
    /// </summary>
    public static class UserNameReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string Parse(byte[] data)
        {
            if (data == null || data.Length == 0) return null;

            int end = Array.IndexOf(data, (byte)0);
            if (end < 0) end = data.Length;

            string name;
            try
            {
                name = StrictUtf8.GetString(data, 0, end).Trim();
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            if (name.Length == 0) return null;
            foreach (var c in name)
                if (char.IsControl(c)) return null;
            return name;
        }
    }
}
