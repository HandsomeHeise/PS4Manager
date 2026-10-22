using System;
using System.Globalization;

namespace FileManager.Core.Presentation
{
    /// <summary>Sizes, rates and dates formatted consistently for the English UI.</summary>
    public static class SizeFormat
    {
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
        private const double Kib = 1024.0;
        private const double Mib = 1024.0 * 1024;
        private const double Gib = 1024.0 * 1024 * 1024;

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(English) + " B";
            if (bytes < 1024L * 1024) return ((bytes + 1023) / 1024).ToString(English) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / Mib).ToString("0.0", English) + " MB";
            return (bytes / Gib).ToString("0.0", English) + " GB";
        }

        public static string Rate(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "";
            if (bytesPerSecond < Mib) return (bytesPerSecond / Kib).ToString("0", English) + " KB/s";
            return (bytesPerSecond / Mib).ToString("0.0", English) + " MB/s";
        }

        public static string Date(DateTime date)
        {
            return date == DateTime.MinValue ? "" : date.ToString("M/d/yyyy", English);
        }
    }
}
