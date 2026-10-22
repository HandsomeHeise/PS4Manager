using System;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Luma;

namespace PS4Manager.Core.Mods
{
    public sealed class ModLumaReload
    {
        private readonly Func<CancellationToken,Task> reload;
        private readonly Func<bool> busy;
        public ModLumaReload(LumaEndpoint endpoint,string token,Func<bool> otherClientActive)
            :this(async ct=>{using(var client=new LumaClient()){await client.ConnectAsync(endpoint,ct).ConfigureAwait(false);await client.HelloAsync(token,ct).ConfigureAwait(false);await client.ReloadAsync(ct).ConfigureAwait(false);}},otherClientActive){}
        internal ModLumaReload(Func<CancellationToken,Task> reload,Func<bool> busy){this.reload=reload;this.busy=busy;}
        public async Task<string> TryAsync(ModReceipt receipt,ModRelease release,string selectedTitle,CancellationToken ct)
        {
            if(receipt.State!=ModInstallState.Installed||release.PackageType!="luma-script"||receipt.ReleaseId!=release.ReleaseId||receipt.ModId!=release.ModId||receipt.TitleId!=selectedTitle)return "Reload is available only for a successfully installed script package for this game.";
            if(busy())return "Luma Script Manager is open. Reload there or close it before retrying here.";
            try{await reload(ct).ConfigureAwait(false);return null;}
            catch(OperationCanceledException){return "Script reload cancelled; installed files are unchanged.";}
            catch(Exception){return "Script reload failed. Installed files and backups are unchanged. Check Luma connection settings and retry.";}
        }
    }
}
