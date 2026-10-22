using System.Drawing;
using FileManager.App.Notifications;
using Xunit;

namespace PS4Manager.Core.Tests.Notifications
{
    public sealed class NotificationLayoutTests
    {
        [Theory][InlineData(96)][InlineData(144)][InlineData(192)]
        public void Cards_stay_within_offset_working_area_without_overlap(int dpi)
        {
            var area=new Rectangle(-1920,40,1920,1000);
            var result=NotificationLayout.Arrange(area,dpi,new[]{new Size(720,440),new Size(720,440),new Size(720,440),new Size(720,440)});
            Assert.InRange(result.Count,1,3);
            for(int i=0;i<result.Count;i++) {
                Assert.True(area.Contains(result[i]));
                for(int j=0;j<i;j++) Assert.False(result[i].IntersectsWith(result[j]));
            }
        }
        [Fact] public void Small_area_reduces_capacity_and_empty_area_has_none()
        {
            Assert.Empty(NotificationLayout.Arrange(Rectangle.Empty,96,new[]{new Size(360,220)}));
            var result=NotificationLayout.Arrange(new Rectangle(0,0,300,250),96,new[]{new Size(360,220),new Size(360,220)});
            Assert.Single(result); Assert.Equal(new Rectangle(12,18,276,220),result[0]);
        }
    }
}
