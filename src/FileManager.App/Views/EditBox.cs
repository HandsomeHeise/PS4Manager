using PS4Manager.UI;

namespace FileManager.App.Views
{
    /// <summary>
    /// Fokussiert und markiert den Namen für den Umbenennen-Dialog.
    /// </summary>
    internal sealed class EditBox : UiTextBox
    {
        public void FocusText()
        {
            Focus(); SelectAll();
        }
    }
}
