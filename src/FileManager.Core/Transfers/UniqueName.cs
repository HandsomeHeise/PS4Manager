using System;

namespace FileManager.Core.Transfers
{
    /// <summary>
    /// „Beide behalten" wie im Explorer: name (2).ext, name (3).ext …
    /// Groß-/Kleinschreibung zählt, wie auf der Konsole. Ordner werden nie an
    /// einem Punkt geteilt; Namen mit führendem Punkt gelten als endungslos.
    /// </summary>
    public static class UniqueName
    {
        public static string For(string name, bool isDirectory, Func<string, bool> exists)
        {
            if (!exists(name)) return name;

            int dot = isDirectory ? -1 : name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name;
            string extension = dot > 0 ? name.Substring(dot) : "";

            for (int i = 2; ; i++)
            {
                var candidate = stem + " (" + i + ")" + extension;
                if (!exists(candidate)) return candidate;
            }
        }
    }
}
