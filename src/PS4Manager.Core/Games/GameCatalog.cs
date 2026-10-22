using System;
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.Games
{
    public sealed class GameCatalog
    {
        public IReadOnlyList<GameEntry> Entries { get; } = Array.AsReadOnly(new[] {
            new GameEntry("bo3-zombies", "Call of Duty: Black Ops III", "CUSA02624", "md5-bf1ffcd3", "Offline Zombies", true)
        });

        public IReadOnlyList<GameEntry> Filter(string search, bool onlyInstalled, IReadOnlyList<InstalledGame> installed)
        {
            search = (search ?? "").Trim();
            return Array.AsReadOnly(Entries.Where(g => (g.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                g.TitleId.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) && (!onlyInstalled ||
                (installed != null && installed.Any(i => i.TitleId == g.TitleId && i.State == InstalledState.Installed)))).ToArray());
        }
    }
}
