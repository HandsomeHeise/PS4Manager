using PS4Manager.UI;
using System;
using System.Net;
using System.Windows.Forms;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using PS4Manager.Core.Config;

namespace PS4Manager.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            DevExpress.XtraEditors.WindowsFormsSettings.EnableFormSkins();
            Application.OleRequired();

            // Transfer 1 + Durchsuchen 2 + Shell 1 (Spec „Verbindungslimit").
            ServicePointManager.DefaultConnectionLimit = FtpGates.ConnectionLimit;

            // Arbeitskopien der letzten Sitzung weg (Spec „Öffnen mit Rückschreiben").
            EditSessionManager.CleanWorkRoot(AppPaths.WorkRoot);

            var settings = LoadSettings();
            AppTheme.Current = settings.Theme == "Light" ? AppTheme.Light : AppTheme.Dark;
            Application.Run(new DashboardForm(settings));
        }

        /// <summary>
        /// Erster Start: Endpunkt und Ansicht aus den Einzel-Apps übernehmen
        /// (Spec „Migration", wiederholbar). Schlägt das Schreiben fehl, startet
        /// der Manager mit Voreinstellungen statt gar nicht.
        /// </summary>
        private static ManagerSettings LoadSettings()
        {
            try
            {
                return LegacySettingsImporter.ImportIfNeeded(
                    AppPaths.Settings, AppPaths.LegacyFileManagerSettings, AppPaths.LegacyGallerySettings);
            }
            catch (Exception)
            {
                return ManagerSettingsStore.Load(AppPaths.Settings);
            }
        }
    }
}
