using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using FileManager.Core.Paths;

namespace PS4Manager.Core.GoldHen
{
    public static class GoldHenBackupTimeline
    {
        public static IReadOnlyList<GoldHenBackupEntry> Load(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return new GoldHenBackupEntry[0];

            var entries = new List<GoldHenBackupEntry>();
            foreach (var directory in Directory.GetDirectories(root))
            {
                var manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                GoldHenBackupManifest manifest;
                if (!TryReadManifest(manifestPath, out manifest)) continue;
                if (string.IsNullOrEmpty(manifest.Source)) continue;

                var payloadPath = Path.Combine(directory, "files", RemotePath.Name(manifest.Source));
                if (!File.Exists(payloadPath)) continue;
                var created = ParseUtc(manifest.CreatedUtc, Directory.GetLastWriteTimeUtc(directory));
                entries.Add(new GoldHenBackupEntry(directory, payloadPath, manifestPath, manifest.Source,
                    manifest.Operation ?? "", created, manifest.Size, manifest.Sha256 ?? ""));
            }

            return entries
                .OrderByDescending(e => e.CreatedUtc)
                .ThenByDescending(e => e.Directory, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool VerifyPayload(GoldHenBackupEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrEmpty(entry.PayloadPath) || !File.Exists(entry.PayloadPath)) return false;
            if (string.IsNullOrEmpty(entry.Sha256)) return false;
            return string.Equals(Hash(File.ReadAllBytes(entry.PayloadPath)), entry.Sha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Hash(byte[] content)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }

        private static bool TryReadManifest(string path, out GoldHenBackupManifest manifest)
        {
            manifest = null;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8));
                using (var stream = new MemoryStream(bytes))
                    manifest = (GoldHenBackupManifest)new DataContractJsonSerializer(
                        typeof(GoldHenBackupManifest)).ReadObject(stream);
                return manifest != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static DateTime ParseUtc(string text, DateTime fallback)
        {
            DateTime value;
            if (DateTime.TryParseExact(text ?? "", "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out value))
                return value;
            return fallback;
        }

        [DataContract]
        private sealed class GoldHenBackupManifest
        {
            [DataMember(Name = "operation")] public string Operation { get; set; }
            [DataMember(Name = "source")] public string Source { get; set; }
            [DataMember(Name = "createdUtc")] public string CreatedUtc { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "sha256")] public string Sha256 { get; set; }
        }
    }
}
