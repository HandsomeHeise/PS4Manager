using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PS4Manager.Core.Session;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.Gallery.App;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class GalleryThumbnailLayoutTests
    {
        [Theory]
        [InlineData(440, 248)]
        [InlineData(640, 200)]
        [InlineData(240, 440)]
        public void Screenshot_tile_shows_all_four_corners_without_distortion(int width, int height)
        {
            StaTest.Run(() => {
                using (var source = new Bitmap(width, height))
                using (var form = new MediaGalleryForm(new ManagerSessionController(new OfflineFactory()))) {
                    var colors = new[] { Color.Magenta, Color.Lime, Color.Cyan, Color.Orange };
                    using (var graphics = Graphics.FromImage(source)) {
                        graphics.Clear(Color.Black);
                        for (int i = 0; i < colors.Length; i++)
                            using (var brush = new SolidBrush(colors[i]))
                                graphics.FillRectangle(brush, i % 2 * (width - width / 4), i / 2 * (height - height / 4), width / 4, height / 4);
                    }

                    var gallery = form.Controls.OfType<AppGallery>().Single();
                    var group = new GalleryItemGroup { Caption = "Preview fixture" };
                    var item = new GalleryItem { Caption = "Screenshot", Image = source };
                    group.Items.Add(item);
                    gallery.Gallery.Groups.Add(group);
                    form.ClientSize = new Size(540, 350);
                    form.Show();
                    Application.DoEvents();

                    using (var rendered = new Bitmap(gallery.Width, gallery.Height)) {
                        gallery.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                        var captureDirectory = Environment.GetEnvironmentVariable("PS4MANAGER_VISUAL_DIR");
                        if (!string.IsNullOrEmpty(captureDirectory)) {
                            System.IO.Directory.CreateDirectory(captureDirectory);
                            rendered.Save(System.IO.Path.Combine(captureDirectory, "thumbnail-" + width + "x" + height + ".png"));
                        }
                        var counts = new int[colors.Length];
                        int left = rendered.Width, top = rendered.Height, right = 0, bottom = 0;
                        for (int y = 0; y < rendered.Height; y++) for (int x = 0; x < rendered.Width; x++) {
                            var pixel = rendered.GetPixel(x, y).ToArgb();
                            for (int i = 0; i < colors.Length; i++) if (pixel == colors[i].ToArgb()) {
                                counts[i]++;
                                left = Math.Min(left, x); top = Math.Min(top, y);
                                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                            }
                        }
                        for (int i = 0; i < colors.Length; i++)
                            Assert.True(counts[i] >= 20, "Corner " + colors[i].Name + " is cropped in " + width + "×" + height + ".");
                        var aspect = (double)(right - left + 1) / (bottom - top + 1);
                        Assert.InRange(aspect, (double)width / height * .94, (double)width / height * 1.06);
                    }
                }
            });
        }

        private sealed class OfflineFactory : IManagerSessionFactory
        {
            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode) => throw new InvalidOperationException("Gallery rendering test must stay offline.");
        }
    }
}
