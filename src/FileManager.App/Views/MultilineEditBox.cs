using PS4Manager.UI;

namespace FileManager.App.Views
{
    internal sealed class MultilineEditBox : UiTextBox
    {
        public MultilineEditBox() { Multiline = true; }
        public void FocusText() { Focus(); SelectAll(); }
    }
}
