using System.IO;
using PS4Manager.Core.Config;
using Xunit;

namespace PS4Manager.Core.Tests.Frame4
{
    public sealed class Frame4SettingsTests
    {
        [Fact]
        public void Manager_settings_default_frame4_to_known_ports()
        {
            var settings = new ManagerSettings();

            Assert.NotNull(settings.Frame4);
            Assert.Equal(2812, settings.Frame4.HttpPort);
            Assert.Equal(3321, settings.Frame4.UartPort);
            Assert.False(settings.Frame4.Enabled);
        }

        [Fact]
        public void Missing_frame4_settings_are_repaired_after_deserialization()
        {
            using (var temp = new TempSettings())
            {
                File.WriteAllText(temp.Path, "{\"Ip\":\"10.0.0.1\",\"Port\":2121,\"Frame4\":null}");

                var settings = ManagerSettingsStore.Load(temp.Path);

                Assert.NotNull(settings.Frame4);
                Assert.Equal(2812, settings.Frame4.HttpPort);
                Assert.Equal(3321, settings.Frame4.UartPort);
            }
        }

        [Fact]
        public void Invalid_frame4_ports_are_repaired_after_deserialization()
        {
            using (var temp = new TempSettings())
            {
                File.WriteAllText(temp.Path, "{\"Ip\":\"10.0.0.1\",\"Port\":2121,\"Frame4\":{\"Enabled\":true,\"HttpPort\":0,\"UartPort\":70000}}");

                var settings = ManagerSettingsStore.Load(temp.Path);

                Assert.True(settings.Frame4.Enabled);
                Assert.Equal(2812, settings.Frame4.HttpPort);
                Assert.Equal(3321, settings.Frame4.UartPort);
            }
        }

        private sealed class TempSettings : System.IDisposable
        {
            private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frame4-settings-" + System.Guid.NewGuid().ToString("N"));

            public TempSettings()
            {
                Directory.CreateDirectory(_dir);
                Path = System.IO.Path.Combine(_dir, "settings.json");
            }

            public string Path { get; private set; }

            public void Dispose()
            {
                try { Directory.Delete(_dir, true); } catch (System.Exception) { }
            }
        }
    }
}
