using System.Text;
using Ps4.Core.Ftp;

namespace FileManager.Core.Presentation
{
    /// <summary>
    /// Namen für die Anzeige (Spec-Entscheidung zu Befund 6): FtpClient liest
    /// LIST in Windows-1252, damit jeder Name byte-genau wieder trifft. Hat ein
    /// anderes Programm einen Namen als UTF-8 abgelegt, erscheint er so als
    /// „Ã„rger" — hier wird er für die Anzeige zurückgedeutet. Für jede
    /// Operation bleibt der Draht-Name maßgeblich.
    /// </summary>
    public static class DisplayNames
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string For(string wireName)
        {
            if (string.IsNullOrEmpty(wireName)) return wireName;

            bool ascii = true;
            foreach (var c in wireName)
            {
                if (c > 0x7F) { ascii = false; break; }
            }
            if (ascii) return wireName;

            var bytes = FtpClient.PathEncoding.GetBytes(wireName);
            if (FtpClient.PathEncoding.GetString(bytes) != wireName) return wireName;   // nicht verlustfrei rückwandelbar
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return wireName;                                                        // kein UTF-8 — Name wie gelistet
            }
        }
    }
}
