using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.Tests.Mods.Support
{
    internal sealed class ModMemoryFtp:IFtpClient,IStrictFtpListingClient
    {
        public readonly Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        public readonly HashSet<string> Directories=new HashSet<string>(StringComparer.Ordinal){"/"};
        public readonly List<string> Mutations=new List<string>();
        public Func<string,Exception> FailRead {get;set;}
        public Action<string> BeforeMutation {get;set;}
        public Action AfterRename {get;set;}
        public Func<string,byte[],byte[]> ReadTransform {get;set;}
        public string RawListing {get;set;}
        public Task<IReadOnlyList<FtpEntry>> ListStrictAsync(string path,CancellationToken ct)
        {return RawListing==null?ListAsync(path,ct):Task.FromResult(UnixListingParser.ParseStrict(RawListing));}
        public void Add(string path,byte[] bytes){Parent(path);Files[path]=bytes.ToArray();}
        public void Parent(string path){var parent=path.Substring(0,path.LastIndexOf('/'));if(parent.Length==0)parent="/";if(Directories.Add(parent)&&parent!="/")Parent(parent);}
        private static FtpException Missing(){return new FtpException("Missing",FtpErrorKind.Permanent,550);}
        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();if(!Directories.Contains(path))throw Missing();
            var prefix=path=="/"?"/":path+"/";
            var entries=new List<FtpEntry>();
            entries.AddRange(Directories.Where(p=>p.StartsWith(prefix,StringComparison.Ordinal)&&p!=path&&!p.Substring(prefix.Length).Contains('/')).Select(p=>new FtpEntry(p.Substring(prefix.Length),true,0)));
            entries.AddRange(Files.Where(p=>p.Key.StartsWith(prefix,StringComparison.Ordinal)&&!p.Key.Substring(prefix.Length).Contains('/')).Select(p=>new FtpEntry(p.Key.Substring(prefix.Length),false,p.Value.Length)));
            return Task.FromResult<IReadOnlyList<FtpEntry>>(entries);
        }
        public Task<byte[]> DownloadAsync(string path,CancellationToken ct){ct.ThrowIfCancellationRequested();var ex=FailRead?.Invoke(path);if(ex!=null)throw ex;byte[] data;if(!Files.TryGetValue(path,out data))throw Missing();return Task.FromResult(ReadTransform==null?data.ToArray():ReadTransform(path,data.ToArray()));}
        public async Task DownloadToFileAsync(string path,string local,IProgress<long> progress,CancellationToken ct){var bytes=await DownloadAsync(path,ct);File.WriteAllBytes(local,bytes);progress?.Report(bytes.Length);}
        public Task DownloadToFileAsync(string path,string local,long offset,IProgress<long> progress,CancellationToken ct){if(offset!=0)throw new NotSupportedException();return DownloadToFileAsync(path,local,progress,ct);}
        public Stream OpenRead(string path,long offset){var s=new MemoryStream(Files[path],false);s.Position=offset;return s;}
        public Task UploadFromFileAsync(string local,string remote,IProgress<long> progress,CancellationToken ct){ct.ThrowIfCancellationRequested();BeforeMutation?.Invoke(remote);Mutations.Add("upload "+remote);Add(remote,File.ReadAllBytes(local));progress?.Report(Files[remote].Length);return Task.CompletedTask;}
        public Task AppendFromFileAsync(string local,long offset,string remote,IProgress<long> progress,CancellationToken ct){throw new NotSupportedException();}
        public Task<long> GetFileSizeAsync(string path,CancellationToken ct){ct.ThrowIfCancellationRequested();if(!Files.ContainsKey(path))throw Missing();return Task.FromResult((long)Files[path].Length);}
        public Task MakeDirectoryAsync(string path,CancellationToken ct){ct.ThrowIfCancellationRequested();BeforeMutation?.Invoke(path);Mutations.Add("mkdir "+path);Parent(path);Directories.Add(path);return Task.CompletedTask;}
        public Task RemoveDirectoryAsync(string path,CancellationToken ct){if(Files.Keys.Any(p=>p.StartsWith(path+"/")))throw new IOException("Not empty");Directories.Remove(path);return Task.CompletedTask;}
        public Task RenameAsync(string from,string to,CancellationToken ct){ct.ThrowIfCancellationRequested();BeforeMutation?.Invoke(to);if(!Files.ContainsKey(from))throw Missing();if(Files.ContainsKey(to))throw new IOException("Destination exists");Mutations.Add("rename "+from+" -> "+to);Files[to]=Files[from];Files.Remove(from);AfterRename?.Invoke();return Task.CompletedTask;}
        public Task DeleteAsync(string path,CancellationToken ct){ct.ThrowIfCancellationRequested();BeforeMutation?.Invoke(path);Mutations.Add("delete "+path);if(!Files.Remove(path))throw Missing();return Task.CompletedTask;}
    }
}
