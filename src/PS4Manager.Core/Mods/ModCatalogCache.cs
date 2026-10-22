using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace PS4Manager.Core.Mods
{
    public sealed class ModCatalogCache
    {
        private readonly string root;
        public ModCatalogCache(string root) { this.root=Path.GetFullPath(root); }
        internal CacheRecord Read(Uri uri)
        {
            var path=FileName(uri);
            try {
                if(!File.Exists(path) || new FileInfo(path).Length>8388608) return null;
                using(var input=File.OpenRead(path)) {
                    var record=(CacheRecord)new DataContractJsonSerializer(typeof(CacheRecord)).ReadObject(input);
                    if(record==null || record.Uri!=uri.AbsoluteUri || record.FetchedUtc>DateTime.UtcNow.AddMinutes(5)) return null;
                    ModJson.ReadCatalog(record.Body);
                    return record;
                }
            } catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is SerializationException || ex is ArgumentException) { return null; }
        }
        internal void Write(Uri uri,string body,string etag)
        {
            ModJson.ReadCatalog(body);
            Directory.CreateDirectory(root);
            var target=FileName(uri); var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    new DataContractJsonSerializer(typeof(CacheRecord)).WriteObject(output,new CacheRecord { Uri=uri.AbsoluteUri, Body=body, ETag=etag, FetchedUtc=DateTime.UtcNow });
                    output.Flush(true);
                }
                lock(this) { if(File.Exists(target)) File.Replace(temporary,target,null); else File.Move(temporary,target); }
            } finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        private string FileName(Uri uri)
        {
            using(var sha=SHA256.Create()) return Path.Combine(root,BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).Replace("-","")+".json");
        }
        [DataContract]
        internal sealed class CacheRecord
        {
            [DataMember] public string Uri;
            [DataMember] public string Body;
            [DataMember] public string ETag;
            [DataMember] public DateTime FetchedUtc;
        }
    }
}
