using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using PS4Manager.Core.Mods;

namespace PS4Manager.Core.Tests.Mods.Support
{
    internal sealed class ModScenario:IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"mod-life-"+Guid.NewGuid().ToString("N"));
        public readonly ModMemoryFtp Ftp=ModGameDiscoveryTests.Console();
        public readonly ModConsoleProfile Profile=new ModConsoleProfile(Guid.NewGuid(),"Test");
        public readonly object Session=new object();
        public readonly FtpGates Gates=new FtpGates();
        public readonly ModReceiptStore Store;
        public readonly ModInstallPlanner Planner;
        public readonly TransferQueue Queue;
        public readonly ModSignatureVerifier Verifier;
        public byte[] PackageBytes {get{return (byte[])zip.Clone();}}
        public ModRelease Release;
        private byte[] zip;
        private readonly RSACryptoServiceProvider rsa=new RSACryptoServiceProvider(2048);
        public ModScenario()
        {
            Store=new ModReceiptStore(Root);Profile.ConfirmForSession(Session);Ftp.Add("/data/mod.txt",Encoding.UTF8.GetBytes("original"));
            var xml=rsa.ToXmlString(false);var verifier=new ModSignatureVerifier(new Dictionary<string,string>{{ModSignatureVerifier.KeyId(xml),xml}});Verifier=verifier;
            Planner=new ModInstallPlanner(Store,verifier,new ModGameDiscovery(Ftp,Gates),Ftp,Gates,Profile,Session,
                (id,ct)=>Task.FromResult(Release),(r,path,ct)=>{Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,zip);return Task.CompletedTask;});
            Queue=new TransferQueue(new ZoneGuardFtpClient(Ftp,new ZonePolicy()),Gates,new Resolver(),new Clock());
            Package("one");
        }
        public void Package(string content,string target="/data/mod.txt",bool otherMod=false)
        {
            var manifest=ModManifestTests.Manifest("payload/file.txt",target);
            using(var output=new MemoryStream()){
                using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true)){
                    Put(archive,"manifest.json",manifest);Put(archive,"payload/file.txt",content);
                }zip=output.ToArray();
            }
            var json=ModContractTests.Item.Replace("\"packageSize\":12","\"packageSize\":"+zip.Length)
                .Replace(new string('a',64),Hash(zip)).Replace("aa6b9003-f0e2-4ee9-a8b5-f48cf3da73c2",Guid.NewGuid().ToString())
                .Replace("503d131c6d8d979e",ModSignatureVerifier.KeyId(rsa.ToXmlString(false)));
            if(otherMod)json=json.Replace("f8615e4a-cb7e-4cf7-8326-5464475f080e",Guid.NewGuid().ToString());
            json=json.TrimEnd('}')+",\"state\":\"published\",\"changelog\":\"test\",\"withdrawalReason\":null,\"signedEnvelope\":\"pending\",\"manifest\":"+manifest+"}";
            var release=ModJson.ReadRelease(json);var envelope=ModSignatureVerifier.Envelope(release);
            var signature=Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(envelope),CryptoConfig.MapNameToOID("SHA256")));
            Release=ModJson.ReadRelease(json.Replace("AQID",signature).Replace("pending",envelope.Replace("\n","\\n")));
        }
        public Task<ModInstallPlan> Prepare(){return Planner.PrepareAsync(Release,new ModGame{TitleId="CUSA00411",Version="1.57"},CancellationToken.None);}
        public async Task Run(IQueuedTransaction body)
        {
            var job=new TransferJob("Test",body,new[]{"/data/mod.txt"},CancellationToken.None);Queue.Enqueue(job);await job.Completion;
            if(job.State!=JobState.Completed)throw new IOException(job.InterruptReason);
        }
        public async Task<Guid> Install(){var plan=await Prepare();await Run(Planner.CreateInstall(plan,true,true));return plan.TransactionId;}
        public string Read(){return Encoding.UTF8.GetString(Ftp.Files["/data/mod.txt"]);}
        private static string Hash(byte[] bytes){using(var s=new MemoryStream(bytes))return ModSignatureVerifier.Hash(s);}
        private static void Put(ZipArchive zip,string path,string value){using(var writer=new StreamWriter(zip.CreateEntry(path,CompressionLevel.NoCompression).Open(),new UTF8Encoding(false)))writer.Write(value);}
        public void Dispose(){rsa.Dispose();if(Directory.Exists(Root))Directory.Delete(Root,true);}
        private sealed class Clock:IClock {public DateTime UtcNow {get{return DateTime.UtcNow;}}}
        private sealed class Resolver:IConflictResolver {public Task<ConflictDecision> ResolveAsync(ConflictInfo context,CancellationToken ct){throw new InvalidOperationException();}}
    }
}
