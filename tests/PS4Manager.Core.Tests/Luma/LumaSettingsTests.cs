using System.IO;
using PS4Manager.Core.Config;
using Xunit;

namespace PS4Manager.Core.Tests.Luma
{
    public sealed class LumaSettingsTests
    {
        [Fact]
        public void Defaults_to_gta_profile_port_without_token()
        {
            var settings = new ManagerSettings();

            Assert.NotNull(settings.Luma);
            Assert.Equal("gta5", settings.Luma.Game);
            Assert.Equal(9615, settings.Luma.Port);
            Assert.Equal("", settings.Luma.Token);
        }

        [Fact]
        public void Repair_restores_luma_settings_when_missing_or_invalid()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllText(path,
                    "{\"Ip\":\"10.0.0.1\",\"Port\":2121,\"Luma\":{\"Game\":\"bad\",\"Port\":0,\"Token\":null}}");

                var settings = ManagerSettingsStore.Load(path);

                Assert.Equal("gta5", settings.Luma.Game);
                Assert.Equal(9615, settings.Luma.Port);
                Assert.Equal("", settings.Luma.Token);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
