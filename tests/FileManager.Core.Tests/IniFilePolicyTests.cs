using FileManager.Core.Editing;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class IniFilePolicyTests
    {
        [Theory]
        [InlineData("settings.ini", true)]
        [InlineData("SETTINGS.INI", true)]
        [InlineData("/data/GoldHEN/plugins.ini", true)]
        [InlineData("settings.ini.bak", false)]
        [InlineData("settings", false)]
        [InlineData("/data/folder.ini/file", false)]
        public void Only_ini_files_use_the_integrated_editor(string path, bool expected)
        {
            Assert.Equal(expected, IniFilePolicy.IsEditable(path));
        }
    }
}
