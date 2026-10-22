using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenBackupWriterTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "goldhen-backup-" + Guid.NewGuid().ToString("N"));
        private readonly FixedClock _clock = new FixedClock(new DateTime(2026, 9, 25, 18, 30, 12, DateTimeKind.Utc));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void A_backup_stores_the_payload_and_a_manifest_with_the_hash()
        {
            var bytes = Encoding.UTF8.GetBytes("; hen.prx\n");

            var backup = new GoldHenBackupWriter(_root, _clock).Write("/data/GoldHEN/plugins.ini", bytes, "toggle-plugin");

            Assert.True(File.Exists(backup.PayloadPath));
            Assert.Equal(bytes, File.ReadAllBytes(backup.PayloadPath));
            Assert.Equal(BitConverter.ToString(SHA256.Create().ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                backup.Sha256);

            var manifest = File.ReadAllText(backup.ManifestPath);
            Assert.Contains("\"version\":1", manifest);
            Assert.Contains("\"operation\":\"toggle-plugin\"", manifest);
            Assert.Contains("\"source\":\"/data/GoldHEN/plugins.ini\"", manifest);
            Assert.Contains("\"size\":" + bytes.Length, manifest);
            Assert.Contains("2026-09-25T18:30:12Z", manifest);
        }

        [Fact]
        public void The_payload_keeps_the_remote_file_name()
        {
            var backup = new GoldHenBackupWriter(_root, _clock)
                .Write("/data/GoldHEN/cheats/CUSA00411.json", new byte[] { 1 }, "replace");

            Assert.Equal("CUSA00411.json", Path.GetFileName(backup.PayloadPath));
        }

        [Fact]
        public void A_remote_manifest_file_cannot_overwrite_the_backup_manifest()
        {
            var bytes = Encoding.UTF8.GetBytes("remote payload");

            var backup = new GoldHenBackupWriter(_root, _clock)
                .Write("/data/GoldHEN/manifest.json", bytes, "delete");

            Assert.NotEqual(backup.PayloadPath, backup.ManifestPath);
            Assert.Equal(bytes, File.ReadAllBytes(backup.PayloadPath));
            Assert.Contains("\"source\":\"/data/GoldHEN/manifest.json\"", File.ReadAllText(backup.ManifestPath));
        }

        [Fact]
        public void A_second_backup_in_the_same_second_gets_its_own_directory()
        {
            var writer = new GoldHenBackupWriter(_root, _clock);

            var first = writer.Write("/data/GoldHEN/plugins.ini", new byte[] { 1 }, "toggle-plugin");
            var second = writer.Write("/data/GoldHEN/plugins.ini", new byte[] { 2 }, "toggle-plugin");

            Assert.NotEqual(first.Directory, second.Directory);
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(first.PayloadPath));
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(second.PayloadPath));
        }

        [Fact]
        public void A_backup_root_that_cannot_be_written_throws()
        {
            var blocked = Path.Combine(_root, "blocked");
            Directory.CreateDirectory(_root);
            File.WriteAllText(blocked, "eine Datei, kein Ordner");

            Assert.ThrowsAny<Exception>(() =>
                new GoldHenBackupWriter(blocked, _clock).Write("/data/GoldHEN/plugins.ini", new byte[] { 1 }, "toggle-plugin"));
        }

        private sealed class FixedClock : IClock
        {
            private readonly DateTime _now;

            public FixedClock(DateTime now) { _now = now; }

            public DateTime UtcNow { get { return _now; } }
        }
    }
}
