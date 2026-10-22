using System.Text.RegularExpressions;

namespace Ps4.Core.Titles
{
    /// <summary>
    /// Vier Großbuchstaben, fünf Ziffern: CUSA (Spiele), PPSA, PCAS, NPXS
    /// (Systemanwendungen) … Bewusst [0-9] statt \d — \d nähme in .NET auch
    /// andere Unicode-Ziffern.
    /// </summary>
    public static class TitleId
    {
        private static readonly Regex Pattern =
            new Regex("^[A-Z]{4}[0-9]{5}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsMatch(string name)
        {
            return name != null && Pattern.IsMatch(name);
        }
    }
}
