using System.IO;
using FileManager.Core.Config;
using PS4Manager.Gallery.Core.Config;

namespace PS4Manager.Core.Config
{
    public static class LegacySettingsImporter
    {
        public static ManagerSettings ImportIfNeeded(string unifiedPath, string fileManagerPath, string galleryPath)
        {
            if (File.Exists(unifiedPath)) return ManagerSettingsStore.Load(unifiedPath);

            var settings = new ManagerSettings();
            if (File.Exists(fileManagerPath))
            {
                var legacy = FileManagerSettingsStore.Load(fileManagerPath);
                settings.Ip = legacy.Ip;
                settings.Port = legacy.Port;
                settings.ExpertMode = legacy.ExpertMode;
                settings.FileManager.ViewMode = legacy.ViewMode;
                settings.FileManager.LastPath = legacy.LastPath;
                settings.FileManager.PinnedFolders = legacy.PinnedFolders;
                settings.FileManager.ColumnWidths = legacy.ColumnWidths;
                settings.FileManager.TreeWidth = legacy.TreeWidth;
            }
            else if (File.Exists(galleryPath))
            {
                var legacy = SettingsStore.Load(galleryPath);
                settings.Ip = legacy.Ip;
                settings.Port = legacy.Port;
            }

            settings.MigrationCompleted = true;
            ManagerSettingsStore.Save(unifiedPath, settings);
            return settings;
        }
    }
}
