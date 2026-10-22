using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PS4Manager.Gallery.Core.Config
{
    /// <summary>
    /// Zwei Felder, eine Datei. Handgeschriebenes Mini-JSON statt NuGet
    /// (Spec-Entscheidung): der Regex-Leser toleriert alles, was er nicht
    /// versteht, und fällt auf die Defaults zurück.
    /// </summary>
    public static class SettingsStore
    {
        private static readonly Regex IpPattern =
            new Regex("\"Ip\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex PortPattern =
            new Regex("\"Port\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        public static AppSettings Load(string path)
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(path)) return settings;
                var text = File.ReadAllText(path);

                var ip = IpPattern.Match(text);
                if (ip.Success && ip.Groups[1].Value.Length > 0)
                    settings.Ip = ip.Groups[1].Value;

                var port = PortPattern.Match(text);
                int parsed;
                if (port.Success && int.TryParse(port.Groups[1].Value, out parsed)
                    && parsed > 0 && parsed <= 65535)
                    settings.Port = parsed;
            }
            catch (Exception)
            {
                // Korrupte Settings sind ein Neuanfang, kein Absturz.
            }
            return settings;
        }

        public static void Save(string path, AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "{\"Ip\":\"" + settings.Ip + "\",\"Port\":" + settings.Port + "}");
        }
    }
}
