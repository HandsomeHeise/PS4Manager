using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace PS4Manager.Core.Mods
{
    public sealed class ModPayloadFile
    {
        public string Source {get;internal set;}
        public string Target {get;internal set;}
        public string LocalPath {get;internal set;}
        public string Sha256 {get;internal set;}
        public long Size {get;internal set;}
    }
    public sealed class ValidatedModPackage
    {
        public string StagingRoot {get;internal set;}
        public ModManifest Manifest {get;internal set;}
        public IReadOnlyList<ModPayloadFile> Files {get;internal set;}
    }
    public sealed class ModPackageValidator
    {
        private static readonly uint[] CrcTable=BuildCrcTable();
        public ValidatedModPackage ValidateAndExtract(string zipPath,string newStagingRoot,ModRelease release,CancellationToken ct)
        {
            var root=Path.GetFullPath(newStagingRoot);
            if(Directory.Exists(root)||File.Exists(root))throw new IOException("Mod staging path already exists.");
            NoReparseAncestors(root);
            bool created=false, complete=false;
            try {
                using(var input=File.OpenRead(zipPath)) {
                    var headers=ModZipDirectory.Read(input);input.Position=0;
                    using(var archive=new ZipArchive(input,ZipArchiveMode.Read,true)) {
                        if(archive.Entries.Count!=headers.Count)throw ModJson.Bad();
                        var manifestEntry=archive.GetEntry("manifest.json");if(manifestEntry==null)throw ModJson.Bad();
                        string json;
                        using(var output=new MemoryStream()) {CopyChecked(manifestEntry,headers.Single(h=>h.Name=="manifest.json"),output,ct);try{json=new UTF8Encoding(false,true).GetString(output.ToArray());}catch(DecoderFallbackException){throw ModJson.Bad();}}
                        var manifest=ModManifest.Parse(json);
                        if(manifest.PackageType!=release.PackageType||release.ManifestData==null||!manifest.SameAs(ModManifest.Parse(release.ManifestData)))throw new InvalidDataException("Archive manifest differs from release metadata.");
                        var payload=headers.Where(h=>!h.Directory && h.Name!="manifest.json").ToList();
                        if(payload.Count!=manifest.Operations.Count||payload.Any(h=>!manifest.Operations.Any(op=>op.Source==h.Name)))throw new InvalidDataException("Archive payload differs from manifest.");
                        // Validate every local path before creating the staging directory.
                        var paths=manifest.Operations.ToDictionary(op=>op.Source,op=>LocalPath(root,op.Source),StringComparer.Ordinal);
                        Directory.CreateDirectory(root);created=true;var files=new List<ModPayloadFile>();
                        foreach(var operation in manifest.Operations) {
                            ct.ThrowIfCancellationRequested();var path=paths[operation.Source];Directory.CreateDirectory(Path.GetDirectoryName(path));NoReparseAncestors(path);
                            var entry=archive.GetEntry(operation.Source);var header=payload.Single(h=>h.Name==operation.Source);
                            using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))CopyChecked(entry,header,output,ct);
                            files.Add(new ModPayloadFile {Source=operation.Source,Target=operation.Target,LocalPath=path,Size=header.Size,Sha256=ModSignatureVerifier.HashFile(path)});
                        }
                        complete=true;
                        return new ValidatedModPackage {StagingRoot=root,Manifest=manifest,Files=files.AsReadOnly()};
                    }
                }
            } catch(EndOfStreamException ex) {throw new InvalidDataException("Truncated mod archive.",ex);}
            finally {
                if(created && !complete) DeleteNewStaging(root);
            }
        }
        internal static void DeleteNewStaging(string root)
        {
            // Only a directory created by this invocation, never a pre-existing destination.
            NoReparseAncestors(root);
            var directories=new List<string>{root};
            for(int i=0;i<directories.Count;i++) {
                foreach(var child in Directory.GetDirectories(directories[i])) {NoReparseAncestors(child);directories.Add(child);}
                foreach(var file in Directory.GetFiles(directories[i])) {NoReparseAncestors(file);File.Delete(file);}
            }
            for(int i=directories.Count-1;i>=0;i--)Directory.Delete(directories[i],false);
        }
        internal static string LocalPath(string root,string relative)
        {
            var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||full.Length>=248)throw new InvalidDataException("Unsafe or unsupported local package path.");
            return full;
        }
        internal static void NoReparseAncestors(string path)
        {for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Mod storage cannot use reparse points.");}
        private static void CopyChecked(ZipArchiveEntry entry,ModZipDirectory.Entry header,Stream output,CancellationToken ct)
        {
            if(entry.Length!=header.Size||entry.CompressedLength!=header.Compressed)throw ModJson.Bad();
            using(var input=entry.Open()) {
                var bytes=new byte[65536];long count=0;uint crc=0xffffffff;int read;
                while((read=input.Read(bytes,0,bytes.Length))>0) {
                    ct.ThrowIfCancellationRequested();count=checked(count+read);if(count>header.Size)throw ModJson.Bad();
                    for(int i=0;i<read;i++)crc=CrcTable[(crc^bytes[i])&255]^(crc>>8);
                    output.Write(bytes,0,read);
                }
                if(count!=header.Size || (crc^0xffffffff)!=header.Crc)throw new InvalidDataException("Mod ZIP CRC or size mismatch.");
            }
        }
        private static uint[] BuildCrcTable(){var table=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int bit=0;bit<8;bit++)c=(c&1)!=0?0xedb88320^(c>>1):c>>1;table[i]=c;}return table;}
    }
}
