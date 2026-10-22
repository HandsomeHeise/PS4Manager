using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using FileManager.Core.Zones;

namespace PS4Manager.Core.Mods
{
    public sealed class ModReceiptStore
    {
        private readonly string root;
        private readonly object sync=new object();
        public ModReceiptStore(string root){this.root=Path.GetFullPath(root);ModPackageValidator.NoReparseAncestors(this.root);}
        public string Root {get{return root;}}
        public IDisposable AcquireLease(Guid profile)
        {
            var dir=ProfileRoot(profile);ModPackageValidator.NoReparseAncestors(dir);Directory.CreateDirectory(dir);
            return new FileStream(Path.Combine(dir,"transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        public string JournalPath(Guid profile,Guid transaction){return Path.Combine(ProfileRoot(profile),Id(transaction)+".json");}
        public string StagingPath(Guid profile,Guid transaction){return Path.Combine(ProfileRoot(profile),"staging",Id(transaction));}
        public string BackupPath(Guid profile,string hash){Hash(hash);return Path.Combine(ProfileRoot(profile),"backups",hash+".bin");}
        public string VerifiedBackupPath(Guid profile,string hash){var path=BackupPath(profile,hash);ModPackageValidator.NoReparseAncestors(path);if(!File.Exists(path)||ModSignatureVerifier.HashFile(path)!=hash)throw new InvalidDataException("Missing or damaged mod safety backup.");return path;}
        public byte[] ReadBackup(Guid profile,string hash){return File.ReadAllBytes(VerifiedBackupPath(profile,hash));}
        public string SaveBackup(Guid profile,byte[] bytes)
        {
            string hash;using(var input=new MemoryStream(bytes,false))hash=ModSignatureVerifier.Hash(input);
            var path=BackupPath(profile,hash);Directory.CreateDirectory(Path.GetDirectoryName(path));ModPackageValidator.NoReparseAncestors(path);
            lock(sync) {if(!File.Exists(path))Atomic(path,output=>output.Write(bytes,0,bytes.Length));VerifiedBackupPath(profile,hash);}return hash;
        }
        public string SaveBackupFile(Guid profile,string source)
        {
            var hash=ModSignatureVerifier.HashFile(source);var path=BackupPath(profile,hash);Directory.CreateDirectory(Path.GetDirectoryName(path));ModPackageValidator.NoReparseAncestors(path);
            lock(sync){if(!File.Exists(path))Atomic(path,output=>{using(var input=File.OpenRead(source))input.CopyTo(output);});VerifiedBackupPath(profile,hash);}return hash;
        }
        public void Save(ModReceipt receipt)
        {
            Validate(receipt);var path=JournalPath(receipt.ProfileId,receipt.TransactionId);Directory.CreateDirectory(Path.GetDirectoryName(path));ModPackageValidator.NoReparseAncestors(path);
            lock(sync){receipt.UpdatedUtc=DateTime.UtcNow;Atomic(path,output=>Serializer().WriteObject(output,receipt));}
        }
        public IReadOnlyList<ModReceipt> Load(Guid profile)
        {
            var dir=ProfileRoot(profile);if(!Directory.Exists(dir))return new ModReceipt[0];ModPackageValidator.NoReparseAncestors(dir);
            var paths=Directory.GetFiles(dir,"*.json");if(paths.Length>10000)throw new InvalidDataException("Too many mod journals.");
            return paths.Select(path=>Read(path,profile)).OrderBy(r=>r.UpdatedUtc).ToList().AsReadOnly();
        }
        public ModReceipt LoadTransaction(Guid profile,Guid transaction){return Read(JournalPath(profile,transaction),profile);}
        private ModReceipt Read(string path,Guid profile)
        {
            try {
                ModPackageValidator.NoReparseAncestors(path);if(new FileInfo(path).Length>16777216)throw ModJson.Bad();
                using(var input=File.OpenRead(path)){
                    var result=(ModReceipt)Serializer().ReadObject(input);Validate(result);
                    if(result.ProfileId!=profile||Path.GetFileName(path)!=Id(result.TransactionId)+".json")throw ModJson.Bad();return result;
                }
            }catch(Exception ex) when(ex is SerializationException||ex is FormatException||ex is ArgumentException){throw new InvalidDataException("Invalid mod journal; recovery requires inspection.",ex);}
        }
        private static DataContractJsonSerializer Serializer(){return new DataContractJsonSerializer(typeof(ModReceipt),new DataContractJsonSerializerSettings {MaxItemsInObjectGraph=500000});}
        private string ProfileRoot(Guid id){return Path.Combine(root,"profiles",Id(id));}
        private static string Id(Guid id){if(id==Guid.Empty)throw ModJson.Bad();return id.ToString("N");}
        private static void Hash(string hash){if(hash==null||!Regex.IsMatch(hash,"^[0-9a-f]{64}$"))throw ModJson.Bad();}
        private static void Validate(ModReceipt receipt)
        {
            if(receipt==null||receipt.SchemaVersion!=1||!Enum.IsDefined(typeof(ModInstallState),receipt.State)||receipt.Files==null||receipt.Files.Count>10000)throw ModJson.Bad();
            Id(receipt.TransactionId);Id(receipt.ProfileId);Id(receipt.ModId);Id(receipt.ReleaseId);
            if(ModCompatibility.NormalizeTitleId(receipt.TitleId)!=receipt.TitleId||ModCompatibility.NormalizeVersion(receipt.GameVersion)!=receipt.GameVersion)throw ModJson.Bad();
            var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var policy=new ZonePolicy();
            for(int i=0;i<receipt.Files.Count;i++) {
                var f=receipt.Files[i];if(f==null)throw ModJson.Bad();
                if(f.Target==null||!f.Target.StartsWith("/",StringComparison.Ordinal))throw ModJson.Bad();ModManifest.ValidatePath(f.Target.Substring(1));
                if(policy.Classify(f.Target)==Zone.Locked||!targets.Add(FileManager.Core.Paths.RemotePath.Canonical(f.Target))||!Enum.IsDefined(typeof(ModFileState),f.State))throw ModJson.Bad();
                if(f.DesiredHash!=null)Hash(f.DesiredHash);if(f.PriorHash!=null)Hash(f.PriorHash);if(f.OriginalHash!=null)Hash(f.OriginalHash);
                if(f.DesiredSize<0||f.PriorSize<0||f.OriginalSize<0)throw ModJson.Bad();
                if(f.PriorExists!=(f.PriorHash!=null)||f.OriginalExists!=(f.OriginalHash!=null))throw ModJson.Bad();
                if(f.StagePath!=null&&f.StagePath!=RemoteName(receipt,i,"stage"))throw ModJson.Bad();
                if(f.RemoteOriginalPath!=null&&f.RemoteOriginalPath!=RemoteName(receipt,i,"original"))throw ModJson.Bad();
                if(f.Source!=null){ModManifest.ValidatePath(f.Source);if(!f.Source.StartsWith("payload/",StringComparison.Ordinal))throw ModJson.Bad();}
            }
            ModManifest.NoPrefixes(targets);
        }
        internal static string RemoteName(ModReceipt receipt,int index,string suffix)
        {return FileManager.Core.Paths.RemotePath.Parent(receipt.Files[index].Target).TrimEnd('/')+"/.ps4m-"+receipt.TransactionId.ToString("N")+"-"+index+"."+suffix;}
        private static void Atomic(string path,Action<FileStream> write)
        {
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){write(output);output.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
