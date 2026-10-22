using PS4Manager.UI;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using PS4Manager.Core.Tests.Presentation;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    [Collection(UiControlCollection.Name)]
    public sealed class DesktopNotificationServiceTests
    {
        [Fact] public void Cards_survive_minimize_but_clear_and_dispose_remove_them()
        {
            StaTest.Run(() => {
                using(var host=new Form()) using(var service=new DesktopNotificationService(host,null)) {
                    host.Show(); service.Show(new DesktopNotification("Upload","Complete",MessageKind.Success));
                    host.WindowState=FormWindowState.Minimized; Application.DoEvents();
                    var card=Assert.Single(Application.OpenForms.Cast<Form>().OfType<NotificationWindow>());
                    Assert.True(card.Visible); Assert.Null(card.Owner);
                    service.Clear(); Assert.Empty(Application.OpenForms.Cast<Form>().OfType<NotificationWindow>());
                    service.Show(new DesktopNotification("Next","",MessageKind.Error)); service.Dispose();
                    service.Show(new DesktopNotification("Too late","",MessageKind.Success)); Application.DoEvents();
                    Assert.Empty(Application.OpenForms.Cast<Form>().OfType<NotificationWindow>());
                }
            });
        }
        [Fact] public void Posted_show_after_disposal_is_ignored()
        {
            StaTest.Run(() => {
                using(var host=new Form()) using(var service=new DesktopNotificationService(host,null)) {
                    host.Show();
                    var worker=new Thread(()=>service.Show(new DesktopNotification("Late","",MessageKind.Success)));
                    worker.Start(); worker.Join(); service.Dispose(); Application.DoEvents();
                    Assert.Empty(Application.OpenForms.Cast<Form>().OfType<NotificationWindow>());
                }
            });
        }
        [Fact] public void Throwing_details_action_is_contained_and_explained()
        {
            StaTest.Run(() => {
                using(var host=new Form()) using(var service=new DesktopNotificationService(host,null)) {
                    host.Show(); service.Show(new DesktopNotification("Result","",MessageKind.Success,null,()=>throw new InvalidOperationException("Details unavailable")));
                    var card=Assert.Single(Application.OpenForms.Cast<Form>().OfType<NotificationWindow>());
                    MessageDialogTests.Descendants(card).OfType<UiButton>().Single(b=>b.Text=="Details").PerformClick();
                    Application.DoEvents();
                    Assert.Contains(Application.OpenForms.Cast<Form>().OfType<NotificationWindow>(),f=>MessageDialogTests.Descendants(f).Any(c=>c.Text.Contains("Details unavailable")));
                }
            });
        }
    }
}
