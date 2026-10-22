using PS4Manager.UI;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    [Collection(UiControlCollection.Name)]
    public sealed class NotificationVisualTests
    {
        [Theory][InlineData(false,1f)][InlineData(true,1f)][InlineData(false,1.5f)][InlineData(true,1.5f)][InlineData(false,2f)][InlineData(true,2f)]
        public void Dialog_and_card_render_in_each_skin_and_scale(bool light,float scale)
        {
            StaTest.Run(()=>{
                var previous=AppTheme.Current;
                try {
                    AppTheme.Current=light?AppTheme.Light:AppTheme.Dark;
                    using(var dialog=new MessageDialog("Mod Library","Enabled in GoldHEN.\r\nStart (or restart) the game to load the plugin.",new[]{"OK"},0,0,null,MessageKind.Success,null))
                    using(var card=new NotificationWindow(new DesktopNotification("Luma installed","The plugin files are installed.\r\nOpen Mod Library to enable the plugin.",MessageKind.Success))) {
                        dialog.Show();card.Show();dialog.Scale(new SizeF(scale,scale));card.Scale(new SizeF(scale,scale));
                        dialog.PerformLayout();card.PerformLayout();
                        using(var bitmap=new Bitmap(Math.Max(dialog.Width,card.Width)+40,dialog.Height+card.Height+60))
                        using(var graphics=Graphics.FromImage(bitmap)) {
                            graphics.Clear(light?Color.White:Color.Black);
                            dialog.DrawToBitmap(bitmap,new Rectangle(20,20,dialog.Width,dialog.Height));
                            card.DrawToBitmap(bitmap,new Rectangle(20,dialog.Height+40,card.Width,card.Height));
                            var folder=Environment.GetEnvironmentVariable("PS4MANAGER_UI_CAPTURE_DIR");
                            if(!string.IsNullOrEmpty(folder)){
                                Directory.CreateDirectory(folder);bitmap.Save(Path.Combine(folder,(light?"light":"dark")+"-"+(int)(scale*100)+".png"),ImageFormat.Png);
                                RenderErrorStack(folder,light,scale);
                            }
                            Assert.True(bitmap.Width>card.ClientSize.Width);
                        }
                    }
                }finally{AppTheme.Current=previous;}
            });
        }
        private static void RenderErrorStack(string folder,bool light,float scale)
        {
            var path="/data/GoldHEN/plugins/"+new string('x',180)+"/Luma.prx";
            var details="Target: "+path+"\r\nWrite failed. The original file was preserved.\r\nInspect the transfer before retrying.";
            using(var dialog=new MessageDialog("Upload details","0 of 25 transferred, 25 failed",new[]{"Open Transfers","Close"},1,1,null,MessageKind.Error,details))
            using(var error=new NotificationWindow(new DesktopNotification("Upload failed",details,MessageKind.Error)))
            using(var warning=new NotificationWindow(new DesktopNotification("Transfer interrupted","Connection lost. Open Details to inspect or resume.",MessageKind.Warning)))
            using(var success=new NotificationWindow(new DesktopNotification("Upload completed","12 of 12 transferred",MessageKind.Success))) {
                var cards=new[]{error,warning,success};
                dialog.Show(); dialog.Scale(new SizeF(scale,scale)); dialog.PerformLayout();
                foreach(var card in cards) { card.Show();card.Scale(new SizeF(scale,scale));card.PerformLayout(); }
                int stackWidth=error.Width+24, stackHeight=error.Height*3+48;
                var positions=NotificationLayout.Arrange(new Rectangle(0,0,stackWidth,stackHeight),96,new[]{error.Size,warning.Size,success.Size});
                Assert.Equal(3,positions.Count);
                using(var bitmap=new Bitmap(dialog.Width+stackWidth+60,Math.Max(dialog.Height,stackHeight)+40))
                using(var graphics=Graphics.FromImage(bitmap)) {
                    graphics.Clear(light?Color.White:Color.Black);
                    dialog.DrawToBitmap(bitmap,new Rectangle(20,20,dialog.Width,dialog.Height));
                    for(int i=0;i<cards.Length;i++) { var bounds=positions[i];bounds.Offset(dialog.Width+40,20);cards[i].DrawToBitmap(bitmap,bounds); }
                    bitmap.Save(Path.Combine(folder,(light?"light":"dark")+"-"+(int)(scale*100)+"-errors-stack.png"),ImageFormat.Png);
                }
            }
        }
    }
}
