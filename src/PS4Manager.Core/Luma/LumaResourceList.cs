using System;
using System.Collections.Generic;

namespace PS4Manager.Core.Luma
{
    public sealed class LumaResourceList
    {
        public LumaResourceList(IReadOnlyList<LumaResourceEntry> entries, bool truncated)
        {
            Entries = entries ?? new LumaResourceEntry[0];
            Truncated = truncated;
        }

        public IReadOnlyList<LumaResourceEntry> Entries { get; }
        public bool Truncated { get; }

        public static LumaResourceList Parse(string payload)
        {
            var entries = new List<LumaResourceEntry>();
            var truncated = false;
            foreach (var raw in (payload ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("--", StringComparison.Ordinal))
                {
                    truncated = true;
                    continue;
                }
                var split = line.LastIndexOf(' ');
                if (split <= 0) continue;
                entries.Add(new LumaResourceEntry(line.Substring(0, split), line.Substring(split + 1)));
            }
            return new LumaResourceList(entries, truncated);
        }
    }

    public sealed class LumaResourceEntry
    {
        public LumaResourceEntry(string name, string state)
        {
            Name = name ?? "";
            State = state ?? "";
        }

        public string Name { get; }
        public string State { get; }
    }
}
