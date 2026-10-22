using System;
using System.IO;

namespace PS4Manager.App
{
    /// <summary>Lokale Daten des PS4 Managers (Spec „Einstellungen und Daten").</summary>
    internal static class AppPaths
    {
        private static readonly string LocalAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static readonly string Root = Path.Combine(LocalAppData, "PS4Manager");

        public static readonly string Settings = Path.Combine(Root, "settings.json");

        /// <summary>Arbeitskopien fürs Öffnen mit Rückschreiben.</summary>
        public static readonly string WorkRoot = Path.Combine(Root, "open");

        /// <summary>Sicherungen vor Änderungen (GoldHEN); die Konsole bleibt unberührt.</summary>
        public static readonly string GoldHenBackupRoot = Path.Combine(Root, "backups", "goldhen");

        /// <summary>Heruntergeladene Updatepakete vor der Übergabe an PS4Manager.Updater.</summary>
        public static readonly string UpdateRoot = Path.Combine(Root, "updates");
        public static readonly string ModRoot = Path.Combine(Root, "mods");

        // Die Einzel-Apps behalten ihre Dateien (Spec: Migration lässt alles
        // unangetastet und ist wiederholbar). Hier stehen nur ihre Pfade für den
        // einmaligen Import — bewusst als Text, damit der Import nicht von den
        // Projekten der Einzel-Apps abhängt.
        public static readonly string LegacyFileManagerSettings =
            Path.Combine(LocalAppData, "PS4FileManager", "settings.json");

        public static readonly string LegacyGallerySettings =
            Path.Combine(LocalAppData, "ScreenshotGallery", "settings.json");
    }
}
