using System;
using System.IO;
using PS4Manager.Gallery.Core.Caching;
using Xunit;

namespace PS4Manager.Gallery.Core.Tests
{
    public class ThumbnailCacheTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "sg-cache-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void Put_then_get_round_trips()
        {
            var cache = new ThumbnailCache(_root);
            cache.Put("CUSA00411", "a.jpg", new byte[] { 1, 2, 3 });
            Assert.Equal(new byte[] { 1, 2, 3 }, cache.TryGet("CUSA00411", "a.jpg"));
        }

        [Fact]
        public void Staged_put_is_invisible_until_publish_and_cleanup_is_exact()
        {
            var cache = new ThumbnailCache(_root);
            var first = cache.Stage("CUSA00411", "shot.jpg", new byte[] { 1 });
            var second = cache.Stage("CUSA00411", "shot.jpg", new byte[] { 2 });

            Assert.Null(cache.TryGet("CUSA00411", "shot.jpg"));
            cache.Discard(first);
            cache.Publish("CUSA00411", "shot.jpg", second);

            Assert.False(File.Exists(first));
            Assert.Equal(new byte[] { 2 }, cache.TryGet("CUSA00411", "shot.jpg"));
        }

        [Fact]
        public void A_miss_yields_null()
        {
            var cache = new ThumbnailCache(_root);
            Assert.Null(cache.TryGet("CUSA00411", "fehlt.jpg"));
        }

        [Fact]
        public void A_zero_byte_file_counts_as_a_miss()
        {
            // Ein abgebrochener Download hinterlässt 0 Bytes — die dürfen nie
            // als "Bild" zurückkommen, sonst zeigt die Gallery ewig Müll.
            var cache = new ThumbnailCache(_root);
            Directory.CreateDirectory(Path.Combine(_root, "CUSA00411"));
            File.WriteAllBytes(Path.Combine(_root, "CUSA00411", "leer.jpg"), new byte[0]);
            Assert.Null(cache.TryGet("CUSA00411", "leer.jpg"));
        }

        [Fact]
        public void Remove_deletes_the_entry()
        {
            var cache = new ThumbnailCache(_root);
            cache.Put("CUSA00411", "a.jpg", new byte[] { 1 });
            cache.Remove("CUSA00411", "a.jpg");
            Assert.Null(cache.TryGet("CUSA00411", "a.jpg"));
            cache.Remove("CUSA00411", "a.jpg");     // doppelt: kein Wurf
        }
    }
}
