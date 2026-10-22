using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Mods
{
    public sealed class ModCompatibility
    {
        public ModCompatibility(string titleId, string version)
        { TitleId = NormalizeTitleId(titleId); GameVersion = NormalizeVersion(version); }
        public string TitleId { get; }
        public string GameVersion { get; }
        public string Key { get { return TitleId + "@" + GameVersion; } }
        private static string Input(string value)
        {
            if (value == null || Regex.IsMatch(value, "[\\x00-\\x1f\\x7f]")) throw new FormatException("Invalid compatibility value.");
            return value.Trim();
        }
        public static string NormalizeTitleId(string value)
        {
            value = Input(value).ToUpperInvariant();
            if (!Regex.IsMatch(value, "^CUSA[0-9]{5}$")) throw new FormatException("Invalid title ID.");
            return value;
        }
        public static string NormalizeVersion(string value)
        {
            var match = Regex.Match(Input(value), "^([0-9]{1,3})\\.([0-9]{1,3})$");
            if (!match.Success) throw new FormatException("Unknown or invalid game version.");
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "." +
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture).ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
