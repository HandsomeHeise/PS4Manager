using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FileManager.Core.Zones;

namespace PS4Manager.Core.Mods
{
    public sealed class ModOperation
    {
        public string Source { get; internal set; }
        public string Target { get; internal set; }
        public Zone Zone { get; internal set; }
    }
    public sealed class ModManifest
    {
        public string PackageType { get; private set; }
        public string LumaProfile { get; private set; }
        public IReadOnlyList<ModOperation> Operations { get; private set; }
        public static ModManifest Parse(string json){return Parse(ModJson.Object(ModJson.Parse(json)));}
        internal static ModManifest Parse(Dictionary<string,object> data)
        {
            var kind=ModJson.Text(data,"packageType",20);bool luma=kind=="luma-script";
            Exact(data,luma?new[]{"formatVersion","packageType","operations","lumaProfile"}:new[]{"formatVersion","packageType","operations"});
            if(ModJson.Number(data,"formatVersion",1,1)!=1 || (!luma && kind!="files"))throw ModJson.Bad();
            var profile=luma?ModJson.Text(data,"lumaProfile",20):null;
            if(luma && profile!="gta5" && profile!="rdr2")throw ModJson.Bad();
            var array=ModJson.Array(data,"operations");if(array.Count<1 || array.Count>10000)throw ModJson.Bad();
            var operations=new List<ModOperation>();var sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var policy=new ZonePolicy();
            foreach(var value in array) {
                var op=ModJson.Object(value);Exact(op,new[]{"source","target"});var source=ModJson.Text(op,"source",1024);var target=ModJson.Text(op,"target",1025);
                ValidatePath(source);if(!source.StartsWith("payload/",StringComparison.Ordinal))throw ModJson.Bad();
                if(!target.StartsWith("/",StringComparison.Ordinal))throw ModJson.Bad();ValidatePath(target.Substring(1));
                var zone=policy.Classify(target);if(zone==Zone.Locked)throw new InvalidDataException("Package targets a forbidden PS4 path.");
                if(luma && !target.StartsWith(profile=="gta5"?"/data/gtalua/scripts/":"/data/rdr2lua/scripts/",StringComparison.Ordinal))throw ModJson.Bad();
                if(!sources.Add(source)||!targets.Add(FileManager.Core.Paths.RemotePath.Canonical(target)))throw new InvalidDataException("Colliding package paths.");
                operations.Add(new ModOperation {Source=source,Target=target,Zone=zone});
            }
            NoPrefixes(sources);NoPrefixes(targets);
            return new ModManifest {PackageType=kind,LumaProfile=profile,Operations=operations.AsReadOnly()};
        }
        internal static void Exact(Dictionary<string,object> value,string[] fields)
        {if(value.Count!=fields.Length || fields.Any(f=>!value.ContainsKey(f)))throw ModJson.Bad();}
        internal static void ValidatePath(string path)
        {
            if(string.IsNullOrEmpty(path)||path.Length>1024||!path.IsNormalized(NormalizationForm.FormC))throw ModJson.Bad();
            foreach(var part in path.Split('/'))
                if(part.Length==0||part=="."||part==".."||Encoding.UTF8.GetByteCount(part)>255||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||
                   Regex.IsMatch(part,"[\\\\<>:\"|?*\\x00-\\x1f\\x7f]")||Regex.IsMatch(part,@"^(con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))throw new InvalidDataException("Unsafe package path.");
        }
        internal static void NoPrefixes(HashSet<string> paths)
        {foreach(var path in paths)for(int i=path.IndexOf('/');i>=0;i=path.IndexOf('/',i+1))if(paths.Contains(path.Substring(0,i)))throw new InvalidDataException("File/directory target conflict.");}
        internal bool SameAs(ModManifest other)
        {return other.PackageType==PackageType && other.LumaProfile==LumaProfile && other.Operations.Count==Operations.Count && Operations.Zip(other.Operations,(a,b)=>a.Source==b.Source&&a.Target==b.Target).All(x=>x);}
    }
}
