using System;

namespace PS4Manager.Core.Updates
{
    public sealed class UpdateCheckContext
    {
        public UpdateCheckContext(Version currentVersion, string channel, string publicKeyXml)
        {
            CurrentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
            Channel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim();
            PublicKeyXml = publicKeyXml;
        }

        public Version CurrentVersion { get; }
        public string Channel { get; }
        public string PublicKeyXml { get; }
    }
}
