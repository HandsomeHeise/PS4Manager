using PS4Manager.UI;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using PS4Manager.Core.Tests.Presentation;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    [Collection(UiControlCollection.Name)]
    public sealed class NotificationWindowTests
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr handle,int index);
        [Fact] public void Card_is_topmost_without_disabling_explicit_focus()
        {
            StaTest.Run(()=>{
                using(var card=new NotificationWindow(new DesktopNotification("Title","Text",MessageKind.Information))) {
                    card.Show(); Application.DoEvents(); Assert.True((GetWindowLong(card.Handle,-20)&0x8)!=0,"ExStyle="+GetWindowLong(card.Handle,-20).ToString("X")+" Managed="+card.TopMost);
                    Assert.Equal(0,GetWindowLong(card.Handle,-20)&0x08000000);
                    card.Activate();
                    var button=MessageDialogTests.Descendants(card).OfType<UiButton>().Single(b=>b.Text=="Details");
                    button.Focus();Assert.True(button.Focused);
                }
            });
        }
        [Fact] public void Showing_card_does_not_change_foreground_and_dismiss_is_explicit()
        {
            StaTest.Run(() => {
                using(var form = new Form()) using(var card = new NotificationWindow(new DesktopNotification("Transfer", "Done", MessageKind.Success))) {
                    form.Show(); form.Activate(); var before=GetForegroundWindow();
                    int dismissed=0; string activationTrace=""; card.Dismissed+=(s,e)=>dismissed++;
                    card.Activated+=(s,e)=>activationTrace=Environment.StackTrace;
                    card.Show(); Application.DoEvents();
                    Assert.True(before==GetForegroundWindow(),"Before="+before+" After="+GetForegroundWindow()+" Host="+form.Handle+" Card="+card.Handle+"\n"+activationTrace); Assert.False(card.ShowInTaskbar);
                    Assert.Equal(FormBorderStyle.None,card.FormBorderStyle);
                    MessageDialogTests.Descendants(card).OfType<UiButton>().Single(b=>b.AccessibleName=="Close notification").PerformClick();
                    Assert.Equal(1,dismissed); Assert.False(card.Visible);
                }
            });
        }
        [Theory][InlineData(false,1f)][InlineData(true,1.5f)][InlineData(false,2f)]
        public void Long_card_keeps_actions_reachable_and_follows_skin(bool light,float scale)
        {
            StaTest.Run(() => {
                var old=AppTheme.Current;
                try {
                    AppTheme.Current=light?AppTheme.Light:AppTheme.Dark;
                    using(var card=new NotificationWindow(new DesktopNotification(new string('x',200),new string('y',4000),MessageKind.Error))) {
                        card.Show(); card.Scale(new SizeF(scale,scale)); card.PerformLayout();
                        Assert.All(MessageDialogTests.Descendants(card).OfType<UiButton>(),b=>Assert.True(card.RectangleToScreen(card.ClientRectangle).Contains(b.RectangleToScreen(b.ClientRectangle))));
                        var before=card.BackColor; AppTheme.Current=light?AppTheme.Dark:AppTheme.Light;
                        Assert.NotEqual(before,card.BackColor);
                    }
                } finally { AppTheme.Current=old; }
            });
        }
    }
}
