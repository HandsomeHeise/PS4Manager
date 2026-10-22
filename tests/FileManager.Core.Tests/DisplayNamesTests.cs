using System.Text;
using FileManager.Core.Presentation;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class DisplayNamesTests
    {
        /// <summary>So kommt ein Name, den ein anderes Programm als UTF-8 abgelegt hat, aus unserem LIST.</summary>
        private static string AsListedFromUtf8(string name)
        {
            return FtpClient.PathEncoding.GetString(Encoding.UTF8.GetBytes(name));
        }

        [Fact]
        public void A_utf8_name_from_another_program_is_shown_decoded()
        {
            Assert.Equal("Ärger ü.txt", DisplayNames.For(AsListedFromUtf8("Ärger ü.txt")));
        }

        [Fact]
        public void A_name_we_uploaded_ourselves_stays_as_it_is()
        {
            Assert.Equal("Ärger ü.txt", DisplayNames.For("Ärger ü.txt"));
        }

        [Fact]
        public void Ascii_names_are_untouched()
        {
            Assert.Equal("plugins.ini", DisplayNames.For("plugins.ini"));
        }

        [Fact]
        public void Null_and_empty_pass_through()
        {
            Assert.Null(DisplayNames.For(null));
            Assert.Equal("", DisplayNames.For(""));
        }
    }
}
