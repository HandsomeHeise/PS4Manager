using System;
using System.Globalization;

namespace PS4Manager.Gallery.Core.Ps4
{
    /// <summary>Ein Foto oder Clip auf der PS4 — reine Daten, keine FTP-Kenntnis.</summary>
    public sealed class MediaItem
    {
        public MediaItem(MediaKind kind, string titleId, string hashDir, string fileName,
            long size, DateTime timestamp)
        {
            Kind = kind;
            TitleId = titleId;
            HashDir = hashDir;
            FileName = fileName;
            Size = size;
            Timestamp = timestamp;
        }

        public MediaKind Kind { get; }
        public string TitleId { get; }
        public string HashDir { get; }
        public string FileName { get; }
        public long Size { get; }
        public DateTime Timestamp { get; }

        /// <summary>
        /// PS4-Dateinamen kodieren die Aufnahmezeit: JJJJMMTT_HHMMSS_XXXXXXXX.<ext>.
        /// Gilt für beide Zweige gleichermaßen. Bei jeder Abweichung gilt der
        /// Fallback (Spec: FTP-Änderungsdatum bzw. was der Aufrufer sonst hat) —
        /// lieber falsch sortiert als abgestürzt.
        /// </summary>
        public static DateTime ParseTimestamp(string fileName, DateTime fallback)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length < 15) return fallback;

            DateTime parsed;
            if (DateTime.TryParseExact(fileName.Substring(0, 15), "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed;

            return fallback;
        }
    }
}
