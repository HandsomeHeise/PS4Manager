using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Mods
{
    public sealed class ModTransaction:IQueuedTransaction
    {
        private readonly ModInstallPlanner planner;
        private readonly Guid id;
        private readonly string action;
        internal ModTransaction(ModInstallPlanner planner,Guid id,string action){this.planner=planner;this.id=id;this.action=action;}
        public async Task ExecuteAsync(TransferTransactionContext context,CancellationToken ct)
        {
            planner.Confirmed();using(planner.Store.AcquireLease(planner.Profile.Id))
            {
                var r=planner.Store.LoadTransaction(planner.Profile.Id,id);CheckTransition(r);
                try
                {
                    context.Report("Checking signed package and recovery backups");planner.ValidateSaved(r,ct);
                    ModInstallPlanner.Conflicts(r,planner.Store.Load(r.ProfileId));
                    if(action=="install"){
                        var release=await planner.Fresh(r.ReleaseId,r.TitleId,r.GameVersion,ct).ConfigureAwait(false);
                        if(release.Sha256!=r.PackageHash)throw new InvalidDataException("Release package changed.");
                    }
                    foreach(var f in r.Files)await ModInstallPlanner.CheckParents(context.Ftp,f.Target,ct).ConfigureAwait(false);
                    if(action=="install")await Install(context,r,ct).ConfigureAwait(false);
                    else await Restore(context,r,ct).ConfigureAwait(false);
                }
                catch(Exception ex)
                {
                    r.State=ModInstallState.Incomplete;r.LastError=ex.Message;
                    // If persistence fails, the preceding flushed intent remains the recovery authority.
                    planner.Store.Save(r);throw;
                }
            }
        }
        private void CheckTransition(ModReceipt r)
        {
            if(r.State==ModInstallState.Uninstalled||r.State==ModInstallState.RolledBack||r.State==ModInstallState.Superseded)throw new InvalidOperationException("This receipt is no longer active.");
            if(action=="install"&&(r.State==ModInstallState.Installed||r.RecoveryAction!=null))throw new InvalidOperationException("Use the corresponding recovery action for this transaction.");
            if(action=="uninstall"&&r.State!=ModInstallState.Installed&&r.RecoveryAction!="uninstall")throw new InvalidOperationException("Uninstall requires an installed receipt; unfinished installs need rollback.");
            if(r.RecoveryAction!=null&&r.RecoveryAction!=action)throw new InvalidOperationException("Finish the recorded recovery action first.");
        }
        private async Task Install(TransferTransactionContext context,ModReceipt r,CancellationToken ct)
        {
            var ftp=context.Ftp;
            Save(r,ModInstallState.BackingUp);
            // Verify every backup and current target before the first mutation. On resume an
            // already moved original or installed desired file is also a recognized state.
            foreach(var f in r.Files){
                await Recognize(ftp,r,f,ct).ConfigureAwait(false);
                if(f.Source==null&&f.DesiredHash!=null&&!Matches(await Read(ftp,r,f.Target,ct).ConfigureAwait(false),f.DesiredHash,f.DesiredSize))
                    await RetainedOriginal(ftp,r,f,true,ct).ConfigureAwait(false);
            }
            Save(r,ModInstallState.Staging);context.Report("Staging verified payloads");
            foreach(var f in r.Files){
                ct.ThrowIfCancellationRequested();
                if(f.DesiredHash==null||f.Source==null)continue;
                var target=await Read(ftp,r,f.Target,ct).ConfigureAwait(false);
                if(Matches(target,f.DesiredHash,f.DesiredSize))continue;
                await Stage(ftp,r,f.StagePath,f.DesiredHash,f.DesiredSize,ct).ConfigureAwait(false);
                f.State=ModFileState.Staged;planner.Store.Save(r);
            }
            Save(r,ModInstallState.Replacing);
            for(int i=0;i<r.Files.Count;i++){
                var f=r.Files[i];ct.ThrowIfCancellationRequested();context.Report("Installing "+(i+1)+" / "+r.Files.Count,i,r.Files.Count);
                await Recognize(ftp,r,f,ct).ConfigureAwait(false);
                var target=await Read(ftp,r,f.Target,ct).ConfigureAwait(false);
                if(!Matches(target,f.DesiredHash,f.DesiredSize)){
                    var source=f.DesiredHash==null?null:f.Source==null?await RetainedOriginal(ftp,r,f,true,ct).ConfigureAwait(false):f.StagePath;
                    if(target.Exists){
                        await Verify(ftp,r,f.RemoteOriginalPath,null,0,ct).ConfigureAwait(false);
                        f.State=ModFileState.MoveOriginalIntent;planner.Store.Save(r);
                        await Verify(ftp,r,f.Target,f.PriorHash,f.PriorSize,ct).ConfigureAwait(false);
                        await ftp.RenameAsync(f.Target,f.RemoteOriginalPath,ct).ConfigureAwait(false);
                        f.State=ModFileState.OriginalMoved;planner.Store.Save(r);
                        await Verify(ftp,r,f.RemoteOriginalPath,f.PriorHash,f.PriorSize,ct).ConfigureAwait(false);
                    }
                    if(f.DesiredHash!=null){
                        f.State=ModFileState.InstallIntent;planner.Store.Save(r);
                        await Verify(ftp,r,f.Target,null,0,ct).ConfigureAwait(false);
                        await Verify(ftp,r,source,f.DesiredHash,f.DesiredSize,ct).ConfigureAwait(false);
                        await ftp.RenameAsync(source,f.Target,ct).ConfigureAwait(false);
                    }
                }
                await Verify(ftp,r,f.Target,f.DesiredHash,f.DesiredSize,ct).ConfigureAwait(false);
                f.State=ModFileState.Installed;f.Verification="SIZE + two matching SHA-256 readbacks";planner.Store.Save(r);
            }
            if(r.PredecessorId.HasValue){var prior=planner.Store.LoadTransaction(r.ProfileId,r.PredecessorId.Value);prior.State=ModInstallState.Superseded;planner.Store.Save(prior);}
            r.LastError=null;Save(r,ModInstallState.Installed);context.Report("Installed — verified readback",r.Files.Count,r.Files.Count);
        }
        private async Task Recognize(IFtpClient ftp,ModReceipt r,ModReceiptFile f,CancellationToken ct)
        {
            var target=await Read(ftp,r,f.Target,ct).ConfigureAwait(false);
            var original=await Read(ftp,r,f.RemoteOriginalPath,ct).ConfigureAwait(false);
            if(original.Exists&&!Matches(original,f.PriorHash,f.PriorSize))throw new InvalidDataException("Saved remote original was modified: "+f.RemoteOriginalPath);
            bool before=Matches(target,f.PriorHash,f.PriorSize)&&!original.Exists;
            bool moved=!target.Exists&&(original.Exists||!f.PriorExists);
            bool after=Matches(target,f.DesiredHash,f.DesiredSize)&&(original.Exists||!f.PriorExists||f.PriorHash==f.DesiredHash);
            if(!before&&!moved&&!after)throw new InvalidDataException("Remote target has unknown changes: "+f.Target);
        }
        private async Task Restore(TransferTransactionContext context,ModReceipt r,CancellationToken ct)
        {
            r.RecoveryAction=action;Save(r,action=="rollback"?ModInstallState.RollingBack:ModInstallState.Uninstalling);
            // Preflight all targets before restoring any one of them.
            for(int i=0;i<r.Files.Count;i++){
                await RecognizeRestore(context.Ftp,r,i,ct).ConfigureAwait(false);
                var f=r.Files[i];var hash=action=="rollback"?f.PriorHash:f.OriginalHash;var size=action=="rollback"?f.PriorSize:f.OriginalSize;
                if(hash!=null&&!Matches(await Read(context.Ftp,r,f.Target,ct).ConfigureAwait(false),hash,size))await RetainedOriginal(context.Ftp,r,f,action=="uninstall",ct).ConfigureAwait(false);
            }
            for(int i=0;i<r.Files.Count;i++){
                ct.ThrowIfCancellationRequested();var f=r.Files[i];string hash=action=="rollback"?f.PriorHash:f.OriginalHash;long size=action=="rollback"?f.PriorSize:f.OriginalSize;
                context.Report(action+" "+(i+1)+" / "+r.Files.Count,i,r.Files.Count);
                await RecognizeRestore(context.Ftp,r,i,ct).ConfigureAwait(false);
                var target=await Read(context.Ftp,r,f.Target,ct).ConfigureAwait(false);
                if(!Matches(target,hash,size)){
                    var source=hash==null?null:await RetainedOriginal(context.Ftp,r,f,action=="uninstall",ct).ConfigureAwait(false);
                    var discard=ModReceiptStore.RemoteName(r,i,"retained");
                    // An update that removed this target temporarily put the pre-mod
                    // original back. Rollback must return it to its ancestral slot so
                    // uninstalling the reinstated predecessor still has its provenance.
                    if(action=="rollback"&&f.Source==null&&f.OriginalHash!=null&&Matches(target,f.OriginalHash,f.OriginalSize))
                        discard=OriginalSlot(r,f);
                    f.State=ModFileState.RestoreIntent;planner.Store.Save(r);
                    if(target.Exists){
                        await Verify(context.Ftp,r,discard,null,0,ct).ConfigureAwait(false);
                        await Verify(context.Ftp,r,f.Target,target.Hash,target.Size,ct).ConfigureAwait(false);
                        await context.Ftp.RenameAsync(f.Target,discard,ct).ConfigureAwait(false);
                        await Verify(context.Ftp,r,discard,target.Hash,target.Size,ct).ConfigureAwait(false);
                    }
                    if(hash!=null){
                        await Verify(context.Ftp,r,f.Target,null,0,ct).ConfigureAwait(false);
                        await Verify(context.Ftp,r,source,hash,size,ct).ConfigureAwait(false);
                        await context.Ftp.RenameAsync(source,f.Target,ct).ConfigureAwait(false);
                    }
                }
                await Verify(context.Ftp,r,f.Target,hash,size,ct).ConfigureAwait(false);f.State=ModFileState.Restored;planner.Store.Save(r);
            }
            if(action=="rollback"&&r.PredecessorId.HasValue){var prior=planner.Store.LoadTransaction(r.ProfileId,r.PredecessorId.Value);prior.State=ModInstallState.Installed;planner.Store.Save(prior);}
            r.LastError=null;Save(r,action=="rollback"?ModInstallState.RolledBack:ModInstallState.Uninstalled);context.Report("Recovery complete",r.Files.Count,r.Files.Count);
        }
        private string OriginalSlot(ModReceipt receipt,ModReceiptFile file)
        {
            string slot=null;var current=receipt;int depth=0;
            while(current!=null){
                var candidate=current.Files.SingleOrDefault(f=>ModInstallPlanner.Same(f.Target,file.Target));
                if(candidate!=null&&candidate.PriorHash==file.OriginalHash&&candidate.PriorSize==file.OriginalSize)slot=candidate.RemoteOriginalPath;
                if(++depth>101)throw ModJson.Bad();current=current.PredecessorId.HasValue?planner.Store.LoadTransaction(receipt.ProfileId,current.PredecessorId.Value):null;
            }
            if(slot==null)throw new InvalidDataException("Original console storage slot is unavailable.");return slot;
        }
        private async Task<string> RetainedOriginal(IFtpClient ftp,ModReceipt receipt,ModReceiptFile file,bool preMod,CancellationToken ct)
        {
            var hash=preMod?file.OriginalHash:file.PriorHash;var size=preMod?file.OriginalSize:file.PriorSize;
            var history=new List<ModReceipt>{receipt};
            if(preMod){var current=receipt;while(current.PredecessorId.HasValue){current=planner.Store.LoadTransaction(receipt.ProfileId,current.PredecessorId.Value);history.Add(current);if(history.Count>101)throw ModJson.Bad();}history.Reverse();}
            foreach(var record in history){
                var candidate=record.Files.SingleOrDefault(f=>ModInstallPlanner.Same(f.Target,file.Target));
                if(candidate==null||candidate.PriorHash!=hash||candidate.PriorSize!=size)continue;
                await ModInstallPlanner.CheckParents(ftp,candidate.RemoteOriginalPath,ct).ConfigureAwait(false);
                var actual=await Read(ftp,receipt,candidate.RemoteOriginalPath,ct).ConfigureAwait(false);
                if(!actual.Exists)continue;
                if(!Matches(actual,hash,size))throw new InvalidDataException("Retained console original was changed: "+candidate.RemoteOriginalPath);
                return candidate.RemoteOriginalPath;
            }
            // A repeatably wrong RETR can produce a self-consistent but false local backup.
            // Never substitute that blob for a missing console original automatically.
            throw new InvalidDataException("Retained console original is unavailable; automatic restoration stopped. Inspect recovery files manually.");
        }
        private async Task RecognizeRestore(IFtpClient ftp,ModReceipt r,int index,CancellationToken ct)
        {
            var f=r.Files[index];var target=await Read(ftp,r,f.Target,ct).ConfigureAwait(false);
            var retained=await Read(ftp,r,ModReceiptStore.RemoteName(r,index,"retained"),ct).ConfigureAwait(false);
            var original=await Read(ftp,r,f.RemoteOriginalPath,ct).ConfigureAwait(false);
            if(original.Exists&&!Matches(original,f.PriorHash,f.PriorSize))throw new InvalidDataException("Remote original changed.");
            if(retained.Exists&&!Matches(retained,f.DesiredHash,f.DesiredSize)&&!Matches(retained,f.PriorHash,f.PriorSize))throw new InvalidDataException("Retained transaction file changed.");
            var restoreHash=action=="rollback"?f.PriorHash:f.OriginalHash;var restoreSize=action=="rollback"?f.PriorSize:f.OriginalSize;
            if(Matches(target,restoreHash,restoreSize)||Matches(target,f.DesiredHash,f.DesiredSize)||Matches(target,f.PriorHash,f.PriorSize))return;
            if(!target.Exists&&(original.Exists||retained.Exists||!f.PriorExists))return;
            throw new InvalidDataException("User-modified target blocks recovery: "+f.Target);
        }
        private async Task Stage(IFtpClient ftp,ModReceipt r,string path,string hash,long size,CancellationToken ct)
        {
            var staged=await Read(ftp,r,path,ct).ConfigureAwait(false);
            if(staged.Exists){if(!Matches(staged,hash,size))throw new InvalidDataException("Staging file is incomplete or modified; retained for inspection: "+path);return;}
            var local=planner.Store.VerifiedBackupPath(r.ProfileId,hash);
            if(new FileInfo(local).Length!=size)throw new InvalidDataException("Backup size mismatch.");
            await ftp.UploadFromFileAsync(local,path,null,ct).ConfigureAwait(false);
            await Verify(ftp,r,path,hash,size,ct).ConfigureAwait(false);
        }
        private Task<ModRemoteSnapshot> Read(IFtpClient ftp,ModReceipt r,string path,CancellationToken ct){return ModRemoteSnapshot.ReadAsync(ftp,path,planner.Store,r.ProfileId,ct);}
        private Task Verify(IFtpClient ftp,ModReceipt r,string path,string hash,long size,CancellationToken ct){return ModRemoteSnapshot.VerifyAsync(ftp,path,hash,size,planner.Store,r.ProfileId,ct);}
        private static bool Matches(ModRemoteSnapshot actual,string hash,long size){return actual.Exists==(hash!=null)&&(!actual.Exists||(actual.Hash==hash&&actual.Size==size));}
        private void Save(ModReceipt r,ModInstallState state){r.State=state;planner.Store.Save(r);}
    }
}
