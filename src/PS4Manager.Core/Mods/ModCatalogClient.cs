using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Mods
{
    public sealed class ModHttpException : IOException
    {
        public ModHttpException(int status,string retry) : base("Mod server returned HTTP "+status+(string.IsNullOrEmpty(retry)?".":". Retry after "+retry+".")) { StatusCode=status; }
        public int StatusCode { get; }
    }
    public sealed class ModCatalogClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly ModCatalogCache cache;
        private readonly bool ownsHttp;
        public Uri Origin { get; }
        public ModCatalogClient(Uri origin,string cacheRoot) : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan },origin,cacheRoot) { ownsHttp=true; }
        public ModCatalogClient(HttpClient http,Uri origin,string cacheRoot)
        {
            this.http=http??throw new ArgumentNullException(nameof(http));
            Origin=ModJson.Url(origin.AbsoluteUri);
            if(Origin.AbsolutePath!="/" || Origin.Query.Length!=0)throw ModJson.Bad();
            cache=new ModCatalogCache(cacheRoot);
        }
        public async Task<ModCatalogPage> CatalogAsync(string titleId,string version,int page,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(page<1 || page>1000000)throw new ArgumentOutOfRangeException(nameof(page));
            var uri=Api("catalog?titleId="+ModCompatibility.NormalizeTitleId(titleId)+"&gameVersion="+ModCompatibility.NormalizeVersion(version)+"&page="+page+"&pageSize=24");
            var previous=cache.Read(uri);
            using(var timeout=TimeoutFor(ct,30))
            using(var request=new HttpRequestMessage(HttpMethod.Get,uri)) {
                if(previous!=null && !string.IsNullOrEmpty(previous.ETag))request.Headers.TryAddWithoutValidation("If-None-Match",previous.ETag);
                try {
                    using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                        string body;
                        if(response.StatusCode==HttpStatusCode.NotModified) {
                            if(previous==null)throw ModJson.Bad(); body=previous.Body;
                        } else {
                            CheckResponse(response,uri); body=Decode(await Bytes(response,4194304,timeout.Token).ConfigureAwait(false));
                        }
                        var result=ModJson.ReadCatalog(body);
                        if(result.Page!=page || result.PageSize!=24)throw ModJson.Bad();
                        foreach(var item in result.Items) {
                            CheckUri(item.DetailUrl); if(item.CoverUrl!=null)CheckUri(item.CoverUrl);
                            bool matches=false; foreach(var pair in item.Compatibility)if(pair.TitleId==ModCompatibility.NormalizeTitleId(titleId)&&pair.GameVersion==ModCompatibility.NormalizeVersion(version))matches=true;
                            if(!matches)throw ModJson.Bad();
                        }
                        try { cache.Write(uri,body,response.Headers.ETag==null?previous?.ETag:response.Headers.ETag.ToString()); } catch(IOException) { /* Cache failure must not hide a valid online result. */ }
                        return result;
                    }
                } catch(HttpRequestException) when(previous!=null && !ct.IsCancellationRequested) {
                    var result=ModJson.ReadCatalog(previous.Body); result.IsOffline=true; result.FetchedUtc=new DateTimeOffset(previous.FetchedUtc,TimeSpan.Zero);return result;
                }
            }
        }
        public async Task<ModDetail> DetailAsync(Guid modId,CancellationToken ct)
        {
            var result=ModJson.ReadMod(await JsonAsync(Api("mods/"+modId.ToString("D")),ct).ConfigureAwait(false));
            if(result.ModId!=modId)throw ModJson.Bad();
            foreach(var m in result.Media)CheckUri(m.Url);
            foreach(var r in result.Releases){CheckUri(r.DetailUrl);if(r.CoverUrl!=null)CheckUri(r.CoverUrl);}
            return result;
        }
        public async Task<ModRelease> ReleaseAsync(Guid releaseId,CancellationToken ct)
        {
            var result=ModJson.ReadRelease(await JsonAsync(Api("releases/"+releaseId.ToString("D")),ct).ConfigureAwait(false));
            if(result.ReleaseId!=releaseId)throw ModJson.Bad(); CheckUri(result.DetailUrl);if(result.CoverUrl!=null)CheckUri(result.CoverUrl);return result;
        }
        public async Task DownloadAsync(ModRelease release,string newFile,IProgress<long> progress,CancellationToken ct)
        {
            if(release.State!="published" || release.PackageSize<1 || release.PackageSize>2147483648L)throw ModJson.Bad();
            if(File.Exists(newFile))throw new IOException("Download destination already exists.");
            var temporary=newFile+"."+Guid.NewGuid().ToString("N")+".download";
            var uri=Api("releases/"+release.ReleaseId.ToString("D")+"/download");
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newFile)));
                using(var timeout=TimeoutFor(ct,600))
                using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                    CheckResponse(response,uri);
                    if(response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value!=release.PackageSize)throw ModJson.Bad();
                    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        var count=await Copy(response,output,release.PackageSize,progress,timeout.Token).ConfigureAwait(false);
                        if(count!=release.PackageSize)throw new InvalidDataException("Incomplete mod package download.");
                        output.Flush(true);
                    }
                }
                File.Move(temporary,newFile);
            } finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
        public async Task<byte[]> MediaAsync(Uri url,CancellationToken ct)
        {
            CheckUri(url);
            if(!url.AbsolutePath.StartsWith("/api/v1/media/",StringComparison.Ordinal))throw ModJson.Bad();
            using(var timeout=TimeoutFor(ct,30))
            using(var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                CheckResponse(response,url);var type=response.Content.Headers.ContentType?.MediaType;
                if(type!="image/png" && type!="image/jpeg" && type!="video/mp4")throw new InvalidDataException("Unsupported mod preview type.");
                return await Bytes(response,52428800,timeout.Token).ConfigureAwait(false);
            }
        }
        private async Task<string> JsonAsync(Uri uri,CancellationToken ct)
        {
            using(var timeout=TimeoutFor(ct,30))
            using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
                CheckResponse(response,uri);return Decode(await Bytes(response,4194304,timeout.Token).ConfigureAwait(false));
            }
        }
        private Uri Api(string path){return new Uri(Origin,"api/v1/"+path);}
        private void CheckUri(Uri uri)
        {
            ModJson.Url(uri.AbsoluteUri);
            if(uri.Scheme!=Origin.Scheme || uri.Host!=Origin.Host || uri.Port!=Origin.Port)throw new InvalidDataException("Untrusted mod origin.");
        }
        private void CheckResponse(HttpResponseMessage response,Uri requested)
        {
            if(response.RequestMessage!=null && response.RequestMessage.RequestUri!=requested)throw new InvalidDataException("Redirected mod request.");
            var code=(int)response.StatusCode;if(code>=300 && code<400)throw new InvalidDataException("Mod server redirects are not allowed.");
            if(code!=200)throw new ModHttpException(code,response.Headers.RetryAfter?.ToString());
        }
        private static string Decode(byte[] bytes){try{return new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){throw ModJson.Bad();}}
        private static CancellationTokenSource TimeoutFor(CancellationToken ct,int seconds){var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));return timeout;}
        private static async Task<byte[]> Bytes(HttpResponseMessage response,long maximum,CancellationToken ct)
        {using(var output=new MemoryStream()){await Copy(response,output,maximum,null,ct).ConfigureAwait(false);return output.ToArray();}}
        private static async Task<long> Copy(HttpResponseMessage response,Stream output,long maximum,IProgress<long> progress,CancellationToken ct)
        {
            if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("Mod response exceeds limit.");
            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) {
                var buffer=new byte[65536];long total=0;int count;
                while((count=await input.ReadAsync(buffer,0,buffer.Length,ct).ConfigureAwait(false))>0) {
                    total=checked(total+count);if(total>maximum)throw new InvalidDataException("Mod response exceeds limit.");
                    await output.WriteAsync(buffer,0,count,ct).ConfigureAwait(false);progress?.Report(total);
                }
                return total;
            }
        }
        public void Dispose(){if(ownsHttp)http.Dispose();}
    }
}
