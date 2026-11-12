using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class DashboardSummaryLayoutTests
    {
        [Theory]
        [InlineData(1080, false)] [InlineData(1440, false)] [InlineData(1920, false)]
        [InlineData(1080, true)] [InlineData(1440, true)] [InlineData(1920, true)]
        public void Summary_values_descriptions_and_actions_have_separate_visible_space(int width, bool light)
        {
            StaTest.Run(() => {
                var previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    using (var form = new DashboardForm(new ManagerSettings())) {
                        form.Show(); form.ClientSize = new Size(width, 900); Application.DoEvents();
                        var titles = new[] { "Consoles", "Supported games", "Mods and plugins", "Tools" };
                        var cards = Descendants(form).OfType<GroupControl>().Where(g => titles.Contains(g.Text) && Descendants(g).OfType<UiLabel>().Any(l => l.Appearance.Font.Size >= 20)).ToArray();
                        Assert.Equal(4, cards.Length);
                        var output = Environment.GetEnvironmentVariable("PS4MANAGER_VISUAL_DIR");
                        if (!string.IsNullOrEmpty(output)) {
                            System.IO.Directory.CreateDirectory(output);
                            using (var bitmap = new Bitmap(form.Width, form.Height)) {
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                bitmap.Save(System.IO.Path.Combine(output, "dashboard-" + width + "-" + (light ? "light" : "dark") + ".png"));
                            }
                        }
                        foreach (var card in cards) {
                            var leaves = Descendants(card).Where(c => c is UiLabel || c is UiButton || c is PictureEdit).ToArray();
                            foreach (var label in leaves.OfType<UiLabel>()) {
                                var needed = label.CalcBestSize();
                                Assert.True(label.Height >= needed.Height, card.Text + ": " + label.Text + " has " + label.Height + "px height for " + needed.Height + "px text");
                                Assert.True(label.Width >= needed.Width, card.Text + ": " + label.Text + " is clipped horizontally");
                                var view = label.GetViewInfo();
                                Assert.False((bool)view.GetType().GetProperty("IsCropped").GetValue(view), card.Text + ": " + label.Text + " native text is cropped");
                            }
                            foreach (var leaf in leaves) {
                                var bounds = card.RectangleToClient(leaf.Parent.RectangleToScreen(leaf.Bounds));
                                Assert.True(card.DisplayRectangle.Contains(bounds), card.Text + ": " + leaf.Text + " outside card content");
                                foreach (var other in leaves.Where(c => !ReferenceEquals(c, leaf))) {
                                    var otherBounds = card.RectangleToClient(other.Parent.RectangleToScreen(other.Bounds));
                                    Assert.False(bounds.IntersectsWith(otherBounds), card.Text + ": " + leaf.Text + " overlaps " + other.Text);
                                }
                            }
                        }
                    }
                } finally { AppTheme.Current = previous; }
            });
        }
        private static IEnumerable<Control> Descendants(Control root)
        { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    }
}
