using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Tests.Mods.Support;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModRemoteSnapshotTests:IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"snapshot-"+Guid.NewGuid().ToString("N"));
        [Fact] public async Task Lossy_listing_cannot_establish_absence_through_the_zone_guard()
        {
            var ftp=new ModMemoryFtp{RawListing="prw-r--r-- 1 0 0 0 Aug 1 2026 x"};
            var guarded=new FileManager.Core.Zones.ZoneGuardFtpClient(ftp,new FileManager.Core.Zones.ZonePolicy());
            await Assert.ThrowsAsync<InvalidDataException>(()=>ModRemoteSnapshot.ReadAsync(guarded,"/data/x",new ModReceiptStore(root),Guid.NewGuid(),CancellationToken.None));
            Assert.Empty(ftp.Mutations);
        }
        [Fact] public async Task Absence_requires_successful_parent_and_zero_length_is_present()
        {
            var ftp=new ModMemoryFtp();var store=new ModReceiptStore(root);var p=Guid.NewGuid();
            await Assert.ThrowsAsync<FtpException>(()=>ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,p,CancellationToken.None));
            ftp.Directories.Add("/data");Assert.False((await ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,p,CancellationToken.None)).Exists);
            ftp.Add("/data/x",new byte[0]);Assert.True((await ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,p,CancellationToken.None)).Exists);
            ftp.FailRead=path=>new FtpException("denied",FtpErrorKind.Permanent,550);
            await Assert.ThrowsAsync<FtpException>(()=>ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,p,CancellationToken.None));
        }
        [Fact] public async Task Equal_size_different_reads_and_wrong_expected_hash_are_rejected()
        {
            var ftp=new ModMemoryFtp();ftp.Add("/data/x",new byte[]{1});var reads=0;
            ftp.FailRead=path=>{if(++reads==2)ftp.Files[path]=new byte[]{2};return null;};
            var store=new ModReceiptStore(root);var p=Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidDataException>(()=>ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,p,CancellationToken.None));
            ftp.FailRead=null;
            await Assert.ThrowsAsync<InvalidDataException>(()=>ModRemoteSnapshot.VerifyAsync(ftp,"/data/x",new string('0',64),1,store,p,CancellationToken.None));
        }
        [Fact] public async Task Truncated_read_and_directory_are_rejected()
        {
            var ftp=new ModMemoryFtp();ftp.Add("/data/x",new byte[]{1,2});
            ftp.FailRead=path=>{ftp.Files[path]=new byte[]{1};return null;};
            var store=new ModReceiptStore(root);var profile=Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidDataException>(()=>ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,profile,CancellationToken.None));
            ftp.Files.Remove("/data/x");ftp.Directories.Add("/data/x");
            await Assert.ThrowsAsync<InvalidDataException>(()=>ModRemoteSnapshot.ReadAsync(ftp,"/data/x",store,profile,CancellationToken.None));
        }
        public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
