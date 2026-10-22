using System;
using System.IO;
using FileManager.Core.Config;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class FileManagerSettingsStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fm-settings-" + Guid.NewGuid().ToString("N"));
        private string SettingsPath { get { return Path.Combine(_dir, "settings.json"); } }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        [Fact]
        public void A_missing_file_yields_the_defaults()
        {
            var s = FileManagerSettingsStore.Load(SettingsPath);
            Assert.Equal("", s.Ip);
            Assert.Equal(2121, s.Port);
            Assert.False(s.ExpertMode);
            Assert.Equal("Details", s.ViewMode);
            Assert.Equal("/data", s.LastPath);
            Assert.Empty(s.PinnedFolders);
            Assert.Empty(s.ColumnWidths);
            Assert.Equal(260, s.TreeWidth);
        }

        [Fact]
        public void Everything_survives_a_round_trip()
        {
            var s = new FileManagerSettings
            {
                Ip = "10.10.10.236", Port = 2121, ExpertMode = true, ViewMode = "Tiles",
                LastPath = "/data/GoldHEN", TreeWidth = 300
            };
            s.PinnedFolders.Add("/data/InsulinBO3");
            s.ColumnWidths["Name"] = 320;

            FileManagerSettingsStore.Save(SettingsPath, s);
            var loaded = FileManagerSettingsStore.Load(SettingsPath);

            Assert.Equal("10.10.10.236", loaded.Ip);
            Assert.True(loaded.ExpertMode);
            Assert.Equal("Tiles", loaded.ViewMode);
            Assert.Equal("/data/GoldHEN", loaded.LastPath);
            Assert.Equal(300, loaded.TreeWidth);
            Assert.Equal(new[] { "/data/InsulinBO3" }, loaded.PinnedFolders.ToArray());
            Assert.Equal(320, loaded.ColumnWidths["Name"]);
            Assert.False(File.Exists(SettingsPath + ".tmp"));
        }

        [Fact]
        public void A_corrupt_file_is_a_fresh_start()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{ das ist kein json");
            Assert.Equal(2121, FileManagerSettingsStore.Load(SettingsPath).Port);
        }

        [Fact]
        public void Missing_fields_keep_their_defaults()
        {
            // DataContractJsonSerializer ruft keinen Konstruktor auf — ohne
            // OnDeserializing wären Port 0 und PinnedFolders null.
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{\"Ip\":\"10.10.10.236\"}");

            var s = FileManagerSettingsStore.Load(SettingsPath);

            Assert.Equal("10.10.10.236", s.Ip);
            Assert.Equal(2121, s.Port);
            Assert.NotNull(s.PinnedFolders);
            Assert.NotNull(s.ColumnWidths);
            Assert.Equal("/data", s.LastPath);
        }

        [Fact]
        public void Nonsense_values_are_repaired()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SettingsPath, "{\"Port\":70000,\"ViewMode\":\"Riesig\",\"TreeWidth\":5,\"LastPath\":null,\"PinnedFolders\":null}");

            var s = FileManagerSettingsStore.Load(SettingsPath);

            Assert.Equal(2121, s.Port);
            Assert.Equal("Details", s.ViewMode);
            Assert.Equal(260, s.TreeWidth);
            Assert.Equal("/data", s.LastPath);
            Assert.NotNull(s.PinnedFolders);
        }
    }
}
