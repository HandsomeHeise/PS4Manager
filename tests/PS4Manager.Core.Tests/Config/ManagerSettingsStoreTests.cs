using System;
using System.IO;
using PS4Manager.Core.Config;
using Xunit;

namespace PS4Manager.Core.Tests.Config
{
    public sealed class ManagerSettingsStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "manager-settings-" + Guid.NewGuid().ToString("N"));
        private string SettingsPath { get { return Path.Combine(_dir, "settings.json"); } }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        [Fact]
        public void Older_settings_receive_safe_mod_defaults_and_local_profiles_round_trip()
        {
            Directory.CreateDirectory(_dir);File.WriteAllText(SettingsPath,"{\"Ip\":\"10.0.0.1\"}");
            var settings=ManagerSettingsStore.Load(SettingsPath);Assert.Equal("https://ps4manager.devkit.online/",settings.Mods.CatalogOrigin);
            var id=Guid.NewGuid();settings.Mods.Profiles.Add(new ModProfileSettings{Id=id,Name="Bedroom"});settings.Mods.SelectedProfileId=id;
            ManagerSettingsStore.Save(SettingsPath,settings);var loaded=ManagerSettingsStore.Load(SettingsPath);
            Assert.Equal(id,loaded.Mods.SelectedProfileId);Assert.Equal("Bedroom",Assert.Single(loaded.Mods.Profiles).Name);
        }
        [Fact]
        public void Corrupt_settings_fall_back_to_repaired_defaults()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "not json");

            var settings = ManagerSettingsStore.Load(SettingsPath);

            Assert.Equal(2121, settings.Port);
            Assert.NotNull(settings.FileManager.ColumnWidths);
            Assert.NotNull(settings.FileManager.PinnedFolders);
        }

        [Fact]
        public void Save_replaces_the_file_atomically_and_round_trips()
        {
            ManagerSettingsStore.Save(SettingsPath, new ManagerSettings { Ip = "192.168.0.10", Port = 2121 });
            ManagerSettingsStore.Save(SettingsPath, new ManagerSettings { Ip = "192.168.0.20", Port = 2122 });

            var loaded = ManagerSettingsStore.Load(SettingsPath);
            Assert.Equal("192.168.0.20", loaded.Ip);
            Assert.Equal(2122, loaded.Port);
            Assert.False(File.Exists(SettingsPath + ".tmp"));
        }

        [Fact]
        public void Missing_and_invalid_fields_are_repaired_after_deserialization()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{\"Ip\":\"10.0.0.1\",\"Port\":70000,\"FileManager\":{\"PinnedFolders\":null,\"ColumnWidths\":null,\"TreeWidth\":5}}");

            var settings = ManagerSettingsStore.Load(SettingsPath);

            Assert.Equal("10.0.0.1", settings.Ip);
            Assert.Equal(2121, settings.Port);
            Assert.NotNull(settings.FileManager.PinnedFolders);
            Assert.NotNull(settings.FileManager.ColumnWidths);
            Assert.Equal(260, settings.FileManager.TreeWidth);
        }
    }
}
