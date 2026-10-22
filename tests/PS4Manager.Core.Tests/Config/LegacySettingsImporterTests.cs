using System;
using System.IO;
using FileManager.Core.Config;
using PS4Manager.Core.Config;
using PS4Manager.Gallery.Core.Config;
using Xunit;

namespace PS4Manager.Core.Tests.Config
{
    public sealed class LegacySettingsImporterTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "manager-import-" + Guid.NewGuid().ToString("N"));
        private string UnifiedPath { get { return Path.Combine(_dir, "manager", "settings.json"); } }
        private string FileManagerPath { get { return Path.Combine(_dir, "file-manager", "settings.json"); } }
        private string GalleryPath { get { return Path.Combine(_dir, "gallery", "settings.json"); } }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        [Fact]
        public void File_manager_endpoint_and_view_preferences_win_when_both_legacy_settings_exist()
        {
            var oldFileManager = new FileManagerSettings
            {
                Ip = "10.0.0.10", Port = 2121, ExpertMode = true,
                ViewMode = "Tiles", LastPath = "/data/game", TreeWidth = 310
            };
            oldFileManager.PinnedFolders.Add("/data/GoldHEN");
            oldFileManager.ColumnWidths["Name"] = 320;
            FileManagerSettingsStore.Save(FileManagerPath, oldFileManager);
            SettingsStore.Save(GalleryPath, new AppSettings { Ip = "10.0.0.20", Port = 2122 });
            var originalFileManager = File.ReadAllBytes(FileManagerPath);
            var originalGallery = File.ReadAllBytes(GalleryPath);

            var imported = LegacySettingsImporter.ImportIfNeeded(UnifiedPath, FileManagerPath, GalleryPath);

            Assert.Equal("10.0.0.10", imported.Ip);
            Assert.Equal(2121, imported.Port);
            Assert.True(imported.ExpertMode);
            Assert.Equal("Tiles", imported.FileManager.ViewMode);
            Assert.Equal("/data/game", imported.FileManager.LastPath);
            Assert.Equal(310, imported.FileManager.TreeWidth);
            Assert.Equal(new[] { "/data/GoldHEN" }, imported.FileManager.PinnedFolders);
            Assert.Equal(320, imported.FileManager.ColumnWidths["Name"]);
            Assert.True(imported.MigrationCompleted);
            Assert.True(ManagerSettingsStore.Load(UnifiedPath).MigrationCompleted);
            Assert.Equal(originalFileManager, File.ReadAllBytes(FileManagerPath));
            Assert.Equal(originalGallery, File.ReadAllBytes(GalleryPath));
        }

        [Fact]
        public void Gallery_endpoint_is_used_when_file_manager_settings_are_absent()
        {
            SettingsStore.Save(GalleryPath, new AppSettings { Ip = "10.0.0.20", Port = 2122 });

            var imported = LegacySettingsImporter.ImportIfNeeded(UnifiedPath, FileManagerPath, GalleryPath);

            Assert.Equal("10.0.0.20", imported.Ip);
            Assert.Equal(2122, imported.Port);
            Assert.True(imported.MigrationCompleted);
        }

        [Fact]
        public void Repeating_a_completed_import_does_not_overwrite_unified_changes()
        {
            FileManagerSettingsStore.Save(FileManagerPath,
                new FileManagerSettings { Ip = "10.0.0.10", Port = 2121 });
            var first = LegacySettingsImporter.ImportIfNeeded(UnifiedPath, FileManagerPath, GalleryPath);
            first.Ip = "10.0.0.99";
            ManagerSettingsStore.Save(UnifiedPath, first);

            var repeated = LegacySettingsImporter.ImportIfNeeded(UnifiedPath, FileManagerPath, GalleryPath);

            Assert.Equal("10.0.0.99", repeated.Ip);
        }

        [Fact]
        public void Existing_unified_settings_are_preserved_even_without_a_completion_marker()
        {
            FileManagerSettingsStore.Save(FileManagerPath,
                new FileManagerSettings { Ip = "10.0.0.10", Port = 2121 });
            ManagerSettingsStore.Save(UnifiedPath,
                new ManagerSettings { Ip = "10.0.0.99", Port = 2123 });

            var imported = LegacySettingsImporter.ImportIfNeeded(UnifiedPath, FileManagerPath, GalleryPath);

            Assert.Equal("10.0.0.99", imported.Ip);
            Assert.False(imported.MigrationCompleted);
        }

        [Fact]
        public void A_failed_write_does_not_mark_migration_complete()
        {
            Directory.CreateDirectory(UnifiedPath);

            Assert.ThrowsAny<Exception>(() => LegacySettingsImporter.ImportIfNeeded(
                UnifiedPath, FileManagerPath, GalleryPath));

            Directory.Delete(UnifiedPath);
            var retry = LegacySettingsImporter.ImportIfNeeded(UnifiedPath, FileManagerPath, GalleryPath);
            Assert.True(retry.MigrationCompleted);
            Assert.True(ManagerSettingsStore.Load(UnifiedPath).MigrationCompleted);
        }
    }
}
