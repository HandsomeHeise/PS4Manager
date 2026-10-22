using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PS4Manager.Core.Updates
{
    public static class UpdateVerifier
    {
        public static UpdateCheckResult Evaluate(UpdateManifest manifest, UpdateCheckContext context)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (manifest.Version == null)
                return Result(manifest, UpdateValidationState.InvalidManifest, "The update version is invalid.");
            if (manifest.Version <= context.CurrentVersion)
                return Result(manifest, UpdateValidationState.NotNewer, "No newer update is available.");
            if (!string.Equals(manifest.Channel, context.Channel, StringComparison.OrdinalIgnoreCase))
                return Result(manifest, UpdateValidationState.ChannelMismatch, "The update channel does not match.");
            if (!IsHttps(manifest.Url))
                return Result(manifest, UpdateValidationState.InsecureUrl, "The update package URL must use HTTPS.");
            if (!IsSha256Hex(manifest.Sha256))
                return Result(manifest, UpdateValidationState.InvalidHash, "The update package hash must be SHA-256 hex.");
            if (!string.IsNullOrWhiteSpace(context.PublicKeyXml))
            {
                if (string.IsNullOrWhiteSpace(manifest.Signature))
                    return Result(manifest, UpdateValidationState.MissingSignature, "The update manifest is not signed.");
                if (!VerifySignature(manifest, context.PublicKeyXml))
                    return Result(manifest, UpdateValidationState.InvalidSignature, "The update manifest signature is invalid.");
            }

            return Result(manifest, UpdateValidationState.Valid, "Update available.");
        }

        public static bool VerifyFileHash(string path, string expectedSha256)
        {
            if (!File.Exists(path)) return false;
            if (!IsSha256Hex(expectedSha256)) return false;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static UpdateCheckResult Result(UpdateManifest manifest, UpdateValidationState state, string message)
        {
            return new UpdateCheckResult(manifest, state, message);
        }

        private static bool IsHttps(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                   && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSha256Hex(string value)
        {
            return value != null
                   && value.Length == 64
                   && value.All(IsHex);
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                   || (value >= 'a' && value <= 'f')
                   || (value >= 'A' && value <= 'F');
        }

        private static bool VerifySignature(UpdateManifest manifest, string publicKeyXml)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);
                    var payload = new UTF8Encoding(false).GetBytes(manifest.SignedPayload());
                    var signature = Convert.FromBase64String(manifest.Signature);
                    return rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), signature);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
