using System.Collections.Generic;
using System.Linq;
using FileManager.Core.Zones;

namespace FileManager.Core.Presentation
{
    /// <summary>Die Statuszeile (Spec): Verbindung, Elemente, Auswahl mit Größe, Zone.</summary>
    public static class StatusText
    {
        private const string Separator = " · ";

        public static string Line(bool connected, int itemCount, IReadOnlyList<FolderRow> selection, Zone zone)
        {
            if (!connected) return "Disconnected";
            var parts = new List<string> { "Connected", Items(itemCount) };
            if (selection != null && selection.Count > 0)
            {
                var files = selection.Where(r => !r.IsFolder && !r.IsLink).ToList();
                var chosen = selection.Count + " selected";
                if (files.Count > 0) chosen += " (" + SizeFormat.Bytes(files.Sum(r => r.Entry.Size)) + ")";
                parts.Add(chosen);
            }
            parts.Add("Zone: " + ZonePolicy.Describe(zone));
            return string.Join(Separator, parts);
        }

        public static string Items(int count)
        {
            return count == 1 ? "1 item" : count + " items";
        }
    }
}
