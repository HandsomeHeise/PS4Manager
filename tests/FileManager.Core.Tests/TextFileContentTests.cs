using System.IO;
using System.Linq;
using System.Text;
using FileManager.Core.Editing;
using FileManager.Core.Tests.Support;
using Xunit;

namespace FileManager.Core.Tests
{
    public sealed class TextFileContentTests
    {
        [Fact]
        public void Utf8_bom_is_preserved_when_saving()
        {
            using (var temp = new TempDir())
            {
                var path = Path.Combine(temp.Path, "plugins.ini");
                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("name=old\r\n")).ToArray());

                var document = TextFileContent.Load(path);
                document.Save(path, "name=new\r\n");

                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("name=new\r\n")).ToArray(), File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void ToBytes_returns_exactly_what_FromBytes_reads_back()
        {
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("hen.prx\r\n")).ToArray();

            var document = TextFileContent.FromBytes(bytes);

            Assert.Equal("hen.prx\r\n", document.Text);
            Assert.Equal(bytes, document.ToBytes(document.Text));
        }

        [Fact]
        public void Utf16_little_endian_is_preserved_when_saving()
        {
            using (var temp = new TempDir())
            {
                var path = Path.Combine(temp.Path, "plugins.ini");
                var encoding = new UnicodeEncoding(false, true);
                File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes("name=ä\r\n")).ToArray());

                var document = TextFileContent.Load(path);
                document.Save(path, "name=ö\r\n");

                Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes("name=ö\r\n")).ToArray(), File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void Legacy_ansi_without_a_bom_is_preserved_when_saving()
        {
            using (var temp = new TempDir())
            {
                var path = Path.Combine(temp.Path, "plugins.ini");
                var encoding = Encoding.GetEncoding(1252);
                File.WriteAllBytes(path, encoding.GetBytes("name=ä\r\n"));

                var document = TextFileContent.Load(path);
                document.Save(path, "name=ö\r\n");

                Assert.Equal(encoding.GetBytes("name=ö\r\n"), File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void Legacy_ansi_rejects_characters_that_cannot_be_represented()
        {
            using (var temp = new TempDir())
            {
                var path = Path.Combine(temp.Path, "plugins.ini");
                File.WriteAllBytes(path, Encoding.GetEncoding(1252).GetBytes("name=ä\r\n"));
                var document = TextFileContent.Load(path);

                Assert.Throws<EncoderFallbackException>(() => document.Save(path, "name=😀\r\n"));
                Assert.Equal(Encoding.GetEncoding(1252).GetBytes("name=ä\r\n"), File.ReadAllBytes(path));
            }
        }
    }
}
