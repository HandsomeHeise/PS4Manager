using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PS4Manager.Core.Updates;
using Xunit;

namespace PS4Manager.Core.Tests.Updates
{
    public sealed class UpdateManifestTests
    {
        [Fact]
        public void Parse_reads_static_server_manifest()
        {
            var manifest = UpdateManifest.Parse(Json("1.2.0",
                "https://example.com/PS4Manager-1.2.0.zip", GoodHash, "Changed things"));

            Assert.Equal(new Version(1, 2, 0), manifest.Version);
            Assert.Equal("stable", manifest.Channel);
            Assert.Equal("https://example.com/PS4Manager-1.2.0.zip", manifest.Url);
            Assert.Equal(GoodHash, manifest.Sha256);
            Assert.Equal("Changed things", manifest.Notes);
        }

        [Fact]
        public void Evaluate_reports_available_update_only_for_newer_matching_channel()
        {
            var manifest = UpdateManifest.Parse(Json("1.2.0",
                "https://example.com/PS4Manager-1.2.0.zip", GoodHash, ""));

            var result = UpdateVerifier.Evaluate(manifest,
                new UpdateCheckContext(new Version(1, 1, 0), "stable", null));

            Assert.True(result.IsUpdateAvailable);
            Assert.Equal(UpdateValidationState.Valid, result.State);
        }

        [Fact]
        public void Evaluate_rejects_downgrades_and_channel_mismatch()
        {
            var old = UpdateManifest.Parse(Json("1.0.0",
                "https://example.com/PS4Manager-1.0.0.zip", GoodHash, ""));
            var preview = UpdateManifest.Parse(Json("1.2.0", "https://example.com/p.zip", GoodHash, "preview", ""));

            Assert.Equal(UpdateValidationState.NotNewer,
                UpdateVerifier.Evaluate(old, new UpdateCheckContext(new Version(1, 1, 0), "stable", null)).State);
            Assert.Equal(UpdateValidationState.ChannelMismatch,
                UpdateVerifier.Evaluate(preview, new UpdateCheckContext(new Version(1, 1, 0), "stable", null)).State);
        }

        [Theory]
        [InlineData("http://example.com/PS4Manager.zip", UpdateValidationState.InsecureUrl)]
        [InlineData("https://example.com/PS4Manager.zip", UpdateValidationState.InvalidHash)]
        public void Evaluate_rejects_unsafe_package_metadata(string url, UpdateValidationState expected)
        {
            var manifest = UpdateManifest.Parse(Json("1.2.0", url, "abc", ""));

            var result = UpdateVerifier.Evaluate(manifest,
                new UpdateCheckContext(new Version(1, 1, 0), "stable", null));

            Assert.Equal(expected, result.State);
        }

        [Fact]
        public void VerifyFileHash_compares_manifest_hash_with_downloaded_package()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "payload", new UTF8Encoding(false));
                var expected = Hex(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes("payload")));

                Assert.True(UpdateVerifier.VerifyFileHash(path, expected));
                Assert.False(UpdateVerifier.VerifyFileHash(path, GoodHash));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private const string GoodHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static string Json(string version, string url, string sha256, string notes)
        {
            return Json(version, url, sha256, "stable", notes);
        }

        private static string Json(string version, string url, string sha256, string channel, string notes)
        {
            return "{"
                   + "\"version\":\"" + version + "\","
                   + "\"channel\":\"" + channel + "\","
                   + "\"url\":\"" + url + "\","
                   + "\"sha256\":\"" + sha256 + "\","
                   + "\"notes\":\"" + notes + "\""
                   + "}";
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
