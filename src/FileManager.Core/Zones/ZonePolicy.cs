using System.Collections.Generic;
using FileManager.Core.Paths;

namespace FileManager.Core.Zones
{
    /// <summary>
    /// Freigabeliste nach Spec „Zonen": Was nicht ausdrücklich frei oder
    /// Warnzone ist, ist gesperrt. Eine Regel trifft nur, was UNTERHALB ihrer
    /// Wurzel liegt — /data selbst zu löschen ist gesperrt, darin zu arbeiten
    /// frei. Lesen ist immer erlaubt; die Zonen gelten nur für Schreibzugriffe.
    /// Pfadbasiert, weil die Rechteangaben des Servers nicht stimmen (/system
    /// meldet 0777).
    ///
    /// Invariante: keine Free- oder Warn-Wurzel liegt unterhalb einer
    /// anderen. Classify/Strictest prüfen jede Wurzel unabhängig gegen den
    /// flachen Listen — eine verschachtelte Wurzel (z. B. „/data/x" als
    /// eigener Eintrag neben „/data") würde nicht strenger, sondern nur
    /// redundant geprüft; die Reihenfolge der Listen entscheidet dann
    /// zufällig, welche Zone gewinnt, statt dass die engere Regel zählt.
    /// ZonePolicyTests.No_zone_root_lies_inside_another_root wacht darüber.
    /// </summary>
    public sealed class ZonePolicy
    {
        private static readonly string[] FreeRoots =
        {
            "/data",
            "/mnt/usb0", "/mnt/usb1", "/mnt/usb2", "/mnt/usb3",
            "/mnt/usb4", "/mnt/usb5", "/mnt/usb6", "/mnt/usb7",
            "/user/av_contents"
        };

        private static readonly string[] WarnRoots =
        {
            "/user/home", "/user/app", "/user/patch", "/user/addcont", "/user/appmeta", "/mnt/ext0/user"
        };

        /// <summary>Nur für Tests: die Freizonen-Wurzeln, unverändert (siehe Invariante oben).</summary>
        internal static IReadOnlyList<string> FreeRootsForTests { get { return FreeRoots; } }

        /// <summary>Nur für Tests: die Warnzonen-Wurzeln, unverändert (siehe Invariante oben).</summary>
        internal static IReadOnlyList<string> WarnRootsForTests { get { return WarnRoots; } }

        /// <summary>Stuft „gesperrt" zu „Warnung" herab; Warnungen bleiben.</summary>
        public bool ExpertMode { get; set; }

        public Zone Classify(string path)
        {
            var p = RemotePath.Canonical(path);
            foreach (var root in FreeRoots)
                if (RemotePath.IsStrictlyBelow(p, root)) return Zone.Free;
            foreach (var root in WarnRoots)
                if (RemotePath.IsStrictlyBelow(p, root)) return Zone.Warn;
            return Zone.Locked;
        }

        public Zone Effective(string path)
        {
            var zone = Classify(path);
            return ExpertMode && zone == Zone.Locked ? Zone.Warn : zone;
        }

        public Zone Strictest(IEnumerable<string> paths)
        {
            var result = Zone.Free;
            foreach (var path in paths)
            {
                var zone = Effective(path);
                if (zone > result) result = zone;
            }
            return result;
        }

        public static string Describe(Zone zone)
        {
            switch (zone)
            {
                case Zone.Free: return "Free";
                case Zone.Warn: return "Warning";
                default: return "Locked";
            }
        }
    }
}
