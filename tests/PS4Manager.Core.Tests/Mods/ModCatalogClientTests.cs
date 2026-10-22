using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModCatalogClientTests : IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"mod-tests-"+Guid.NewGuid().ToString("N"));
        private const string Empty="{\"page\":1,\"pageSize\":24,\"items\":[]}";
        private static readonly Uri Origin=new Uri("https://mods.example.test/");
        [Fact] public async Task Corrupt_inner_cache_does_not_block_a_fresh_online_fetch()
        {
            int calls=0;
            using(var http=new HttpClient(new Handler(r=>{calls++;return Json(Empty);})))
            using(var client=new ModCatalogClient(http,Origin,root)){
                await client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None);
                var path=Directory.GetFiles(root,"*.json",SearchOption.AllDirectories)[0];
                File.WriteAllText(path,File.ReadAllText(path).Replace("\\\"page\\\":1","\\\"page\\\":null"));
                Assert.False((await client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None)).IsOffline);
                Assert.Equal(2,calls);
            }
        }
        [Fact]
        public async Task Exact_query_and_valid_cache_support_offline_browsing()
        {
            int calls=0; Uri requested=null;
            using(var http=new HttpClient(new Handler(request=>{
                calls++; requested=request.RequestUri;
                if(calls>1) throw new HttpRequestException("offline");
                return Json(Empty);
            }))) using(var client=new ModCatalogClient(http,Origin,root)) {
                Assert.False((await client.CatalogAsync("cusa00411","01.57",1,CancellationToken.None)).IsOffline);
                Assert.Equal("https://mods.example.test/api/v1/catalog?titleId=CUSA00411&gameVersion=1.57&page=1&pageSize=24",requested.AbsoluteUri);
                Assert.True((await client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None)).IsOffline);
            }
        }
        [Fact]
        public async Task Redirect_is_not_followed()
        {
            int calls=0;
            using(var http=new HttpClient(new Handler(request=>{calls++;var r=new HttpResponseMessage(HttpStatusCode.Redirect);r.Headers.Location=new Uri("https://evil.test/data");return r;})))
            using(var client=new ModCatalogClient(http,Origin,root)) {
                await Assert.ThrowsAsync<InvalidDataException>(()=>client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None));
                Assert.Equal(1,calls);
            }
        }
        [Theory]
        [InlineData("http://mods.example.test/")]
        [InlineData("https://user:secret@mods.example.test/")]
        public void Unsafe_origins_fail(string origin)
        { using(var http=new HttpClient()) Assert.Throws<InvalidDataException>(()=>new ModCatalogClient(http,new Uri(origin),root)); }
        [Fact]
        public async Task Foreign_media_is_rejected_before_network()
        {
            int calls=0;
            using(var http=new HttpClient(new Handler(r=>{calls++;return Json(Empty);})))
            using(var client=new ModCatalogClient(http,Origin,root)) {
                await Assert.ThrowsAsync<InvalidDataException>(()=>client.MediaAsync(new Uri("https://evil.test/a.png"),CancellationToken.None));
                Assert.Equal(0,calls);
            }
        }
        [Fact]
        public async Task Invalid_utf8_and_oversized_actual_json_are_rejected()
        {
            foreach(var bytes in new[]{new byte[]{0xff,0xff},Encoding.UTF8.GetBytes(new string(' ',4194305))})
            using(var http=new HttpClient(new Handler(r=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})))
            using(var client=new ModCatalogClient(http,Origin,root))
                await Assert.ThrowsAsync<InvalidDataException>(()=>client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None));
        }
        [Fact]
        public async Task Not_modified_requires_valid_matching_cache()
        {
            using(var http=new HttpClient(new Handler(r=>new HttpResponseMessage(HttpStatusCode.NotModified))))
            using(var client=new ModCatalogClient(http,Origin,root))
                await Assert.ThrowsAsync<InvalidDataException>(()=>client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None));
        }
        [Fact]
        public async Task Cancellation_is_not_hidden_by_offline_cache()
        {
            using(var http=new HttpClient(new Handler(r=>Json(Empty))))
            using(var client=new ModCatalogClient(http,Origin,root)) {
                await client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.CatalogAsync("CUSA00411","1.57",1,new CancellationToken(true)));
            }
        }
        [Fact]
        public async Task Rate_limit_is_not_presented_as_valid_empty_catalog()
        {
            using(var http=new HttpClient(new Handler(r=>new HttpResponseMessage((HttpStatusCode)429))))
            using(var client=new ModCatalogClient(http,Origin,root))
                await Assert.ThrowsAsync<ModHttpException>(()=>client.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None));
        }
        [Theory]
        [InlineData(11)]
        [InlineData(13)]
        public async Task Inexact_download_is_never_accepted(int bytes)
        {
            var release=ModJson.ReadRelease(ModContractTests.Item.TrimEnd('}')+",\"state\":\"published\",\"withdrawalReason\":null,\"changelog\":\"\",\"signedEnvelope\":\"fixture\",\"manifest\":{}}");
            var target=Path.Combine(root,"download.zip");
            using(var http=new HttpClient(new Handler(r=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[bytes])})))
            using(var client=new ModCatalogClient(http,Origin,root)) {
                await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(release,target,null,CancellationToken.None));
                Assert.False(File.Exists(target));
                Assert.Empty(Directory.GetFiles(root,"*.download"));
            }
        }
        [Fact]
        public async Task Valid_download_never_overwrites_existing_file()
        {
            Directory.CreateDirectory(root); var target=Path.Combine(root,"existing.zip");File.WriteAllText(target,"keep");
            var release=ModJson.ReadRelease(ModContractTests.Item.TrimEnd('}')+",\"state\":\"published\",\"withdrawalReason\":null,\"changelog\":\"\",\"signedEnvelope\":\"fixture\",\"manifest\":{}}");
            using(var http=new HttpClient(new Handler(r=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[12])})))
            using(var client=new ModCatalogClient(http,Origin,root)) {
                await Assert.ThrowsAsync<IOException>(()=>client.DownloadAsync(release,target,null,CancellationToken.None));
                Assert.Equal("keep",File.ReadAllText(target));
            }
        }
        public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
        private static HttpResponseMessage Json(string body){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
        private sealed class Handler:HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage,HttpResponseMessage> send;
            public Handler(Func<HttpRequestMessage,HttpResponseMessage> send){this.send=send;}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(send(request));}
        }
    }
}
