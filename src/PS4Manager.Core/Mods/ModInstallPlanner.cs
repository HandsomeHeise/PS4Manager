using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Mods
{
    public sealed class ModInstallPlan
    {
        internal ModInstallPlanner Owner {get;set;}
        public Guid TransactionId {get;internal set;}
        public string Title {get;internal set;}
        public IReadOnlyList<string> Targets {get;internal set;}
        public IReadOnlyList<string> Replacements {get;internal set;}
        public bool RequiresZoneApproval {get;internal set;}
    }
    public sealed class ModInstallPlanner
    {
        internal readonly ModReceiptStore Store;
        internal readonly ModSignatureVerifier Verifier;
        internal readonly ModConsoleProfile Profile;
        private readonly object session;
        private readonly ModGameDiscovery discovery;
        private readonly IFtpClient ftp;
        private readonly FtpGates gates;
        private readonly Func<Guid,CancellationToken,Task<ModRelease>> fetch;
        private readonly Func<ModRelease,string,CancellationToken,Task> download;
        public ModInstallPlanner(ModReceiptStore store,ModSignatureVerifier verifier,ModGameDiscovery discovery,IFtpClient ftp,FtpGates gates,ModConsoleProfile profile,object session,ModCatalogClient catalog)
            :this(store,verifier,discovery,ftp,gates,profile,session,catalog.ReleaseAsync,(r,p,ct)=>catalog.DownloadAsync(r,p,null,ct)){}
        internal ModInstallPlanner(ModReceiptStore store,ModSignatureVerifier verifier,ModGameDiscovery discovery,IFtpClient ftp,FtpGates gates,ModConsoleProfile profile,object session,
            Func<Guid,CancellationToken,Task<ModRelease>> fetch,Func<ModRelease,string,CancellationToken,Task> download)
        {Store=store;Verifier=verifier;this.discovery=discovery;this.ftp=ftp;this.gates=gates;Profile=profile;this.session=session;this.fetch=fetch;this.download=download;}
        internal void Confirmed(){if(!Profile.IsConfirmedForSession(session))throw new InvalidOperationException("Confirm this console profile for the current connection first.");}
        public async Task<ModInstallPlan> PrepareAsync(ModRelease selected,ModGame game,CancellationToken ct)
        {
            Confirmed();using(Store.AcquireLease(Profile.Id))
            {
                var release=await Fresh(selected.ReleaseId,game.TitleId,game.Version,ct).ConfigureAwait(false);
                if(ModSignatureVerifier.Envelope(selected)!=ModSignatureVerifier.Envelope(release))throw new InvalidDataException("Release changed; review it again.");
                var id=Guid.NewGuid();var dir=Store.StagingPath(Profile.Id,id);ModPackageValidator.NoReparseAncestors(dir);Directory.CreateDirectory(dir);
                var zip=Path.Combine(dir,"package.zip");await download(release,zip,ct).ConfigureAwait(false);
                using(var input=File.OpenRead(zip))Verifier.Verify(release,input);
                var package=new ModPackageValidator().ValidateAndExtract(zip,Path.Combine(dir,"validated"),release,ct);
                ValidateProfile(package,game.TitleId);
                var receipt=new ModReceipt{TransactionId=id,ProfileId=Profile.Id,ModId=release.ModId,ReleaseId=release.ReleaseId,TitleId=game.TitleId,GameVersion=game.Version,
                    Title=release.Title,ReleaseVersion=release.ReleaseVersion,ReleaseJson=release.RawJson,PackageHash=Store.SaveBackupFile(Profile.Id,zip),State=ModInstallState.Prepared};
                var all=Store.Load(Profile.Id);var predecessors=all.Where(r=>r.ModId==release.ModId&&r.State==ModInstallState.Installed).ToArray();
                if(predecessors.Length>1)throw new InvalidDataException("Multiple installed receipts need manual inspection.");
                var prior=predecessors.SingleOrDefault();if(prior!=null){ValidateSaved(prior,ct);receipt.PredecessorId=prior.TransactionId;}
                foreach(var file in package.Files)receipt.Files.Add(new ModReceiptFile{Source=file.Source,Target=file.Target,DesiredHash=file.Sha256,DesiredSize=file.Size});
                if(prior!=null)foreach(var old in prior.Files.Where(f=>!receipt.Files.Any(n=>Same(n.Target,f.Target))))
                    receipt.Files.Add(new ModReceiptFile{Target=old.Target,DesiredHash=old.OriginalHash,DesiredSize=old.OriginalSize});
                Conflicts(receipt,all);
                await FtpGates.RunAsync(gates.Transfer,async ()=>{
                    for(int i=0;i<receipt.Files.Count;i++){
                        var f=receipt.Files[i];await CheckParents(ftp,f.Target,ct).ConfigureAwait(false);
                        var snapshot=await ModRemoteSnapshot.ReadAsync(ftp,f.Target,Store,Profile.Id,ct).ConfigureAwait(false);
                        f.PriorExists=snapshot.Exists;f.PriorHash=snapshot.Hash;f.PriorSize=snapshot.Size;
                        var old=prior?.Files.SingleOrDefault(p=>Same(p.Target,f.Target));
                        if(old!=null&&(snapshot.Hash!=old.DesiredHash||snapshot.Size!=old.DesiredSize))throw new InvalidDataException("Installed target was changed outside Mod Library: "+f.Target);
                        f.OriginalExists=old?.OriginalExists??snapshot.Exists;f.OriginalHash=old==null?snapshot.Hash:old.OriginalHash;f.OriginalSize=old?.OriginalSize??snapshot.Size;
                        f.StagePath=ModReceiptStore.RemoteName(receipt,i,"stage");f.RemoteOriginalPath=ModReceiptStore.RemoteName(receipt,i,"original");
                    }return 0;
                },ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();Store.Save(receipt);
                return new ModInstallPlan{Owner=this,TransactionId=id,Title=receipt.Title,Targets=receipt.Files.Select(f=>f.Target).ToList().AsReadOnly(),
                    Replacements=receipt.Files.Where(f=>f.PriorExists).Select(f=>f.Target).ToList().AsReadOnly(),RequiresZoneApproval=receipt.Files.Any(f=>new ZonePolicy().Classify(f.Target)==Zone.Warn)};
            }
        }
        public IQueuedTransaction CreateInstall(ModInstallPlan plan,bool gameClosed,bool approveZones)
        {if(plan==null||plan.Owner!=this)throw new InvalidOperationException("Plan belongs to another console session.");return Create(plan.TransactionId,"install",gameClosed,approveZones);}
        public void Abandon(ModInstallPlan plan)
        {
            if(plan==null||plan.Owner!=this)throw new InvalidOperationException("Foreign plan.");
            using(Store.AcquireLease(Profile.Id)){
                var r=Store.LoadTransaction(Profile.Id,plan.TransactionId);
                if(r.State!=ModInstallState.Prepared)throw new InvalidOperationException("Started transactions require recovery.");
                r.State=ModInstallState.RolledBack;Store.Save(r);
            }
        }
        internal IQueuedTransaction Create(Guid transaction,string action,bool gameClosed,bool approveZones)
        {
            Confirmed();var receipt=Store.LoadTransaction(Profile.Id,transaction);
            if(!gameClosed)throw new InvalidOperationException("Close the target game and confirm before changing mod files.");
            if(!approveZones&&receipt.Files.Any(f=>new ZonePolicy().Classify(f.Target)==Zone.Warn))throw new InvalidOperationException("Warning-zone approval is required.");
            return new ModTransaction(this,transaction,action);
        }
        internal async Task<ModRelease> Fresh(Guid id,string title,string version,CancellationToken ct)
        {
            var release=await fetch(id,ct).ConfigureAwait(false);Verifier.VerifyMetadata(release);
            if(release.ReleaseId!=id||release.State!="published")throw new InvalidDataException("Release is no longer published. Use rollback instead.");
            var current=await discovery.RefreshAsync(title,ct).ConfigureAwait(false);
            if(current.Version==null||current.Version!=version||!release.Compatibility.Any(c=>c.TitleId==title&&c.GameVersion==version))throw new InvalidDataException("Console game version changed or is incompatible.");
            if(current.RunningState==ModRunningState.Running&&release.PackageType=="files")throw new InvalidOperationException("Close the target game first.");
            return release;
        }
        internal void ValidateSaved(ModReceipt receipt,CancellationToken ct,int depth=0)
        {
            if(depth>100)throw new InvalidDataException("Receipt history is too deep.");
            var release=ModJson.ReadRelease(receipt.ReleaseJson);Verifier.VerifyMetadata(release);
            if(receipt.ModId!=release.ModId||receipt.ReleaseId!=release.ReleaseId||receipt.PackageHash!=release.Sha256||!release.Compatibility.Any(c=>c.TitleId==receipt.TitleId&&c.GameVersion==receipt.GameVersion))throw ModJson.Bad();
            var zip=Store.VerifiedBackupPath(Profile.Id,receipt.PackageHash);using(var input=File.OpenRead(zip))Verifier.Verify(release,input);
            // No longer than the preparation directory 'validated': accepted paths
            // must remain valid during recovery under net48's Windows path limit.
            var root=Path.Combine(Store.StagingPath(Profile.Id,receipt.TransactionId),"v"+Guid.NewGuid().ToString("N").Substring(0,7));
            var package=new ModPackageValidator().ValidateAndExtract(zip,root,release,ct);
            try{
            ValidateProfile(package,receipt.TitleId);
            ModReceipt prior=null;if(receipt.PredecessorId.HasValue){prior=Store.LoadTransaction(Profile.Id,receipt.PredecessorId.Value);if(prior.ModId!=receipt.ModId)throw ModJson.Bad();ValidateSaved(prior,ct,depth+1);}
            foreach(var file in package.Files){var recorded=receipt.Files.SingleOrDefault(f=>f.Target==file.Target);if(recorded==null||recorded.Source!=file.Source||recorded.DesiredHash!=file.Sha256||recorded.DesiredSize!=file.Size)throw ModJson.Bad();}
            foreach(var f in receipt.Files){
                var payload=package.Files.SingleOrDefault(p=>p.Target==f.Target);var old=prior?.Files.SingleOrDefault(p=>Same(p.Target,f.Target));
                if(payload==null&&(old==null||f.Source!=null||f.DesiredHash!=old.OriginalHash||f.DesiredSize!=old.OriginalSize))throw ModJson.Bad();
                if(old!=null&&(f.OriginalHash!=old.OriginalHash||f.OriginalSize!=old.OriginalSize||f.PriorHash!=old.DesiredHash||f.PriorSize!=old.DesiredSize))throw ModJson.Bad();
                if(old==null&&(f.OriginalHash!=f.PriorHash||f.OriginalSize!=f.PriorSize))throw ModJson.Bad();
                if(f.PriorHash!=null)Store.VerifiedBackupPath(Profile.Id,f.PriorHash);if(f.OriginalHash!=null)Store.VerifiedBackupPath(Profile.Id,f.OriginalHash);
                if(payload!=null)Store.SaveBackupFile(Profile.Id,payload.LocalPath);
            }
            }finally{ModPackageValidator.DeleteNewStaging(root);}
        }
        internal static bool Same(string a,string b){return string.Equals(RemotePath.Canonical(a),RemotePath.Canonical(b),StringComparison.OrdinalIgnoreCase);}
        internal static void Conflicts(ModReceipt proposed,IReadOnlyList<ModReceipt> all)
        {
            foreach(var r in all){
                if(r.TransactionId==proposed.TransactionId||r.TransactionId==proposed.PredecessorId||r.State==ModInstallState.Uninstalled||r.State==ModInstallState.RolledBack||r.State==ModInstallState.Superseded)continue;
                foreach(var a in proposed.Files)foreach(var b in r.Files){var x=RemotePath.Canonical(a.Target);var y=RemotePath.Canonical(b.Target);
                    if(Same(x,y)||x.StartsWith(y+"/",StringComparison.OrdinalIgnoreCase)||y.StartsWith(x+"/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Target belongs to another installed or unfinished mod: "+a.Target);
                }
            }
        }
        private static void ValidateProfile(ValidatedModPackage package,string title)
        {
            if(package.Manifest.PackageType!="luma-script")return;
            var expected=title=="CUSA00411"?"gta5":title=="CUSA03041"?"rdr2":null;
            if(expected==null||package.Manifest.LumaProfile!=expected)throw new InvalidDataException("Luma script profile does not match this game.");
        }
        internal static async Task CheckParents(IFtpClient ftp,string target,CancellationToken ct)
        {
            var parent=RemotePath.Parent(target);var parts=parent.Trim('/').Split('/');string current="/";
            foreach(var part in parts){if(part.Length==0)continue;var entries=await ModRemoteSnapshot.ListStrictAsync(ftp,current,ct).ConfigureAwait(false);var found=entries.Where(e=>string.Equals(e.Name,part,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(found.Length!=1||found[0].Name!=part||found[0].Kind!=FtpEntryKind.Directory)throw new InvalidDataException("Target parent must already exist as a regular directory: "+parent);
                current=current.TrimEnd('/')+"/"+part;
            }
        }
    }
}
