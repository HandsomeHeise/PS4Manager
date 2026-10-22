using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class MessageDialogTests
    {
        [Theory][InlineData(-1, 1)][InlineData(2, 1)][InlineData(0, -1)][InlineData(0, 2)]
        public void Invalid_choice_indices_are_rejected(int chosen, int cancel)
        {
            StaTest.Run(() => Assert.Throws<ArgumentOutOfRangeException>(() => new MessageDialog("Confirm", "Text", new[] { "Yes", "No" }, chosen, cancel, null)));
        }
        [Theory][InlineData(Keys.Enter)][InlineData(Keys.Escape)]
        public void Safe_default_and_escape_do_not_approve(Keys key)
        {
            StaTest.Run(() => {
                using(var dialog = new MessageDialog("Confirm", "Continue?", new[] { "Continue", "Cancel" }, 1, 1, null)) {
                    dialog.Show(); Key(dialog, key); Assert.Equal(1, dialog.Choice);
                }
            });
        }
        [Fact] public void Focused_button_and_checkbox_retain_existing_behavior()
        {
            StaTest.Run(() => {
                using(var dialog = new MessageDialog("Confirm", "Continue?", new[] { "Continue", "Cancel" }, 1, 1, "Remember")) {
                    dialog.Show(); var controls = Descendants(dialog).ToList();
                    controls.OfType<UiCheckBox>().Single().Checked = true;
                    controls.OfType<UiButton>().Single(b => b.Text == "Continue").Focus();
                    Key(dialog, Keys.Enter); Assert.Equal(0, dialog.Choice); Assert.True(dialog.Checked);
                }
            });
        }
        [Fact] public void Window_close_keeps_cancel_choice()
        {
            StaTest.Run(() => { using(var dialog = new MessageDialog("Confirm", "Continue?", new[] { "Continue", "Cancel" }, 1, 1, null)) {
                dialog.Show(); dialog.Close(); Assert.Equal(1, dialog.Choice);
            }});
        }
        [Theory][InlineData(false,1f)][InlineData(true,1f)][InlineData(false,1.5f)][InlineData(true,1.5f)][InlineData(false,2f)][InlineData(true,2f)]
        public void Long_content_is_bounded_and_recoverable(bool light, float scale)
        {
            StaTest.Run(() => {
                var previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    var message = new string('x',4096) + string.Concat(Enumerable.Repeat("\r\nNext line",100));
                    using(var dialog = new MessageDialog("Long result", message, new[] { "OK" }, 0, 0, null)) {
                        dialog.Show(); dialog.Scale(new SizeF(scale,scale)); dialog.PerformLayout();
                        Assert.True(Screen.FromControl(dialog).WorkingArea.Contains(dialog.Bounds));
                        var content = Descendants(dialog).OfType<UiTextBox>().Single();
                        Assert.Equal(message,content.Text); Assert.True(content.ReadOnly);
                        Assert.All(Descendants(dialog).OfType<UiButton>(), button => Assert.True(dialog.RectangleToScreen(dialog.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle))));
                    }
                } finally { AppTheme.Current = previous; }
            });
        }
        internal static IEnumerable<Control> Descendants(Control parent)
        { foreach(Control child in parent.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; } }
        private static void Key(MessageDialog dialog, Keys key)
        { typeof(MessageDialog).GetMethod("ProcessDialogKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[]{key}); }
    }
}
