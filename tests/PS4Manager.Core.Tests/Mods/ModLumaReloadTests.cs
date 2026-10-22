using System;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    public sealed class ModLumaReloadTests
    {
        [Fact] public async Task Only_completed_scripts_reload_and_errors_never_echo_tokens()
        {
            int calls=0;var reload=new ModLumaReload(ct=>{calls++;throw new Exception("secret-token");},()=>false);
            var release=new ModRelease{PackageType="files"};var receipt=ModReceiptStoreTests.Receipt();receipt.State=ModInstallState.Installed;
            Assert.NotNull(await reload.TryAsync(receipt,release,"CUSA00411",CancellationToken.None));Assert.Equal(0,calls);
            release.PackageType="luma-script";receipt.ReleaseId=release.ReleaseId;receipt.ModId=release.ModId;
            receipt.State=ModInstallState.Incomplete;Assert.NotNull(await reload.TryAsync(receipt,release,"CUSA00411",CancellationToken.None));Assert.Equal(0,calls);
            receipt.State=ModInstallState.Installed;
            var error=await reload.TryAsync(receipt,release,"CUSA00411",CancellationToken.None);
            Assert.Equal(1,calls);Assert.DoesNotContain("secret-token",error);Assert.Equal(ModInstallState.Installed,receipt.State);
            await reload.TryAsync(receipt,release,"CUSA00411",CancellationToken.None);Assert.Equal(2,calls);
        }
    }
}
