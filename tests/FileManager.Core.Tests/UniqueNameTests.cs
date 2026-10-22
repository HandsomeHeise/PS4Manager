using System.Collections.Generic;
using FileManager.Core.Transfers;
using Xunit;

namespace FileManager.Core.Tests
{
    public class UniqueNameTests
    {
        private static string For(string name, bool isDirectory, params string[] taken)
        {
            var set = new HashSet<string>(taken);
            return UniqueName.For(name, isDirectory, set.Contains);
        }

        [Fact]
        public void A_free_name_stays()
        {
            Assert.Equal("a.txt", For("a.txt", false));
        }

        [Fact]
        public void The_number_goes_before_the_extension()
        {
            Assert.Equal("plugins (2).ini", For("plugins.ini", false, "plugins.ini"));
            Assert.Equal("plugins (3).ini", For("plugins.ini", false, "plugins.ini", "plugins (2).ini"));
        }

        [Fact]
        public void Only_the_last_extension_counts()
        {
            Assert.Equal("archiv.tar (2).gz", For("archiv.tar.gz", false, "archiv.tar.gz"));
        }

        [Fact]
        public void Names_without_extension_and_dot_files_get_the_number_at_the_end()
        {
            Assert.Equal("README (2)", For("README", false, "README"));
            Assert.Equal(".config (2)", For(".config", false, ".config"));
        }

        [Fact]
        public void Folders_never_split_at_a_dot()
        {
            Assert.Equal("New folder (2)", For("New folder", true, "New folder"));
            Assert.Equal("v1.2 (2)", For("v1.2", true, "v1.2"));
        }

        [Fact]
        public void Case_matters_like_on_the_console()
        {
            Assert.Equal("A.txt", For("A.txt", false, "a.txt"));
        }

        [Theory]
        [InlineData("plugins.ini", "plugins.ini")]
        [InlineData("a:b?.txt", "a_b_.txt")]
        [InlineData("x<y>|z\"", "x_y__z_")]
        [InlineData("name. ", "name")]
        [InlineData("CON", "_CON")]
        [InlineData("con.txt", "_con.txt")]
        [InlineData("COM1", "_COM1")]
        [InlineData("CONSOLE.txt", "CONSOLE.txt")]
        [InlineData("...", "_")]
        public void Console_names_become_windows_safe(string name, string expected)
        {
            Assert.Equal(expected, LocalName.Sanitize(name));
        }
    }
}
