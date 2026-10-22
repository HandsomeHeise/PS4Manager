namespace PS4Manager.Gallery.Core.Ps4
{
    /// <summary>
    /// Alle PS4-Pfade an EINER Stelle — Foto-Zweig verifiziert am 2026-08-01,
    /// Video-Zweig am 2026-08-15 (siehe Specs). Beide Zweige folgen derselben
    /// Regel; es tauscht nur ein Pfadsegment, darum die Sorte als Parameter
    /// statt eines zweiten Methodensatzes.
    /// </summary>
    public static class Ps4Paths
    {
        private const string PhotoRoot = "/user/av_contents/photo/NPXS20001";
        private const string VideoRoot = "/user/av_contents/video/NPXS20001";
        private const string PhotoThumbnailRoot = "/user/av_contents/thumbnails/photo/NPXS20001";
        private const string VideoThumbnailRoot = "/user/av_contents/thumbnails/video/NPXS20001";

        public static string MediaRoot(MediaKind kind)
        {
            return kind == MediaKind.Video ? VideoRoot : PhotoRoot;
        }

        public static string ThumbnailRoot(MediaKind kind)
        {
            return kind == MediaKind.Video ? VideoThumbnailRoot : PhotoThumbnailRoot;
        }

        public static string TitleDir(MediaKind kind, string titleId)
        {
            return MediaRoot(kind) + "/" + titleId;
        }

        public static string MediaPath(MediaItem item)
        {
            return MediaRoot(item.Kind) + "/" + item.TitleId + "/" + item.HashDir + "/" + item.FileName;
        }

        public static string DatPath(MediaItem item)
        {
            var name = item.FileName;
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(0, dot);
            return MediaRoot(item.Kind) + "/" + item.TitleId + "/" + item.HashDir + "/" + name + ".dat";
        }

        /// <summary>Die PS4 hängt ans Thumbnail schlicht ein zweites ".jpg" an —
        /// im Video-Zweig genauso, also "….mp4.jpg".</summary>
        public static string ThumbnailPath(MediaItem item)
        {
            return ThumbnailRoot(item.Kind) + "/" + item.TitleId + "/" + item.HashDir + "/" + item.FileName + ".jpg";
        }
    }
}
