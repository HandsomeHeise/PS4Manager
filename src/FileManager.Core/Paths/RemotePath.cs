using System;
using System.Collections.Generic;

namespace FileManager.Core.Paths
{
    /// <summary>
    /// Unix-Pfade der Konsole. Groß-/Kleinschreibung zählt (Ordinal) — das
    /// PS4-Dateisystem unterscheidet sie.
    /// </summary>
    public static class RemotePath
    {
        public const string Root = "/";

        private const string UserData = "/user/data";
        private const string Data = "/data";

        private static readonly string[] CaseInsensitiveRoots =
        {
            "/mnt/usb0", "/mnt/usb1", "/mnt/usb2", "/mnt/usb3",
            "/mnt/usb4", "/mnt/usb5", "/mnt/usb6", "/mnt/usb7"
        };

        /// <summary>Führt „/“ voran, faltet „//“, „.“ und „..“, entfernt ein Schluss-„/“. „..“ über die Wurzel bleibt „/“.</summary>
        public static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var segment in (path ?? "").Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(segment);
            }
            return "/" + string.Join("/", parts);
        }

        public static string Combine(string dir, string name)
        {
            return Normalize(Normalize(dir) + "/" + name);
        }

        public static string Parent(string path)
        {
            var p = Normalize(path);
            int slash = p.LastIndexOf('/');
            return slash <= 0 ? Root : p.Substring(0, slash);
        }

        public static string Name(string path)
        {
            var p = Normalize(path);
            return p == Root ? "" : p.Substring(p.LastIndexOf('/') + 1);
        }

        public static bool IsSameOrBelow(string path, string ancestor)
        {
            var p = Normalize(path);
            var a = Normalize(ancestor);
            if (a == Root) return true;
            return p == a || p.StartsWith(a + "/", StringComparison.Ordinal);
        }

        public static bool IsStrictlyBelow(string path, string ancestor)
        {
            var p = Normalize(path);
            return p != Normalize(ancestor) && IsSameOrBelow(p, ancestor);
        }

        /// <summary>
        /// /user/data und /data sind auf der Konsole derselbe Ordner (Spec,
        /// „Verifizierte Fakten"). Nur für Zonenentscheidungen — angezeigt wird
        /// der Pfad, den der Anwender gewählt hat.
        /// </summary>
        public static string Canonical(string path)
        {
            var p = Normalize(path);
            if (IsSameOrBelow(p, UserData)) return Data + p.Substring(UserData.Length);
            return p;
        }

        /// <summary>
        /// USB-Sticks sind FAT/exFAT: dort ist „README.TXT" dieselbe Datei wie
        /// „Readme.txt" (Abschluss-Review Plan 1, Important #2). Gilt für die
        /// Laufwerkswurzel und alles darunter.
        /// </summary>
        public static bool IsCaseInsensitive(string path)
        {
            var p = Normalize(path);
            foreach (var root in CaseInsensitiveRoots)
            {
                if (IsSameOrBelow(p, root)) return true;
            }
            return false;
        }

        /// <summary>Namensvergleich für Einträge eines Ordners — auf USB ohne Groß-/Kleinschreibung.</summary>
        public static StringComparer NameComparer(string directory)
        {
            return IsCaseInsensitive(directory) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        /// <summary>Ziel einer Verknüpfung als absoluter Pfad: absolut bleibt absolut, relativ gilt ab dem Ordner der Verknüpfung.</summary>
        public static string ResolveLink(string linkDirectory, string target)
        {
            if (string.IsNullOrEmpty(target)) throw new ArgumentException("The shortcut has no target.", nameof(target));
            return target.StartsWith("/", StringComparison.Ordinal) ? Normalize(target) : Combine(linkDirectory, target);
        }
    }
}
