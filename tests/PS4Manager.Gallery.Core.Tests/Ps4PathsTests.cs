using System;
using PS4Manager.Gallery.Core.Ps4;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    public class Ps4PathsTests
    {
        private static readonly MediaItem Photo = new MediaItem(MediaKind.Photo,
            "CUSA00411", "104", "20260801_084933_00470650.jpg", 461788, DateTime.MinValue);

        // Am 2026-08-15 real auf der Konsole vorgefunden.
        private static readonly MediaItem Clip = new MediaItem(MediaKind.Video,
            "CUSA00411", "2c7", "20260810_212645_00367837.mp4", 15454876, DateTime.MinValue);

        [Fact]
        public void The_photo_paths_match_the_verified_ps4_layout()
        {
            Assert.Equal("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg",
                Ps4Paths.MediaPath(Photo));
            Assert.Equal("/user/av_contents/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.dat",
                Ps4Paths.DatPath(Photo));
            Assert.Equal("/user/av_contents/thumbnails/photo/NPXS20001/CUSA00411/104/20260801_084933_00470650.jpg.jpg",
                Ps4Paths.ThumbnailPath(Photo));
        }

        [Fact]
        public void The_video_paths_match_the_verified_ps4_layout()
        {
            Assert.Equal("/user/av_contents/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.mp4",
                Ps4Paths.MediaPath(Clip));
            Assert.Equal("/user/av_contents/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.dat",
                Ps4Paths.DatPath(Clip));
            // Die PS4 hängt auch beim Clip schlicht ein zweites ".jpg" an den
            // VOLLEN Dateinamen an — inklusive ".mp4".
            Assert.Equal("/user/av_contents/thumbnails/video/NPXS20001/CUSA00411/2c7/20260810_212645_00367837.mp4.jpg",
                Ps4Paths.ThumbnailPath(Clip));
        }

        [Fact]
        public void The_roots_and_title_dirs_follow_the_kind()
        {
            Assert.Equal("/user/av_contents/photo/NPXS20001", Ps4Paths.MediaRoot(MediaKind.Photo));
            Assert.Equal("/user/av_contents/video/NPXS20001", Ps4Paths.MediaRoot(MediaKind.Video));
            Assert.Equal("/user/av_contents/thumbnails/photo/NPXS20001", Ps4Paths.ThumbnailRoot(MediaKind.Photo));
            Assert.Equal("/user/av_contents/thumbnails/video/NPXS20001", Ps4Paths.ThumbnailRoot(MediaKind.Video));
            Assert.Equal("/user/av_contents/video/NPXS20001/CUSA00411", Ps4Paths.TitleDir(MediaKind.Video, "CUSA00411"));
        }
    }
}
