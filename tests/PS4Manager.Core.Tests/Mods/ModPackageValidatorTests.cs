using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModPackageValidatorTests:IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"mod-zip-"+Guid.NewGuid().ToString("N"));
        [Fact]
        public void Valid_zip_extracts_only_declared_files()
        {
            var zip=MakeZip();var result=Validate(zip);
            Assert.Equal("content",File.ReadAllText(Assert.Single(result.Files).LocalPath));
        }
        [Theory]
        [InlineData("payload/extra.txt")][InlineData("payload/FILE.txt")][InlineData("../escape")]
        public void Undeclared_colliding_and_traversal_entries_fail(string extra)
        {Assert.Throws<InvalidDataException>(()=>Validate(MakeZip(extra)));}
        [Theory]
        [InlineData("flags")][InlineData("symlink")][InlineData("crc")][InlineData("name")][InlineData("method")]
        public void Forged_zip_metadata_fails(string field)
        {
            var zip=MakeZip();var bytes=File.ReadAllBytes(zip);int central=Find(bytes,0x02014b50);
            switch(field){
                case "flags":bytes[central+8]|=1;break;
                case "symlink":bytes[central+41]=0xa0;break;
                case "crc":bytes[central+16]^=0xff;break;
                case "name":bytes[30]^=1;break;
                case "method":bytes[central+10]=99;break;
            }
            File.WriteAllBytes(zip,bytes);Assert.Throws<InvalidDataException>(()=>Validate(zip));
        }
        [Fact]
        public void Existing_staging_directory_is_never_reused()
        {
            var zip=MakeZip();Directory.CreateDirectory(Path.Combine(root,"stage"));File.WriteAllText(Path.Combine(root,"stage","keep"),"original");
            Assert.Throws<IOException>(()=>Validate(zip));Assert.Equal("original",File.ReadAllText(Path.Combine(root,"stage","keep")));
        }
        [Fact]
        public void Truncated_archive_and_enormous_expansion_are_rejected()
        {
            var zip=MakeZip();var bytes=File.ReadAllBytes(zip);Array.Resize(ref bytes,bytes.Length-10);File.WriteAllBytes(zip,bytes);
            Assert.Throws<InvalidDataException>(()=>Validate(zip));
            zip=MakeZip();bytes=File.ReadAllBytes(zip);int central=Find(bytes,0x02014b50);Buffer.BlockCopy(BitConverter.GetBytes(2000000000U),0,bytes,central+24,4);File.WriteAllBytes(zip,bytes);
            Assert.Throws<InvalidDataException>(()=>Validate(zip));
        }
        [Fact]
        public void Release_manifest_must_match_archive()
        {
            var zip=MakeZip();var release=Release();release.ManifestData=ModJson.Object(ModJson.Parse(ModManifestTests.Manifest("payload/file.txt","/data/other")));
            Assert.Throws<InvalidDataException>(()=>new ModPackageValidator().ValidateAndExtract(zip,Path.Combine(root,"stage"),release,CancellationToken.None));
        }
        [Fact]
        public void Cancelled_extraction_leaves_no_staging_directory()
        {
            var zip=MakeZip();
            Assert.ThrowsAny<OperationCanceledException>(()=>new ModPackageValidator().ValidateAndExtract(zip,Path.Combine(root,"stage"),Release(),new CancellationToken(true)));
            Assert.False(Directory.Exists(Path.Combine(root,"stage")));
        }
        [Fact]
        public void Failed_payload_integrity_removes_its_new_staging_only()
        {
            var zip=MakeZip();var bytes=File.ReadAllBytes(zip);int central=0;
            for(int i=0;i<bytes.Length-4;i++)if(BitConverter.ToUInt32(bytes,i)==0x02014b50)central=i;
            int local=(int)BitConverter.ToUInt32(bytes,central+42);
            bytes[central+16]^=1;bytes[local+14]^=1;File.WriteAllBytes(zip,bytes);
            Assert.Throws<InvalidDataException>(()=>Validate(zip));
            Assert.False(Directory.Exists(Path.Combine(root,"stage")));
            Assert.True(File.Exists(zip));
        }
        private ValidatedModPackage Validate(string zip){return new ModPackageValidator().ValidateAndExtract(zip,Path.Combine(root,"stage"),Release(),CancellationToken.None);}
        private static ModRelease Release(){return new ModRelease {PackageType="files",ManifestData=ModJson.Object(ModJson.Parse(ModManifestTests.Manifest()))};}
        private string MakeZip(string extra=null)
        {
            Directory.CreateDirectory(root);var zip=Path.Combine(root,"package.zip");
            using(var stream=File.Create(zip))using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)){
                Put(archive,"manifest.json",ModManifestTests.Manifest());Put(archive,"payload/file.txt","content");if(extra!=null)Put(archive,extra,"extra");
            }
            return zip;
        }
        private static void Put(ZipArchive zip,string path,string text){using(var writer=new StreamWriter(zip.CreateEntry(path,CompressionLevel.NoCompression).Open(),new UTF8Encoding(false)))writer.Write(text);}
        private static int Find(byte[] bytes,uint sig){for(int i=0;i<bytes.Length-4;i++)if(BitConverter.ToUInt32(bytes,i)==sig)return i;throw new Exception("Missing central directory");}
        public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
