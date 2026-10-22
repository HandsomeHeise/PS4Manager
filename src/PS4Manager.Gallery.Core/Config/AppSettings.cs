namespace PS4Manager.Gallery.Core.Config
{
    /// <summary>
    /// Gestalt der Alt-Einstellungen der früheren Galerie-App. Seit dem
    /// PS4 Manager liest sie nur noch der Import beim ersten Start
    /// (<c>LegacySettingsImporter</c>) — deshalb keine Vorgabe-IP mehr:
    /// Werte kommen immer aus der Datei.
    /// </summary>
    public sealed class AppSettings
    {
        public string Ip { get; set; } = "";
        public int Port { get; set; } = 2121;
    }
}
