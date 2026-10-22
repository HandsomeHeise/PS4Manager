using System;
using System.IO;
using System.Linq;
using System.Text;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenBackupTimelineTests : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "ps4manager-test-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
        }

        [Fact]
        public void Reads_manifest_backups_newest_first()
        {
            WriteBackup("20260927-010000", "/data/GoldHEN/plugins.ini", "toggle-plugin",
                "old", "2026-09-27T01:00:00Z", "cba06b5736faf67e54b07b561eae94395e774c517a7d910a54369e1263ccfbd4");
            WriteBackup("20260927-020000", "/data/GoldHEN/config.ini", "edit-configuration",
                "new", "2026-09-27T02:00:00Z", "11507a0e2f5e69d5c2d5b9f6586f120765c9d1e5db7f0feda615a0f9d90976f8");

            var entries = GoldHenBackupTimeline.Load(_temp);

            Assert.Equal(new[] { "/data/GoldHEN/config.ini", "/data/GoldHEN/plugins.ini" },
                entries.Select(e => e.Source).ToArray());
            Assert.Equal("edit-configuration", entries[0].Operation);
            Assert.Equal(3, entries[0].Size);
            Assert.Equal(Path.Combine(_temp, "20260927-020000", "files", "config.ini"), entries[0].PayloadPath);
        }

        [Fact]
        public void Verifies_payload_against_the_manifest_hash()
        {
            WriteBackup("20260927-010000", "/data/GoldHEN/plugins.ini", "toggle-plugin",
                "old", "2026-09-27T01:00:00Z", "cba06b5736faf67e54b07b561eae94395e774c517a7d910a54369e1263ccfbd4");
            var entry = GoldHenBackupTimeline.Load(_temp).Single();

            Assert.True(GoldHenBackupTimeline.VerifyPayload(entry));

            File.WriteAllText(entry.PayloadPath, "changed", Encoding.UTF8);

            Assert.False(GoldHenBackupTimeline.VerifyPayload(entry));
        }

        private void WriteBackup(string directory, string source, string operation, string payload,
            string createdUtc, string sha256)
        {
            var root = Path.Combine(_temp, directory);
            var files = Path.Combine(root, "files");
            Directory.CreateDirectory(files);
            File.WriteAllBytes(Path.Combine(files, Path.GetFileName(source)), Encoding.UTF8.GetBytes(payload));
            File.WriteAllText(Path.Combine(root, "manifest.json"),
                "{\"version\":1,\"operation\":\"" + operation + "\",\"source\":\"" + source +
                "\",\"createdUtc\":\"" + createdUtc + "\",\"size\":" + payload.Length +
                ",\"sha256\":\"" + sha256 + "\"}", Encoding.UTF8);
        }
    }
}
