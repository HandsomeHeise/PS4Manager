using System;
using System.Collections.Generic;

namespace PS4Manager.Core.Mods
{
    public class ModCatalogItem
    {
        public Guid ModId { get; internal set; }
        public Guid ReleaseId { get; internal set; }
        public string Slug { get; internal set; }
        public string Title { get; internal set; }
        public string Author { get; internal set; }
        public string Summary { get; internal set; }
        public string Category { get; internal set; }
        public Uri CoverUrl { get; internal set; }
        public Uri DetailUrl { get; internal set; }
        public string ReleaseVersion { get; internal set; }
        public string PackageType { get; internal set; }
        public long PackageSize { get; internal set; }
        public string Sha256 { get; internal set; }
        public string Signature { get; internal set; }
        public string SigningKeyId { get; internal set; }
        public IReadOnlyList<ModCompatibility> Compatibility { get; internal set; }
        public DateTimeOffset PublishedUtc { get; internal set; }
    }
    public sealed class ModRelease : ModCatalogItem
    {
        internal string RawJson {get;set;}
        public string State { get; internal set; }
        public string WithdrawalReason { get; internal set; }
        public string Changelog { get; internal set; }
        public string SignedEnvelope { get; internal set; }
        internal Dictionary<string, object> ManifestData { get; set; }
    }
    public sealed class ModCatalogPage
    {
        public int Page { get; internal set; }
        public int PageSize { get; internal set; }
        public IReadOnlyList<ModCatalogItem> Items { get; internal set; }
        public bool IsOffline { get; internal set; }
        public DateTimeOffset FetchedUtc { get; internal set; }
    }
    public sealed class ModDetail
    {
        public Guid ModId { get; internal set; }
        public string Title { get; internal set; }
        public string Description { get; internal set; }
        public IReadOnlyList<ModCatalogItem> Releases { get; internal set; }
        public IReadOnlyList<ModMedia> Media { get; internal set; }
    }
    public sealed class ModMedia
    {
        public string Kind { get; internal set; }
        public int Position { get; internal set; }
        public Uri Url { get; internal set; }
    }
}
