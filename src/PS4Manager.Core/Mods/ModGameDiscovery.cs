using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using Ps4.Core.Ftp;
using Ps4.Core.Titles;

namespace PS4Manager.Core.Mods
{
    public sealed class ModGameDiscovery
    {
        private readonly IFtpClient ftp;private readonly FtpGates gates;
        private readonly Func<CancellationToken,Task<string>> runningTitle;
        public ModGameDiscovery(IFtpClient ftp,FtpGates gates,Func<CancellationToken,Task<string>> runningTitle=null)
        {this.ftp=ftp;this.gates=gates;this.runningTitle=runningTitle;}
        public async Task<IReadOnlyList<ModGame>> DiscoverAsync(CancellationToken ct)
        {
            return (await DiscoverReportAsync(ct).ConfigureAwait(false)).Games;
        }
        public async Task<ModGameDiscoveryReport> DiscoverReportAsync(CancellationToken ct)
        {
            bool complete=true;
            var ids=await InstalledIds(ct,()=>complete=false).ConfigureAwait(false);string running=await RunningTitle(ct).ConfigureAwait(false);
            var games=new List<ModGame>();foreach(var id in ids.Keys.OrderBy(x=>x,StringComparer.Ordinal))games.Add(await ReadGame(id,ids[id],running,ct).ConfigureAwait(false));
            return new ModGameDiscoveryReport(games,complete);
        }
        public async Task<ModGame> RefreshAsync(string titleId,CancellationToken ct)
        {
            titleId=ModCompatibility.NormalizeTitleId(titleId);
            var ids=await InstalledIds(ct).ConfigureAwait(false);bool external;
            if(!ids.TryGetValue(titleId,out external))return Invalid(titleId,"Title is no longer installed or its directory cannot be read.");
            return await ReadGame(titleId,external,await RunningTitle(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
        }
        private async Task<Dictionary<string,bool>> InstalledIds(CancellationToken ct,Action incomplete=null)
        {
            var result=new Dictionary<string,bool>(StringComparer.Ordinal);
            foreach(var root in new[]{"/user/app","/mnt/ext0/user/app"}) {
                IReadOnlyList<FtpEntry> entries;
                try{entries=await FtpGates.RunAsync(gates.Browse,()=>ftp.ListAsync(root,ct),ct).ConfigureAwait(false);}
                catch(FtpException ex) when(!ex.IsTransient){incomplete?.Invoke();continue;}
                foreach(var entry in entries)if(entry.IsDirectory){try{var id=ModCompatibility.NormalizeTitleId(entry.Name);if(id==entry.Name){bool previous;result.TryGetValue(id,out previous);result[id]=previous||root=="/mnt/ext0/user/app";}}catch(FormatException){}}
            }
            return result;
        }
        private async Task<string> RunningTitle(CancellationToken ct)
        {
            if(runningTitle==null)return null;
            try{var id=await runningTitle(ct).ConfigureAwait(false);return id==null?null:ModCompatibility.NormalizeTitleId(id);}
            catch(OperationCanceledException){throw;}
            catch(Exception){return null;}
        }
        private async Task<ModGame> ReadGame(string id,bool external,string running,CancellationToken ct)
        {
            var game=Invalid(id,null);
            try {
                byte[] bytes;
                try{bytes=await FtpGates.RunAsync(gates.Browse,()=>ftp.DownloadAsync(TitlePaths.SfoPath(id),ct),ct).ConfigureAwait(false);}
                catch(FtpException ex) when(ex.StatusCode==550&&external){
                    // External titles retain their installed metadata here while the game
                    // is closed. The normal appmeta alias may exist only while mounted.
                    bytes=await FtpGates.RunAsync(gates.Browse,()=>ftp.DownloadAsync("/system_data/priv/appmeta/external/"+id+"/param.sfo",ct),ct).ConfigureAwait(false);
                }
                ValidateSfo(bytes);
                var title=ModCompatibility.NormalizeTitleId(SfoReader.ReadString(bytes,"TITLE_ID"));
                if(title!=id)return Invalid(id,"Console metadata title ID does not match the installed directory.");
                var name=SfoReader.ReadTitle(bytes);game.Name=string.IsNullOrWhiteSpace(name)?id:name;
                game.Version=ModCompatibility.NormalizeVersion(SfoReader.ReadString(bytes,"APP_VER"));
                game.RunningState=running==null?ModRunningState.Unknown:(running==id?ModRunningState.Running:ModRunningState.NotRunning);
                return game;
            } catch(OperationCanceledException){throw;}
            catch(FtpException ex) when(ex.IsTransient){throw;}
            catch(Exception ex) when(ex is FtpException||ex is InvalidDataException||ex is FormatException){game.InvalidReason="Game version unavailable: "+ex.Message;return game;}
        }
        private static ModGame Invalid(string id,string reason){return new ModGame {TitleId=id,Name=id,InvalidReason=reason,RunningState=ModRunningState.Unknown};}
        private static void ValidateSfo(byte[] bytes)
        {
            if(bytes==null||bytes.Length<20||bytes.Length>1048576||BitConverter.ToUInt32(bytes,0)!=0x46535000)throw new InvalidDataException("Invalid param.sfo.");
            long keys=BitConverter.ToUInt32(bytes,8),data=BitConverter.ToUInt32(bytes,12),count=BitConverter.ToUInt32(bytes,16);
            if(count>4096||20+count*16>keys||keys>data||data>bytes.Length)throw ModJson.Bad();
            var names=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<count;i++) {
                int entry=20+i*16;long start=keys+BitConverter.ToUInt16(bytes,entry);if(start>=data)throw ModJson.Bad();
                int end=(int)start;while(end<data&&bytes[end]!=0)end++;if(end>=data)throw ModJson.Bad();
                var name=Encoding.ASCII.GetString(bytes,(int)start,end-(int)start);if(!names.Add(name))throw ModJson.Bad();
                long length=BitConverter.ToUInt32(bytes,entry+4),maximum=BitConverter.ToUInt32(bytes,entry+8),offset=data+BitConverter.ToUInt32(bytes,entry+12);
                if(length>maximum||offset+maximum>bytes.Length)throw ModJson.Bad();
                if(name=="TITLE_ID"||name=="APP_VER"||name=="TITLE") {
                    if(BitConverter.ToUInt16(bytes,entry+2)!=0x0204||length<1||bytes[offset+length-1]!=0)throw ModJson.Bad();
                    for(long p=offset;p<offset+length-1;p++)if(bytes[p]==0)throw ModJson.Bad();
                    try{new UTF8Encoding(false,true).GetString(bytes,(int)offset,(int)length);}catch(DecoderFallbackException){throw ModJson.Bad();}
                }
            }
        }
    }
}
