using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PS4Manager.Core.Updates
{
    [DataContract]
    public sealed class UpdateManifest
    {
        public UpdateManifest()
        {
            VersionText = "";
            Channel = "stable";
            Url = "";
            Sha256 = "";
            Notes = "";
            Signature = "";
        }

        [DataMember(Name = "version")] public string VersionText { get; private set; }
        [DataMember(Name = "channel")] public string Channel { get; private set; }
        [DataMember(Name = "url")] public string Url { get; private set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; private set; }
        [DataMember(Name = "notes")] public string Notes { get; private set; }
        [DataMember(Name = "signature")] public string Signature { get; private set; }

        public Version Version
        {
            get
            {
                Version version;
                return Version.TryParse(VersionText ?? "", out version) ? version : null;
            }
        }

        public static UpdateManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("The update manifest is empty.", nameof(json));
            using (var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json)))
            {
                var manifest = (UpdateManifest)new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(stream);
                if (manifest == null) throw new InvalidDataException("The update manifest is invalid.");
                manifest.Repair();
                return manifest;
            }
        }

        public string SignedPayload()
        {
            return (VersionText ?? "") + "\n"
                   + (Channel ?? "") + "\n"
                   + (Url ?? "") + "\n"
                   + (Sha256 ?? "") + "\n"
                   + (Notes ?? "");
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Repair();
        }

        private void Repair()
        {
            if (Channel == null) Channel = "stable";
            if (VersionText == null) VersionText = "";
            if (Url == null) Url = "";
            if (Sha256 == null) Sha256 = "";
            if (Notes == null) Notes = "";
            if (Signature == null) Signature = "";
        }
    }
}
