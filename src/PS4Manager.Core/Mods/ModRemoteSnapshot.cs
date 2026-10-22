using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Mods
{
    /// <summary>Two agreeing RETRs establish consistency, not server authenticity.</summary>
    public sealed class ModRemoteSnapshot
    {
        public bool Exists {get;private set;}
        public string Hash {get;private set;}
        public long Size {get;private set;}
        internal static Task<System.Collections.Generic.IReadOnlyList<FtpEntry>> ListStrictAsync(IFtpClient ftp,string path,CancellationToken ct)
        {
            var strict=ftp as IStrictFtpListingClient;
            if(strict==null)throw new InvalidDataException("The FTP client cannot provide a complete safety listing.");
            return strict.ListStrictAsync(path,ct);
        }
        public static async Task<ModRemoteSnapshot> ReadAsync(IFtpClient ftp,string path,ModReceiptStore store,Guid profile,CancellationToken ct)
        {
            var entries=await ListStrictAsync(ftp,RemotePath.Parent(path),ct).ConfigureAwait(false);
            var matches=entries.Where(e=>string.Equals(e.Name,RemotePath.Name(path),StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length==0)return new ModRemoteSnapshot();
            if(matches.Length!=1||matches[0].Name!=RemotePath.Name(path)||matches[0].Kind!=FtpEntryKind.File)throw new InvalidDataException("Ambiguous or non-regular remote target: "+path);
            long size=await ftp.GetFileSizeAsync(path,ct).ConfigureAwait(false);
            if(size<0||size>4294967296L||matches[0].Size!=size)throw new InvalidDataException("Remote size is inconsistent.");
            var directory=Path.Combine(store.Root,"readback");ModPackageValidator.NoReparseAncestors(directory);Directory.CreateDirectory(directory);
            var first=Path.Combine(directory,Guid.NewGuid().ToString("N"));var second=first+".second";
            try
            {
                await ftp.DownloadToFileAsync(path,first,null,ct).ConfigureAwait(false);
                if(new FileInfo(first).Length!=size)throw new InvalidDataException("Truncated remote readback.");
                var hash=ModSignatureVerifier.HashFile(first);
                await ftp.DownloadToFileAsync(path,second,null,ct).ConfigureAwait(false);
                if(new FileInfo(second).Length!=size||ModSignatureVerifier.HashFile(second)!=hash||await ftp.GetFileSizeAsync(path,ct).ConfigureAwait(false)!=size)throw new InvalidDataException("Remote content changed during readback.");
                if(store.SaveBackupFile(profile,first)!=hash)throw new InvalidDataException("Backup changed while saving.");
                return new ModRemoteSnapshot {Exists=true,Size=size,Hash=hash};
            }
            finally{if(File.Exists(first))File.Delete(first);if(File.Exists(second))File.Delete(second);}
        }
        public static async Task VerifyAsync(IFtpClient ftp,string path,string hash,long size,ModReceiptStore store,Guid profile,CancellationToken ct)
        {
            var actual=await ReadAsync(ftp,path,store,profile,ct).ConfigureAwait(false);
            if(actual.Exists!=(hash!=null)||(actual.Exists&&(actual.Hash!=hash||actual.Size!=size)))throw new InvalidDataException("Remote target no longer matches the approved content: "+path);
        }
    }
}
