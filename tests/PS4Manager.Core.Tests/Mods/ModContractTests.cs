using System;
using System.IO;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModContractTests
    {
        [Theory]
        [InlineData("01.57", "1.57")]
        [InlineData("1.5", "1.05")]
        [InlineData(" 1.00 ", "1.00")]
        public void Versions_match_server(string input, string expected)
        { Assert.Equal(expected, ModCompatibility.NormalizeVersion(input)); }

        [Theory]
        [InlineData("1.57\n")]
        [InlineData("1.57.0")]
        [InlineData("1.57 or newer")]
        [InlineData("1000.01")]
        public void Ambiguous_versions_fail(string input)
        { Assert.Throws<FormatException>(() => ModCompatibility.NormalizeVersion(input)); }

        [Fact]
        public void Catalog_reads_documented_fields()
        {
            var page = ModJson.ReadCatalog("{\"page\":1,\"pageSize\":24,\"items\":[" + Item + "]}");
            Assert.Equal("Luma", Assert.Single(page.Items).Title);
            Assert.Equal("CUSA00411@1.57", page.Items[0].Compatibility[0].Key);
            Assert.Equal(12L, page.Items[0].PackageSize);
        }

        [Theory]
        [InlineData("{\"page\":1,\"p\\u0061ge\":2,\"pageSize\":24,\"items\":[]}")]
        [InlineData("{\"page\":1,\"pageSize\":24,\"items\":[]} trailing")]
        [InlineData("{\"page\":1,\"pageSize\":24,\"items\":[],}")]
        [InlineData("{\"page\":1.5,\"pageSize\":24,\"items\":[]}")]
        public void Malformed_or_duplicate_json_fails(string json)
        { Assert.Throws<InvalidDataException>(() => ModJson.ReadCatalog(json)); }

        [Theory]
        [InlineData("\"packageSize\":12", "\"packageSize\":-1")]
        [InlineData("\"releaseVersion\":\"1.0.0\"", "\"releaseVersion\":\"1.0.0\\nforged\"")]
        [InlineData("CUSA00411", "INVALID")]
        [InlineData("503d131c6d8d979e", "not-a-key")]
        public void Invalid_release_fields_fail(string oldValue, string newValue)
        { Assert.Throws<InvalidDataException>(() => ModJson.ReadCatalog("{\"page\":1,\"pageSize\":24,\"items\":[" + Item.Replace(oldValue,newValue) + "]}")); }

        [Fact]
        public void Deep_json_and_unpaired_surrogates_fail()
        {
            Assert.Throws<InvalidDataException>(() => ModJson.ReadCatalog(new string('[',34) + "null" + new string(']',34)));
            Assert.Throws<InvalidDataException>(() => ModJson.ReadCatalog("{\"page\":1,\"pageSize\":24,\"items\":[],\"x\":\"\\ud800\"}"));
        }

        [Theory]
        [InlineData("draft")]
        [InlineData("unknown")]
        public void Nonpublic_release_states_fail(string state)
        {
            Assert.Throws<InvalidDataException>(() => ModJson.ReadRelease(Item.TrimEnd('}') + ",\"state\":\""+state+"\"}"));
        }

        [Theory]
        [InlineData("\"signature\":\"AQID\"", "\"signature\":\"!\"")]
        [InlineData("f8615e4a-cb7e-4cf7-8326-5464475f080e", "f8615e4a-cb7e-1cf7-8326-5464475f080e")]
        [InlineData("\"packageSize\":12", "\"packageSize\":2147483649")]
        [InlineData("\"title\":\"Luma\",", "")]
        public void Invalid_crypto_ids_limits_or_missing_fields_fail(string oldValue,string newValue)
        { Assert.Throws<InvalidDataException>(() => ModJson.ReadCatalog("{\"page\":1,\"pageSize\":24,\"items\":["+Item.Replace(oldValue,newValue)+"]}")); }

        [Fact]
        public void Duplicate_normalized_compatibility_fails()
        {
            var item = Item.Replace("\"gameVersion\":\"1.57\"}", "\"gameVersion\":\"1.57\"},{\"titleId\":\"cusa00411\",\"gameVersion\":\"01.57\"}");
            Assert.Throws<InvalidDataException>(() => ModJson.ReadCatalog("{\"page\":1,\"pageSize\":24,\"items\":["+item+"]}"));
        }

        internal const string Item = "{\"modId\":\"f8615e4a-cb7e-4cf7-8326-5464475f080e\",\"slug\":\"luma\",\"title\":\"Luma\",\"author\":\"Ghosted\",\"summary\":\"Lua runtime\",\"category\":\"Scripting\",\"coverUrl\":null,\"releaseId\":\"aa6b9003-f0e2-4ee9-a8b5-f48cf3da73c2\",\"releaseVersion\":\"1.0.0\",\"packageType\":\"files\",\"packageSize\":12,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"signature\":\"AQID\",\"signingKeyId\":\"503d131c6d8d979e\",\"compatibility\":[{\"titleId\":\"CUSA00411\",\"gameVersion\":\"1.57\"}],\"publishedUtc\":\"2026-09-30T00:54:15.294Z\",\"detailUrl\":\"https://ps4manager.devkit.online/api/v1/mods/f8615e4a-cb7e-4cf7-8326-5464475f080e\"}";
    }
}
