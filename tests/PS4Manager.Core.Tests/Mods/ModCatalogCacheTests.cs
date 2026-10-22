using System;
using System.IO;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModCatalogCacheTests:IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"mod-cache-"+Guid.NewGuid().ToString("N"));
        [Theory][InlineData(false)][InlineData(true)]
        public void Corrupt_inner_catalog_or_null_record_is_a_cache_miss(bool nullRecord)
        {
            var cache=new ModCatalogCache(root);var uri=new Uri("https://one.test/catalog");cache.Write(uri,"{\"page\":1,\"pageSize\":24,\"items\":[]}",null);
            var path=Directory.GetFiles(root)[0];var text=File.ReadAllText(path);File.WriteAllText(path,nullRecord?"null":text.Replace("\\\"page\\\":1","\\\"page\\\":null"));
            Assert.Null(cache.Read(uri));
        }
        [Fact]
        public void Cache_is_separated_by_origin_and_compatibility()
        {
            var cache=new ModCatalogCache(root);var uri=new Uri("https://one.test/api/v1/catalog?a=1");
            cache.Write(uri,"{\"page\":1,\"pageSize\":24,\"items\":[]}","\"etag\"");
            Assert.Equal("\"etag\"",cache.Read(uri).ETag);
            Assert.Null(cache.Read(new Uri("https://two.test/api/v1/catalog?a=1")));
            Assert.Null(cache.Read(new Uri("https://one.test/api/v1/catalog?a=2")));
            File.WriteAllText(Directory.GetFiles(root)[0],"broken");
            Assert.Null(cache.Read(uri));
        }
        [Fact]
        public async Task Concurrent_cache_updates_leave_complete_record()
        {
            var cache=new ModCatalogCache(root);var uri=new Uri("https://one.test/catalog");
            await Task.WhenAll(Task.Run(()=>cache.Write(uri,"{\"page\":1,\"pageSize\":24,\"items\":[]}","\"a\"")),Task.Run(()=>cache.Write(uri,"{\"page\":1,\"pageSize\":24,\"items\":[]}","\"b\"")));
            Assert.NotNull(cache.Read(uri));Assert.Empty(Directory.GetFiles(root,"*.tmp"));
        }
        public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
