using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Mods
{
    /// <summary>Strict bounded JSON, including duplicate members before model conversion.</summary>
    public static class ModJson
    {
        public static ModCatalogPage ReadCatalog(string json)
        {
            var value = Object(Parse(json));
            var page = new ModCatalogPage { Page = (int)Number(value,"page",1,1000000), PageSize = (int)Number(value,"pageSize",1,100), FetchedUtc = DateTimeOffset.UtcNow };
            var items = Array(value,"items");
            if (items.Count > page.PageSize) throw Bad();
            page.Items = items.Select(x => Item(Object(x), new ModCatalogItem())).ToList().AsReadOnly();
            return page;
        }
        public static ModRelease ReadRelease(string json)
        {
            var value = Object(Parse(json));
            var release = (ModRelease)Item(value, new ModRelease());
            release.State = Text(value,"state",20);
            if (release.State != "published" && release.State != "withdrawn") throw Bad();
            release.Changelog = Text(value,"changelog",100000, true);
            release.WithdrawalReason = NullableText(value,"withdrawalReason",100000);
            release.SignedEnvelope = Text(value,"signedEnvelope",100000);
            release.ManifestData = Object(Get(value,"manifest"));
            release.RawJson=json;
            return release;
        }
        public static ModDetail ReadMod(string json)
        {
            var value = Object(Parse(json));
            var detail = new ModDetail { ModId = Id(value,"modId"), Title = Text(value,"title",255), Description = Text(value,"description",100000) };
            var releases = Array(value,"releases");
            if (releases.Count > 1000) throw Bad();
            detail.Releases = releases.Select(x => Item(Object(x),new ModCatalogItem())).ToList().AsReadOnly();
            if (detail.Releases.Any(x => x.ModId != detail.ModId)) throw Bad();
            var media = Array(value,"media");
            if (media.Count > 100) throw Bad();
            detail.Media = media.Select(x => {
                var m = Object(x); var kind = Text(m,"kind",20);
                if (kind != "cover" && kind != "screenshot" && kind != "video") throw Bad();
                return new ModMedia { Kind=kind, Position=(int)Number(m,"position",0,100000), Url=Url(Text(m,"url",2048)) };
            }).ToList().AsReadOnly();
            return detail;
        }
        private static ModCatalogItem Item(Dictionary<string,object> v, ModCatalogItem item)
        {
            item.ModId=Id(v,"modId"); item.ReleaseId=Id(v,"releaseId");
            item.Slug=Text(v,"slug",160); item.Title=Text(v,"title",255); item.Author=Text(v,"author",255);
            item.Summary=Text(v,"summary",1000,true); item.Category=Text(v,"category",100);
            var cover=NullableText(v,"coverUrl",2048); item.CoverUrl=cover==null?null:Url(cover);
            item.DetailUrl=Url(Text(v,"detailUrl",2048));
            item.ReleaseVersion=Text(v,"releaseVersion",100);
            if (item.ReleaseVersion.Any(char.IsControl)) throw Bad();
            item.PackageType=Text(v,"packageType",20);
            if (item.PackageType!="files" && item.PackageType!="luma-script") throw Bad();
            item.PackageSize=Number(v,"packageSize",1,2147483648L);
            item.Sha256=Hex(v,"sha256",64); item.SigningKeyId=Hex(v,"signingKeyId",16);
            item.Signature=Text(v,"signature",2048);
            try { Convert.FromBase64String(item.Signature); } catch(FormatException) { throw Bad(); }
            var pairs=Array(v,"compatibility");
            if(pairs.Count==0 || pairs.Count>1000) throw Bad();
            try { item.Compatibility=pairs.Select(p=>{var pair=Object(p); return new ModCompatibility(Text(pair,"titleId",20),Text(pair,"gameVersion",20));}).ToList().AsReadOnly(); }
            catch(FormatException) { throw Bad(); }
            if(item.Compatibility.Select(p=>p.Key).Distinct(StringComparer.Ordinal).Count()!=pairs.Count) throw Bad();
            DateTimeOffset date;
            if(!DateTimeOffset.TryParse(Text(v,"publishedUtc",60),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out date) || date.Offset!=TimeSpan.Zero) throw Bad();
            item.PublishedUtc=date; return item;
        }
        internal static object Parse(string json)
        {
            if(json==null || Encoding.UTF8.GetByteCount(json)>4194304) throw Bad();
            return new Parser(json).Read();
        }
        internal static Dictionary<string,object> Object(object value) { var v=value as Dictionary<string,object>; if(v==null) throw Bad(); return v; }
        internal static object Get(Dictionary<string,object> v,string key) { object item; if(!v.TryGetValue(key,out item)) throw Bad(); return item; }
        internal static List<object> Array(Dictionary<string,object> v,string key) { var a=Get(v,key) as List<object>; if(a==null) throw Bad(); return a; }
        internal static string Text(Dictionary<string,object> v,string key,int max,bool empty=false)
        { var s=Get(v,key) as string; if(s==null || s.Length>max || (!empty && s.Length==0)) throw Bad(); return s; }
        internal static string NullableText(Dictionary<string,object> v,string key,int max)
        { return Get(v,key)==null?null:Text(v,key,max,true); }
        internal static long Number(Dictionary<string,object> v,string key,long min,long max)
        { var n=Get(v,key); if(!(n is decimal)) throw Bad(); var d=(decimal)n; if(d<min || d>max || decimal.Truncate(d)!=d) throw Bad(); return (long)d; }
        internal static Guid Id(Dictionary<string,object> v,string key)
        { var s=Text(v,key,36); Guid id; if(!Regex.IsMatch(s,"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$") || !Guid.TryParseExact(s,"D",out id)) throw Bad(); return id; }
        private static string Hex(Dictionary<string,object> v,string key,int len) { var s=Text(v,key,len); if(!Regex.IsMatch(s,"^[0-9a-f]{"+len+"}$")) throw Bad(); return s; }
        internal static Uri Url(string value)
        { Uri uri; if(!Uri.TryCreate(value,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.UserInfo.Length!=0 || uri.Fragment.Length!=0) throw Bad(); return uri; }
        internal static InvalidDataException Bad() { return new InvalidDataException("Invalid mod data."); }

        private sealed class Parser
        {
            private readonly string text; private int index;
            public Parser(string text) { this.text=text; }
            public object Read() { var value=Value(0); White(); if(index!=text.Length) throw Bad(); return value; }
            private void White() { while(index<text.Length && (text[index]==' '||text[index]=='\r'||text[index]=='\n'||text[index]=='\t')) index++; }
            private bool Take(char c) { White(); if(index<text.Length && text[index]==c) {index++;return true;} return false; }
            private void Need(char c) { if(!Take(c)) throw Bad(); }
            private object Value(int depth)
            {
                White(); if(depth>32 || index>=text.Length) throw Bad();
                if(Take('{')) {
                    var obj=new Dictionary<string,object>(StringComparer.Ordinal);
                    if(Take('}')) return obj;
                    do { White(); var key=String(); Need(':'); if(obj.ContainsKey(key)) throw Bad(); obj.Add(key,Value(depth+1)); } while(Take(','));
                    Need('}'); return obj;
                }
                if(Take('[')) {
                    var array=new List<object>(); if(Take(']')) return array;
                    do { if(array.Count>=100001) throw Bad(); array.Add(Value(depth+1)); } while(Take(','));
                    Need(']'); return array;
                }
                if(text[index]=='"') return String();
                foreach(var keyword in new[]{"null","true","false"}) if(index+keyword.Length<=text.Length && string.CompareOrdinal(text,index,keyword,0,keyword.Length)==0) { index+=keyword.Length; return keyword=="null"?null:(object)(keyword=="true"); }
                var start=index; while(index<text.Length && "0123456789.eE+-".IndexOf(text[index])>=0) index++;
                var number=text.Substring(start,index-start); decimal value;
                if(!Regex.IsMatch(number,@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$") || !decimal.TryParse(number,NumberStyles.Float,CultureInfo.InvariantCulture,out value)) throw Bad();
                return value;
            }
            private string String()
            {
                if(index>=text.Length || text[index++]!='"') throw Bad(); var result=new StringBuilder(); bool closed=false;
                while(index<text.Length) {
                    char c=text[index++]; if(c=='"'){closed=true;break;} if(c<32) throw Bad();
                    if(c=='\\') {
                        if(index>=text.Length) throw Bad(); c=text[index++];
                        switch(c) {
                            case '"': case '\\': case '/': break;
                            case 'b': c='\b';break; case 'f':c='\f';break;case 'n':c='\n';break;case 'r':c='\r';break;case 't':c='\t';break;
                            case 'u': ushort code; if(index+4>text.Length || !ushort.TryParse(text.Substring(index,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out code))throw Bad();index+=4;c=(char)code;break;
                            default:throw Bad();
                        }
                    }
                    result.Append(c);
                }
                if(!closed) throw Bad(); var s=result.ToString();
                for(int i=0;i<s.Length;i++) { if(char.IsHighSurrogate(s[i])) { if(++i>=s.Length || !char.IsLowSurrogate(s[i])) throw Bad(); } else if(char.IsLowSurrogate(s[i])) throw Bad(); }
                return s;
            }
        }
    }
}
