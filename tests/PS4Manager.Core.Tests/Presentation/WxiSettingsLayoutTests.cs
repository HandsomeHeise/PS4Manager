using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Session;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class WxiSettingsLayoutTests
    {
        [Theory]
        [InlineData(9f)]
        [InlineData(13.5f)]
        public void Update_commands_have_room_for_their_text(float fontSize)
        {
            StaTest.Run(() => {
                using (var form = new UpdateManagerForm(new ManagerSettings(), () => { }))
                {
                    Prepare(form, fontSize);
                    AssertButtonsFit(form);
                }
            });
        }

        [Fact]
        public void Update_manifest_field_tracks_available_width_and_keeps_settings_binding()
        {
            StaTest.Run(() => {
                var settings = new ManagerSettings();
                int saves = 0;
                using (var form = new UpdateManagerForm(settings, () => saves++))
                {
                    Prepare(form, 9f);
                    var url = Descendants<UiTextBox>(form).Single(c => !c.Multiline);
                    int width = url.Width;
                    form.ClientSize = new Size(form.ClientSize.Width + 240, form.ClientSize.Height);
                    form.PerformLayout();
                    Assert.InRange(url.Width - width, 235, 245);
                    url.Text = "https://example.invalid/manifest.json";
                    Assert.Equal(url.Text, settings.Updates.ManifestUrl);
                    Assert.True(saves > 0);
                }
            });
        }

        [Theory]
        [InlineData(9f)]
        [InlineData(13.5f)]
        public void Package_commands_have_room_for_their_text(float fontSize)
        {
            StaTest.Run(() => {
                using (var form = new PackageManagerForm(new ManagerSessionController(new UnusedFactory())))
                {
                    Prepare(form, fontSize);
                    form.Size = form.MinimumSize;
                    form.PerformLayout();
                    AssertButtonsFit(form);
                }
            });
        }

        [Theory]
        [InlineData(9f)]
        [InlineData(13.5f)]
        public void Settings_rows_align_and_remain_inside_the_dialog(float fontSize)
        {
            StaTest.Run(() => {
                using (var form = new ManagerSettingsDialog(new ManagerSettings()))
                {
                    Prepare(form, fontSize);
                    var label = Descendants<UiLabel>(form).Single(c => c.Text == "Appearance");
                    var theme = Descendants<UiComboBox>(form).Single();
                    var labelBounds = RelativeBounds(form, label);
                    var inputBounds = RelativeBounds(form, theme);
                    Assert.InRange(Math.Abs(labelBounds.Top + labelBounds.Height / 2 - inputBounds.Top - inputBounds.Height / 2), 0, 3);
                    var fields = Descendants<Control>(form).Where(c => c is UiButton || c is UiLabel || c is UiCheckBox || c is UiComboBox).ToList();
                    foreach (var control in fields)
                    {
                        Assert.True(form.ClientRectangle.Contains(RelativeBounds(form, control)),
                            control.Text + " bounds " + RelativeBounds(form, control) + " are outside " + form.ClientRectangle);
                        Assert.True(control.Height >= control.GetPreferredSize(Size.Empty).Height,
                            control.Text + " height " + control.Height + " is shorter than preferred " + control.GetPreferredSize(Size.Empty).Height);
                    }
                    for (int i = 0; i < fields.Count; i++)
                        for (int j = 0; j < i; j++)
                            Assert.False(RelativeBounds(form, fields[i]).IntersectsWith(RelativeBounds(form, fields[j])),
                                fields[i].Text + " overlaps " + fields[j].Text);
                    foreach (var text in Descendants<UiLabel>(form))
                        Assert.False((bool)text.GetViewInfo().GetType().GetProperty("IsCropped").GetValue(text.GetViewInfo()), text.Text + " is cropped by the native renderer");
                    var option = Descendants<UiCheckBox>(form).Single();
                    Assert.True(option.Width >= TextRenderer.MeasureText(option.Text, option.Font).Width + 20,
                        "Frame4 caption does not fit beside the check glyph");
                    AssertButtonsFit(form);
                }
            });
        }

        private static void Prepare(Form form, float fontSize)
        {
            AppTheme.Current = AppTheme.Dark;
            form.Font = new Font("Segoe UI", fontSize);
            foreach (var control in Descendants<Control>(form)) {
                var property = control.GetType().GetProperties().FirstOrDefault(p => p.Name == "Appearance" && typeof(DevExpress.Utils.AppearanceObject).IsAssignableFrom(p.PropertyType));
                var appearance = property?.GetValue(control) as DevExpress.Utils.AppearanceObject;
                if (appearance == null) continue;
                appearance.Font = form.Font;
                appearance.Options.UseFont = true;
            }
            form.Show();
            form.PerformLayout();
            Application.DoEvents();
        }

        private static void AssertButtonsFit(Control form)
        {
            var buttons = Descendants<UiButton>(form).ToList();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                var preferred = button.GetPreferredSize(Size.Empty);
                Assert.True(button.Height >= preferred.Height,
                    button.Text + ": height " + button.Height + ", preferred " + preferred.Height);
                Assert.True(button.Width >= preferred.Width,
                    button.Text + ": width " + button.Width + ", preferred " + preferred.Width);
                Assert.True(form.ClientRectangle.Contains(RelativeBounds(form, button)), button.Text + " is outside the form");
            }
        }

        private static Rectangle RelativeBounds(Control root, Control child)
        {
            return root.RectangleToClient(child.Parent.RectangleToScreen(child.Bounds));
        }

        private static IEnumerable<T> Descendants<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        private sealed class UnusedFactory : IManagerSessionFactory
        {
            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode) => throw new InvalidOperationException("Layout tests must not connect.");
        }
    }
}
