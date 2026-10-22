using System.Globalization;
using System.Threading;
using FileManager.Core.Presentation;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class FileTypeTextTests
    {
        [Fact]
        public void Type_names_stay_English_under_a_German_culture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

                Assert.Equal("File folder", FileTypeText.For("mods", true));
                Assert.Equal("TXT file", FileTypeText.For("readme.txt", false));
                Assert.Equal("File", FileTypeText.For("README", false));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
