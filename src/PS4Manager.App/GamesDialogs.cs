using FileManager.App.Dialogs;
using PS4Manager.Core.Games;

namespace PS4Manager.App
{
    internal static class GamesDialogs
    {
        internal static MessageDialog CreateAction(ConfirmationRequest request) => new MessageDialog(
            "Confirm trainer action", request.Message, new[] { "Cancel", "I am paused — continue" }, 0, 0, null);

        internal static MessageDialog CreateCloseWarning() => new MessageDialog("Godmode may remain enabled",
            "Godmode is enabled or its last state is uncertain. A pending action may still complete.\r\n\r\n" +
            "No changes will be undone when this window closes. To disable Godmode, cancel and use Disable Godmode while paused.",
            new[] { "Cancel", "Close and leave enabled" }, 0, 0, null, MessageKind.Warning, null);
    }
}
