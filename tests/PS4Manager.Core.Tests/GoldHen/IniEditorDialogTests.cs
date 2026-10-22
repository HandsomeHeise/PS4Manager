using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    [Collection(UiControlCollection.Name)]
    public sealed class IniEditorDialogTests
    {
        [Fact]
        public void Editor_shows_the_original_multiline_text()
        {
            var text = "[CUSA00411]\r\n/data/GoldHEN/plugins/tool.prx\r\n\r\n[PPSA00001]\r\n/data/GoldHEN/plugins/other.prx\r\n";
            using (var dialog = new IniEditorDialog("plugins.ini", text, value => Task.FromResult(true)))
            {
                dialog.CreateControl();

                var editor = FindTextBoxes(dialog).Single();

                Assert.True(editor.Multiline);
                Assert.Equal(text, editor.Text);
                Assert.Equal(ScrollBars.Both, editor.ScrollBars);
                Assert.False(editor.WordWrap);
            }
        }

        [Fact]
        public void Editor_displays_lf_only_ini_as_lines()
        {
            var text = "[CUSA00411]\n/data/GoldHEN/plugins/tool.prx\n\n[PPSA00001]\n/data/GoldHEN/plugins/other.prx\n";
            using (var dialog = new IniEditorDialog("plugins.ini", text, value => Task.FromResult(true)))
            {
                dialog.CreateControl();

                var editor = FindTextBoxes(dialog).Single();

                Assert.Equal("[CUSA00411]\r\n/data/GoldHEN/plugins/tool.prx\r\n\r\n[PPSA00001]\r\n/data/GoldHEN/plugins/other.prx\r\n", editor.Text);
            }
        }

        private static System.Collections.Generic.IEnumerable<TextBox> FindTextBoxes(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var textBox = child as TextBox;
                if (textBox != null) yield return textBox;
                foreach (var nested in FindTextBoxes(child)) yield return nested;
            }
        }
    }
}
