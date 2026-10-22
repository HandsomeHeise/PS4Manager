using System;
using System.IO;
using PS4Manager.Gallery.Core.Config;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    public class SettingsStoreTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(),
            "sg-settings-" + Guid.NewGuid().ToString("N") + ".json");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Fact]
        public void Save_then_load_round_trips()
        {
            SettingsStore.Save(_path, new AppSettings { Ip = "192.168.0.7", Port = 1337 });
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("192.168.0.7", loaded.Ip);
            Assert.Equal(1337, loaded.Port);
        }

        [Fact]
        public void A_missing_file_yields_empty_defaults()
        {
            // Diese Einstellungen sind nur noch die Gestalt der Alt-Datei, die
            // der Import beim ersten Start liest — eine Vorgabe-IP gibt es
            // deshalb nicht mehr (sie war die Adresse der Testkonsole).
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("", loaded.Ip);
            Assert.Equal(2121, loaded.Port);
        }

        [Fact]
        public void A_corrupt_file_yields_the_defaults_instead_of_throwing()
        {
            File.WriteAllText(_path, "{ kaputt !!!");
            var loaded = SettingsStore.Load(_path);
            Assert.Equal("", loaded.Ip);
            Assert.Equal(2121, loaded.Port);
        }
    }
}
