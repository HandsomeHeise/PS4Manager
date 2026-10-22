using System;
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.Mods
{
    public sealed class ModGameDiscoveryReport
    {
        public ModGameDiscoveryReport(IEnumerable<ModGame> games, bool enumerationComplete)
        { Games = Array.AsReadOnly(games.ToArray()); EnumerationComplete = enumerationComplete; }
        public IReadOnlyList<ModGame> Games { get; }
        public bool EnumerationComplete { get; }
    }
}
