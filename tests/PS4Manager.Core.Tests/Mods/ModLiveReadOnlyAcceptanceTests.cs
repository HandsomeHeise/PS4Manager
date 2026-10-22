using PS4Manager.UI;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Session;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    [Collection(UiControlCollection.Name)]
    public sealed class ModLiveReadOnlyAcceptanceTests
    {
        [Fact] public async Task Published_Luma_download_and_cover_validate_without_any_console_connection()
        {
            if(Environment.GetEnvironmentVariable("PS4_MOD_LIVE_READONLY")!="1")return;
            var root=Path.Combine(Path.GetTempPath(),"mod-live-"+Guid.NewGuid().ToString("N"));var previous=AppTheme.Current;
            try{
                using(var catalog=new ModCatalogClient(new Uri(ModSettings.DefaultOrigin),Path.Combine(root,"cache"))){
                    var page=await catalog.CatalogAsync("CUSA00411","1.57",1,CancellationToken.None);var item=page.Items.Single(x=>x.ModId==Guid.Parse("f8615e4a-cb7e-4cf7-8326-5464475f080e"));
                    var release=await catalog.ReleaseAsync(item.ReleaseId,CancellationToken.None);var detail=await catalog.DetailAsync(item.ModId,CancellationToken.None);
                    var zip=Path.Combine(root,"package.zip");await catalog.DownloadAsync(release,zip,null,CancellationToken.None);
                    using(var input=File.OpenRead(zip))new ModSignatureVerifier(ModTrustKeys.Production).Verify(release,input);
                    var package=new ModPackageValidator().ValidateAndExtract(zip,Path.Combine(root,"validated"),release,CancellationToken.None);
                    Assert.Equal("/data/GoldHEN/plugins/Luma.prx",Assert.Single(package.Files).Target);
                    var cover=await catalog.MediaAsync(item.CoverUrl,CancellationToken.None);release.CoverUrl=null;
                    AppTheme.Current=AppTheme.Dark;
                    using(var form=new ModLibraryForm(new ManagerSessionController(new UnusedFactory()),new ManagerSettings(),()=>{},()=>{},()=>{},()=>false)){
                        form.Show();typeof(ModLibraryForm).GetField("profile",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,new ModConsoleProfile(Guid.NewGuid(),"Read-only catalog preview"));
                        var game=new ModGame{Name="Grand Theft Auto V",TitleId="CUSA00411",Version="1.57"};
                        var state=new ModLibraryState{Games=new[]{game},Game=game,Catalog=new ModCatalogPage{Items=new[]{release},Page=1,PageSize=24},Release=release,Detail=detail,Status="Read-only acceptance: signature, SHA-256, archive manifest and cover verified. No console connected or modified."};
                        typeof(ModLibraryForm).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{state});
                        Find(form).OfType<PictureBox>().Single().Image=ModLibraryForm.DecodeCover(cover);form.PerformLayout();
                        var folder=Environment.GetEnvironmentVariable("PS4_MOD_UI_CAPTURE_DIR");if(!string.IsNullOrEmpty(folder)){Directory.CreateDirectory(folder);using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"live-luma-dark.png"));}}
                    }
                }
            }finally{AppTheme.Current=previous;if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        private static System.Collections.Generic.IEnumerable<Control> Find(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Find(c))yield return n;}}
        private sealed class UnusedFactory:IManagerSessionFactory{public ManagerSession Create(ManagerEndpoint endpoint,bool expertMode){throw new InvalidOperationException("No hardware in acceptance test.");}}
    }
}
