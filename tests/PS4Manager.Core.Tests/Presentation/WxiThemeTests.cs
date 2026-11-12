using System;
using System.Drawing;
using System.Linq;
using DevExpress.LookAndFeel;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class WxiThemeTests
    {
        [Theory] [InlineData(false)] [InlineData(true)]
        public void Custom_dashboard_banner_keeps_its_text_readable_in_both_palettes(bool light)
        {
            StaTest.Run(() => {
                string previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    using (var form = new DashboardForm(new ManagerSettings())) {
                        form.Show(); form.ClientSize = new Size(1440, 900); form.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                        var banner = Descendants(form).OfType<DashboardBanner>().Single();
                        using (var image = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                            var captureRoot = Environment.GetEnvironmentVariable("PS4MANAGER_VISUAL_DIR");
                            if (!string.IsNullOrEmpty(captureRoot)) {
                                System.IO.Directory.CreateDirectory(captureRoot);
                                image.Save(System.IO.Path.Combine(captureRoot, "banner-" + (light ? "light" : "dark") + ".png"));
                            }
                            var origin = form.PointToClient(banner.PointToScreen(Point.Empty)); int brightPixels = 0;
                            for (int y = origin.Y + 20; y < origin.Y + 145; y++) for (int x = origin.X + 20; x < origin.X + 700; x++) {
                                Color pixel = image.GetPixel(x, y); if (pixel.R > 170 && pixel.G > 170 && pixel.B > 170) brightPixels++;
                            }
                            Assert.True(brightPixels > 200, "The blue banner must retain readable white title/body text; bright pixels=" + brightPixels);
                        }
                    }
                } finally { AppTheme.Current = previous; }
            });
        }
        private static System.Collections.Generic.IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent)
        { foreach (System.Windows.Forms.Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
        // WXI's built-in light palette reports an empty ActiveSvgPaletteName.
        [Theory] [InlineData(false, "Darkness")] [InlineData(true, "")]
        public void Theme_switch_uses_real_WXI_palettes_and_preserves_mode_when_Studio_opens(bool light, string palette)
        {
            StaTest.Run(() => {
                string previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    Assert.Equal("WXI", UserLookAndFeel.Default.SkinName);
                    Assert.Equal(palette, UserLookAndFeel.Default.ActiveSvgPaletteName);
                    Assert.Equal(light, AppTheme.IsLight);
                    using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                        form.Show(); System.Windows.Forms.Application.DoEvents();
                        Assert.Equal("WXI", form.LookAndFeel.ActiveSkinName);
                        Assert.Equal(palette, UserLookAndFeel.Default.ActiveSvgPaletteName);
                        Assert.Equal(light, AppTheme.IsLight);
                        Assert.Equal(Color.FromArgb(24, 27, 33), form.ActiveEditor.Styles[ScintillaNET.Style.Default].BackColor);
                        AppTheme.Current = light ? AppTheme.Dark : AppTheme.Light;
                        Assert.Equal(!light, AppTheme.IsLight);
                        Assert.Equal("WXI", UserLookAndFeel.Default.SkinName);
                        Assert.Equal(light ? "Darkness" : "", UserLookAndFeel.Default.ActiveSvgPaletteName);
                        form.DiscardAllForTest();
                    }
                    AppTheme.Current = light ? AppTheme.Dark : AppTheme.Light;
                    Assert.Equal(!light, AppTheme.IsLight);
                    Assert.Equal("WXI", UserLookAndFeel.Default.SkinName);
                    Assert.Equal(light ? "Darkness" : "", UserLookAndFeel.Default.ActiveSvgPaletteName);
                } finally { AppTheme.Current = previous; }
            });
        }
    }
}
