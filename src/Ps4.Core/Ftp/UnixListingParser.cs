using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Parst GoldHENs Unix-artiges LIST-Format:
    ///     drwxr-xr-x 2 0 0 512 Aug  1 2026 CUSA00411
    ///     -rwxrwx--- 1 0 0 592 Aug  1 2026 name mit leerzeichen.dat
    ///     lrwxrwxrwx 1 0 0 9 Sep  1 2026 usb -> /mnt/usb0
    /// Feldaufbau: Rechte, Links, UID, GID, Größe, Monat, Tag, Jahr/Zeit, Name.
    /// Der Name ist ab Feld 9 ALLES (Leerzeichen inklusive) — deshalb Split
    /// mit Obergrenze 9 statt naivem Zerhacken.
    ///
    /// Datum: GoldHEN schreibt immer „Mon TT JJJJ". Die ls-übliche Form
    /// „Mon TT HH:MM" (ohne Jahr) wird trotzdem verstanden: laufendes Jahr,
    /// oder Vorjahr, wenn das Datum sonst in der Zukunft läge.
    /// </summary>
    public static class UnixListingParser
    {
        private static readonly string[] Months =
            { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        public static IReadOnlyList<FtpEntry> ParseStrict(string listing)
        {
            if (listing == null || listing.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Missing or oversized FTP listing.");
            foreach (var raw in listing.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = raw.Split(new[] { ' ' }, 9, StringSplitOptions.RemoveEmptyEntries);
                long number;
                if (fields.Length == 2 && fields[0] == "total" &&
                    long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out number)) continue;
                if (fields.Length != 9 || fields[0].Length < 10 || "-dl".IndexOf(fields[0][0]) < 0 ||
                    !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out number) ||
                    ParseDate(fields[5], fields[6], fields[7], DateTime.Today) == DateTime.MinValue)
                    throw new InvalidDataException("Unsupported or incomplete FTP listing row.");
                var name = fields[8];
                if (fields[0][0] == 'l')
                {
                    var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow <= 0 || arrow + 4 == name.Length) throw new InvalidDataException("Incomplete FTP link row.");
                    name = name.Substring(0, arrow);
                }
                if (name.Length == 0 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
                    throw new InvalidDataException("Ambiguous FTP listing name.");
                foreach (var c in raw) if (char.IsControl(c)) throw new InvalidDataException("Control character in FTP listing.");
            }
            return Parse(listing);
        }

        public static IReadOnlyList<FtpEntry> Parse(string listing)
        {
            return Parse(listing, DateTime.Today);
        }

        public static IReadOnlyList<FtpEntry> Parse(string listing, DateTime today)
        {
            var entries = new List<FtpEntry>();
            if (string.IsNullOrEmpty(listing)) return entries;

            foreach (var raw in listing.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = raw.Split(new[] { ' ' }, 9, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;                       // "total 12", Müll
                if (fields[0].Length < 10) continue;                   // kein Rechte-Feld

                FtpEntryKind kind;
                switch (fields[0][0])
                {
                    case 'd': kind = FtpEntryKind.Directory; break;
                    case '-': kind = FtpEntryKind.File; break;
                    case 'l': kind = FtpEntryKind.Link; break;
                    default: continue;                                 // Geräte, Pipes, Sockets
                }

                long size;
                if (!long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out size)) continue;

                string name = fields[8];
                string target = null;
                if (kind == FtpEntryKind.Link)
                {
                    int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow >= 0)
                    {
                        target = name.Substring(arrow + 4);
                        name = name.Substring(0, arrow);
                    }
                }
                if (name == "." || name == "..") continue;

                entries.Add(new FtpEntry(name, kind, size, ParseDate(fields[5], fields[6], fields[7], today), target));
            }

            return entries;
        }

        internal static DateTime ParseDate(string month, string day, string yearOrTime, DateTime today)
        {
            int m = Array.FindIndex(Months, x => string.Equals(x, month, StringComparison.OrdinalIgnoreCase)) + 1;
            if (m == 0) return DateTime.MinValue;

            int d;
            if (!int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out d)) return DateTime.MinValue;

            try
            {
                int colon = yearOrTime.IndexOf(':');
                if (colon < 0)
                {
                    int year;
                    if (!int.TryParse(yearOrTime, NumberStyles.None, CultureInfo.InvariantCulture, out year))
                        return DateTime.MinValue;
                    return new DateTime(year, m, d);
                }

                int hour, minute;
                if (!int.TryParse(yearOrTime.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out hour) ||
                    !int.TryParse(yearOrTime.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out minute))
                    return DateTime.MinValue;

                var candidate = new DateTime(today.Year, m, d, hour, minute, 0);
                if (candidate > today.Date.AddDays(1)) candidate = candidate.AddYears(-1);
                return candidate;
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;                              // 31. Februar o. ä.
            }
        }
    }
}
