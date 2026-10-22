using System;
using System.IO;
using System.Linq;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// Die Konsole erlaubt Dateinamen, die Windows ablehnt (':', '?', ein Punkt
    /// am Ende, „CON" …). Für jede lokale Kopie — Download, Arbeitskopie zum
    /// Öffnen, virtuelle Datei für Explorer — wird der Name hier entschärft.
    /// </summary>
    public static class LocalName
    {
        private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string Sanitize(string name)
        {
            var chars = (name ?? "").Select(c => Array.IndexOf(Invalid, c) >= 0 ? '_' : c).ToArray();
            var result = new string(chars).TrimEnd(' ', '.');
            if (result.Length == 0) return "_";

            int dot = result.IndexOf('.');
            var stem = dot >= 0 ? result.Substring(0, dot) : result;
            if (Reserved.Any(r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase))) result = "_" + result;
            return result;
        }
    }
}
