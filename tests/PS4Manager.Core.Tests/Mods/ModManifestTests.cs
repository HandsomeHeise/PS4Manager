using System.IO;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModManifestTests
    {
        [Theory][InlineData("/user/data/a")][InlineData("/user/data/a/child")]
        public void Canonical_alias_collisions_are_rejected(string other)
        {
            var json="{\"formatVersion\":1,\"packageType\":\"files\",\"operations\":[{\"source\":\"payload/a\",\"target\":\"/data/a\"},{\"source\":\"payload/b\",\"target\":\""+other+"\"}]}";
            Assert.Throws<InvalidDataException>(()=>ModManifest.Parse(json));
        }
        [Fact]
        public void Valid_file_package_retains_exact_target()
        {Assert.Equal("/data/test/file.txt",Assert.Single(ModManifest.Parse(Manifest()).Operations).Target);}
        [Theory]
        [InlineData("../evil")][InlineData("payload/../evil")][InlineData("payload\\evil")]
        [InlineData("payload/COM¹.txt")][InlineData("payload/LPT².txt")][InlineData("payload/a:stream")]
        [InlineData("payload/a.")][InlineData("payload/a ")][InlineData("payload//a")]
        [InlineData("payload/e\u0301.txt")][InlineData("/absolute")]
        public void Unsafe_sources_fail(string source)
        {Assert.Throws<InvalidDataException>(()=>ModManifest.Parse(Manifest(source.Replace("\\","\\\\"))));}
        [Theory]
        [InlineData("/system/a")][InlineData("/data")][InlineData("/data/../system/a")][InlineData("data/a")]
        public void Forbidden_targets_fail(string target)
        {Assert.Throws<InvalidDataException>(()=>ModManifest.Parse(Manifest("payload/file.txt",target)));}
        [Fact]
        public void Duplicates_prefixes_unknown_fields_and_foreign_luma_roots_fail()
        {
            Assert.Throws<InvalidDataException>(()=>ModManifest.Parse(Manifest().Replace("\"formatVersion\":1","\"formatVersion\":1,\"extra\":1")));
            Assert.Throws<InvalidDataException>(()=>ModManifest.Parse(Manifest().Replace("\"packageType\":\"files\"","\"packageType\":\"luma-script\",\"lumaProfile\":\"gta5\"")));
            foreach(var target in new[]{"/data/test/FILE.txt","/data/test/file.txt/child"})
                Assert.Throws<InvalidDataException>(()=>ModManifest.Parse(Manifest().Replace("}]","},{\"source\":\"payload/other\",\"target\":\""+target+"\"}]")));
        }
        internal static string Manifest(string source="payload/file.txt",string target="/data/test/file.txt")
        {return "{\"formatVersion\":1,\"packageType\":\"files\",\"operations\":[{\"source\":\""+source+"\",\"target\":\""+target+"\"}]}";}
    }
}
