using PS4Manager.UI;
using System;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Session;
using PS4Manager.Core.Mods;
using Xunit;

namespace PS4Manager.Core.Tests.Mods
{
    [Collection(UiControlCollection.Name)]
    public sealed class ModLibraryFormTests
    {
        [Theory][InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
        public void Activation_dialog_shows_receipt_title_and_defaults_to_later(float scale)
        {
            var receipt=new ModReceipt{Title="Luma",TitleId="CUSA00411",State=ModInstallState.Installed};
            receipt.Files.Add(new ModReceiptFile{Source="payload/Luma.prx",Target="/data/GoldHEN/plugins/Luma.prx"});
            using(var dialog=new ModPluginActivationDialog(receipt,new[]{new ModGame{TitleId="CUSA03041",Name="Red Dead Redemption 2"},new ModGame{TitleId="CUSA00411",Name="Grand Theft Auto V"}})){
                dialog.Show();dialog.Scale(new SizeF(scale,scale));dialog.PerformLayout();Capture(dialog,"activation-"+scale);
                var message=Assert.Single(Controls(dialog).OfType<UiTextBox>()).Text;
                Assert.Contains("Grand Theft Auto V",message);Assert.Contains("CUSA00411",message);Assert.DoesNotContain("Red Dead Redemption 2",message);
                Assert.Contains("/data/GoldHEN/plugins/Luma.prx",message);
                Assert.Equal("Later",dialog.ActiveControl.Text);
                typeof(ModPluginActivationDialog).GetMethod("ProcessDialogKey",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,new object[]{Keys.Enter});
                Assert.Equal(DialogResult.Cancel,dialog.DialogResult);
            }
        }
        [Theory][InlineData(false)][InlineData(true)]
        public void Profile_reset_or_disconnect_clears_old_game_selection_before_paint(bool disconnect)
        {
            using(var form=new ModLibraryForm(new ManagerSessionController(new UnusedFactory()),new ManagerSettings(),()=>{},()=>{},()=>{},()=>false)){
                var first=new ModGame{TitleId="CUSA00411",Name="GTA V",Version="1.57"};
                var last=new ModGame{TitleId="CUSA03041",Name="Red Dead Redemption 2",Version="1.32"};
                var render=typeof(ModLibraryForm).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance);
                render.Invoke(form,new object[]{new ModLibraryState{Connected=false,Games=new[]{first,last},Game=last}});
                var combo=(UiComboBox)typeof(ModLibraryForm).GetField("games",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                Assert.Same(last,combo.SelectedItem);
                render.Invoke(form,new object[]{disconnect?null:new ModLibraryState{Connected=false}});
                Assert.Null(combo.SelectedItem);
                PaintCombo(combo);
                render.Invoke(form,new object[]{new ModLibraryState{Connected=false,Games=new[]{first},Game=first}});
                Assert.Same(first,combo.SelectedItem);PaintCombo(combo);
            }
        }
        [Fact] public void Empty_recovery_list_clears_old_receipt_selection_before_paint()
        {
            using(var form=new ModLibraryForm(new ManagerSessionController(new UnusedFactory()),new ManagerSettings(),()=>{},()=>{},()=>{},()=>false)){
                var render=typeof(ModLibraryForm).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance);
                render.Invoke(form,new object[]{new ModLibraryState{Connected=false,Receipts=new[]{new ModReceipt{TransactionId=Guid.NewGuid(),Title="Luma"}}}});
                var combo=(UiComboBox)typeof(ModLibraryForm).GetField("receipts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                Assert.NotNull(combo.SelectedItem);
                render.Invoke(form,new object[]{new ModLibraryState{Connected=false}});
                Assert.Null(combo.SelectedItem);PaintCombo(combo);
            }
        }
        private static void PaintCombo(UiComboBox combo)
        {
            using(var bitmap=new Bitmap(300,40))using(var graphics=Graphics.FromImage(bitmap))
                typeof(DevExpress.XtraEditors.BaseControl).GetMethod("OnPaint",BindingFlags.NonPublic|BindingFlags.Instance)
                    .Invoke(combo,new object[]{new PaintEventArgs(graphics,new Rectangle(0,0,300,40))});
        }
        [Fact] public void Shared_transfer_resume_is_disabled_for_interrupted_mod_transactions()
        {
            using(var form=new FileManager.App.FileManagerForm(new ManagerSessionController(new UnusedFactory()),new ManagerSettings())){
                var panel=typeof(FileManager.App.FileManagerForm).GetField("_transfers",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                var grid=(PS4Manager.UI.AppGrid)panel.GetType().GetProperty("Grid").GetValue(panel);
                var button=(UiButton)panel.GetType().GetProperty("ResumeJob").GetValue(panel);
                var ordinary=new FileManager.Core.Transfers.TransferJob(FileManager.Core.Transfers.TransferKind.Upload,"Upload",new FileManager.Core.Transfers.TransferStep[0]);
                var transaction=new FileManager.Core.Transfers.TransferJob("Mod",new UnusedTransaction(),new[]{"/data/mod.txt"},System.Threading.CancellationToken.None);
                foreach(var job in new[]{ordinary,transaction}){
                    typeof(FileManager.Core.Transfers.TransferJob).GetProperty("State").SetValue(job,FileManager.Core.Transfers.JobState.Interrupted);
                    panel.GetType().GetMethod("ShowJobs").Invoke(panel,new object[]{new[]{job}});grid.Selection.Select(0);
                    typeof(FileManager.App.FileManagerForm).GetMethod("UpdateTransferButtons",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
                    Assert.Equal(job==ordinary,button.Enabled);
                }
            }
        }
        private sealed class UnusedTransaction:FileManager.Core.Transfers.IQueuedTransaction
        {public System.Threading.Tasks.Task ExecuteAsync(FileManager.Core.Transfers.TransferTransactionContext context,System.Threading.CancellationToken ct){throw new InvalidOperationException();}}
        [Theory][InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
        public void Disconnected_layout_keeps_install_disabled_and_buttons_readable(float scale)
        {
            using(var form=new ModLibraryForm(new ManagerSessionController(new UnusedFactory()),new ManagerSettings(),()=>{},()=>{},()=>{},()=>false)){
                form.Show();form.Scale(new SizeF(scale,scale));form.PerformLayout();
                var buttons=Controls(form).OfType<UiButton>().ToArray();
                Assert.False(buttons.Single(b=>b.Text=="Review install").Enabled);
                Assert.All(buttons.Where(b=>b.Visible),b=>{Assert.True(b.Width>=70*scale);Assert.True(b.Height>=28*scale);});
                Assert.True(form.MinimumSize.Width>=960);Assert.True(form.MinimumSize.Height>=640);
            }
        }
        private static System.Collections.Generic.IEnumerable<Control> Controls(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Controls(c))yield return n;}}
        [Fact] public void Broken_or_enormous_covers_are_rejected_before_decode()
        {
            Assert.Throws<InvalidDataException>(()=>ModLibraryForm.DecodeCover(new byte[100]));
            var png=new byte[24];png[0]=137;png[1]=80;png[2]=78;png[3]=71;png[16]=127;png[20]=127;
            Assert.Throws<InvalidDataException>(()=>ModLibraryForm.DecodeCover(png));
            using(var image=new Bitmap(1,8192))using(var bytes=new MemoryStream()){
                image.Save(bytes,System.Drawing.Imaging.ImageFormat.Png);
                using(var preview=ModLibraryForm.DecodeCover(bytes.ToArray()))Assert.InRange(preview.Height,1,240);
            }
        }
        [Theory][InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
        public void Long_content_and_review_dialog_can_be_rendered(float scale)
        {
            using(var form=new ModLibraryForm(new ManagerSessionController(new UnusedFactory()),new ManagerSettings(),()=>{},()=>{},()=>{},()=>false)){
                form.Show();typeof(ModLibraryForm).GetField("profile",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,new ModConsoleProfile(Guid.NewGuid(),"Living room PS4"));
                var game=new ModGame{TitleId="CUSA00411",Version="1.57",Name="Grand Theft Auto V"};
                var release=new ModRelease{Title="Luma",ReleaseVersion="1.0.0",Author="Ghosted",Summary="Lua scripting runtime for GTA V on PS4",PackageType="files",PackageSize=244626,Compatibility=new[]{new ModCompatibility("CUSA00411","1.57")},Changelog="Initial release",State="published"};
                var state=new ModLibraryState{Games=new[]{game},Game=game,Catalog=new ModCatalogPage{Items=new[]{release},Page=1,PageSize=24},Release=release,Detail=new ModDetail{Description="Luma adds a Lua scripting runtime to GTA V on PS4.\r\n\r\nThis package contains the PRX plugin. Enable it in GoldHEN Manager and restart the game. Script files are installed separately."},Status="Select a console profile before installing. The catalog matches CUSA00411 @ 1.57."};
                typeof(ModLibraryForm).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{state});
                form.Scale(new SizeF(scale,scale));form.PerformLayout();Capture(form,"library-"+scale);
                Assert.Contains(Controls(form).OfType<UiTextBox>(),t=>t.Text.Contains("Compatibility"));
            }
            using(var dialog=new ModInstallReviewDialog("Luma",new[]{"/data/GoldHEN/plugins/Luma.prx"},new[]{"/data/GoldHEN/plugins/Luma.prx"},"C:/Users/Example/AppData/Local/PS4Manager/mods",false,false)){
                dialog.Show();dialog.Scale(new SizeF(scale,scale));dialog.PerformLayout();Capture(dialog,"review-"+scale);
                Assert.False(Controls(dialog).OfType<UiButton>().Single(b=>b.Text=="Install").Enabled);
            }
        }
        private static void Capture(Form form,string name)
        {
            var folder=Environment.GetEnvironmentVariable("PS4_MOD_UI_CAPTURE_DIR");if(string.IsNullOrEmpty(folder))return;
            Directory.CreateDirectory(folder);using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,name+".png"));}
        }
        private sealed class UnusedFactory:IManagerSessionFactory{public ManagerSession Create(ManagerEndpoint endpoint,bool expertMode){throw new InvalidOperationException();}}
    }
}
